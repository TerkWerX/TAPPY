using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Profiles;

namespace Tappy.Core.Tests;

public sealed class CombinationTests
{
    private static readonly ControlId[] Keys = [new("button-a"), new("button-b"), new("button-c")];
    private static (MappingEngine Engine, RecordingKeyboardOutput Output, TappyProfile Profile) Create(bool banks = true)
    {
        var controller = ControllerProfile.Create(TestProfiles.Identity(), Keys);
        for (var mask = 1; mask < 8; mask++)
        {
            var members = Keys.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            var id = members.Count == 1 ? members[0] : ControlCombination.IdFor(members);
            if (members.Count > 1) { controller.Combinations.Add(new() { ControlId = id, Name = $"Combo {mask}", Members = members }); }
            if (banks) { controller.BankControls.Add(new(id, "raw-input", "target", "Target", BankControlOperation.Select, $"layer-{mask}")); }
            else { controller.Layers[0].Bindings.Add(new() { ControlId = id, PressAction = KeyboardActionDefinition.Tap($"F{mask + 12}") }); }
        }
        var profile = new TappyProfile { Controllers = [controller] };
        var output = new RecordingKeyboardOutput();
        var engine = new MappingEngine(output, new() { SelfInjectionMarker = TestProfiles.Marker }, new FakeClock());
        engine.SetProfile(profile.CreateSnapshot()); TestProfiles.Activate(engine, Keys[0]);
        return (engine, output, profile);
    }
    private static MappingResult Send(MappingEngine e, int key, bool down, bool cleanup = false) =>
        e.Process(ControlSignal.Physical(TestProfiles.Session(), Keys[key], down ? ControlSignalKind.Press : ControlSignalKind.Release, 10) with { IsCleanup = cleanup });

    [Fact]
    public void Every_subset_and_press_release_order_dispatches_only_largest_match()
    {
        var orders = new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        foreach (var mask in Enumerable.Range(1, 7))
            foreach (var pressOrder in orders)
                foreach (var releaseOrder in orders)
                {
                    var (engine, output, _) = Create();
                    var held = pressOrder.Where(i => (mask & (1 << i)) != 0).ToArray();
                    var released = releaseOrder.Where(held.Contains).ToArray();
                    foreach (var key in held) { Assert.Null(Send(engine, key, true).BankControl); Assert.Null(Send(engine, key, true).BankControl); }
                    for (var i = 0; i < released.Length; i++)
                    {
                        var result = Send(engine, released[i], false);
                        if (i < released.Length - 1) { Assert.Null(result.BankControl); }
                        else { Assert.Equal($"layer-{mask}", result.BankControl?.LayerId); }
                    }
                    Assert.Empty(output.Events);
                }
    }

    [Theory]
    [InlineData("cleanup")]
    [InlineData("stop")]
    [InlineData("disconnect")]
    [InlineData("lifecycle")]
    [InlineData("rehearsal-change")]
    [InlineData("profile")]
    public void Interrupted_combination_never_fires(string reason)
    {
        var (engine, output, profile) = Create(); Send(engine, 0, true); Send(engine, 1, true);
        if (reason == "stop") { engine.EmergencyStop(); }
        if (reason == "disconnect") { engine.DisconnectController(TestProfiles.Session()); }
        if (reason == "lifecycle") { engine.ResetForLifecycleTransition(); }
        if (reason == "rehearsal-change") { engine.SetRehearsalMode(!engine.RehearsalMode); }
        if (reason == "profile") { engine.SetProfile(profile.CreateSnapshot()); }
        Assert.Null(Send(engine, 0, false, reason == "cleanup").BankControl);
        Assert.Null(Send(engine, 1, false).BankControl); Assert.Empty(output.Events);
    }

    [Fact]
    public void Run_once_combo_suppresses_members_and_rehearsal_suppresses_output()
    {
        var (engine, output, _) = Create(false); engine.SetRehearsalMode(false);
        Send(engine, 0, true); Send(engine, 1, true); Assert.Empty(output.Events);
        Send(engine, 0, false); Assert.Empty(output.Events); Send(engine, 1, false);
        Assert.Equal(new[] { "F15" }, output.DownKeys); Assert.Equal(output.DownKeys, output.UpKeys);
        engine.SetRehearsalMode(true); Send(engine, 0, true); Send(engine, 1, true); Send(engine, 0, false); Send(engine, 1, false);
        Assert.Single(output.DownKeys);
    }

    [Fact]
    public void Ambiguous_rolling_pairs_and_second_strokes_do_not_choose_arbitrary_actions()
    {
        var (engine, output, _) = Create();
        Send(engine, 0, true); Send(engine, 1, true); Send(engine, 0, false);
        Send(engine, 2, true); Send(engine, 1, false); Assert.Null(Send(engine, 2, false).BankControl);
        Send(engine, 0, true); Send(engine, 1, true); Send(engine, 0, false); Send(engine, 0, true);
        Send(engine, 0, false); Assert.Null(Send(engine, 1, false).BankControl); Assert.Empty(output.Events);
    }

    [Fact]
    public void Combination_profiles_roundtrip_deeply_and_reject_nested_or_forged_ids()
    {
        var (_, _, profile) = Create(); var serializer = new ProfileSerializer();
        var snapshot = serializer.Deserialize(serializer.Serialize(profile));
        profile.Controllers[0].Combinations[0].Members.Clear();
        Assert.Equal(2, snapshot.Controllers[0].Combinations[0].Members.Count);
        var editable = snapshot.ToEditableProfile(); editable.Controllers[0].Combinations[0].Members.Clear();
        Assert.Equal(2, snapshot.Controllers[0].Combinations[0].Members.Count);
        Assert.Throws<InvalidDataException>(() => editable.CreateSnapshot());
    }
}
