using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    private sealed class BankRig : IAsyncDisposable
    {
        internal readonly string Root = NewRoot();
        internal readonly TestHost Host = new();
        internal readonly RecordingKeyboard Output = new();
        internal readonly List<DeviceAwareControllerRuntime> Runtimes = [];
        internal readonly TestTartarusLeds Leds = new();
        internal readonly Queue<Action> Queue = new();
        internal bool DeferUi;
        internal readonly ControllerWorkspace Workspace;
        internal readonly KeyboardEnumerator Devices;
        internal static string Key => RazerTartarusLayout.Key(0x4F);
        internal static string KeyFor(int source) => source == 0 ? Key : RazerTartarusLayout.Key(0x0F);
        internal BankRig(Action<Action>? onUi = null)
        {
            Devices = new KeyboardEnumerator(
                DeviceDescriptorSanitizer.CreateKeyboard(41, @"\\?\HID#{00001124-0000-1000-8000-00805F9B34FB}_VID&0002045E_PID&0764&COL01#TEST#GUID") with { Grouping = PhysicalDeviceGrouping.WindowsContainerId },
                DeviceDescriptorSanitizer.CreateKeyboard(42, @"\\?\HID#VID_1532&PID_0201&MI_00#TARTARUS_A") with { Grouping = PhysicalDeviceGrouping.WindowsContainerId },
                DeviceDescriptorSanitizer.CreateKeyboard(43, @"\\?\HID#VID_1532&PID_0201&MI_00#TARTARUS_B") with { Grouping = PhysicalDeviceGrouping.WindowsContainerId });
            var shared = new OwnedKeyboardOutput(Output);
            Workspace = new ControllerWorkspace(slot =>
            {
                var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(Devices,
                    new SharedInputHostLease(Host), keyboardIsNeutral: () => true), shared,
                    new AtomicProfileStore(Path.Combine(Root, $"slot-{slot}")), tartarusIndicatorOutput: Leds);
                Runtimes.Add(runtime); return runtime;
            }, action => { if (DeferUi) { Queue.Enqueue(action); } else if (onUi is not null) { onUi(action); } else { action(); } }, (_, _) => { });
        }
        internal async Task Start()
        {
            await Workspace.InitializeAsync(); Confirm(0, 41);
            await Workspace.AddAsync(); Confirm(1, 42);
            await Workspace.AddAsync(); Confirm(2, 43);
        }
        internal void Confirm(int index, nint handle)
        {
            var runtime = Runtimes[index]; runtime.RefreshDevices();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single(d => d.SessionId == Devices.Devices.Single(d => d.SessionHandle == handle).SessionId)).Succeeded);
            Press(handle); Release(handle);
            Assert.True(runtime.ConfirmController().Succeeded);
        }
        internal void Press(nint handle) => Host.EmitKey(handle, RawKeyboardFlags.Make, (ushort)(handle == 41 ? 0x4F : 0x0F), (ushort)(handle == 41 ? 0x61 : 0x09));
        internal void Release(nint handle) => Host.EmitKey(handle, RawKeyboardFlags.Break, (ushort)(handle == 41 ? 0x4F : 0x0F), (ushort)(handle == 41 ? 0x61 : 0x09));
        internal void Click(nint handle) { Press(handle); Release(handle); }
        internal void Bind(int source, int target, BankControlOperation operation = BankControlOperation.Next, string layerId = "")
        {
            var endpoint = Runtimes[target].GetBankEndpoint()!;
            var edit = Runtimes[source].BeginAssignmentEdit(KeyFor(source));
            Assert.NotNull(edit);
            Assert.True(Runtimes[source].SetBankControl(edit.Target,
                new(new ControlId(KeyFor(source)), endpoint.ProviderId, endpoint.PersistentId, endpoint.DisplayName, operation, layerId)).Succeeded);
        }
        internal void Drain() { while (Queue.TryDequeue(out var action)) { action(); } }
        internal async Task Idle(int target)
        {
            for (var i = 0; i < 200 && !Workspace.Tabs[target].ViewModel.CanChangeBank; i++) { await Task.Delay(5); }
            Assert.True(Workspace.Tabs[target].ViewModel.CanChangeBank);
        }
        public async ValueTask DisposeAsync()
        { DeferUi = false; Drain(); await Workspace.DisposeAsync(); Directory.Delete(Root, true); }
    }

    [Fact]
    public async Task Microsoft_pad_changes_only_exact_Tartarus_and_target_LED_follow_is_preserved()
    {
        await using var rig = new BankRig(); await rig.Start();
        Assert.True(rig.Runtimes[0].AssignMapping(BankRig.Key, "F24").Succeeded);
        rig.Bind(0, 1);
        rig.Workspace.Tabs[1].ViewModel.TartarusFollowKeymaps = true;
        rig.Press(41);
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        rig.Press(41); // auto-repeat must not switch
        rig.Release(41); await rig.Idle(1);
        Assert.Equal("layer-2", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        Assert.Equal("layer-1", rig.Runtimes[0].GetBankEndpoint()!.ActiveLayerId);
        Assert.Equal("layer-1", rig.Runtimes[2].GetBankEndpoint()!.ActiveLayerId);
        Assert.Equal(2, Assert.Single(rig.Leds.Calls).Mask);
        Assert.Equal(0, rig.Output.Down); // underlying macro and rehearsal outputs suppressed
        var saved = await new AtomicProfileStore(Path.Combine(rig.Root, "slot-2")).LoadAsync();
        Assert.Equal("layer-2", saved.Controllers.Single().ActiveLayerId);
    }

    [Fact]
    public async Task Next_previous_direct_and_self_controls_wrap_and_work_in_all_source_banks()
    {
        await using var rig = new BankRig(); await rig.Start();
        rig.Bind(0, 0, BankControlOperation.Previous);
        rig.Click(41); await rig.Idle(0);
        Assert.Equal("layer-3", rig.Runtimes[0].GetBankEndpoint()!.ActiveLayerId);
        rig.Bind(0, 0);
        rig.Click(41); await rig.Idle(0);
        Assert.Equal("layer-1", rig.Runtimes[0].GetBankEndpoint()!.ActiveLayerId);
        rig.Bind(0, 1, BankControlOperation.Select, "layer-8");
        foreach (var sourceBank in new[] { "layer-1", "layer-2", "layer-3" })
        {
            Assert.True(rig.Runtimes[0].ChangeBank(rig.Runtimes[0].GetBankEndpoint()!, BankControlOperation.Select, sourceBank).Succeeded);
            rig.Click(41); await rig.Idle(1);
            Assert.Equal("layer-8", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        }
        Assert.Empty(rig.Leds.Calls); // no hardware write without Follow enabled
        rig.Bind(1, 0); // interaction works in the opposite direction too
        rig.Click(42); await rig.Idle(0);
        Assert.Equal("layer-1", rig.Runtimes[0].GetBankEndpoint()!.ActiveLayerId);
    }

    [Fact]
    public async Task Held_target_refuses_switch_and_original_owned_output_releases_normally()
    {
        await using var rig = new BankRig(); await rig.Start(); rig.Bind(0, 1);
        Assert.True(rig.Runtimes[1].AssignMapping(BankRig.KeyFor(1), "F24").Succeeded);
        rig.Runtimes[1].IsRehearsal = false;
        rig.Press(42); rig.Click(41);
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        Assert.Contains("Release the target", rig.Workspace.Tabs[0].ViewModel.Status);
        rig.Release(42);
        Assert.Equal(rig.Output.Down, rig.Output.Up);
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId); // no deferred command
        rig.Click(41); await rig.Idle(1);
        Assert.Equal("layer-2", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disconnected_target_never_redirects_to_identical_model_or_later_reconnect(bool queued)
    {
        await using var rig = new BankRig(); await rig.Start(); rig.Bind(0, 1);
        var old = rig.Runtimes[1].GetBankEndpoint()!;
        rig.DeferUi = queued;
        if (queued) { rig.Click(41); }
        rig.Runtimes[1].EmergencyStop("test target unavailable");
        if (!queued) { rig.Click(41); }
        rig.Confirm(1, 42);
        rig.DeferUi = false; rig.Drain();
        Assert.False(old.SameConnection(rig.Runtimes[1].GetBankEndpoint()));
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        Assert.Equal("layer-1", rig.Runtimes[2].GetBankEndpoint()!.ActiveLayerId);
        rig.Click(41); await rig.Idle(1);
        Assert.Equal("layer-2", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
    }

    [Fact]
    public async Task Source_cleanup_editor_pause_and_stale_confirmation_do_not_switch_banks()
    {
        await using var rig = new BankRig(); await rig.Start(); rig.Bind(0, 1);
        rig.Workspace.BankControlsPaused = true; rig.Click(41); rig.Workspace.BankControlsPaused = false;
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        rig.Press(41); rig.Host.EmitRemoval(41);
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        rig.Confirm(0, 41);
        rig.DeferUi = true; rig.Click(41);
        rig.Runtimes[0].EmergencyStop("test stale source"); rig.Confirm(0, 41);
        rig.DeferUi = false; rig.Drain();
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
    }

    [Fact]
    public async Task Saving_and_removing_bank_control_preserves_underlying_macros_and_stale_edits_fail()
    {
        await using var rig = new BankRig(); await rig.Start();
        var source = rig.Runtimes[0];
        Assert.True(source.AssignMapping(BankRig.Key, "F24").Succeeded);
        var edit = source.BeginAssignmentEdit(BankRig.Key)!;
        rig.Bind(0, 1);
        Assert.True((await source.SaveProfileAsync()).Succeeded);
        var loaded = await new AtomicProfileStore(Path.Combine(rig.Root, "slot-1")).LoadAsync();
        Assert.Single(loaded.Controllers.Single().BankControls);
        Assert.Equal("F24", loaded.Controllers.Single().Layers[0].Bindings.Single().PressAction.Keys.Single().Value);
        Assert.True(source.SetBankControl(edit.Target, null).Succeeded);
        source.IsRehearsal = false; rig.Click(41);
        Assert.Equal(1, rig.Output.Down); Assert.Equal(1, rig.Output.Up);
        Assert.True(source.ChangeBank(source.GetBankEndpoint()!, BankControlOperation.Next).Succeeded);
        Assert.False(source.SetBankControl(edit.Target, null).Succeeded);
    }

    [Fact]
    public async Task MIDI_pad_can_switch_keyboard_controller_bank_without_emitting_a_macro()
    {
        var root = NewRoot();
        try
        {
            using var backend = new MidiBackend();
            var host = new TestHost();
            var output = new RecordingKeyboard();
            var runtimes = new List<DeviceAwareControllerRuntime>();
            var tartarus = DeviceDescriptorSanitizer.CreateKeyboard(42, @"\\?\HID#VID_1532&PID_0201&MI_00#MIDI_TARGET");
            await using var workspace = new ControllerWorkspace(slot =>
            {
                var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(
                    new KeyboardEnumerator(slot == 1 ? [] : [tartarus]), new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                    output, new AtomicProfileStore(Path.Combine(root, $"slot-{slot}")),
                    // Force the container-id resolver to stay unresolved: this
                    // test asserts the legacy MIDI identity is session-bound
                    // (RequiresSessionBinding), which only holds while resolution
                    // fails. The real default resolver would otherwise query
                    // whatever MIDI hardware actually sits at device id 0 on the
                    // machine running the test.
                    midiInputProvider: slot == 1
                        ? new WinMmMidiInputProvider(
                            backend, monitorInterval: Timeout.InfiniteTimeSpan,
                            containerIdResolver: new FakeContainerIdResolver(_ => null),
                            deviceInterfacePathResolver: _ => null)
                        : null);
                runtimes.Add(runtime); return runtime;
            }, a => a(), (_, _) => { });
            await workspace.InitializeAsync(); IdentifyMidi(runtimes[0], backend);
            await workspace.AddAsync();
            Assert.True(runtimes[1].BeginIdentification(runtimes[1].Devices.Single()).Succeeded);
            host.EmitKey(42, RawKeyboardFlags.Make, 0x0F, 0x09); host.EmitKey(42, RawKeyboardFlags.Break, 0x0F, 0x09);
            Assert.True(runtimes[1].ConfirmController().Succeeded);
            const string note = "winmm-midi:channel-1:note-60";
            var endpoint = runtimes[1].GetBankEndpoint()!;
            var assigned = runtimes[0].SetBankControl(runtimes[0].BeginAssignmentEdit(note)!.Target,
                new(new ControlId(note), endpoint.ProviderId, endpoint.PersistentId, endpoint.DisplayName, BankControlOperation.Next));
            Assert.True(assigned.Succeeded, assigned.Message);
            backend.Publish(0x00643C90); backend.Publish(0x00003C80);
            for (var i = 0; i < 200 && !workspace.Tabs[1].ViewModel.CanChangeBank; i++) { await Task.Delay(5); }
            Assert.Equal("layer-2", runtimes[1].GetBankEndpoint()!.ActiveLayerId);
            Assert.Equal("layer-1", runtimes[0].GetBankEndpoint()!.ActiveLayerId);
            Assert.Equal(0, output.Down);
            // The reverse direction is supported, but legacy MIDI identity is
            // session-bound: a saved target must not follow a reused MIDI slot.
            var midiTarget = runtimes[0].GetBankEndpoint()!;
            Assert.True(midiTarget.RequiresSessionBinding);
            var keyboardKey = RazerTartarusLayout.Key(0x0F);
            Assert.True(runtimes[1].SetBankControl(runtimes[1].BeginAssignmentEdit(keyboardKey)!.Target,
                new(new ControlId(keyboardKey), midiTarget.ProviderId, midiTarget.PersistentId, midiTarget.DisplayName,
                    BankControlOperation.Next, TargetConfirmationToken: midiTarget.ConnectionToken)).Succeeded);
            void ClickKeyboard() { host.EmitKey(42, RawKeyboardFlags.Make, 0x0F, 0x09); host.EmitKey(42, RawKeyboardFlags.Break, 0x0F, 0x09); }
            ClickKeyboard();
            for (var i = 0; i < 200 && !workspace.Tabs[0].ViewModel.CanChangeBank; i++) { await Task.Delay(5); }
            Assert.Equal("layer-2", runtimes[0].GetBankEndpoint()!.ActiveLayerId);
            runtimes[0].EmergencyStop("MIDI target was disconnected");
            IdentifyMidi(runtimes[0], backend);
            Assert.NotEqual(midiTarget.ConnectionToken, runtimes[0].GetBankEndpoint()!.ConnectionToken);
            ClickKeyboard();
            Assert.Equal("layer-2", runtimes[0].GetBankEndpoint()!.ActiveLayerId);
            Assert.Contains("unavailable", workspace.Tabs[1].ViewModel.Status);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Sending_the_OLED_test_pattern_to_all_Fires_reaches_only_the_confirmed_Fire_tabs()
    {
        // Verifies ControllerWorkspace.SendOledTestPatternToAllFires targets
        // exactly the Fire tabs in a mixed workspace (two Fires plus an APC
        // MINI, matching the owner's real setup) and leaves every other tab
        // untouched. No real WinMmMidiOutput is wired to any tab here, so a
        // targeted Fire is expected to fail for that infrastructure reason
        // (proving it was attempted), not because it was skipped.
        var root = NewRoot();
        try
        {
            using var fire1Backend = new MidiBackend { Devices = [new(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1)] };
            using var fire2Backend = new MidiBackend { Devices = [new(0, "FL STUDIO FIRE", 0x09E8, 0x0043, 1)] };
            using var apcBackend = new MidiBackend();
            var host = new TestHost();
            var output = new RecordingKeyboard();
            var runtimes = new List<DeviceAwareControllerRuntime>();
            var backends = new[] { fire1Backend, fire2Backend, apcBackend };
            await using var workspace = new ControllerWorkspace(slot =>
            {
                var runtime = new DeviceAwareControllerRuntime(
                    new RawInputKeyboardProvider(new KeyboardEnumerator([]), new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                    output, new AtomicProfileStore(Path.Combine(root, $"slot-{slot}")),
                    // Force the container-id resolver to stay unresolved so this
                    // test's outcome does not depend on whatever real MIDI
                    // hardware is attached to the machine running it (see the
                    // similar fix earlier in this file).
                    midiInputProvider: new WinMmMidiInputProvider(
                        backends[slot - 1], monitorInterval: Timeout.InfiniteTimeSpan,
                        containerIdResolver: new FakeContainerIdResolver(_ => null),
                        deviceInterfacePathResolver: _ => null));
                runtimes.Add(runtime); return runtime;
            }, a => a(), (_, _) => { });
            await workspace.InitializeAsync();
            IdentifyMidi(runtimes[0], fire1Backend);
            await workspace.AddAsync();
            IdentifyMidi(runtimes[1], fire2Backend);
            await workspace.AddAsync();
            IdentifyMidi(runtimes[2], apcBackend);

            var results = workspace.SendOledTestPatternToAllFires();

            Assert.Equal(2, results.Count);
            Assert.All(results, result =>
            {
                Assert.False(result.Succeeded);
                Assert.Contains("Hardware LED/OLED output is unavailable", result.Message);
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Removed_overlay_and_busy_LED_target_discard_pending_commands()
    {
        await using var rig = new BankRig(); await rig.Start(); rig.Bind(0, 1);
        rig.DeferUi = true; rig.Click(41);
        Assert.True(rig.Runtimes[0].SetBankControl(rig.Runtimes[0].BeginAssignmentEdit(BankRig.Key)!.Target, null).Succeeded);
        rig.DeferUi = false; rig.Drain();
        Assert.Equal("layer-1", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        rig.Bind(0, 1);
        rig.Workspace.Tabs[1].ViewModel.TartarusFollowKeymaps = true;
        rig.Leds.Block = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Click(41); rig.Click(41);
        Assert.Equal("layer-2", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
        Assert.Single(rig.Leds.Calls);
        rig.Leds.Block.SetResult(2); await rig.Idle(1);
        Assert.Equal("layer-2", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
    }

    [Fact]
    public async Task Cross_controller_bank_updates_real_WPF_picker_with_production_queued_dispatch()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            await using var rig = new BankRig(a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a, System.Windows.Threading.DispatcherPriority.Render));
            await rig.Start();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            rig.Bind(0, 1);
            var targetVm = rig.Workspace.Tabs[1].ViewModel;
            var window = new MainWindow(targetVm, new WindowPlacementStore(rig.Root), new SessionMarker(rig.Root));
            var content = (System.Windows.FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(1500, 900)); content.Arrange(new System.Windows.Rect(0, 0, 1500, 900)); content.UpdateLayout();
            var picker = Assert.IsType<System.Windows.Controls.ComboBox>(window.FindName("ControllerBankBox"));
            Assert.Equal("layer-1", picker.SelectedValue);
            rig.Click(41);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await rig.Idle(1);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal("layer-2", picker.SelectedValue);
            Assert.Equal(2, targetVm.SelectedTartarusKeymap);
            rig.Press(42); picker.SelectedValue = "layer-3";
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal("layer-2", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
            Assert.Equal("layer-2", picker.SelectedValue); // refused selection rolls back visually
            rig.Release(42);
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }
}
