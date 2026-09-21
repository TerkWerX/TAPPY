using System.Runtime.InteropServices;
using System.Text;

namespace Tappy.Windows.Output;

/// <summary>
/// Real WinMM output device. SysEx uses midiOutPrepareHeader/midiOutLongMsg/
/// midiOutUnprepareHeader with a CALLBACK_EVENT handle, not the simpler
/// midiOutShortMsg used for ordinary messages, because SysEx delivery is
/// asynchronous: the driver keeps a pointer into the native buffer until it
/// signals completion, and freeing or reusing that memory earlier is a real
/// use-after-free, not a theoretical one. (A widely used .NET MIDI library was
/// found, while researching the Akai Fire, to free its SysEx buffer immediately
/// after the send call returns — see docs/AKAI_FIRE.md — which is exactly the
/// mistake this class exists to avoid.) The buffer is only ever freed after the
/// completion event fires AND the header's MHDR_DONE flag confirms the driver
/// marked it finished; if a send times out or reports an inconsistent state,
/// that one buffer is deliberately leaked rather than freed while its ownership
/// is unknown. That leak is bounded (one buffer per stuck send, sends are
/// infrequent, at most a few KB each) and is treated as a real fault: it is
/// thrown as an exception, not swallowed.
/// </summary>
internal sealed class WinMmMidiOutputBackend : IWinMmMidiOutputBackend
{
    private static readonly UIntPtr MidiMapperId = UIntPtr.Size == 8
        ? new UIntPtr(ulong.MaxValue)
        : new UIntPtr(uint.MaxValue);
    private const uint CallbackEvent = 0x00050000;
    private const uint MhdrDone = 0x00000001;
    private static readonly TimeSpan SysExCompletionTimeout = TimeSpan.FromSeconds(3);

    private readonly AutoResetEvent _completionEvent = new(false);
    private IntPtr _handle;
    private bool _disposed;

    public IReadOnlyList<WinMmMidiOutputDevice> EnumerateDevices()
    {
        var result = new List<WinMmMidiOutputDevice>();
        var count = midiOutGetNumDevs();
        var size = (uint)Marshal.SizeOf<MidiOutCaps>();
        for (uint index = 0; index < count; index++)
        {
            if (midiOutGetDevCapsW((UIntPtr)index, out var caps, size) == 0 &&
                !string.IsNullOrWhiteSpace(caps.Name))
            {
                result.Add(new WinMmMidiOutputDevice((int)index, caps.Name.Trim(), caps.ManufacturerId, caps.ProductId));
            }
        }

        return result;
    }

    public void Open(int? deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = deviceId is { } value ? (UIntPtr)(uint)value : MidiMapperId;
        var eventHandle = _completionEvent.SafeWaitHandle.DangerousGetHandle();
        var code = midiOutOpen(out _handle, id, eventHandle, IntPtr.Zero, CallbackEvent);
        if (code != 0)
        {
            _handle = IntPtr.Zero;
            throw CreateError(code, "Could not open the MIDI output device");
        }
    }

    public void SendShortMessage(uint packedMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The MIDI output device is not open.");
        }

        var code = midiOutShortMsg(_handle, packedMessage);
        if (code != 0)
        {
            throw CreateError(code, "Windows could not send the MIDI message");
        }
    }

    public void SendSysEx(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The MIDI output device is not open.");
        }

        var buffer = Marshal.AllocHGlobal(Math.Max(1, data.Length));
        var headerHandle = GCHandle.Alloc(default(MidiHdr), GCHandleType.Pinned);
        var headerPtr = headerHandle.AddrOfPinnedObject();
        var headerSize = Marshal.SizeOf<MidiHdr>();
        // Flips to false only once the driver's ownership of the buffer becomes
        // uncertain; the finally block below must not free memory in that case.
        var bufferOwned = true;
        try
        {
            Marshal.Copy(data, 0, buffer, data.Length);
            Marshal.StructureToPtr(
                new MidiHdr { lpData = buffer, dwBufferLength = (uint)data.Length, dwBytesRecorded = (uint)data.Length },
                headerPtr,
                fDeleteOld: false);

            var prepareCode = midiOutPrepareHeader(_handle, headerPtr, headerSize);
            if (prepareCode != 0)
            {
                throw CreateError(prepareCode, "Windows could not prepare the SysEx buffer");
            }

            _completionEvent.Reset();
            var sendCode = midiOutLongMsg(_handle, headerPtr, headerSize);
            if (sendCode != 0)
            {
                // The driver never accepted the buffer, so it is still safe to
                // unprepare and free it below.
                _ = midiOutUnprepareHeader(_handle, headerPtr, headerSize);
                throw CreateError(sendCode, "Windows could not send the SysEx message");
            }

            if (!_completionEvent.WaitOne(SysExCompletionTimeout))
            {
                bufferOwned = false;
                throw new TimeoutException(
                    "Windows did not confirm the SysEx message was sent within the expected time.");
            }

            var completed = Marshal.PtrToStructure<MidiHdr>(headerPtr);
            if ((completed.dwFlags & MhdrDone) == 0)
            {
                bufferOwned = false;
                throw new InvalidOperationException(
                    "Windows signaled MIDI output completion without marking the SysEx buffer done.");
            }

            var unprepareCode = midiOutUnprepareHeader(_handle, headerPtr, headerSize);
            if (unprepareCode != 0)
            {
                bufferOwned = false;
                throw CreateError(unprepareCode, "Windows could not release the SysEx buffer");
            }
        }
        finally
        {
            if (bufferOwned)
            {
                headerHandle.Free();
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public void Reset()
    {
        if (_handle != IntPtr.Zero)
        {
            var code = midiOutReset(_handle);
            if (code != 0)
            {
                throw CreateError(code, "Windows could not reset MIDI output");
            }
        }
    }

    public void Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        _ = midiOutReset(_handle);
        _ = midiOutClose(_handle);
        _handle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
        _completionEvent.Dispose();
    }

    private static Exception CreateError(int code, string context)
    {
        var text = new StringBuilder(256);
        return midiOutGetErrorTextW(code, text, text.Capacity) == 0
            ? new InvalidOperationException($"{context}: {text} (MIDI error {code}).")
            : new InvalidOperationException($"{context} (MIDI error {code}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    /// <summary>Mirrors the native MIDIHDR layout exactly; field order/types/size
    /// must not change without re-verifying against winmm.h.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public UIntPtr dwUser;
        public uint dwFlags;
        public IntPtr lpNext;
        public UIntPtr reserved;
        public uint dwOffset;
        public UIntPtr reserved0;
        public UIntPtr reserved1;
        public UIntPtr reserved2;
        public UIntPtr reserved3;
        public UIntPtr reserved4;
        public UIntPtr reserved5;
        public UIntPtr reserved6;
        public UIntPtr reserved7;
    }

    [DllImport("winmm.dll")]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetDevCapsW(UIntPtr deviceId, out MidiOutCaps caps, uint capsSize);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetErrorTextW(int error, StringBuilder text, int textLength);

    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr handle, UIntPtr deviceId, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll")]
    private static extern int midiOutPrepareHeader(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int midiOutUnprepareHeader(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int midiOutLongMsg(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int midiOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr handle);
}
