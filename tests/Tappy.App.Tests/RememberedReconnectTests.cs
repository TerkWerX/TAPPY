using Tappy.App.Runtime;
using Tappy.App.ViewModels;
using Tappy.Core.Input;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G13_reconnect_restores_presentation_then_waits_for_neutral_without_output(bool deferredUi)
    {
        var root = NewRoot();
        try
        {
            var descriptor = G13() with { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
            var host = new TestHost();
            var devices = new G13Enumerator(descriptor);
            var output = new RecordingKeyboard();
            var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), host, keyboardIsNeutral: () => true),
                new LogitechG13InputProvider(devices, host), output, new AtomicProfileStore(root));
            var ui = new Queue<Action>();
            await using var vm = new MainViewModel(runtime,
                action => { if (deferredUi) { ui.Enqueue(action); } else { action(); } }, (_, _) => { });
            void Drain() { while (ui.TryDequeue(out var action)) { action(); } }
            RuntimeState? state = null;
            runtime.StateChanged += (_, next) => state = next;
            await vm.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.EmitHid(1);
            host.EmitHid(0);
            Assert.True(runtime.ConfirmController().Succeeded);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(c => c.Control == LogitechG13Control.G1).ControlId.Value;
            Assert.True(runtime.AssignMapping(g1, "F24").Succeeded);
            var oldEdit = runtime.BeginAssignmentEdit(g1)!;
            var workspace = runtime.GetControllerLayout()!;
            var custom = workspace.Controls.Select(c => c.ControlId == g1
                ? c with { X = 380, Y = 210, Width = 1.5, Height = 0.75, ColorKey = "Purple" } : c).ToArray();
            Assert.True(runtime.UpdateControllerLayout(workspace with { Controls = custom }).Succeeded);
            runtime.IsRehearsal = false;
            host.EmitHid(1);
            Assert.Equal(1, output.Down);
            devices.Devices = [];
            host.EmitRemoval(descriptor.SessionHandle);
            Drain();
            Assert.Empty(vm.Controls);
            Assert.False(vm.HasControllerPhoto);
            Assert.Equal(1, output.Up);

            devices.Devices = [descriptor with { SessionId = "g13-reconnected-session" }];
            runtime.PollRememberedReconnect();
            Drain();
            Assert.True(state!.IsRememberedReconnect);
            Assert.False(state.IsConfirmed);
            Assert.True(vm.HasControllerPhoto);
            Assert.Equal(LogitechG13InputProvider.SupportedControls.Count, vm.Controls.Count);
            var tile = vm.Controls.Single(c => c.ControlId == g1);
            Assert.Equal("Hold F24 until release", tile.Action);
            Assert.Equal(380, tile.X);
            Assert.Equal(210, tile.Y);
            Assert.Equal("Purple", tile.ColorKey);
            host.EmitHid(1);
            runtime.PollRememberedReconnect();
            Assert.False(state.IsConfirmed);
            Assert.Equal(1, output.Down);
            host.EmitHid(0);
            runtime.PollRememberedReconnect();
            Drain();
            Assert.True(state.IsConfirmed);
            Assert.False(state.IsRememberedReconnect);
            Assert.True(vm.IsRehearsal);
            Assert.False(runtime.AssignKeyboardMapping(oldEdit.Target,
                KeyboardMappingAssignment.PressOnce("stale", ["F13"])).Succeeded);
            host.EmitHid(1);
            host.EmitHid(0);
            Assert.Equal(1, output.Down);
            Assert.Equal(1, output.Up);
            runtime.IsRehearsal = false;
            host.EmitHid(1);
            host.EmitHid(0);
            Assert.Equal(2, output.Down);
            Assert.Equal(2, output.Up);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("emergency")]
    [InlineData("lock")]
    [InlineData("different-device")]
    [InlineData("ambiguous")]
    [InlineData("duplicate")]
    [InlineData("stop-while-restoring")]
    [InlineData("duplicate-while-restoring")]
    public async Task Reconnect_does_not_override_stop_or_trust_another_device(string reason)
    {
        var root = NewRoot();
        try
        {
            var descriptor = G13() with
            {
                Grouping = reason == "ambiguous"
                ? PhysicalDeviceGrouping.RawInputInterfaceFallback : PhysicalDeviceGrouping.WindowsContainerId
            };
            var devices = new G13Enumerator(descriptor);
            var host = new TestHost();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), host, keyboardIsNeutral: () => true),
                new LogitechG13InputProvider(devices, host), new RecordingKeyboard(), new AtomicProfileStore(root));
            RuntimeState? state = null;
            runtime.StateChanged += (_, next) => state = next;
            await runtime.InitializeAsync();
            runtime.BeginIdentification(runtime.Devices.Single());
            host.EmitHid(1);
            host.EmitHid(0);
            Assert.True(runtime.ConfirmController().Succeeded);
            devices.Devices = [];
            host.EmitRemoval(descriptor.SessionHandle);
            if (reason == "emergency") { runtime.EmergencyStop("test"); }
            if (reason == "lock") { host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked); }
            var returned = descriptor with { PersistentId = reason == "different-device" ? "other-g13" : descriptor.PersistentId };
            devices.Devices = reason == "duplicate"
                ? [returned, returned with { SessionHandle = (nint)302, SessionId = "duplicate", MemberSessionHandles = [(nint)302] }]
                : [returned];
            runtime.PollRememberedReconnect();
            if (reason == "stop-while-restoring") { runtime.EmergencyStop("cancel reconnect"); }
            if (reason == "duplicate-while-restoring")
            {
                devices.Devices = [returned, returned with { SessionHandle = (nint)302,
                    SessionId = "later-duplicate", MemberSessionHandles = [(nint)302] }];
                runtime.PollRememberedReconnect();
            }
            host.EmitHid(0);
            runtime.PollRememberedReconnect();
            Assert.False(state!.IsConfirmed);
            Assert.False(state.IsRememberedReconnect);
            Assert.False(runtime.IsIdentificationCaptureActive);
            Assert.Null(runtime.GetControllerLayout());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Keyboard_reconnect_accepts_new_session_handle_only_for_the_remembered_identity()
    {
        var root = NewRoot();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_1234&PID_5678#RECONNECT");
            var devices = new KeyboardEnumerator(descriptor);
            var host = new TestHost();
            var neutral = true;
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(devices, host, keyboardIsNeutral: () => neutral), output, new AtomicProfileStore(root));
            RuntimeState? state = null;
            runtime.StateChanged += (_, next) => state = next;
            await runtime.InitializeAsync();
            runtime.BeginIdentification(runtime.Devices.Single());
            host.EmitKey((nint)41, RawKeyboardFlags.Make);
            host.EmitKey((nint)41, RawKeyboardFlags.Break);
            runtime.ConfirmController();
            var id = ControlId.FromRawInputKeyboard(0x4F).Value;
            runtime.AssignMapping(id, "F24");
            devices.Devices = [];
            host.EmitRemoval((nint)41);
            devices.Devices = [descriptor with { SessionHandle = (nint)42, SessionId = "new-session", MemberSessionHandles = [(nint)42] }];
            neutral = false;
            runtime.PollRememberedReconnect();
            Assert.False(state!.IsConfirmed);
            neutral = true;
            runtime.PollRememberedReconnect();
            Assert.True(state.IsConfirmed);
            Assert.True(runtime.IsRehearsal);
            Assert.NotNull(runtime.GetControllerAction(id));
            runtime.IsRehearsal = false;
            host.EmitKey((nint)41, RawKeyboardFlags.Make);
            Assert.Equal(0, output.Down);
            host.EmitKey((nint)42, RawKeyboardFlags.Make);
            host.EmitKey((nint)42, RawKeyboardFlags.Break);
            Assert.Equal(1, output.Down);
            Assert.Equal(1, output.Up);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Ambiguous_WinMM_identity_requires_identification_and_keeps_saved_controls()
    {
        var root = NewRoot();
        try
        {
            using var backend = new MidiBackend();
            var midi = new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan);
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true),
                output, new AtomicProfileStore(root), midiInputProvider: midi);
            RuntimeState? state = null;
            runtime.StateChanged += (_, next) => state = next;
            await runtime.InitializeAsync();
            IdentifyMidi(runtime, backend);
            runtime.AssignMapping("winmm-midi:channel-1:note-60", "F24");
            var layout = runtime.GetControllerLayout()!;
            runtime.IsRehearsal = false;
            backend.Publish(0x00643C90);
            backend.Devices = [];
            runtime.RefreshDevices();
            Assert.False(state!.IsConfirmed);
            Assert.Equal(1, output.Down);
            Assert.Equal(1, output.Up);
            backend.Devices = [new(1, "APC MINI", 1, 2, 3)];
            runtime.PollRememberedReconnect();
            runtime.RefreshDevices();
            Assert.False(state.IsConfirmed);
            Assert.False(state.IsRememberedReconnect);
            Assert.True(runtime.IsRehearsal);
            Assert.Null(runtime.GetControllerLayout());
            IdentifyMidi(runtime, backend);
            Assert.Equal(layout.Controls.Count, runtime.GetControllerLayout()!.Controls.Count);
            backend.Publish(0x00643C90);
            backend.Publish(0x00003C80);
            Assert.Equal(1, output.Down);
            Assert.Equal(1, output.Up);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Keyboard_reconnect_across_app_restart_restores_without_identification()
    {
        var root = NewRoot();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_1234&PID_5678#RESTART");
            // The physical device itself (never unplugged across the simulated
            // restart) is shared between the two runtime instances below; the host
            // and provider are recreated fresh, matching what an actual app restart
            // would look like.
            var devices = new KeyboardEnumerator(descriptor);
            var host = new TestHost();
            var output = new RecordingKeyboard();
            var id = ControlId.FromRawInputKeyboard(0x4F).Value;
            await using (var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(devices, host, keyboardIsNeutral: () => true), output, new AtomicProfileStore(root)))
            {
                await runtime.InitializeAsync();
                runtime.BeginIdentification(runtime.Devices.Single());
                host.EmitKey((nint)41, RawKeyboardFlags.Make);
                host.EmitKey((nint)41, RawKeyboardFlags.Break);
                Assert.True(runtime.ConfirmController().Succeeded);
                Assert.True(runtime.AssignMapping(id, "F24").Succeeded);
                var workspace = runtime.GetControllerLayout()!;
                Assert.True(runtime.UpdateControllerLayout(workspace with
                {
                    Controls = workspace.Controls.Select(c => c.ControlId == id ? c with { X = 500, Y = 250 } : c).ToArray()
                }).Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }

            var restartedHost = new TestHost();
            await using var restarted = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(devices, restartedHost, keyboardIsNeutral: () => true), output, new AtomicProfileStore(root));
            RuntimeState? state = null;
            restarted.StateChanged += (_, next) => state = next;
            await restarted.InitializeAsync();
            Assert.True(state!.IsRememberedReconnect);
            Assert.False(state.IsConfirmed);
            Assert.Contains("no identification gesture is needed", state.IdentificationStatus);
            restarted.PollRememberedReconnect();
            Assert.True(state.IsConfirmed);
            Assert.False(state.IsRememberedReconnect);
            Assert.True(restarted.IsRehearsal);
            Assert.Equal(500, restarted.GetControllerLayout()!.Controls.Single(c => c.ControlId == id).X);
            Assert.NotNull(restarted.GetControllerAction(id));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Startup_restore_does_not_happen_when_the_saved_device_is_absent()
    {
        var root = NewRoot();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_1234&PID_5678#ABSENT");
            var devices = new KeyboardEnumerator(descriptor);
            var host = new TestHost();
            var output = new RecordingKeyboard();
            await using (var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(devices, host, keyboardIsNeutral: () => true), output, new AtomicProfileStore(root)))
            {
                await runtime.InitializeAsync();
                runtime.BeginIdentification(runtime.Devices.Single());
                host.EmitKey((nint)41, RawKeyboardFlags.Make);
                host.EmitKey((nint)41, RawKeyboardFlags.Break);
                Assert.True(runtime.ConfirmController().Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }

            // Restart with the saved device NOT connected this time (still unplugged).
            var emptyDevices = new KeyboardEnumerator();
            var restartedHost = new TestHost();
            await using var restarted = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(emptyDevices, restartedHost, keyboardIsNeutral: () => true), output, new AtomicProfileStore(root));
            RuntimeState? state = null;
            restarted.StateChanged += (_, next) => state = next;
            await restarted.InitializeAsync();
            Assert.False(state!.IsRememberedReconnect);
            Assert.False(state.IsConfirmed);
            Assert.Empty(restarted.Devices);

            // It shows up and restores normally once actually reconnected, matching
            // the existing mid-session remembered-reconnect contract.
            emptyDevices.Devices = [descriptor];
            restarted.PollRememberedReconnect();
            Assert.True(state.IsConfirmed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task MIDI_identity_never_restores_across_app_restart()
    {
        var root = NewRoot();
        try
        {
            using var backend = new MidiBackend();
            var output = new RecordingKeyboard();
            // Force the container-id resolver to stay unresolved rather than
            // relying on the real default resolver finding nothing: on a machine
            // with real MIDI hardware attached, the default resolver can
            // genuinely resolve a container id for whatever sits at device id 0,
            // which would make this "resolution unavailable" scenario flaky
            // depending on ambient hardware.
            var unresolved = new FakeContainerIdResolver(_ => null);
            var midi = new WinMmMidiInputProvider(
                backend, monitorInterval: Timeout.InfiniteTimeSpan,
                containerIdResolver: unresolved, deviceInterfacePathResolver: _ => null);
            await using (var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true),
                output, new AtomicProfileStore(root), midiInputProvider: midi))
            {
                await runtime.InitializeAsync();
                IdentifyMidi(runtime, backend);
                Assert.True(runtime.AssignMapping("winmm-midi:channel-1:note-60", "F24").Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }

            // Same backend/device list "still connected" across the simulated
            // restart. The container-id resolver stays forced-unresolved, so this
            // exercises the fallback path (Windows ContainerId could not be
            // resolved for this device) and must stay a manual identification, by
            // design — see
            // MIDI_identity_with_a_resolved_container_id_restores_across_app_restart
            // below for the case where resolution succeeds (D-026).
            var restartedMidi = new WinMmMidiInputProvider(
                backend, monitorInterval: Timeout.InfiniteTimeSpan,
                containerIdResolver: unresolved, deviceInterfacePathResolver: _ => null);
            await using var restarted = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true),
                output, new AtomicProfileStore(root), midiInputProvider: restartedMidi);
            RuntimeState? state = null;
            restarted.StateChanged += (_, next) => state = next;
            await restarted.InitializeAsync();
            Assert.False(state!.IsRememberedReconnect);
            Assert.False(state.IsConfirmed);
            Assert.Null(restarted.GetControllerLayout());
            IdentifyMidi(restarted, backend);
            Assert.NotNull(restarted.GetControllerAction("winmm-midi:channel-1:note-60"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task MIDI_identity_with_a_resolved_container_id_restores_across_app_restart()
    {
        var root = NewRoot();
        try
        {
            // Simulates the device staying in the same USB port across the
            // restart: Windows would return the same cached ContainerId for
            // that same device-interface path both times (see D-026).
            var containerId = Guid.NewGuid();
            var resolver = new FakeContainerIdResolver(path => path == "fake-fire-path" ? containerId : null);
            string? PathResolver(int deviceId) => deviceId == 0 ? "fake-fire-path" : null;

            using var backend = new MidiBackend();
            var output = new RecordingKeyboard();
            var midi = new WinMmMidiInputProvider(
                backend, monitorInterval: Timeout.InfiniteTimeSpan,
                containerIdResolver: resolver, deviceInterfacePathResolver: PathResolver);
            await using (var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true),
                output, new AtomicProfileStore(root), midiInputProvider: midi))
            {
                await runtime.InitializeAsync();
                IdentifyMidi(runtime, backend);
                Assert.True(runtime.AssignMapping("winmm-midi:channel-1:note-60", "F24").Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }

            var restartedMidi = new WinMmMidiInputProvider(
                backend, monitorInterval: Timeout.InfiniteTimeSpan,
                containerIdResolver: resolver, deviceInterfacePathResolver: PathResolver);
            await using var restarted = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), new TestHost(), keyboardIsNeutral: () => true),
                output, new AtomicProfileStore(root), midiInputProvider: restartedMidi);
            RuntimeState? state = null;
            restarted.StateChanged += (_, next) => state = next;
            await restarted.InitializeAsync();

            Assert.True(state!.IsRememberedReconnect);
            Assert.NotNull(restarted.GetControllerLayout());
            backend.Publish(0x00643D90);
            backend.Publish(0x00003D80);
            restarted.PollRememberedReconnect();
            Assert.True(state.IsConfirmed);
            Assert.NotNull(restarted.GetControllerAction("winmm-midi:channel-1:note-60"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FakeContainerIdResolver(Func<string, Guid?> resolve) : IDeviceContainerIdResolver
    {
        public Guid? Resolve(string deviceInterfacePath) => resolve(deviceInterfacePath);
    }

    [Fact]
    public async Task Startup_reconnect_survives_a_benign_membership_change_on_a_multi_interface_device()
    {
        var root = NewRoot();
        try
        {
            // Mirrors a real multi-interface composite device (e.g. Razer Tartarus):
            // Windows reports each of its interfaces arriving separately even though
            // the whole unit was never unplugged, which the provider reclassifies as
            // a "membership changed" notification (not a fresh Arrival/Removal) since
            // the container still has another member interface present.
            var containerId = new Guid("6F1DB6D0-9B1F-4E0B-8E44-1E9B6F6E9B10");
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboardGroup(
                containerId,
                [
                    new RawKeyboardDeviceCandidate((nint)501, @"\\?\HID#VID_1532&PID_0201&COL01#A", 0x1532, 0x0201, null),
                    new RawKeyboardDeviceCandidate((nint)502, @"\\?\HID#VID_1532&PID_0201&COL02#B", 0x1532, 0x0201, null),
                ]);
            var devices = new KeyboardEnumerator(descriptor);
            var output = new RecordingKeyboard();
            var id = ControlId.FromRawInputKeyboard(0x4F).Value;

            await using (var host = new TestHost())
            await using (var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(devices, host, keyboardIsNeutral: () => true), output, new AtomicProfileStore(root)))
            {
                await runtime.InitializeAsync();
                runtime.BeginIdentification(runtime.Devices.Single());
                host.EmitKey((nint)501, RawKeyboardFlags.Make);
                host.EmitKey((nint)501, RawKeyboardFlags.Break);
                Assert.True(runtime.ConfirmController().Succeeded);
                Assert.True(runtime.AssignMapping(id, "F24").Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }

            var restartedHost = new TestHost();
            await using var restarted = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(devices, restartedHost, keyboardIsNeutral: () => true), output, new AtomicProfileStore(root));
            RuntimeState? state = null;
            restarted.StateChanged += (_, next) => state = next;
            await restarted.InitializeAsync();
            Assert.True(state!.IsRememberedReconnect);
            Assert.False(state.IsConfirmed);

            // One of the device's own interfaces "arrives" a moment after Tappy
            // starts, reclassified internally as MembershipChanged (not Removal),
            // because the enumerator still reports the same container with its
            // other member intact -- exactly the startup noise a multi-interface
            // device produces, not a genuine identity change.
            restartedHost.EmitRemoval((nint)502);

            // Must NOT have been demoted to requiring manual re-identification: the
            // membership-change handler reports this the same way as an ordinary,
            // self-healing disconnect, not "select and identify it again".
            Assert.False(state.IsConfirmed);
            Assert.True(state.IsRememberedReconnect);
            Assert.DoesNotContain("identify it again", state.IdentificationStatus, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("re-identification", state.Status, StringComparison.OrdinalIgnoreCase);

            restarted.PollRememberedReconnect();
            Assert.True(state.IsConfirmed);
            Assert.True(restarted.IsRehearsal);
            Assert.NotNull(restarted.GetControllerAction(id));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
