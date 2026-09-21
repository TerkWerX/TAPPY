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
        // No container-id resolver is injected on purpose elsewhere in this file,
        // but that leaves the real default resolver in play, which queries
        // whatever MIDI hardware is actually attached at these device IDs on the
        // machine running the test. Force it to stay unresolved here so this
        // test's "no resolver available" scenario doesn't depend on ambient
        // hardware.
        await using var provider = new WinMmMidiInputProvider(
            backend,
            containerIdResolver: new FakeContainerIdResolver(_ => null),
            deviceInterfacePathResolver: _ => null);

        await provider.StartAsync();
        var devices = provider.EnumerateControllers();

        Assert.Equal(2, devices.Count);
        Assert.All(devices, device => Assert.True(device.IsAmbiguous));
        Assert.All(devices, device => Assert.StartsWith("MIDI: APC MINI", device.DisplayName));
        Assert.All(devices, device => Assert.StartsWith("winmm-midi:sha256-", device.PersistentId));
        Assert.NotEqual(devices[0].PersistentId, devices[1].PersistentId);
    }

    [Fact]
    public async Task Enumeration_resolves_a_stable_container_identity_when_available()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1));
        var containerId = Guid.NewGuid();
        await using var provider = new WinMmMidiInputProvider(
            backend,
            containerIdResolver: new FakeContainerIdResolver(path => path == "fake-path-0" ? containerId : null),
            deviceInterfacePathResolver: deviceId => deviceId == 0 ? "fake-path-0" : null);

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());

        Assert.False(device.IsAmbiguous);
        Assert.StartsWith("winmm-midi:vid-09E8:pid-0043:container-sha256-", device.PersistentId);
    }

    [Fact]
    public async Task Enumeration_falls_back_to_the_ordinal_identity_when_no_interface_path_is_found()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1));
        await using var provider = new WinMmMidiInputProvider(
            backend,
            containerIdResolver: new FakeContainerIdResolver(_ => null),
            deviceInterfacePathResolver: _ => null);

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());

        Assert.True(device.IsAmbiguous);
        Assert.StartsWith("winmm-midi:sha256-", device.PersistentId);
    }

    [Fact]
    public async Task Enumeration_stays_ambiguous_when_the_container_resolver_throws()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1));
        await using var provider = new WinMmMidiInputProvider(
            backend,
            containerIdResolver: new FakeContainerIdResolver(_ => throw new InvalidOperationException("boom")),
            deviceInterfacePathResolver: _ => "fake-path");

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());

        Assert.True(device.IsAmbiguous);
        Assert.StartsWith("winmm-midi:sha256-", device.PersistentId);
    }

    [Fact]
    public async Task Two_identical_units_get_different_container_based_identities_when_resolved()
    {
        using var backend = new FakeMidiBackend(
            new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1),
            new WinMmMidiInputDevice(1, "FL STUDIO FIRE", 0x09E8, 0x0043, 1));
        var containerIds = new Dictionary<string, Guid> { ["path-0"] = Guid.NewGuid(), ["path-1"] = Guid.NewGuid() };
        await using var provider = new WinMmMidiInputProvider(
            backend,
            containerIdResolver: new FakeContainerIdResolver(path => containerIds.GetValueOrDefault(path)),
            deviceInterfacePathResolver: deviceId => $"path-{deviceId}");

        await provider.StartAsync();
        var devices = provider.EnumerateControllers();

        Assert.Equal(2, devices.Count);
        Assert.All(devices, device => Assert.False(device.IsAmbiguous));
        Assert.NotEqual(devices[0].PersistentId, devices[1].PersistentId);
    }

    [Fact]
    public async Task Repeated_enumeration_resolves_the_container_identity_only_once_per_device()
    {
        // EnumerateControllers runs on every poll tick across every device tab.
        // Re-resolving a real Windows device enumeration on every single tick
        // made a tab with a MIDI device connected unusable — see D-026.
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1));
        var pathResolutions = 0;
        var containerResolutions = 0;
        await using var provider = new WinMmMidiInputProvider(
            backend,
            containerIdResolver: new FakeContainerIdResolver(_ => { containerResolutions++; return Guid.NewGuid(); }),
            deviceInterfacePathResolver: _ => { pathResolutions++; return "fake-path"; });

        await provider.StartAsync();
        for (var i = 0; i < 10; i++)
        {
            provider.EnumerateControllers();
        }

        Assert.Equal(1, pathResolutions);
        Assert.Equal(1, containerResolutions);
    }

    [Fact]
    public async Task A_device_that_actually_changes_identity_is_resolved_again()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1));
        var resolutions = 0;
        await using var provider = new WinMmMidiInputProvider(
            backend,
            containerIdResolver: new FakeContainerIdResolver(_ => { resolutions++; return Guid.NewGuid(); }),
            deviceInterfacePathResolver: _ => "fake-path");

        await provider.StartAsync();
        provider.EnumerateControllers();
        Assert.Equal(1, resolutions);

        // A different physical device now occupies the same WinMM device ID
        // (e.g. after a USB topology change) - must not reuse the stale entry.
        backend.ReplaceDevices([new WinMmMidiInputDevice(0, "2 - FL STUDIO FIRE", 0x09E8, 0x0043, 1)]);
        provider.EnumerateControllers();

        Assert.Equal(2, resolutions);
    }

    private sealed class FakeContainerIdResolver(Func<string, Guid?> resolve) : IDeviceContainerIdResolver
    {
        public Guid? Resolve(string deviceInterfacePath) => resolve(deviceInterfacePath);
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
    public async Task Relative_control_changes_decode_signed_values_not_absolute_diffs()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "Akai Fire", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend);
        var inputs = new List<MidiInput>();
        provider.IdentificationInputReceived += (_, args) => inputs.Add(args.Input);

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());
        Assert.True(provider.SetCaptureTarget(device.DeviceId));
        provider.SetRelativeControlChanges([16]);

        backend.Publish(Pack(0xB0, 16, 5));   // 5 = five clockwise steps
        backend.Publish(Pack(0xB0, 16, 122)); // 122 - 128 = six counterclockwise steps
        backend.Publish(Pack(0xB0, 16, 0));   // neutral value: no movement, no event
        backend.Publish(Pack(0xB0, 16, 64));  // neutral value: no movement, no event
        backend.Publish(Pack(0xB0, 17, 5));   // CC 17 was never declared relative: absolute diffing applies

        Assert.Equal(6, inputs.Count);
        Assert.Equal("winmm-midi:channel-1:cc-16:increase", inputs[0].ControlId.Value);
        Assert.Equal(MidiInputControlKind.RelativeControlIncrease, inputs[0].ControlKind);
        Assert.Equal(5, inputs[0].Value);
        Assert.Equal(ControlSignalKind.Press, inputs[0].Signal.Kind);
        Assert.Equal(ControlSignalKind.Release, inputs[1].Signal.Kind);
        Assert.Equal("winmm-midi:channel-1:cc-16:decrease", inputs[2].ControlId.Value);
        Assert.Equal(MidiInputControlKind.RelativeControlDecrease, inputs[2].ControlKind);
        Assert.Equal(6, inputs[2].Value);
        Assert.Equal(ControlSignalKind.Press, inputs[2].Signal.Kind);
        Assert.Equal(ControlSignalKind.Release, inputs[3].Signal.Kind);
        Assert.Equal("winmm-midi:channel-1:cc-17:increase", inputs[4].ControlId.Value);
        Assert.Equal(MidiInputControlKind.ControlIncrease, inputs[4].ControlKind);
        Assert.Equal(5, inputs[4].Value); // raw absolute byte for the non-relative path, not a delta
        Assert.True(provider.IsCaptureTargetNeutral);
    }

    [Fact]
    public async Task Relative_control_changes_reset_when_capture_is_cleared()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "Akai Fire", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend);
        var inputs = new List<MidiInput>();
        provider.IdentificationInputReceived += (_, args) => inputs.Add(args.Input);

        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());
        Assert.True(provider.SetCaptureTarget(device.DeviceId));
        provider.SetRelativeControlChanges([16]);
        provider.ClearCaptureTarget();

        Assert.True(provider.SetCaptureTarget(device.DeviceId));
        backend.Publish(Pack(0xB0, 16, 5));

        Assert.Equal("winmm-midi:channel-1:cc-16:increase", inputs[0].ControlId.Value);
        Assert.Equal(MidiInputControlKind.ControlIncrease, inputs[0].ControlKind); // back to the absolute-diff default
        Assert.Equal(5, inputs[0].Value); // raw absolute byte, not a decoded relative delta
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

    [Fact]
    public async Task Periodic_inventory_monitor_detects_MIDI_only_unplug_without_user_refresh()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend, monitorInterval: TimeSpan.FromMilliseconds(20));
        var lost = new TaskCompletionSource<MidiCaptureLostEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.CaptureLost += (_, args) => lost.TrySetResult(args);
        await provider.StartAsync();
        var device = Assert.Single(provider.EnumerateControllers());
        Assert.True(provider.SetCaptureTarget(device.DeviceId));
        Assert.True(provider.SetConfirmedPersistentId(device.PersistentId));
        backend.Publish(Pack(0x90, 60, 100));
        backend.ReplaceDevices([]);

        var notification = await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(device.PersistentId, notification.Descriptor.PersistentId);
        Assert.False(provider.IsCaptureConfirmed);
        Assert.Null(provider.CurrentCaptureGeneration);
        Assert.False(backend.IsOpen);
    }

    [Fact]
    public async Task Topology_change_invalidates_capture_even_when_the_integer_port_survives()
    {
        var first = new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3);
        using var backend = new FakeMidiBackend(first, first with { DeviceId = 1 });
        await using var provider = new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan);
        var losses = new List<MidiCaptureLostEventArgs>();
        provider.CaptureLost += (_, args) => losses.Add(args);
        await provider.StartAsync();
        var device = provider.EnumerateControllers()[0];
        Assert.True(provider.SetCaptureTarget(0));
        Assert.True(provider.SetConfirmedPersistentId(device.PersistentId));
        backend.ReplaceDevices([first]);
        _ = provider.EnumerateControllers();
        _ = provider.EnumerateControllers();

        Assert.Single(losses);
        Assert.False(provider.IsCaptureConfirmed);
        Assert.False(backend.IsOpen);
    }

    [Fact]
    public async Task A_buffered_message_from_a_closed_native_session_cannot_enter_the_new_capture()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan);
        var inputs = new List<MidiInput>();
        provider.IdentificationInputReceived += (_, args) => inputs.Add(args.Input);
        await provider.StartAsync();
        Assert.True(provider.SetCaptureTarget(0));
        var generation = backend.SessionGeneration;
        var oldCapture = provider.CurrentCaptureGeneration!.Value;
        provider.ClearCaptureTarget();
        Assert.True(provider.SetCaptureTarget(0));
        backend.Publish(new WinMmMidiInputMessage(generation, Pack(0x90, 60, 100)));
        Assert.Empty(inputs);
        Assert.False(provider.IsCurrentCapture(oldCapture));
        backend.Publish(Pack(0x90, 61, 100));
        Assert.True(provider.IsCurrentCapture(Assert.Single(inputs).CaptureGeneration));
    }

    [Fact]
    public async Task Backend_failure_invalidates_capture_and_is_reported()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan);
        Exception? failure = null;
        provider.Faulted += (_, exception) => failure = exception;
        await provider.StartAsync();
        Assert.True(provider.SetCaptureTarget(0));
        var expected = new IOException("MIDI input queue overflowed");
        backend.Fail(expected);
        Assert.Same(expected, failure);
        Assert.Null(provider.CurrentCaptureGeneration);
        Assert.False(backend.IsOpen);
    }

    [Fact]
    public async Task Late_backend_failure_from_a_previous_open_cannot_close_the_current_capture()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan);
        var failures = 0;
        provider.Faulted += (_, _) => failures++;
        await provider.StartAsync();
        Assert.True(provider.SetCaptureTarget(0));
        var oldGeneration = backend.SessionGeneration;
        provider.ClearCaptureTarget();
        Assert.True(provider.SetCaptureTarget(0));
        var currentCapture = provider.CurrentCaptureGeneration;

        backend.Fail(new WinMmMidiInputSessionException(oldGeneration, new IOException("old stream failure")));

        Assert.Equal(currentCapture, provider.CurrentCaptureGeneration);
        Assert.True(backend.IsOpen);
        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task Inventory_failure_reports_once_and_prevents_capture_without_a_working_monitor()
    {
        using var backend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
        await using var provider = new WinMmMidiInputProvider(backend, monitorInterval: TimeSpan.FromMilliseconds(20));
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        provider.Faulted += (_, exception) =>
        {
            Interlocked.Increment(ref count);
            failed.TrySetResult(exception);
        };
        await provider.StartAsync();
        Assert.True(provider.SetCaptureTarget(0));
        var expected = new IOException("MIDI inventory unavailable");
        backend.EnumerationFailure = expected;

        Assert.Same(expected, await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(provider.SetCaptureTarget(0));
        Assert.Null(provider.CurrentCaptureGeneration);
        await provider.StopAsync();
        Assert.Equal(1, count);
    }

    private sealed class FakeMidiBackend(params WinMmMidiInputDevice[] devices) : IWinMmMidiInputBackend
    {
        private IReadOnlyList<WinMmMidiInputDevice> _devices = devices;
        private Exception? _enumerationFailure;

        public event Action<WinMmMidiInputMessage>? ShortMessageReceived;
        public event EventHandler<Exception>? Faulted;
        public long SessionGeneration { get; private set; }

        public Exception? OpenException { get; init; }
        public Exception? EnumerationFailure
        {
            get => Volatile.Read(ref _enumerationFailure);
            set => Volatile.Write(ref _enumerationFailure, value);
        }

        public bool IsOpen { get; private set; }

        public IReadOnlyList<WinMmMidiInputDevice> EnumerateDevices() =>
            EnumerationFailure is { } exception ? throw exception : Volatile.Read(ref _devices);
        public void ReplaceDevices(IReadOnlyList<WinMmMidiInputDevice> devices) => Volatile.Write(ref _devices, devices);

        public void Open(int deviceId)
        {
            if (OpenException is not null)
            {
                throw OpenException;
            }

            Assert.Contains(_devices, device => device.DeviceId == deviceId);
            SessionGeneration++;
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
            ShortMessageReceived?.Invoke(new WinMmMidiInputMessage(SessionGeneration, message));
        }

        public void Publish(WinMmMidiInputMessage message) => ShortMessageReceived?.Invoke(message);
        public void Fail(Exception exception) => Faulted?.Invoke(this, exception);

        public void Dispose() => IsOpen = false;
    }
}
