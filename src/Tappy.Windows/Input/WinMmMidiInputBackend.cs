using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace Tappy.Windows.Input;

internal sealed class WinMmMidiInputBackend : IWinMmMidiInputBackend
{
    private const uint CallbackFunction = 0x00030000;
    private const uint MidiData = 0x03C3;
    private readonly object _gate = new();
    private readonly MidiInCallback _callback;
    private IntPtr _handle;
    private bool _disposed;

    internal WinMmMidiInputBackend()
    {
        _callback = OnMidiInput;
    }

    public event Action<uint>? ShortMessageReceived;

    public IReadOnlyList<WinMmMidiInputDevice> EnumerateDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var devices = new List<WinMmMidiInputDevice>();
        var size = (uint)Marshal.SizeOf<MidiInCaps>();
        var count = midiInGetNumDevs();
        for (uint index = 0; index < count; index++)
        {
            if (midiInGetDevCapsW((UIntPtr)index, out var caps, size) != 0 ||
                string.IsNullOrWhiteSpace(caps.Name))
            {
                continue;
            }

            devices.Add(new WinMmMidiInputDevice(
                (int)index,
                caps.Name.Trim(),
                caps.ManufacturerId,
                caps.ProductId,
                caps.DriverVersion));
        }

        return devices;
    }

    public void Open(int deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            CloseLocked();
            var result = midiInOpen(
                out _handle,
                (UIntPtr)(uint)deviceId,
                _callback,
                IntPtr.Zero,
                CallbackFunction);
            if (result != 0)
            {
                _handle = IntPtr.Zero;
                throw CreateError(result, $"Windows could not open MIDI input device {deviceId}");
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("A MIDI input device must be opened before listening starts.");
            }

            var result = midiInStart(_handle);
            if (result != 0)
            {
                throw CreateError(result, "Windows could not start the MIDI input stream");
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_handle != IntPtr.Zero)
            {
                _ = midiInStop(_handle);
                _ = midiInReset(_handle);
            }
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            CloseLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            CloseLocked();
            _disposed = true;
        }
    }

    private void CloseLocked()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        _ = midiInStop(_handle);
        _ = midiInReset(_handle);
        _ = midiInClose(_handle);
        _handle = IntPtr.Zero;
    }

    private void OnMidiInput(IntPtr midiHandle, uint message, IntPtr instance, UIntPtr parameter1, UIntPtr parameter2)
    {
        if (message != MidiData)
        {
            return;
        }

        try
        {
            ShortMessageReceived?.Invoke(unchecked((uint)parameter1.ToUInt64()));
        }
        catch (Exception exception)
        {
            // No managed exception may cross the native multimedia callback boundary.
            Debug.WriteLine($"Tappy MIDI callback failure: {exception.Message}");
        }
    }

    private static Exception CreateError(int code, string context)
    {
        var text = new StringBuilder(256);
        return midiInGetErrorTextW(code, text, text.Capacity) == 0
            ? new InvalidOperationException($"{context}: {text} (MIDI error {code}).")
            : new InvalidOperationException($"{context} (MIDI error {code}).");
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void MidiInCallback(
        IntPtr midiHandle,
        uint message,
        IntPtr instance,
        UIntPtr parameter1,
        UIntPtr parameter2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Support;
    }

    [DllImport("winmm.dll")]
    private static extern uint midiInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int midiInGetDevCapsW(UIntPtr deviceId, out MidiInCaps caps, uint capsSize);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int midiInGetErrorTextW(int error, StringBuilder text, int textLength);

    [DllImport("winmm.dll")]
    private static extern int midiInOpen(
        out IntPtr handle,
        UIntPtr deviceId,
        MidiInCallback callback,
        IntPtr instance,
        uint flags);

    [DllImport("winmm.dll")]
    private static extern int midiInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiInClose(IntPtr handle);
}
