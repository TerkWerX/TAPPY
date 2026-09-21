using System.Runtime.InteropServices;
using System.Text;
using Tappy.Core.Output;

namespace Tappy.Windows.Output;

public sealed class WinMmMidiOutput : IMidiOutput
{
    private readonly object _gate = new();
    private readonly IWinMmMidiOutputBackend _backend;
    private bool _isOpen;
    private string _activeDevice = string.Empty;

    public WinMmMidiOutput() : this(new WinMmMidiOutputBackend())
    {
    }

    internal WinMmMidiOutput(IWinMmMidiOutputBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

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
            EnsureOpenLocked(preferredDeviceName?.Trim() ?? string.Empty);
            try
            {
                _backend.SendShortMessage(message.PackedValue);
            }
            catch (Exception exception)
            {
                var device = _activeDevice;
                CloseLocked();
                throw new InvalidOperationException(
                    $"Windows could not send the MIDI message to {device}: {exception.Message}", exception);
            }
        }
    }

    /// <summary>
    /// Sends a complete System Exclusive message (including the leading F0 and
    /// trailing F7) and blocks the calling thread until Windows confirms delivery
    /// or the wait times out — callers must invoke this off the UI thread. See
    /// WinMmMidiOutputBackend for the buffer-lifetime contract this relies on.
    /// </summary>
    public void SendSysEx(string? preferredDeviceName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 2 || data[0] != 0xF0 || data[^1] != 0xF7)
        {
            throw new ArgumentException("A SysEx message must start with F0 and end with F7.", nameof(data));
        }

        lock (_gate)
        {
            EnsureOpenLocked(preferredDeviceName?.Trim() ?? string.Empty);
            try
            {
                _backend.SendSysEx(data);
            }
            catch (Exception exception)
            {
                var device = _activeDevice;
                CloseLocked();
                throw new InvalidOperationException(
                    $"Windows could not send the SysEx message to {device}: {exception.Message}", exception);
            }
        }
    }

    private void EnsureOpenLocked(string deviceName)
    {
        if (_isOpen && deviceName.Equals(_activeDevice, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CloseLocked();
        int? id = null;
        var displayName = "Windows default MIDI output";
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var selected = _backend.EnumerateDevices().FirstOrDefault(device =>
                device.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                throw new InvalidOperationException(
                    $"The selected MIDI output '{deviceName}' is not connected.");
            }

            id = selected.DeviceId;
            displayName = selected.Name;
        }

        try
        {
            _backend.Open(id);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Could not open {displayName}: {exception.Message}", exception);
        }

        _isOpen = true;
        // An empty active name denotes the mapper and is distinct from any named device.
        _activeDevice = deviceName;
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_isOpen)
            {
                _backend.Reset();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CloseLocked();
            _backend.Dispose();
        }
    }

    private void CloseLocked()
    {
        if (!_isOpen)
        {
            return;
        }

        _backend.Close();
        _isOpen = false;
        _activeDevice = string.Empty;
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
}
