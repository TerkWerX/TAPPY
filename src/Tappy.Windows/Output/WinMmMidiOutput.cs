using System.Runtime.InteropServices;
using System.Text;
using Tappy.Core.Output;

namespace Tappy.Windows.Output;

public sealed class WinMmMidiOutput : IDisposable
{
    private static readonly UIntPtr MidiMapperId = UIntPtr.Size == 8
        ? new UIntPtr(ulong.MaxValue)
        : new UIntPtr(uint.MaxValue);
    private readonly object _gate = new();
    private IntPtr _handle;
    private string _activeDevice = string.Empty;

    public sealed record Device(int DeviceId, string Name, bool IsSystemDefault = false)
    {
        public string DisplayName => IsSystemDefault ? "Windows default MIDI output" : Name;
    }

    public static IReadOnlyList<Device> GetDevices(bool includeSystemDefault = true)
    {
        var result = new List<Device>();
        var count = midiOutGetNumDevs();
        if (includeSystemDefault)
        {
            result.Add(new Device(-1, string.Empty, true));
        }

        var size = (uint)Marshal.SizeOf<MidiOutCaps>();
        for (uint index = 0; index < count; index++)
        {
            if (midiOutGetDevCapsW((UIntPtr)index, out var capabilities, size) == 0 &&
                !string.IsNullOrWhiteSpace(capabilities.Name))
            {
                result.Add(new Device((int)index, capabilities.Name.Trim()));
            }
        }

        return result;
    }

    public void Send(string? preferredDeviceName, MidiShortMessage message)
    {
        lock (_gate)
        {
            EnsureOpen(preferredDeviceName?.Trim() ?? string.Empty);
            var code = midiOutShortMsg(_handle, message.PackedValue);
            if (code == 0)
            {
                return;
            }

            var device = _activeDevice;
            Close();
            throw CreateError(code, $"Windows could not send the MIDI message to {device}");
        }
    }

    private void EnsureOpen(string deviceName)
    {
        if (_handle != IntPtr.Zero &&
            deviceName.Equals(_activeDevice, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Close();
        var id = MidiMapperId;
        var displayName = "Windows default MIDI output";
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var selected = GetDevices(false).FirstOrDefault(device =>
                device.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                throw new InvalidOperationException(
                    $"The selected MIDI output '{deviceName}' is not connected.");
            }

            id = (UIntPtr)(uint)selected.DeviceId;
            displayName = selected.Name;
        }

        var code = midiOutOpen(out _handle, id, IntPtr.Zero, IntPtr.Zero, 0);
        if (code != 0)
        {
            _handle = IntPtr.Zero;
            throw CreateError(code, $"Could not open {displayName}");
        }

        // An empty active name denotes the mapper and is distinct from any named device.
        _activeDevice = deviceName;
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_handle != IntPtr.Zero)
            {
                _ = midiOutReset(_handle);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close();
        }
    }

    private void Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        _ = midiOutReset(_handle);
        _ = midiOutClose(_handle);
        _handle = IntPtr.Zero;
        _activeDevice = string.Empty;
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

    [DllImport("winmm.dll")]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetDevCapsW(UIntPtr deviceId, out MidiOutCaps caps, uint capsSize);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetErrorTextW(int error, StringBuilder text, int textLength);

    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr handle, UIntPtr deviceId, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll")]
    private static extern int midiOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr handle);
}
