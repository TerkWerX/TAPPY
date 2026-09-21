using Tappy.App.Runtime;
using Tappy.App.ViewModels;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Theory]
    [InlineData(false, "unlocked")]
    [InlineData(true, "unlocked")]
    [InlineData(false, "lock-first")]
    [InlineData(true, "lock-first")]
    [InlineData(false, "sleep-first")]
    [InlineData(true, "sleep-first")]
    [InlineData(false, "unlock-before-resume")]
    [InlineData(true, "unlock-before-resume")]
    public async Task Sleep_preserves_G13_presentation_and_previous_mode_without_replaying_the_wake_press(bool rehearsal, string order)
    {
        var root = NewRoot();
        try
        {
            var descriptor = G13() with { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
            var devices = new G13Enumerator(descriptor);
            var host = new TestHost();
            var output = new RecordingKeyboard();
            var actions = new RecordingActions();
            var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), host, keyboardIsNeutral: () => true),
                new LogitechG13InputProvider(devices, host), output, new AtomicProfileStore(root), actionOutput: actions);
            var ui = new Queue<Action>();
            await using var vm = new MainViewModel(runtime, action => ui.Enqueue(action), (_, _) => { });
            void Drain() { while (ui.TryDequeue(out var action)) { action(); } }
            RuntimeState? state = null;
            runtime.StateChanged += (_, value) => state = value;
            await vm.InitializeAsync();
            runtime.BeginIdentification(runtime.Devices.Single());
            host.EmitHid(1);
            host.EmitHid(0);
            Assert.True(runtime.ConfirmController().Succeeded);
            var g1 = LogitechG13InputProvider.SupportedControls.Single(c => c.Control == LogitechG13Control.G1).ControlId.Value;
            runtime.AssignMapping(g1, "F24");
            var layout = runtime.GetControllerLayout()!;
            runtime.UpdateControllerLayout(layout with
            {
                Controls = layout.Controls.Select(c => c.ControlId == g1
                ? c with { X = 432, Y = 123, ColorKey = "Purple" } : c).ToArray()
            });
            var g2 = LogitechG13InputProvider.SupportedControls.Single(c => c.Control == LogitechG13Control.G2).ControlId.Value;
            runtime.AssignControllerAction(g2, ReleaseMacro());
            runtime.IsRehearsal = rehearsal;

            // Run two complete sleep/wake cycles, including repeated power messages.
            for (var cycle = 0; cycle < 2; cycle++)
            {
                host.EmitHid(3);
                var downsBeforeSleep = output.Down;
                var releasesBeforeSleep = actions.Starts;
                if (order is "lock-first" or "unlock-before-resume") { host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked); }
                host.EmitLifecycle(WindowsLifecycleSignal.Suspending);
                if (order == "sleep-first") { host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked); }
                host.EmitLifecycle(WindowsLifecycleSignal.Suspending);
                runtime.PollRememberedReconnect();
                Drain();
                Assert.True(state!.IsLifecyclePaused);
                Assert.False(state.IsConfirmed);
                Assert.True(vm.HasControllerPhoto);
                Assert.Equal(LogitechG13InputProvider.SupportedControls.Count, vm.Controls.Count);
                Assert.Equal(rehearsal, vm.IsRehearsal);
                Assert.Equal(432, vm.Controls.Single(c => c.ControlId == g1).X);
                Assert.Equal("Purple", vm.Controls.Single(c => c.ControlId == g1).ColorKey);
                Assert.Equal(output.Down, output.Up);
                Assert.Equal(releasesBeforeSleep, actions.Starts);
                host.EmitHid(0);
                host.EmitHid(3);
                Assert.Equal(downsBeforeSleep, output.Down);

                if (order == "unlock-before-resume")
                {
                    host.EmitLifecycle(WindowsLifecycleSignal.SessionUnlocked);
                    runtime.PollRememberedReconnect();
                    Assert.False(state.IsConfirmed);
                }
                host.EmitLifecycle(WindowsLifecycleSignal.Resuming);
                if (order is not "unlocked" and not "unlock-before-resume")
                {
                    runtime.PollRememberedReconnect();
                    Assert.False(state.IsConfirmed);
                    Assert.False(runtime.IsIdentificationCaptureActive);
                    host.EmitLifecycle(WindowsLifecycleSignal.SessionUnlocked);
                }
                // A held wake button must not execute either press or release actions.
                host.EmitHid(3);
                runtime.PollRememberedReconnect();
                Assert.False(state.IsConfirmed);
                Assert.Equal(downsBeforeSleep, output.Down);
                host.EmitHid(0);
                runtime.PollRememberedReconnect();
                Drain();
                Assert.True(state.IsConfirmed);
                Assert.False(state.IsLifecyclePaused);
                Assert.Equal(rehearsal, runtime.IsRehearsal);
                Assert.Equal(rehearsal, vm.IsRehearsal);
                Assert.Equal(downsBeforeSleep, output.Down);
                Assert.Equal(releasesBeforeSleep, actions.Starts);
                host.EmitLifecycle(WindowsLifecycleSignal.Resuming);
                host.EmitHid(1);
                host.EmitHid(0);
                Assert.Equal(downsBeforeSleep + (rehearsal ? 0 : 1), output.Down);
                Assert.Equal(output.Down, output.Up);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("emergency")]
    [InlineData("input-fault")]
    [InlineData("shutdown")]
    [InlineData("release-rejected")]
    [InlineData("wrong-device")]
    public async Task Sleep_resume_does_not_override_stop_faults_or_identity_checks(string reason)
    {
        var root = NewRoot();
        try
        {
            var descriptor = G13() with { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
            var devices = new G13Enumerator(descriptor);
            var host = new TestHost();
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), host, keyboardIsNeutral: () => true),
                new LogitechG13InputProvider(devices, host), output, new AtomicProfileStore(root));
            RuntimeState? state = null;
            runtime.StateChanged += (_, value) => state = value;
            await runtime.InitializeAsync();
            runtime.BeginIdentification(runtime.Devices.Single());
            host.EmitHid(1);
            host.EmitHid(0);
            runtime.ConfirmController();
            runtime.AssignMapping(LogitechG13InputProvider.SupportedControls.Single(c => c.Control == LogitechG13Control.G1).ControlId.Value, "F24");
            runtime.IsRehearsal = false;
            host.EmitHid(1);
            if (reason == "release-rejected") { output.RejectRelease = true; }
            host.EmitLifecycle(WindowsLifecycleSignal.Suspending);
            if (reason == "emergency") { runtime.EmergencyStop("cancel sleep recovery"); }
            if (reason == "input-fault") { host.EmitFault(); }
            if (reason == "shutdown") { host.EmitLifecycle(WindowsLifecycleSignal.ShutdownRequested); }
            if (reason == "wrong-device") { devices.Devices = [descriptor with { PersistentId = "other-g13", SessionId = "other" }]; }
            host.EmitLifecycle(WindowsLifecycleSignal.Resuming);
            host.EmitHid(0);
            runtime.PollRememberedReconnect();
            Assert.False(state!.IsConfirmed);
            Assert.False(runtime.IsIdentificationCaptureActive);
            host.EmitHid(1);
            Assert.Equal(1, output.Down);
            if (reason != "wrong-device") { Assert.True(runtime.IsRehearsal); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
