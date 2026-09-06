using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class WinMmMidiInputProviderTests
{
    [Theory]
    [InlineData(0x00643C90u, IncomingMidiMessageKind.NoteOn, 1, 60, 100)]
    [InlineData(0x00003C90u, IncomingMidiMessageKind.NoteOff, 1, 60, 0)]
    [InlineData(0x00203C82u, IncomingMidiMessageKind.NoteOff, 3, 60, 32)]
    [InlineData(0x007F10B9u, IncomingMidiMessageKind.ControlChange, 10, 16, 127)]
    [InlineData(0x000005C1u, IncomingMidiMessageKind.ProgramChange, 2, 5, 0)]
    public void Decode_normalizes_supported_short_messages(
        uint packed,
        IncomingMidiMessageKind kind,
        int channel,
        int data1,
        int data2)
    {
        var message = IncomingMidiShortMessage.Decode(packed);

        Assert.Equal(kind, message.Kind);
        Assert.Equal(channel, message.Channel);
        Assert.Equal(data1, message.Data1);
        Assert.Equal(data2, message.Data2);
    }

    [Fact]
    public async Task Enumeration_sanitizes_identity_and_marks_duplicate_models_ambiguous()
    {
        using var backend = new FakeMidiBackend(
            new(0, "APC MINI", 0x1234, 0x5678, 1),
            new(1, "APC MINI", 0x1234, 0x5678, 1));
        await using var provider = new WinMmMidiInputProvider(backend);

        await provider.StartAsync();
        var devices = provider.EnumerateControllers();

        Assert.Equal(2, devices.Count);
        Assert.All(devices, device => Assert.True(device.IsAmbiguous));
        Assert.All(devices, device => Assert.StartsWith("MIDI: APC MINI", device.DisplayName));
        Assert.All(devices, device => Assert.StartsWith("winmm-midi:sha256-", device.PersistentId));
        Assert.NotEqual(devices[0].PersistentId, devices[1].PersistentId);
    }

    [Fact]
    public async Task Notes_follow_identification_then_confirmed_press_release_routing()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
        var timestamp = 100L;
        await using var provider = new WinMmMidiInputProvider(backend, () => timestamp++);
        var identification = new List<MidiInput>();
        var mapped = new List<MidiInput>();
        provider.IdentificationInputReceived += (_, args) => identification.Add(args.Input);
        provider.InputReceived += (_, args) => mapped.Add(args.Input);

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());
        Assert.True(provider.SetCaptureTarget(device.DeviceId));

        backend.Publish(Pack(0x90, 36, 127));
        backend.Publish(Pack(0x80, 36, 0));

        Assert.Collection(
            identification,
            item => Assert.Equal(ControlSignalKind.Press, item.Signal.Kind),
            item => Assert.Equal(ControlSignalKind.Release, item.Signal.Kind));
        Assert.True(provider.IsCaptureTargetNeutral);
        Assert.True(provider.SetConfirmedPersistentId(device.PersistentId));

        backend.Publish(Pack(0x90, 37, 64));
        backend.Publish(Pack(0x90, 37, 0));

        Assert.Collection(
            mapped,
            item =>
            {
                Assert.Equal("winmm-midi:channel-1:note-37", item.ControlId.Value);
                Assert.Equal(ControlSignalKind.Press, item.Signal.Kind);
                Assert.Equal(64, item.Value);
            },
            item => Assert.Equal(ControlSignalKind.Release, item.Signal.Kind));
    }

    [Fact]
    public async Task Control_changes_publish_directional_pulses_without_stuck_state()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend);
        var inputs = new List<MidiInput>();
        provider.IdentificationInputReceived += (_, args) => inputs.Add(args.Input);

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());
        Assert.True(provider.SetCaptureTarget(device.DeviceId));

        backend.Publish(Pack(0xB0, 48, 80));
        backend.Publish(Pack(0xB0, 48, 72));

        Assert.Equal(4, inputs.Count);
        Assert.Equal("winmm-midi:channel-1:cc-48:increase", inputs[0].ControlId.Value);
        Assert.Equal(ControlSignalKind.Press, inputs[0].Signal.Kind);
        Assert.Equal(ControlSignalKind.Release, inputs[1].Signal.Kind);
        Assert.Equal("winmm-midi:channel-1:cc-48:decrease", inputs[2].ControlId.Value);
        Assert.Equal(ControlSignalKind.Press, inputs[2].Signal.Kind);
        Assert.Equal(ControlSignalKind.Release, inputs[3].Signal.Kind);
        Assert.True(provider.IsCaptureTargetNeutral);
    }

    [Fact]
    public async Task Port_open_failure_is_fail_closed_for_mapping_capture()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "Busy MIDI port", 1, 2, 3))
        {
            OpenException = new InvalidOperationException("already allocated"),
        };
        await using var provider = new WinMmMidiInputProvider(backend);
        Exception? fault = null;
        provider.Faulted += (_, exception) => fault = exception;

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());

        Assert.False(provider.SetCaptureTarget(device.DeviceId));
        Assert.NotNull(fault);
        Assert.False(provider.IsCaptureConfirmed);
    }

    private static uint Pack(byte status, byte data1, byte data2) =>
        status | ((uint)data1 << 8) | ((uint)data2 << 16);

    private sealed class FakeMidiBackend(params WinMmMidiInputDevice[] devices) : IWinMmMidiInputBackend
    {
        private readonly IReadOnlyList<WinMmMidiInputDevice> _devices = devices;

        public event Action<uint>? ShortMessageReceived;

        public Exception? OpenException { get; init; }

        public bool IsOpen { get; private set; }

        public IReadOnlyList<WinMmMidiInputDevice> EnumerateDevices() => _devices;

        public void Open(int deviceId)
        {
            if (OpenException is not null)
            {
                throw OpenException;
            }

            Assert.Contains(_devices, device => device.DeviceId == deviceId);
            IsOpen = true;
        }

        public void Start() => Assert.True(IsOpen);

        public void Stop()
        {
        }

        public void Close() => IsOpen = false;

        public void Publish(uint message)
        {
            Assert.True(IsOpen);
            ShortMessageReceived?.Invoke(message);
        }

        public void Dispose() => IsOpen = false;
    }
}
