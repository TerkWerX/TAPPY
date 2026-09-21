using Tappy.App.Services;
using Tappy.App.Runtime;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Fact]
    public async Task Checked_buttons_create_a_separate_movable_saved_tile_and_removal_keeps_physical_tiles()
    {
        await using var rig = new BankRig(); await rig.Start();
        var vm = rig.Workspace.Tabs[0].ViewModel; var runtime = rig.Runtimes[0];
        var members = new[] { BankRig.Key, RazerTartarusLayout.Key(0x50) };
        var count = vm.Controls.Count;
        foreach (var tile in vm.Controls.Where(t => members.Contains(t.ControlId))) { tile.IsGroupSelected = true; }
        Assert.Equal(2, vm.CheckedControlIds().Length);
        Assert.True((await vm.CreateCombinationAsync(runtime.GetBankEndpoint()!, vm.CheckedControlIds())).Succeeded);
        var combo = Assert.Single(vm.Controls, t => t.ControlId.StartsWith("combination:"));
        Assert.Equal(count + 1, vm.Controls.Count); Assert.Empty(vm.CheckedControlIds());
        vm.SelectControl(combo); Assert.True(vm.SelectedIsCombination);
        vm.MoveControl(combo, 1100, 300); await vm.SaveControllerLayoutAsync();
        var x = combo.X; var y = combo.Y;
        rig.Confirm(0, 41);
        var restored = runtime.GetControllerLayout()!.Controls.Single(c => c.ControlId == combo.ControlId);
        Assert.Equal(x, restored.X); Assert.Equal(y, restored.Y);
        vm.SelectControl(vm.Controls.Single(t => t.ControlId == combo.ControlId));
        Assert.True((await vm.RemoveCombinationAsync(vm.BeginSelectedAssignmentEdit()!.Target)).Succeeded);
        Assert.False(vm.SelectedIsCombination); Assert.Equal(count, vm.Controls.Count);
        Assert.All(members, m => Assert.Contains(vm.Controls, t => t.ControlId == m));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Combinations_work_through_both_G13_HID_and_MIDI_provider_paths(bool useMidi)
    {
        var root = NewRoot();
        try
        {
            var host = new TestHost();
            using var backend = new MidiBackend();
            var keyboard = new RawInputKeyboardProvider(new KeyboardEnumerator(), host, keyboardIsNeutral: () => true);
            var output = new RecordingKeyboard();
            await using var runtime = useMidi
                ? new DeviceAwareControllerRuntime(keyboard, output, new AtomicProfileStore(root), midiInputProvider: new WinMmMidiInputProvider(backend, monitorInterval: Timeout.InfiniteTimeSpan))
                : new DeviceAwareControllerRuntime(keyboard, new LogitechG13InputProvider(new G13Enumerator(G13()), host), output, new AtomicProfileStore(root));
            await runtime.InitializeAsync();
            if (useMidi)
            {
                IdentifyMidi(runtime, backend);
                backend.Publish(0x00643C90); backend.Publish(0x00003C80);
            }
            else
            {
                Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
                host.EmitHid(1); host.EmitHid(0); Assert.True(runtime.ConfirmController().Succeeded);
            }
            var members = useMidi ? new[] { "winmm-midi:channel-1:note-60", "winmm-midi:channel-1:note-61" }
                : LogitechG13InputProvider.SupportedControls.Where(c => c.Control is LogitechG13Control.G1 or LogitechG13Control.G2).Select(c => c.ControlId.Value).ToArray();
            Assert.True(runtime.CreateCombination(runtime.GetBankEndpoint()!, members).Succeeded);
            var combo = ControlCombination.IdFor(members.Select(m => new ControlId(m)));
            var target = runtime.GetBankEndpoint()!;
            Assert.True(runtime.SetBankControl(runtime.BeginAssignmentEdit(combo.Value)!.Target, new(combo, target.ProviderId, target.PersistentId, target.DisplayName, BankControlOperation.Next)).Succeeded);
            var requests = 0; runtime.BankControlRequested += (_, _) => requests++;
            if (useMidi) { backend.Publish(0x00643C90); backend.Publish(0x00643D90); backend.Publish(0x00003C80); }
            else { host.EmitHid(1); host.EmitHid(3); host.EmitHid(2); }
            Assert.Equal(0, requests);
            if (useMidi) { backend.Publish(0x00003D80); } else { host.EmitHid(0); }
            Assert.Equal(1, requests); Assert.Equal(0, output.Down);
            Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            Assert.Single((await new AtomicProfileStore(root).LoadAsync()).Controllers.Single().Combinations);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Numpad_combinations_select_pairs_and_toggle_all_on_off_on_exact_target()
    {
        await using var rig = new BankRig(); await rig.Start();
        var source = rig.Runtimes[0]; var target = rig.Runtimes[1].GetBankEndpoint()!;
        var keys = new[] { ControlId.FromRawInputKeyboard(0x35, true), ControlId.FromRawInputKeyboard(0x37), ControlId.FromRawInputKeyboard(0x4A) };
        var layerByMask = new[] { 0, 1, 2, 4, 3, 5, 6, 7 };
        for (var mask = 1; mask <= 7; mask++)
        {
            var members = keys.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            var id = members.Length == 1 ? members[0] : ControlCombination.IdFor(members);
            if (members.Length > 1) { Assert.True(source.CreateCombination(source.GetBankEndpoint()!, members.Select(k => k.Value).ToArray()).Succeeded); }
            var edit = source.BeginAssignmentEdit(id.Value); Assert.NotNull(edit);
            Assert.True(source.SetBankControl(edit.Target, new(id, target.ProviderId, target.PersistentId, target.DisplayName,
                mask == 7 ? BankControlOperation.Toggle : BankControlOperation.Select, $"layer-{layerByMask[mask]}", AlternateLayerId: mask == 7 ? "layer-8" : "")).Succeeded);
        }
        rig.Workspace.Tabs[1].ViewModel.TartarusFollowKeymaps = true;
        void Stroke(int i, bool down) => rig.Host.EmitKey(41, (down ? RawKeyboardFlags.Make : RawKeyboardFlags.Break) | (i == 0 ? RawKeyboardFlags.E0 : 0),
            (ushort)(i == 0 ? 0x35 : i == 1 ? 0x37 : 0x4A), (ushort)(i == 0 ? 0x6F : i == 1 ? 0x6A : 0x6D));
        foreach (var mask in new[] { 1, 2, 4, 3, 5, 6, 7, 7 })
        {
            var before = rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId;
            var members = Enumerable.Range(0, 3).Where(i => (mask & (1 << i)) != 0).ToArray();
            foreach (var i in members) { Stroke(i, true); }
            Assert.Equal(before, rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
            foreach (var i in members) { Stroke(i, false); }
            await rig.Idle(1);
            Assert.Equal(mask == 7 && before == "layer-7" ? "layer-8" : $"layer-{layerByMask[mask]}", rig.Runtimes[1].GetBankEndpoint()!.ActiveLayerId);
            Assert.Equal("layer-1", rig.Runtimes[2].GetBankEndpoint()!.ActiveLayerId);
        }
        Assert.Equal(new byte[] { 1, 2, 4, 3, 5, 6, 7, 0 }, rig.Leds.Calls.Select(c => (byte)c.Mask));
        Assert.Equal(0, rig.Output.Down);
        Assert.True((await source.SaveProfileAsync()).Succeeded);
        var saved = await new AtomicProfileStore(Path.Combine(rig.Root, "slot-1")).LoadAsync();
        Assert.Equal(4, saved.Controllers.Single().Combinations.Count);
        var combo = saved.Controllers.Single().Combinations[0];
        Assert.True(source.RemoveCombination(source.BeginAssignmentEdit(combo.ControlId.Value)!.Target).Succeeded);
        Assert.Null(source.BeginAssignmentEdit(combo.ControlId.Value));
        Assert.NotNull(source.GetBankControl(keys[0].Value));
    }

    [Fact]
    public async Task Combination_creation_rejects_held_members_and_stale_source_without_erasing_assignments()
    {
        await using var rig = new BankRig(); await rig.Start(); var runtime = rig.Runtimes[0];
        var a = BankRig.Key; var b = RazerTartarusLayout.Key(0x50);
        Assert.True(runtime.AssignMapping(a, "F24").Succeeded);
        var result = runtime.CreateCombination(runtime.GetBankEndpoint()!, [a, b]);
        Assert.False(result.Succeeded); Assert.Contains("held/repeating", result.Message);
        Assert.False(runtime.CreateCombination(runtime.GetBankEndpoint()! with { ConnectionToken = "stale" }, [a, b]).Succeeded);
        Assert.True((await runtime.SaveProfileAsync()).Succeeded);
        var profile = await new AtomicProfileStore(Path.Combine(rig.Root, "slot-1")).LoadAsync();
        Assert.Empty(profile.Controllers.Single().Combinations);
        Assert.Contains(profile.Controllers.Single().Layers.SelectMany(l => l.Bindings), binding => binding.ControlId.Value == a);
    }
}
