using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Profiles;

namespace Tappy.Core.Tests;

public sealed class BankControlTests
{
    private static readonly ControlId Key = ControlId.FromRawInputKeyboard(0x4F);
    private static readonly BankControlBinding Command = new(Key, "raw-input", "destination", "Tartarus", BankControlOperation.Next);

    private static (MappingEngine Engine, RecordingKeyboardOutput Output, TappyProfile Profile) Create()
    {
        var controller = ControllerProfile.Create(TestProfiles.Identity(), [Key]);
        controller.BankControls.Add(Command);
        foreach (var layer in controller.Layers)
        { layer.Bindings.Add(new() { ControlId = Key, Name = "Keep me", PressAction = KeyboardActionDefinition.Hold("F24"), ReleaseAction = KeyboardActionDefinition.Tap("F22") }); }
        var profile = new TappyProfile { Controllers = [controller] };
        var output = new RecordingKeyboardOutput();
        var engine = new MappingEngine(output, new() { SelfInjectionMarker = TestProfiles.Marker }, new FakeClock());
        engine.SetProfile(profile.CreateSnapshot());
        TestProfiles.Activate(engine, Key);
        return (engine, output, profile);
    }
    private static ControlSignal Signal(ControlSignalKind kind) => ControlSignal.Physical(TestProfiles.Session(), Key, kind, 10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Navigation_overrides_but_preserves_macros_in_every_bank_and_only_emits_on_release(bool rehearsal)
    {
        var (engine, output, profile) = Create();
        engine.SetRehearsalMode(rehearsal);
        foreach (var layer in profile.Controllers[0].Layers)
        {
            Assert.True(engine.SetActiveLayer(TestProfiles.Session(), layer.Id));
            Assert.Null(engine.Process(Signal(ControlSignalKind.Release)).BankControl);
            Assert.Null(engine.Process(Signal(ControlSignalKind.Press)).BankControl);
            Assert.Null(engine.Process(Signal(ControlSignalKind.Repeat)).BankControl);
            Assert.Null(engine.Process(Signal(ControlSignalKind.Press)).BankControl);
            var result = engine.Process(Signal(ControlSignalKind.Release));
            Assert.Equal(Command, result.BankControl);
            Assert.False(result.ProducedOutput);
            Assert.Null(engine.Process(Signal(ControlSignalKind.Release)).BankControl);
        }
        Assert.Empty(output.Events);
        Assert.All(profile.Controllers[0].Layers, layer => Assert.Equal("Keep me", Assert.Single(layer.Bindings).Name));
    }

    [Theory]
    [InlineData("cleanup")]
    [InlineData("stop")]
    [InlineData("disconnect")]
    [InlineData("lifecycle")]
    public void Safety_cleanup_cannot_trigger_a_bank_change(string interruption)
    {
        var (engine, output, _) = Create();
        engine.Process(Signal(ControlSignalKind.Press));
        if (interruption == "stop") { engine.EmergencyStop(); }
        if (interruption == "disconnect") { engine.DisconnectController(TestProfiles.Session()); }
        if (interruption == "lifecycle") { engine.ResetForLifecycleTransition(); }
        Assert.Null(engine.Process(Signal(ControlSignalKind.Release) with { IsCleanup = interruption == "cleanup" }).BankControl);
        Assert.Empty(output.Events);
    }

    [Fact]
    public void Removing_overlay_restores_original_binding_and_snapshots_are_immutable()
    {
        var (engine, output, profile) = Create();
        var snapshot = profile.CreateSnapshot();
        profile.Controllers[0].BankControls.Clear();
        Assert.Single(snapshot.Controllers[0].BankControls);
        engine.SetProfile(profile.CreateSnapshot());
        engine.SetRehearsalMode(false);
        engine.Process(Signal(ControlSignalKind.Press));
        engine.Process(Signal(ControlSignalKind.Release));
        Assert.Equal(new[] { "F24", "F22" }, output.DownKeys);
    }

    [Fact]
    public void Bank_controls_roundtrip_and_old_profiles_upgrade_without_losing_macros()
    {
        var (_, _, profile) = Create();
        var serializer = new ProfileSerializer();
        var json = serializer.Serialize(profile);
        var loaded = serializer.Deserialize(json).ToEditableProfile();
        Assert.Equal(Command, Assert.Single(loaded.Controllers[0].BankControls));
        Assert.All(loaded.Controllers[0].Layers, layer => Assert.Equal("Keep me", Assert.Single(layer.Bindings).Name));
        loaded.Controllers[0].BankControls.Clear();
        var oldJson = serializer.Serialize(loaded).Replace(
            $"\"SchemaVersion\": {TappyProfile.CurrentSchemaVersion}", "\"SchemaVersion\": 3");
        var upgraded = serializer.Deserialize(oldJson);
        Assert.Equal(TappyProfile.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.Empty(upgraded.Controllers[0].BankControls);
        loaded.Controllers[0].BankControls.Add(Command with { TargetPersistentId = "" });
        Assert.Throws<InvalidDataException>(() => loaded.CreateSnapshot());
    }
}
