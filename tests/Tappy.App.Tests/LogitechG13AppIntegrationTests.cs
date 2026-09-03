using Tappy.App.Runtime;
using Tappy.Core.Input;
using Tappy.Core.Output;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed class LogitechG13AppIntegrationTests
{
    [Fact]
    public async Task Provider_specific_selection_keeps_C232_keyboard_distinct_and_ignores_it_during_G13_identification()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var keyboard = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)201,
                @"\\?\HID#VID_046D&PID_C232#VIRTUAL_KEYBOARD");
            var g13 = G13Descriptor((nint)301, "g13-session-a", "g13-persistent-a");
            var host = new FakeDualMessageHost();
            var keyboardProvider = new RawInputKeyboardProvider(
                new FakeKeyboardEnumerator(keyboard),
                host,
                keyboardIsNeutral: static () => true);
            var g13Provider = new LogitechG13InputProvider(new FakeG13Enumerator(g13), host);
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboardProvider, g13Provider, new RecordingOutput(), new AtomicProfileStore(root));

            await runtime.InitializeAsync();

            Assert.Equal(2, runtime.Devices.Count);
            var c232 = Assert.Single(runtime.Devices, item => item.ProviderId == "raw-input");
            Assert.Contains("C232", c232.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("G13", c232.DisplayName, StringComparison.OrdinalIgnoreCase);
            var g13Choice = Assert.Single(runtime.Devices, item => item.ProviderId == "raw-hid-g13");
            Assert.Equal("Logitech G13", g13Choice.DisplayName);

            Assert.True(runtime.BeginIdentification(g13Choice).Succeeded);
            host.EmitKeyboard(KeyboardPacket((nint)201, RawKeyboardFlags.Make));
            host.EmitKeyboard(KeyboardPacket((nint)201, RawKeyboardFlags.Break));
            Assert.False(runtime.ConfirmController().Succeeded);

            host.EmitHid(G13Packet((nint)301, 1));
            Assert.False(runtime.ConfirmController().Succeeded);
            host.EmitHid(G13Packet((nint)301, 0));
            Assert.True(runtime.ConfirmController().Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Optional_G13_registration_failure_keeps_keyboard_runtime_healthy_and_hides_inert_G13_choice()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var keyboard = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)211,
                @"\\?\HID#VID_05A4&PID_9862#SPARE_NUMPAD");
            var g13 = G13Descriptor((nint)311, "g13-session-unavailable", "g13-persistent-unavailable");
            var host = new FakeDualMessageHost
            {
                StartFault = new RawInputCapabilityException(
                    RawInputCapability.LogitechG13,
                    "G13 registration unavailable.")
            };
            var keyboardProvider = new RawInputKeyboardProvider(
                new FakeKeyboardEnumerator(keyboard),
                host,
                keyboardIsNeutral: static () => true);
            var g13Provider = new LogitechG13InputProvider(new FakeG13Enumerator(g13), host);
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboardProvider, g13Provider, new RecordingOutput(), new AtomicProfileStore(root));
            RuntimeState? state = null;
            runtime.StateChanged += (_, value) => state = value;

            await runtime.InitializeAsync();

            var choice = Assert.Single(runtime.Devices);
            Assert.Equal("raw-input", choice.ProviderId);
            Assert.False(g13Provider.IsAvailable);
            Assert.True(runtime.IsOutputStateConfirmedSafe);
            Assert.Contains("G13 Raw Input is unavailable", state?.Status ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Raw Input is unavailable. Nothing is armed", state?.IdentificationStatus ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(runtime.BeginIdentification(choice).Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Confirmed_G13_projects_the_code_rendered_39_control_layout_and_simultaneous_state()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var g13 = G13Descriptor((nint)302, "g13-session-layout", "g13-persistent-layout");
            var host = new FakeDualMessageHost();
            await using var runtime = CreateRuntime(root, host, g13, out var output);
            var updates = new List<RuntimeControlUpdate>();
            runtime.ControlChanged += (_, update) => updates.Add(update);

            await runtime.InitializeAsync();
            IdentifyAndConfirmG13(runtime, host, (nint)302);

            var snapshots = updates.Where(update => update.IsSnapshot).ToArray();
            Assert.Equal(39, snapshots.Length);
            Assert.Equal(39, snapshots.Select(update => update.ControlId).Distinct(StringComparer.Ordinal).Count());
            Assert.All(LogitechG13InputProvider.SupportedControls, definition =>
                Assert.Contains(snapshots, update =>
                    update.ControlId == definition.ControlId.Value &&
                    update.DisplayLabel == definition.DisplayName));

            updates.Clear();
            host.EmitHid(G13Packet((nint)302, (1UL << 0) | (1UL << 1)));
            Assert.Contains(updates, update => update.DisplayLabel == "G1" && update.IsPressed);
            Assert.Contains(updates, update =>
                update.DisplayLabel == "G2" && update.IsPressed && update.SimultaneousCount == 2);
            host.EmitHid(G13Packet((nint)302, 0));
            Assert.Equal(2, updates.Count(update => !update.IsSnapshot && !update.IsPressed));

            updates.Clear();
            host.EmitHid(G13Packet((nint)302, 0, x: 0));
            host.EmitHid(G13Packet((nint)302, 0));
            Assert.Contains(updates, update => update.DisplayLabel == "Stick left" && update.IsPressed);
            Assert.Contains(updates, update => update.DisplayLabel == "Stick left" && !update.IsPressed);

            var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
            Assert.True(runtime.AssignMapping(g1.ControlId.Value, "F24").Succeeded);
            EmitG13Cycle(host, (nint)302, 0);
            Assert.Empty(output.Down);
            Assert.Empty(output.Up);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task G13_mapping_profile_and_all_39_layout_controls_round_trip_to_a_new_session()
    {
        var root = NewTemporaryDirectory();
        const string persistentId = "raw-hid-g13:stable-test-identity";
        var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
        try
        {
            var firstHost = new FakeDualMessageHost();
            var first = G13Descriptor((nint)303, "g13-session-first", persistentId);
            await using (var runtime = CreateRuntime(root, firstHost, first, out _))
            {
                await runtime.InitializeAsync();
                IdentifyAndConfirmG13(runtime, firstHost, (nint)303);
                Assert.True(runtime.AssignMapping(g1.ControlId.Value, "F13").Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }

            var stored = await new AtomicProfileStore(root).LoadAsync("default");
            var controller = Assert.Single(stored.Controllers);
            Assert.Equal("raw-hid-g13", controller.Identity.ProviderId);
            Assert.Equal(LogitechG13Protocol.UsagePage, controller.Identity.UsagePage);
            Assert.Equal(LogitechG13Protocol.Usage, controller.Identity.Usage);
            Assert.Equal(39, controller.Layout.Rows.SelectMany(row => row.Controls)
                .Count(control => control.ControlId is not null));
            Assert.All(
                controller.Layout.Rows.SelectMany(row => row.Controls).Where(control => control.ControlId is not null),
                control =>
                {
                    Assert.StartsWith("raw-hid-g13:", control.ControlId!.Value.Value, StringComparison.Ordinal);
                    Assert.DoesNotContain(":sc", control.ControlId.Value.Value, StringComparison.Ordinal);
                });
            Assert.Equal("F13", controller.Layers[0].FindBinding(g1.ControlId)?.PressAction.Keys.Single().Value);

            var secondHost = new FakeDualMessageHost();
            var second = G13Descriptor((nint)304, "g13-session-second", persistentId);
            await using var secondRuntime = CreateRuntime(root, secondHost, second, out var output);
            var restored = new List<RuntimeControlUpdate>();
            secondRuntime.ControlChanged += (_, update) => restored.Add(update);
            await secondRuntime.InitializeAsync();
            IdentifyAndConfirmG13(secondRuntime, secondHost, (nint)304);

            Assert.Contains(restored, update =>
                update.IsSnapshot &&
                update.ControlId == g1.ControlId.Value &&
                update.AssignedAction == "Hold F13 until release");
            secondRuntime.IsRehearsal = false;
            EmitG13Cycle(secondHost, (nint)304, 0);
            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.Equal("F13", output.Down[0].Keys.Single().Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task G13_emergency_stop_releases_output_disarms_and_restores_rehearsal()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var g13 = G13Descriptor((nint)305, "g13-session-stop", "g13-persistent-stop");
            var host = new FakeDualMessageHost();
            await using var runtime = CreateRuntime(root, host, g13, out var output);
            RuntimeState? state = null;
            runtime.StateChanged += (_, value) => state = value;
            await runtime.InitializeAsync();
            IdentifyAndConfirmG13(runtime, host, (nint)305);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
            runtime.AssignMapping(g1.ControlId.Value, "F13");
            runtime.IsRehearsal = false;
            host.EmitHid(G13Packet((nint)305, 1));

            runtime.EmergencyStop("test stop");

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(state?.IsConfirmed ?? true);
            host.EmitHid(G13Packet((nint)305, 0));
            EmitG13Cycle(host, (nint)305, 0);
            Assert.Single(output.Down);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Shared_host_lifecycle_signal_cleans_up_G13_output_exactly_once()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var g13 = G13Descriptor((nint)306, "g13-session-life", "g13-persistent-life");
            var host = new FakeDualMessageHost();
            await using var runtime = CreateRuntime(root, host, g13, out var output);
            await runtime.InitializeAsync();
            IdentifyAndConfirmG13(runtime, host, (nint)306);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
            runtime.AssignMapping(g1.ControlId.Value, "F13");
            runtime.IsRehearsal = false;
            host.EmitHid(G13Packet((nint)306, 1));

            host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Shared_host_fault_cleans_up_G13_output_once_and_enters_needs_attention()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var g13 = G13Descriptor((nint)3061, "g13-session-fault", "g13-persistent-fault");
            var host = new FakeDualMessageHost();
            await using var runtime = CreateRuntime(root, host, g13, out var output);
            RuntimeState? state = null;
            runtime.StateChanged += (_, value) => state = value;
            await runtime.InitializeAsync();
            IdentifyAndConfirmG13(runtime, host, (nint)3061);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
            runtime.AssignMapping(g1.ControlId.Value, "F13");
            runtime.IsRehearsal = false;
            host.EmitHid(G13Packet((nint)3061, 1));

            host.EmitFault(new InvalidOperationException("test host fault"));

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(state?.IsConfirmed ?? true);
            Assert.Contains("Needs attention", state?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task G13_unplug_while_held_releases_output_and_requires_reidentification()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var g13 = G13Descriptor((nint)307, "g13-session-unplug", "g13-persistent-unplug");
            var host = new FakeDualMessageHost();
            var enumerator = new FakeG13Enumerator(g13);
            var keyboardProvider = new RawInputKeyboardProvider(
                new FakeKeyboardEnumerator(),
                host,
                keyboardIsNeutral: static () => true);
            var g13Provider = new LogitechG13InputProvider(enumerator, host);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboardProvider, g13Provider, output, new AtomicProfileStore(root));
            RuntimeState? state = null;
            runtime.StateChanged += (_, value) => state = value;
            await runtime.InitializeAsync();
            IdentifyAndConfirmG13(runtime, host, (nint)307);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
            runtime.AssignMapping(g1.ControlId.Value, "F13");
            runtime.IsRehearsal = false;
            host.EmitHid(G13Packet((nint)307, 1));

            enumerator.Remove((nint)307);
            host.EmitDevice((nint)307, RawInputDeviceChangeKind.Removal);

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.Empty(runtime.Devices);
            Assert.True(runtime.IsRehearsal);
            Assert.False(state?.IsConfirmed ?? true);
            Assert.False(runtime.ConfirmController().Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task G13_membership_change_disarms_visibly_instead_of_leaving_silent_confirmed_runtime()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var logicalHandle = new nint(320);
            var firstMember = new nint(321);
            var remainingMember = new nint(322);
            var initial = G13Descriptor(
                logicalHandle,
                "g13-session-membership",
                "g13-persistent-membership",
                firstMember,
                remainingMember);
            var remaining = initial with { MemberSessionHandles = [remainingMember] };
            var host = new FakeDualMessageHost();
            var enumerator = new FakeG13Enumerator(initial);
            var g13Provider = new LogitechG13InputProvider(enumerator, host);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(
                    new FakeKeyboardEnumerator(),
                    host,
                    keyboardIsNeutral: static () => true),
                g13Provider,
                output,
                new AtomicProfileStore(root));
            RuntimeState? state = null;
            runtime.StateChanged += (_, value) => state = value;
            await runtime.InitializeAsync();
            IdentifyAndConfirmG13(runtime, host, firstMember);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(
                item => item.Control == LogitechG13Control.G1);
            Assert.True(runtime.AssignMapping(g1.ControlId.Value, "F13").Succeeded);
            runtime.IsRehearsal = false;
            host.EmitHid(G13Packet(firstMember, 1));
            enumerator.SetDescriptors(remaining);

            host.EmitDevice(firstMember, RawInputDeviceChangeKind.Removal);

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(state?.IsConfirmed ?? true);
            Assert.Contains("membership changed", state?.Status ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            Assert.Null(g13Provider.CaptureTarget);
            Assert.Single(runtime.Devices, item => item.ProviderId == "raw-hid-g13");

            EmitG13Cycle(host, remainingMember, 0);
            Assert.Single(output.Down);
            Assert.Single(output.Up);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DeviceAwareControllerRuntime CreateRuntime(
        string root,
        FakeDualMessageHost host,
        SanitizedDeviceDescriptor g13,
        out RecordingOutput output)
    {
        output = new RecordingOutput();
        return new DeviceAwareControllerRuntime(
            new RawInputKeyboardProvider(
                new FakeKeyboardEnumerator(),
                host,
                keyboardIsNeutral: static () => true),
            new LogitechG13InputProvider(new FakeG13Enumerator(g13), host),
            output,
            new AtomicProfileStore(root));
    }

    private static void IdentifyAndConfirmG13(
        DeviceAwareControllerRuntime runtime,
        FakeDualMessageHost host,
        nint handle)
    {
        var choice = Assert.Single(runtime.Devices, item => item.ProviderId == "raw-hid-g13");
        Assert.True(runtime.BeginIdentification(choice).Succeeded);
        EmitG13Cycle(host, handle, 0);
        Assert.True(runtime.ConfirmController().Succeeded);
    }

    private static void EmitG13Cycle(FakeDualMessageHost host, nint handle, int buttonBit)
    {
        host.EmitHid(G13Packet(handle, 1UL << buttonBit));
        host.EmitHid(G13Packet(handle, 0));
    }

    private static RawHidInputPacket G13Packet(nint handle, ulong buttons, byte x = 128, byte y = 128) =>
        new(handle, LogitechG13Protocol.InputReportId, x, y, buttons);

    private static RawKeyboardPacket KeyboardPacket(nint handle, RawKeyboardFlags flags) =>
        new(handle, 0x4F, flags, 0, 0x61,
            flags.HasFlag(RawKeyboardFlags.Break) ? 0x101u : 0x100u, 0);

    private static SanitizedDeviceDescriptor G13Descriptor(
        nint handle,
        string sessionId,
        string persistentId,
        params nint[] memberHandles) =>
        new(
            handle,
            sessionId,
            persistentId,
            new string('A', 64),
            RawInputDeviceKind.Hid,
            LogitechG13Protocol.VendorId,
            LogitechG13Protocol.ProductId,
            LogitechG13Protocol.UsagePage,
            LogitechG13Protocol.Usage,
            "Logitech G13")
        {
            Grouping = PhysicalDeviceGrouping.WindowsContainerId,
            MemberSessionHandles = memberHandles.Length == 0 ? [handle] : memberHandles,
        };

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Tappy-G13-AppTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeKeyboardEnumerator(params SanitizedDeviceDescriptor[] devices)
        : IRawInputDeviceEnumerator
    {
        private readonly Dictionary<nint, SanitizedDeviceDescriptor> _devices =
            devices.ToDictionary(device => device.SessionHandle);

        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards() => _devices.Values.ToArray();

        public SanitizedDeviceDescriptor? DescribeKeyboard(nint deviceHandle) =>
            _devices.GetValueOrDefault(deviceHandle);
    }

    private sealed class FakeG13Enumerator(params SanitizedDeviceDescriptor[] devices)
        : ILogitechG13DeviceEnumerator
    {
        private readonly Dictionary<nint, SanitizedDeviceDescriptor> _devices =
            devices.ToDictionary(device => device.SessionHandle);

        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateControllers() => _devices.Values.ToArray();

        public SanitizedDeviceDescriptor? DescribeController(nint deviceHandle) =>
            _devices.GetValueOrDefault(deviceHandle);

        public void Remove(nint deviceHandle) => _devices.Remove(deviceHandle);

        public void SetDescriptors(params SanitizedDeviceDescriptor[] devices)
        {
            _devices.Clear();
            foreach (var device in devices)
            {
                _devices.Add(device.SessionHandle, device);
            }
        }
    }

    private sealed class FakeDualMessageHost : IRawHidInputMessageHost
    {
        public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;
        public event EventHandler<RawHidInputPacketEventArgs>? HidPacketReceived;
        public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;
        public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;
        public event EventHandler<Exception>? Faulted;

        public bool IsRunning { get; private set; }
        public nint WindowHandle => (nint)900;
        public Exception? StartFault { get; init; }

        private bool _startFaultEmitted;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            if (!_startFaultEmitted && StartFault is not null)
            {
                _startFaultEmitted = true;
                Faulted?.Invoke(this, StartFault);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void EmitKeyboard(RawKeyboardPacket packet) =>
            KeyboardPacketReceived?.Invoke(this, new RawKeyboardPacketEventArgs(packet));

        public void EmitHid(RawHidInputPacket packet) =>
            HidPacketReceived?.Invoke(this, new RawHidInputPacketEventArgs(packet));

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
}
