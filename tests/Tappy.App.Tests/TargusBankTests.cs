using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Input;
using Tappy.Core.Output;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Fact]
    public async Task Targus_reports_keep_stable_boxes_separate_assignments_and_balanced_held_output()
    {
        var root = NewRoot();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_05A4&PID_9862#BANK_TEST");
            var host = new TestHost();
            var sink = new BankKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(descriptor), host, keyboardIsNeutral: () => true),
                sink, new AtomicProfileStore(root));
            await using var vm = new MainViewModel(runtime, action => action(), (_, _) => { });
            await vm.InitializeAsync();
            vm.SelectedDevice = vm.Devices.Single();
            vm.BeginIdentification();
            host.EmitKey(41, RawKeyboardFlags.Make);
            host.EmitKey(41, RawKeyboardFlags.Break);
            vm.ConfirmController();
            Assert.Equal(29, vm.Controls.Count);
            var stableTiles = vm.Controls.ToDictionary(c => c.ControlId, c => (Tile: c, c.X, c.Y, c.Label));
            Assert.Contains("numeric", vm.InputBankLabel);
            Assert.Equal("#48FF63", vm.NumLockLedBrush);
            var seven = TargusKeypadLayout.Key(0x47);
            var enter = TargusKeypadLayout.Key(0x1C, true);
            var tile = vm.Controls.Single(key => key.ControlId == seven);
            var originalPosition = (tile.X, tile.Y);
            Assert.True(runtime.AssignMapping(seven, "F13").Succeeded);
            Tap(0x45, 0x90, false);
            Assert.True(runtime.AssignMapping(TargusKeypadLayout.Key(0x47, true), "F14").Succeeded);
            Tap(0x45, 0x90, false);
            var edit = runtime.BeginAssignmentEdit(seven)!;
            runtime.IsRehearsal = false;
            host.EmitKey(41, RawKeyboardFlags.Make, 0x47, 0x67); // Hold numeric 7.
            Tap(0x47, 0x24, true); // A separate navigation report while numeric output is held.
            Assert.Contains("navigation", vm.InputBankLabel);
            Assert.Equal("#122C19", vm.NumLockLedBrush);
            Assert.True(tile.IsPressed);
            Assert.Equal(["F14"], sink.Ups);
            Assert.True(runtime.AssignKeyboardMapping(edit.Target,
                KeyboardMappingAssignment.HoldUntilRelease("Hold F13 until release", ["F13"])).Succeeded);
            host.EmitKey(41, RawKeyboardFlags.Break, 0x47, 0x67);
            Assert.Equal(["F13", "F14"], sink.Downs);
            Assert.Equal(["F14", "F13"], sink.Ups);
            Assert.Same(tile, vm.Controls.Single(key => key.ControlId == seven));
            Assert.Equal(originalPosition, (tile.X, tile.Y));
            Assert.Equal("Hold F13 until release", tile.Action);
            Assert.Equal(29, vm.Controls.Count);
            foreach (var current in vm.Controls)
            {
                var before = stableTiles[current.ControlId];
                Assert.Same(before.Tile, current);
                Assert.Equal((before.X, before.Y, before.Label), (current.X, current.Y, current.Label));
            }
            Tap(0x45, 0x90, false);
            Tap(0x4A, 0x6D, false);
            Tap(0x45, 0x90, true);
            Assert.Contains("navigation", vm.InputBankLabel);
            var numLock = vm.Controls.Single(c => c.ControlId == TargusKeypadLayout.Key(0x45, true));
            Assert.False(numLock.IsIlluminated);
            Assert.False(numLock.CanAssign);
            Assert.Null(runtime.BeginAssignmentEdit(numLock.ControlId));
            Assert.True(vm.Controls.Single(key => key.ControlId == TargusKeypadLayout.Key(0x4C)).CanAssign);
            Assert.False(vm.Controls.Single(key => key.ControlId == TargusKeypadLayout.TripleZeroReference).CanAssign);
            Assert.NotNull(runtime.BeginAssignmentEdit(enter));
            Tap(0x1C, 0x0D, false);
            Assert.True(runtime.AssignMapping(TargusKeypadLayout.Key(0x1C), "F16").Succeeded);
            Tap(0x45, 0x90, false);
            Assert.True(runtime.AssignKeyboardMapping(edit.Target,
                KeyboardMappingAssignment.HoldUntilRelease("Hold F13 until release", ["F13"])).Succeeded);
            Assert.NotNull(runtime.BeginAssignmentEdit(enter));
            Tap(0x1C, 0x0D, true);
            Assert.True(runtime.AssignMapping(enter, "F17").Succeeded);
            Assert.Equal("Hold F17 until release", runtime.GetControllerAction(enter)!.Name);
            Assert.Equal("Hold F13 until release", runtime.GetControllerAction(seven)!.Name);
            Tap(0x45, 0x90, false);
            Assert.Equal("Hold F16 until release", runtime.GetControllerAction(TargusKeypadLayout.Key(0x1C))!.Name);
            await vm.SaveControllerLayoutAsync();
            Assert.DoesNotContain("changed while", vm.MappingStatus);
            await runtime.SaveProfileAsync();
            // Reload without copying live lock state. Bindings survive; state is
            // freshly inferred from the identification press, not a saved LED flag.
            var secondHost = new TestHost();
            await using var reloaded = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(descriptor), secondHost, keyboardIsNeutral: () => true),
                new BankKeyboard(), new AtomicProfileStore(root));
            await reloaded.InitializeAsync();
            reloaded.BeginIdentification(reloaded.Devices.Single());
            secondHost.EmitKey(41, RawKeyboardFlags.E0, 0x47, 0x24);
            secondHost.EmitKey(41, RawKeyboardFlags.E0 | RawKeyboardFlags.Break, 0x47, 0x24);
            reloaded.ConfirmController();
            Assert.Equal("Hold F14 until release", reloaded.GetControllerAction(TargusKeypadLayout.Key(0x47, true))!.Name);
            secondHost.EmitKey(41, RawKeyboardFlags.Make, 0x47, 0x67);
            secondHost.EmitKey(41, RawKeyboardFlags.Break, 0x47, 0x67);
            Assert.Equal("Hold F13 until release", reloaded.GetControllerAction(seven)!.Name);

            void Tap(ushort scan, ushort vk, bool extended)
            {
                var flags = extended ? RawKeyboardFlags.E0 : RawKeyboardFlags.Make;
                host.EmitKey(41, flags, scan, vk);
                host.EmitKey(41, flags | RawKeyboardFlags.Break, scan, vk);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Targus_bank_indicator_ignores_other_keyboard_and_resets_after_disconnect()
    {
        var root = NewRoot();
        try
        {
            var first = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_05A4&PID_9862#A");
            var second = DeviceDescriptorSanitizer.CreateKeyboard((nint)42, @"\\?\HID#VID_05A4&PID_9862#B");
            var host = new TestHost();
            var devices = new KeyboardEnumerator(first, second);
            await using var a = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(
                devices, new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                new BankKeyboard(), new AtomicProfileStore(Path.Combine(root, "a")));
            await using var b = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(
                devices, new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                new BankKeyboard(), new AtomicProfileStore(Path.Combine(root, "b")));
            await using var avm = new MainViewModel(a, action => action(), (_, _) => { });
            await using var bvm = new MainViewModel(b, action => action(), (_, _) => { });
            await avm.InitializeAsync(); await bvm.InitializeAsync();
            Confirm(avm, first.SessionId, 41); Confirm(bvm, second.SessionId, 42);
            host.EmitKey(41, RawKeyboardFlags.E0, 0x47, 0x24);
            host.EmitKey(41, RawKeyboardFlags.E0 | RawKeyboardFlags.Break, 0x47, 0x24);
            Assert.Contains("navigation", avm.InputBankLabel);
            Assert.Contains("numeric", bvm.InputBankLabel);
            devices.Devices = [second];
            host.EmitRemoval(41);
            Assert.False(avm.HasInputBank);
            Assert.Contains("numeric", bvm.InputBankLabel);

            void Confirm(MainViewModel vm, string session, nint handle)
            {
                vm.SelectedDevice = vm.Devices.Single(device => device.SessionId == session);
                vm.BeginIdentification();
                host.EmitKey(handle, RawKeyboardFlags.Make);
                host.EmitKey(handle, RawKeyboardFlags.Break);
                vm.ConfirmController();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class BankKeyboard : IKeyboardOutput
    {
        internal List<string> Downs { get; } = [];
        internal List<string> Ups { get; } = [];
        public void KeyDown(KeyboardOutputRequest request) => Downs.AddRange(request.Keys.Select(key => key.Value));
        public void KeyUp(KeyboardOutputRequest request) => Ups.AddRange(request.Keys.Select(key => key.Value));
    }
}

public sealed class TargusBankStateTests
{
    [Fact]
    public void Unknown_is_not_guessed_repeats_do_not_toggle_and_fresh_reports_correct_inference()
    {
        var state = new TargusBankState();
        state.Observe(new(TargusKeypadLayout.Key(0x45)), ControlSignalKind.Press);
        Assert.Null(state.NumLockOn);
        state.Observe(new(TargusKeypadLayout.Key(0x47)), ControlSignalKind.Press);
        Assert.True(state.NumLockOn);
        state.Observe(new(TargusKeypadLayout.Key(0x45)), ControlSignalKind.Repeat);
        state.Observe(new(TargusKeypadLayout.Key(0x45)), ControlSignalKind.Release);
        Assert.True(state.NumLockOn);
        state.Observe(new(TargusKeypadLayout.Key(0x45)), ControlSignalKind.Press);
        Assert.True(state.NumLockOn);
        state.Observe(new(TargusKeypadLayout.Key(0x52)), ControlSignalKind.Press);
        Assert.True(state.NumLockOn);
        state.Reset();
        Assert.Null(state.NumLockOn);
    }

    [Fact]
    public void Exact_report_assignments_remain_available_in_both_modes()
    {
        var state = new TargusBankState();
        var enter = TargusKeypadLayout.Key(0x1C, true);
        var report = TargusKeypadLayout.Key(0x1C);
        state.Observe(new(TargusKeypadLayout.Key(0x47)), ControlSignalKind.Press);
        Assert.Equal(enter, state.AssignmentReport(enter));
        state.Observe(new(report), ControlSignalKind.Press);
        Assert.Equal(enter, state.AssignmentReport(enter));
        Assert.Equal(report, state.AssignmentReport(report));
        state.Observe(new(TargusKeypadLayout.Key(0x47, true)), ControlSignalKind.Press);
        Assert.Equal(enter, state.AssignmentReport(enter));
        state.Observe(new(report), ControlSignalKind.Press);
        Assert.Equal(enter, state.AssignmentReport(enter));
        Assert.Equal(report, state.AssignmentReport(report));
        Assert.Equal(TargusKeypadLayout.Key(0x4E), state.AssignmentReport(TargusKeypadLayout.Key(0x4E)));
        Assert.Equal(TargusKeypadLayout.Key(0x4C), state.AssignmentReport(TargusKeypadLayout.Key(0x4C)));
        Assert.Null(state.AssignmentReport(TargusKeypadLayout.Key(0x45)));
        Assert.Null(state.AssignmentReport(TargusKeypadLayout.TripleZeroReference));
    }
}
