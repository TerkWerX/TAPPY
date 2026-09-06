using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Tappy.Windows.ExclusiveInput;

internal readonly record struct KeyboardFilterSessionToken(ulong Low, ulong High)
{
    public static KeyboardFilterSessionToken Create()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        var token = new KeyboardFilterSessionToken(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
        return token == default ? new KeyboardFilterSessionToken(1, 0) : token;
    }
}

internal interface ITappyKeyboardFilterTransport : IDisposable
{
    byte[] Invoke(uint controlCode, byte[] input, int outputCapacity);
}

internal interface ITappyKeyboardFilterDeviceSession : IDisposable
{
    void Begin();
    KeyboardFilterInstanceStatus QueryStatus();
    void SetPolicy(
        ulong policyGeneration,
        KeyboardFilterRole role,
        KeyboardFilterMode mode,
        uint watchdogTimeoutMilliseconds);
    void Heartbeat(ulong policyGeneration);
    IReadOnlyList<KeyboardFilterEvent> ReadEvents(int maximumEvents);
    void ForceFailOpen();
}

/// <summary>
/// Broker-side version-1 driver session. This type is intentionally internal:
/// the desktop process must never open the SYSTEM-only driver endpoint directly.
/// </summary>
internal sealed class TappyKeyboardFilterDeviceSession : ITappyKeyboardFilterDeviceSession
{
    private readonly ITappyKeyboardFilterTransport _transport;
    private readonly KeyboardFilterSessionToken _token;
    private bool _begun;
    private bool _disposed;

    public TappyKeyboardFilterDeviceSession(
        ITappyKeyboardFilterTransport transport,
        KeyboardFilterSessionToken token)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _token = token == default
            ? throw new ArgumentException("The driver session token cannot be all zeroes.", nameof(token))
            : token;
    }

    public void Begin()
    {
        ThrowIfDisposed();
        _transport.Invoke(
            TappyKeyboardFilterProtocol.BeginAuthenticatedSession,
            TappyKeyboardFilterWireCodec.SessionRequest(_token),
            0);
        _begun = true;
    }

    public KeyboardFilterInstanceStatus QueryStatus()
    {
        ThrowIfDisposed();
        var bytes = _transport.Invoke(
            TappyKeyboardFilterProtocol.QueryStatus,
            [],
            TappyKeyboardFilterProtocol.StatusSize);
        return TappyKeyboardFilterWireCodec.ParseStatus(bytes);
    }

    public void SetPolicy(
        ulong policyGeneration,
        KeyboardFilterRole role,
        KeyboardFilterMode mode,
        uint watchdogTimeoutMilliseconds)
    {
        EnsureBegun();
        if (policyGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policyGeneration));
        }

        if (mode == KeyboardFilterMode.CaptureAndSuppress &&
            !TappyKeyboardFilterProtocol.IsValidWatchdog(watchdogTimeoutMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(watchdogTimeoutMilliseconds));
        }

        if (role != KeyboardFilterRole.ConfigurableSecondary &&
            mode != KeyboardFilterMode.PassThrough)
        {
            throw new ArgumentException("Only a configurable secondary may request capture and suppression.");
        }

        _transport.Invoke(
            TappyKeyboardFilterProtocol.SetInstancePolicy,
            TappyKeyboardFilterWireCodec.PolicyRequest(
                _token,
                policyGeneration,
                role,
                mode,
                watchdogTimeoutMilliseconds),
            0);
    }

    public void Heartbeat(ulong policyGeneration)
    {
        EnsureBegun();
        _transport.Invoke(
            TappyKeyboardFilterProtocol.Heartbeat,
            TappyKeyboardFilterWireCodec.HeartbeatRequest(_token, policyGeneration),
            0);
    }

    public IReadOnlyList<KeyboardFilterEvent> ReadEvents(int maximumEvents)
    {
        EnsureBegun();
        if (maximumEvents is < 1 or > TappyKeyboardFilterProtocol.MaximumEventsPerRead)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        }

        var bytes = _transport.Invoke(
            TappyKeyboardFilterProtocol.ReadEvents,
            TappyKeyboardFilterWireCodec.ReadRequest(_token, maximumEvents),
            checked(maximumEvents * TappyKeyboardFilterProtocol.EventSize));
        return TappyKeyboardFilterWireCodec.ParseEvents(bytes);
    }

    public void ForceFailOpen()
    {
        EnsureBegun();
        _transport.Invoke(
            TappyKeyboardFilterProtocol.ForceFailOpen,
            TappyKeyboardFilterWireCodec.SessionRequest(_token),
            0);
        _begun = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_begun)
        {
            try
            {
                ForceFailOpen();
            }
            catch (Win32Exception)
            {
                // Closing the exclusive handle is itself a kernel-local
                // fail-open path, including when DeviceIoControl has failed.
            }
            catch (IOException)
            {
                // The handle-close path is the final safety mechanism.
            }
        }

        _transport.Dispose();
        _disposed = true;
    }

    private void EnsureBegun()
    {
        ThrowIfDisposed();
        if (!_begun)
        {
            throw new InvalidOperationException("Begin the authenticated driver session first.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal static class TappyKeyboardFilterWireCodec
{
    public static byte[] SessionRequest(KeyboardFilterSessionToken token)
    {
        var bytes = Header(TappyKeyboardFilterProtocol.SessionRequestSize);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), token.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), token.High);
        return bytes;
    }

    public static byte[] PolicyRequest(
        KeyboardFilterSessionToken token,
        ulong generation,
        KeyboardFilterRole role,
        KeyboardFilterMode mode,
        uint watchdogMilliseconds)
    {
        var bytes = Header(TappyKeyboardFilterProtocol.PolicyRequestSize);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), token.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), token.High);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), generation);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), (uint)role);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), (uint)mode);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), watchdogMilliseconds);
        return bytes;
    }

    public static byte[] HeartbeatRequest(KeyboardFilterSessionToken token, ulong generation)
    {
        var bytes = Header(TappyKeyboardFilterProtocol.HeartbeatRequestSize);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), token.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), token.High);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), generation);
        return bytes;
    }

    public static byte[] ReadRequest(KeyboardFilterSessionToken token, int maximumEvents)
    {
        var bytes = Header(TappyKeyboardFilterProtocol.ReadRequestSize);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), token.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), token.High);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), checked((uint)maximumEvents));
        return bytes;
    }

    public static KeyboardFilterInstanceStatus ParseStatus(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != TappyKeyboardFilterProtocol.StatusSize)
        {
            throw new InvalidDataException("The keyboard filter returned an invalid status size.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (!TappyKeyboardFilterProtocol.IsSupportedVersion(version))
        {
            throw new InvalidDataException($"Unsupported keyboard filter protocol version {version}.");
        }

        var mode = (KeyboardFilterMode)BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var role = (KeyboardFilterRole)BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(role))
        {
            throw new InvalidDataException("The keyboard filter returned an invalid mode or role.");
        }

        return new KeyboardFilterInstanceStatus(
            version,
            mode,
            role,
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]));
    }

    public static IReadOnlyList<KeyboardFilterEvent> ParseEvents(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % TappyKeyboardFilterProtocol.EventSize != 0 ||
            bytes.Length / TappyKeyboardFilterProtocol.EventSize > TappyKeyboardFilterProtocol.MaximumEventsPerRead)
        {
            throw new InvalidDataException("The keyboard filter returned an invalid event batch size.");
        }

        var events = new List<KeyboardFilterEvent>(bytes.Length / TappyKeyboardFilterProtocol.EventSize);
        for (var offset = 0; offset < bytes.Length; offset += TappyKeyboardFilterProtocol.EventSize)
        {
            var item = bytes.Slice(offset, TappyKeyboardFilterProtocol.EventSize);
            if (BinaryPrimitives.ReadUInt16LittleEndian(item[22..]) != 0)
            {
                throw new InvalidDataException("The keyboard filter event reserved field was nonzero.");
            }

            events.Add(new KeyboardFilterEvent(
                BinaryPrimitives.ReadUInt64LittleEndian(item),
                BinaryPrimitives.ReadUInt64LittleEndian(item[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(item[16..]),
                (KeyboardFilterEventFlags)BinaryPrimitives.ReadUInt16LittleEndian(item[18..]),
                BinaryPrimitives.ReadUInt16LittleEndian(item[20..])));
        }

        if (!TappyKeyboardFilterProtocol.IsValidEventBatch(events))
        {
            throw new InvalidDataException("The keyboard filter returned invalid or out-of-order events.");
        }

        return events;
    }

    private static byte[] Header(int size)
    {
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, TappyKeyboardFilterProtocol.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)size));
        return bytes;
    }
}

internal sealed class NativeTappyKeyboardFilterTransport : ITappyKeyboardFilterTransport
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;

    private readonly SafeFileHandle _handle;

    public NativeTappyKeyboardFilterTransport(string deviceInterfacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInterfacePath);
        _handle = CreateFile(
            deviceInterfacePath,
            GenericRead | GenericWrite,
            0,
            nint.Zero,
            OpenExisting,
            FileAttributeNormal,
            nint.Zero);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the Tappy keyboard filter endpoint.");
        }
    }

    public byte[] Invoke(uint controlCode, byte[] input, int outputCapacity)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (outputCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputCapacity));
        }

        var output = new byte[outputCapacity];
        if (!DeviceIoControl(
                _handle,
                controlCode,
                input,
                checked((uint)input.Length),
                output,
                checked((uint)output.Length),
                out var bytesReturned,
                nint.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Tappy keyboard filter IOCTL 0x{controlCode:X8} failed.");
        }

        if (bytesReturned > output.Length)
        {
            throw new InvalidDataException("The keyboard filter reported more output than the supplied buffer.");
        }

        return output.AsSpan(0, checked((int)bytesReturned)).ToArray();
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] input,
        uint inputSize,
        byte[] output,
        uint outputSize,
        out uint bytesReturned,
        nint overlapped);
}

internal static class NativeTappyKeyboardFilterEndpointEnumerator
{
    private const uint PresentInterfacesOnly = 0;
    private const int Success = 0;
    private const int BufferSmall = 0x1A;

    public static IReadOnlyList<string> EnumeratePresent()
    {
        var interfaceClass = TappyKeyboardFilterProtocol.DeviceInterfaceClassId;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = CM_Get_Device_Interface_List_Size(
                out var characterCount,
                ref interfaceClass,
                null,
                PresentInterfacesOnly);
            if (result != Success)
            {
                throw new Win32Exception(result, "Windows could not size the Tappy keyboard filter interface list.");
            }

            if (characterCount <= 1)
            {
                return [];
            }

            var buffer = new char[characterCount];
            result = CM_Get_Device_Interface_List(
                ref interfaceClass,
                null,
                buffer,
                characterCount,
                PresentInterfacesOnly);
            if (result == Success)
            {
                return ParseMultiString(buffer);
            }

            if (result != BufferSmall)
            {
                throw new Win32Exception(result, "Windows could not enumerate the Tappy keyboard filter interfaces.");
            }
        }

        throw new IOException("The Tappy keyboard filter interface list kept changing during enumeration.");
    }

    internal static IReadOnlyList<string> ParseMultiString(ReadOnlySpan<char> buffer)
    {
        var paths = new List<string>();
        var start = 0;
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] != '\0')
            {
                continue;
            }

            if (index == start)
            {
                break;
            }

            paths.Add(buffer[start..index].ToString());
            start = index + 1;
        }

        return paths;
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_Size(
        out uint length,
        ref Guid interfaceClassGuid,
        string? deviceId,
        uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List(
        ref Guid interfaceClassGuid,
        string? deviceId,
        [Out] char[] buffer,
        uint bufferLength,
        uint flags);
}
