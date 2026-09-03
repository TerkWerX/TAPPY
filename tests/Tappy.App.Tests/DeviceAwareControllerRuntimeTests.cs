using Tappy.App.Runtime;
using Tappy.Core.Output;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed class DeviceAwareControllerRuntimeTests
{
    [Fact]
    public async Task Identify_confirm_map_and_unplug_releases_held_output()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)42, @"\\?\HID#VID_1234&PID_5678#PORT_A");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            long timestamp = 0;
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                timestampProvider: () => ++timestamp,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            var controlUpdates = new List<RuntimeControlUpdate>();
            runtime.StateChanged += (_, state) => latestState = state;
            runtime.ControlChanged += (_, update) => controlUpdates.Add(update);

            await runtime.InitializeAsync();
            Assert.Single(runtime.Devices);
            Assert.True(runtime.BeginIdentification(runtime.Devices[0]).Succeeded);

            host.Emit(Packet((nint)42, 0x4F, 0x61, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)42, 0x4F, 0x61, RawKeyboardFlags.Break));
            Assert.True(latestState?.CanConfirm);
            Assert.True(runtime.ConfirmController().Succeeded);

            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)42, 0x4F, 0x61, RawKeyboardFlags.Make));

            Assert.Single(output.Down);
            Assert.Empty(output.Up);
            Assert.Equal("F24", output.Down[0].Keys.Single().Value);

            enumerator.Remove((nint)42);
            host.EmitDevice((nint)42, RawInputDeviceChangeKind.Removal);

            Assert.Single(output.Up);
            Assert.Equal("F24", output.Up[0].Keys.Single().Value);
            Assert.Contains(controlUpdates, update => update.ControlId == controlId && !update.IsPressed);
            Assert.Contains("disconnected", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.False(latestState?.IsConfirmed);
            Assert.True(runtime.IsRehearsal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rehearsal_runs_recognition_without_calling_output()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)51, @"\\?\HID#VID_1000&PID_2000#PORT_B");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            long timestamp = 10;
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                timestampProvider: () => ++timestamp,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeControlUpdate? latestControl = null;
            runtime.ControlChanged += (_, update) => latestControl = update;

            await runtime.InitializeAsync();
            runtime.BeginIdentification(runtime.Devices[0]);
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Break));
            runtime.ConfirmController();
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x50).Value;
            runtime.AssignMapping(controlId, "F23");

            Assert.True(runtime.IsRehearsal);
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Break));

            Assert.Empty(output.Down);
            Assert.Empty(output.Up);
            Assert.NotNull(latestControl);
            Assert.Equal(controlId, latestControl.ControlId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Identification_requires_a_complete_press_release_and_neutral_state_before_confirmation()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)52, @"\\?\HID#VID_1000&PID_2000#NEUTRAL_GATE");
            var host = new FakeMessageHost();
            var output = new RecordingOutput();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            var controlUpdates = new List<RuntimeControlUpdate>();
            runtime.StateChanged += (_, state) => latestState = state;
            runtime.ControlChanged += (_, update) => controlUpdates.Add(update);

            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.Emit(Packet((nint)52, 0x4F, 0x61, RawKeyboardFlags.Make));

            Assert.False(latestState?.CanConfirm ?? true);
            Assert.False(runtime.ConfirmController().Succeeded);
            Assert.Empty(output.Down);
            Assert.Empty(controlUpdates);

            host.Emit(Packet((nint)52, 0x4F, 0x61, RawKeyboardFlags.Break));
            Assert.True(latestState?.CanConfirm);
            Assert.True(runtime.ConfirmController().Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Identification_refuses_to_arm_until_windows_reports_all_keyboard_controls_released()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)53, @"\\?\HID#VID_1000&PID_2000#PRE_ARM_NEUTRAL");
            var host = new FakeMessageHost();
            var keyboardIsNeutral = false;
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: () => keyboardIsNeutral);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, new RecordingOutput(), new AtomicProfileStore(root));

            await runtime.InitializeAsync();

            var rejected = runtime.BeginIdentification(runtime.Devices.Single());
            Assert.False(rejected.Succeeded);
            Assert.Contains("Release every keyboard", rejected.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(provider.CaptureTarget);

            keyboardIsNeutral = true;
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            Assert.Equal(descriptor.SessionHandle, provider.CaptureTarget);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Emergency_stop_releases_disarms_and_restores_rehearsal()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)61, @"\\?\HID#VID_1000&PID_2000#EMERGENCY");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            var updates = new List<RuntimeControlUpdate>();
            runtime.StateChanged += (_, state) => latestState = state;
            runtime.ControlChanged += (_, update) => updates.Add(update);

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)61, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)61, 0x4F, 0x61, RawKeyboardFlags.Make));
            Assert.Single(output.Down);

            runtime.EmergencyStop("test");

            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.False(latestState?.IsIdentificationCaptureActive ?? true);
            Assert.Contains(updates, update => update.ControlId == controlId && !update.IsPressed);

            host.Emit(Packet((nint)61, 0x4F, 0x61, RawKeyboardFlags.Break));
            host.Emit(Packet((nint)61, 0x4F, 0x61, RawKeyboardFlags.Make));
            Assert.Single(output.Down);
            Assert.Single(output.Up);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Session_lock_releases_held_output_and_requires_reidentification()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)62, @"\\?\HID#VID_1000&PID_2000#LOCK");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)62, 0x50, 0x62);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x50).Value;
            runtime.AssignMapping(controlId, "F23");
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)62, 0x50, 0x62, RawKeyboardFlags.Make));

            host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);

            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.Contains("Nothing is armed", latestState?.Status ?? string.Empty, StringComparison.Ordinal);
            host.Emit(Packet((nint)62, 0x50, 0x62, RawKeyboardFlags.Make));
            Assert.Single(output.Down);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Wpf_application_shutdown_relay_reaches_the_same_held_output_cleanup()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)621, @"\\?\HID#VID_1000&PID_2000#APP_SHUTDOWN");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            var applicationLifecycle = new ApplicationLifecycleSignalSource();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root), applicationLifecycle);
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)621, 0x50, 0x62);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x50).Value;
            runtime.AssignMapping(controlId, "F23");
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)621, 0x50, 0x62, RawKeyboardFlags.Make));

            applicationLifecycle.Report(WindowsLifecycleSignal.ShutdownRequested);

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Manual_refresh_recovers_safely_when_a_removal_notification_was_missed()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)63, @"\\?\HID#VID_1000&PID_2000#MISSED_REMOVAL");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)63, 0x51, 0x63);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x51).Value;
            runtime.AssignMapping(controlId, "F22");
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)63, 0x51, 0x63, RawKeyboardFlags.Make));
            enumerator.Remove((nint)63);

            runtime.RefreshDevices();

            Assert.Empty(runtime.Devices);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.Contains("disappeared", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Removing_an_identification_candidate_cancels_capture_without_arming()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)64, @"\\?\HID#VID_1000&PID_2000#CANDIDATE");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                keyboardIsNeutral: static () => true);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, new RecordingOutput(), new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices[0]).Succeeded);
            Assert.True(latestState?.IsIdentificationCaptureActive);
            enumerator.Remove((nint)64);
            host.EmitDevice((nint)64, RawInputDeviceChangeKind.Removal);

            Assert.Empty(runtime.Devices);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.False(latestState?.IsIdentificationCaptureActive ?? true);
            Assert.False(runtime.ConfirmController().Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_profile_reloads_identity_layout_layers_and_mapping_for_a_new_session_handle()
    {
        var root = NewTemporaryDirectory();
        const string path = @"\\?\HID#VID_1234&PID_5678#STABLE_PORT";
        try
        {
            var firstDescriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)71, path);
            var firstHost = new FakeMessageHost();
            var firstProvider = new RawInputKeyboardProvider(
                new FakeEnumerator(firstDescriptor),
                firstHost,
                keyboardIsNeutral: static () => true);
            await using (var firstRuntime = new DeviceAwareControllerRuntime(
                             firstProvider, new RecordingOutput(), new AtomicProfileStore(root)))
            {
                await firstRuntime.InitializeAsync();
                IdentifyAndConfirm(firstRuntime, firstHost, (nint)71, 0x52, 0x60);
                var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x52).Value;
                firstHost.Emit(Packet((nint)71, 0x52, 0x60, RawKeyboardFlags.Make));
                firstHost.Emit(Packet((nint)71, 0x52, 0x60, RawKeyboardFlags.Break));
                Assert.True(firstRuntime.AssignMapping(controlId, "F21").Succeeded);
                Assert.True((await firstRuntime.SaveProfileAsync()).Succeeded);
            }

            var saved = await new AtomicProfileStore(root).LoadAsync("default");
            var savedController = saved.FindController(firstDescriptor.PersistentId);
            var savedControlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x52);
            Assert.NotNull(savedController);
            Assert.Equal(3, savedController.Layers.Count);
            Assert.Contains(savedController.Layout.Rows.SelectMany(row => row.Controls),
                control => control.ControlId == savedControlId);
            Assert.Equal("F21", savedController.Layers[0].FindBinding(savedControlId)?.PressAction.Keys.Single().Value);

            var secondDescriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)72, path);
            var secondHost = new FakeMessageHost();
            var secondOutput = new RecordingOutput();
            var secondProvider = new RawInputKeyboardProvider(
                new FakeEnumerator(secondDescriptor),
                secondHost,
                keyboardIsNeutral: static () => true);
            await using var secondRuntime = new DeviceAwareControllerRuntime(
                secondProvider, secondOutput, new AtomicProfileStore(root));
            var restoredControls = new List<RuntimeControlUpdate>();
            secondRuntime.ControlChanged += (_, update) => restoredControls.Add(update);
            await secondRuntime.InitializeAsync();
            IdentifyAndConfirm(secondRuntime, secondHost, (nint)72, 0x52, 0x60);
            Assert.Contains(restoredControls, update =>
                update.ControlId == savedControlId.Value &&
                update.DisplayLabel.Contains("Numpad 0", StringComparison.Ordinal) &&
                update.AssignedAction == "Hold F21 until release" &&
                !update.IsPressed);
            secondRuntime.IsRehearsal = false;

            secondHost.Emit(Packet((nint)72, 0x52, 0x60, RawKeyboardFlags.Make));
            secondHost.Emit(Packet((nint)72, 0x52, 0x60, RawKeyboardFlags.Break));

            Assert.Single(secondOutput.Down);
            Assert.Single(secondOutput.Up);
            Assert.Equal("F21", secondOutput.Down[0].Keys.Single().Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Self_marked_input_is_invisible_to_mapping_and_ui()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)73, @"\\?\HID#VID_1000&PID_2000#SELF_MARKER");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            var updates = new List<RuntimeControlUpdate>();
            runtime.ControlChanged += (_, update) => updates.Add(update);

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)73, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            runtime.AssignMapping(controlId, "F24");
            runtime.IsRehearsal = false;
            updates.Clear();

            host.Emit(Packet(
                (nint)73, 0x4F, 0x61, RawKeyboardFlags.Make,
                Tappy.Windows.Output.InjectedInputMarker.Value));

            Assert.Empty(output.Down);
            Assert.Empty(output.Up);
            Assert.Empty(updates);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("emergency")]
    [InlineData("lifecycle")]
    [InlineData("unplug")]
    [InlineData("refresh")]
    public async Task Rejected_cleanup_release_is_reported_and_prevents_rearming(string cleanupPath)
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)81, @"\\?\HID#VID_1000&PID_2000#REJECTED_RELEASE");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                keyboardIsNeutral: static () => true);
            var output = new RejectingReleaseOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)81, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)81, 0x4F, 0x61, RawKeyboardFlags.Make));
            output.RejectKeyUp = true;

            RuntimeOperation? emergencyResult = null;
            switch (cleanupPath)
            {
                case "emergency":
                    emergencyResult = runtime.EmergencyStop("test rejection");
                    break;
                case "lifecycle":
                    host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);
                    break;
                case "unplug":
                    enumerator.Remove((nint)81);
                    host.EmitDevice((nint)81, RawInputDeviceChangeKind.Removal);
                    break;
                case "refresh":
                    enumerator.Remove((nint)81);
                    runtime.RefreshDevices();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown cleanup path: {cleanupPath}");
            }

            Assert.Equal(1, output.KeyUpAttempts);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.Contains("cannot confirm", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restart Tappy", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Effective: Needs attention (fail-open)", latestState?.EffectiveSourceLabel);
            runtime.IsRehearsal = false;
            Assert.True(runtime.IsRehearsal);
            if (emergencyResult is not null)
            {
                Assert.False(emergencyResult.Succeeded);
                Assert.Equal(latestState?.Status, emergencyResult.Message);
            }

            if (runtime.Devices.Count > 0)
            {
                var rearm = runtime.BeginIdentification(runtime.Devices[0]);
                Assert.False(rearm.Succeeded);
                Assert.Contains("Restart Tappy", rearm.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void IdentifyAndConfirm(
        DeviceAwareControllerRuntime runtime,
        FakeMessageHost host,
        nint handle,
        ushort scan,
        ushort virtualKey)
    {
        Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
        host.Emit(Packet(handle, scan, virtualKey, RawKeyboardFlags.Make));
        host.Emit(Packet(handle, scan, virtualKey, RawKeyboardFlags.Break));
        Assert.True(runtime.ConfirmController().Succeeded);
    }

    private static RawKeyboardPacket Packet(
        nint handle,
        ushort scan,
        ushort virtualKey,
        RawKeyboardFlags flags,
        uint extraInformation = 0) =>
        new(handle, scan, flags, 0, virtualKey,
            flags.HasFlag(RawKeyboardFlags.Break) ? 0x101u : 0x100u,
            extraInformation);

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Tappy-AppTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeEnumerator(params SanitizedDeviceDescriptor[] devices) : IRawInputDeviceEnumerator
    {
        private readonly Dictionary<nint, SanitizedDeviceDescriptor> _devices =
            devices.ToDictionary(device => device.SessionHandle);

        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards() => _devices.Values.ToArray();

        public SanitizedDeviceDescriptor? DescribeKeyboard(nint deviceHandle) =>
            _devices.GetValueOrDefault(deviceHandle);

        public void Remove(nint handle) => _devices.Remove(handle);
    }

    private sealed class FakeMessageHost : IRawInputMessageHost
    {
        public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;
        public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;
        public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;
        public event EventHandler<Exception>? Faulted;
        public bool IsRunning { get; private set; }
        public nint WindowHandle => (nint)99;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Emit(RawKeyboardPacket packet) =>
            KeyboardPacketReceived?.Invoke(this, new RawKeyboardPacketEventArgs(packet));

        public void EmitDevice(nint handle, RawInputDeviceChangeKind kind) =>
            DeviceChanged?.Invoke(this, new NativeDeviceChangeEventArgs(handle, kind));

        public void EmitLifecycle(WindowsLifecycleSignal signal) =>
            LifecycleChanged?.Invoke(this, new WindowsLifecycleSignalEventArgs(signal));

        public void EmitFault(Exception exception) => Faulted?.Invoke(this, exception);

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingOutput : IKeyboardOutput
    {
        public List<KeyboardOutputRequest> Down { get; } = [];
        public List<KeyboardOutputRequest> Up { get; } = [];

        public void KeyDown(KeyboardOutputRequest request) => Down.Add(request);
        public void KeyUp(KeyboardOutputRequest request) => Up.Add(request);
    }

    private sealed class RejectingReleaseOutput : IKeyboardOutput
    {
        public bool RejectKeyUp { get; set; }

        public int KeyUpAttempts { get; private set; }

        public void KeyDown(KeyboardOutputRequest request)
        {
        }

        public void KeyUp(KeyboardOutputRequest request)
        {
            KeyUpAttempts++;
            if (RejectKeyUp)
            {
                throw new InvalidOperationException("Simulated Windows output rejection.");
            }
        }
    }
}
