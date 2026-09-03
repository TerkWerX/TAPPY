using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class MappingEngineTests
{
    [Fact]
    public void SelfInjectionMarkerMustFitRawInputExtraInformation()
    {
        var options = new MappingEngineOptions
        {
            SelfInjectionMarker = (ulong)uint.MaxValue + 1
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MappingEngine(new RecordingKeyboardOutput(), options, new FakeClock()));
    }

    [Fact]
    public void RehearsalRecognizesPressAndReleaseWithoutOutput()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Hold("Ctrl", "F24"), KeyboardActionDefinition.Tap("F23"))]);
        TestProfiles.Activate(engine, control);
        engine.SetRehearsalMode(true);

        var press = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 10));
        var release = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Release, 11));

        Assert.Equal(MappingDisposition.Rehearsal, press.Disposition);
        Assert.Equal(MappingDisposition.Rehearsal, release.Disposition);
        Assert.Empty(output.Events);
    }

    [Fact]
    public void SelfMarkerAndDeviceLessSignalsAreRejectedBeforeTracking()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Tap("F24"), null)]);
        TestProfiles.Activate(engine, control);

        var markerWithoutInjectedFlag = new ControlSignal(TestProfiles.Session(), control,
            ControlSignalKind.Press, 10, new InputInjectionMetadata(false, TestProfiles.Marker));
        var self = engine.Process(markerWithoutInjectedFlag);
        var deviceLess = engine.Process(new ControlSignal(null, control, ControlSignalKind.Press, 11,
            InputInjectionMetadata.Physical));

        Assert.Equal(MappingDisposition.SelfInjectedRejected, self.Disposition);
        Assert.Equal(MappingDisposition.DeviceLessRejected, deviceLess.Disposition);
        Assert.Empty(engine.InputStates.GetPressedControls(TestProfiles.Session()));
        Assert.Empty(output.Events);
    }

    [Fact]
    public void NeedsAttentionSourceIsNeverArmed()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Tap("F24"), null)],
            effectiveSourceMode: EffectiveSourceMode.NeedsAttention,
            requestedSourceMode: RequestedSourceMode.Exclusive);
        TestProfiles.Activate(engine, control);

        var result = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 10));

        Assert.Equal(MappingDisposition.SourceNeedsAttention, result.Disposition);
        Assert.Equal(EffectiveSourceMode.NeedsAttention, result.FrozenSourceMode);
        Assert.Empty(output.Events);
    }

    [Fact]
    public void RepeatIsTrackedWithoutDispatchingAnotherTap()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Tap("F24"), null)]);
        TestProfiles.Activate(engine, control);

        var press = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 10));
        var repeat = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 11));

        Assert.Equal(MappingDisposition.Handled, press.Disposition);
        Assert.Equal(MappingDisposition.Tracked, repeat.Disposition);
        Assert.Equal(["F24"], output.DownKeys);
        Assert.Equal(["F24"], output.UpKeys);
        Assert.All(output.Events, item => Assert.Equal(TestProfiles.Marker, item.Request.InjectionMarker));
        Assert.All(output.Events, item => Assert.Single(item.Request.Ancestry.Nodes));
    }

    [Fact]
    public void SharedHeldModifierSurvivesUntilFinalControlRelease()
    {
        var output = new RecordingKeyboardOutput();
        var first = ControlId.FromRawInputKeyboard(0x004F);
        var second = ControlId.FromRawInputKeyboard(0x0050);
        var engine = TestProfiles.CreateEngine(output,
        [
            (first, KeyboardActionDefinition.Hold("Ctrl", "A"), null),
            (second, KeyboardActionDefinition.Hold("Ctrl", "B"), null)
        ]);
        TestProfiles.Activate(engine, first);

        engine.Process(ControlSignal.Physical(TestProfiles.Session(), first, ControlSignalKind.Press, 10));
        engine.Process(ControlSignal.Physical(TestProfiles.Session(), second, ControlSignalKind.Press, 11));
        engine.Process(ControlSignal.Physical(TestProfiles.Session(), first, ControlSignalKind.Release, 12));

        Assert.Equal(["CTRL", "A", "B"], output.DownKeys);
        Assert.Equal(["A"], output.UpKeys);

        engine.Process(ControlSignal.Physical(TestProfiles.Session(), second, ControlSignalKind.Release, 13));

        Assert.Equal(["A", "B", "CTRL"], output.UpKeys);
    }

    [Fact]
    public void DisconnectReleasesOnlyThatControllersOwnedOutputs()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var firstIdentity = TestProfiles.Identity("session-a", "controller-a");
        var secondIdentity = TestProfiles.Identity("session-b", "controller-b");
        var first = ControllerProfile.Create(firstIdentity, [control]);
        var second = ControllerProfile.Create(secondIdentity, [control]);
        first.Layers[0].Bindings.Add(Binding(control, KeyboardActionDefinition.Hold("Ctrl", "A")));
        second.Layers[0].Bindings.Add(Binding(control, KeyboardActionDefinition.Hold("Ctrl", "B")));
        var engine = CreateEngine(output, new TappyProfile { Controllers = [first, second] });
        Activate(engine, firstIdentity.SessionId, control, 1);
        Activate(engine, secondIdentity.SessionId, control, 3);
        engine.Process(ControlSignal.Physical(firstIdentity.SessionId, control, ControlSignalKind.Press, 10));
        engine.Process(ControlSignal.Physical(secondIdentity.SessionId, control, ControlSignalKind.Press, 11));

        var firstCleanup = engine.DisconnectController(firstIdentity.SessionId);
        Assert.Equal(1, firstCleanup.ActivePressCount);
        Assert.True(firstCleanup.OutputReleaseSucceeded);
        Assert.Equal(["A"], output.UpKeys);
        Assert.True(engine.Activation.IsActive(secondIdentity.SessionId));

        var secondCleanup = engine.DisconnectController(secondIdentity.SessionId);
        Assert.Equal(1, secondCleanup.ActivePressCount);
        Assert.True(secondCleanup.OutputReleaseSucceeded);
        Assert.Equal(["A", "B", "CTRL"], output.UpKeys);
    }

    [Fact]
    public void EmergencyLifecycleAndProfileChangeCleanupHeldOutputs()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Hold("F24"), null)]);
        TestProfiles.Activate(engine, control);

        engine.Process(ControlSignal.Physical(TestProfiles.Session(), control, ControlSignalKind.Press, 10));
        var emergencyCleanup = engine.EmergencyStop();
        Assert.Equal(1, emergencyCleanup.ActivePressCount);
        Assert.True(emergencyCleanup.OutputReleaseSucceeded);
        Assert.Equal(["F24"], output.UpKeys);
        Assert.Empty(engine.InputStates.GetPressedControls(TestProfiles.Session()));

        var fresh = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 20));
        Assert.Equal(MappingDisposition.Handled, fresh.Disposition);
        var lifecycleCleanup = engine.ResetForLifecycleTransition();
        Assert.Equal(1, lifecycleCleanup.ActivePressCount);
        Assert.True(lifecycleCleanup.OutputReleaseSucceeded);
        Assert.Equal(2, output.UpKeys.Count);

        var freshAfterLifecycle = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 30));
        Assert.Equal(MappingDisposition.Handled, freshAfterLifecycle.Disposition);
        engine.SetProfile(new TappyProfile().CreateSnapshot());
        Assert.Equal(3, output.UpKeys.Count);
        Assert.Empty(engine.InputStates.GetPressedControls(TestProfiles.Session()));
    }

    [Fact]
    public void ReleaseAllAndPhysicalReleaseBypassAcquisitionRateLimits()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Hold("F24"), null)], maximumTransitions: 1);
        TestProfiles.Activate(engine, control);

        Assert.Equal(MappingDisposition.Handled,
            engine.Process(ControlSignal.Physical(TestProfiles.Session(), control, ControlSignalKind.Press, 10)).Disposition);
        var cleanup = engine.ReleaseAll();
        Assert.Equal(1, cleanup.ActivePressCount);
        Assert.True(cleanup.OutputReleaseSucceeded);
        Assert.Equal(["F24"], output.UpKeys);

        Assert.Equal(MappingDisposition.Handled,
            engine.Process(ControlSignal.Physical(TestProfiles.Session(), control, ControlSignalKind.Press, 11)).Disposition);
        engine.Process(ControlSignal.Physical(TestProfiles.Session(), control, ControlSignalKind.Release, 12));

        Assert.Equal(["F24", "F24"], output.UpKeys);
    }

    [Fact]
    public void Cleanup_result_reports_a_rejected_owned_key_release()
    {
        var output = new RejectingReleaseOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var identity = TestProfiles.Identity();
        var controller = ControllerProfile.Create(identity, [control]);
        controller.Layers[0].Bindings.Add(Binding(control, KeyboardActionDefinition.Hold("F24")));
        var engine = new MappingEngine(output, new MappingEngineOptions
        {
            SelfInjectionMarker = TestProfiles.Marker
        }, new FakeClock());
        engine.SetProfile(new TappyProfile { Controllers = [controller] }.CreateSnapshot());
        Activate(engine, identity.SessionId, control, 1);
        Assert.Equal(
            MappingDisposition.Handled,
            engine.Process(ControlSignal.Physical(identity.SessionId, control, ControlSignalKind.Press, 10)).Disposition);
        output.RejectKeyUp = true;

        var cleanup = engine.EmergencyStop();

        Assert.Equal(1, cleanup.ActivePressCount);
        Assert.False(cleanup.OutputReleaseSucceeded);
        Assert.Equal(1, output.KeyUpAttempts);
        Assert.Empty(engine.InputStates.GetPressedControls(identity.SessionId));
    }

    [Fact]
    public void ReleaseUsesBindingLayerAndSourceFrozenAtKeyDown()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var identity = TestProfiles.Identity();
        var controller = ControllerProfile.Create(identity, [control]);
        controller.SourceMode.Requested = RequestedSourceMode.PassThrough;
        controller.SourceMode.Effective = EffectiveSourceMode.PassThrough;
        controller.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            PressAction = KeyboardActionDefinition.Hold("Ctrl"),
            ReleaseAction = KeyboardActionDefinition.Tap("F23")
        });
        controller.Layers[1].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            PressAction = KeyboardActionDefinition.Hold("Shift"),
            ReleaseAction = KeyboardActionDefinition.Tap("F22")
        });
        var engine = CreateEngine(output, new TappyProfile { Controllers = [controller] });
        Activate(engine, identity.SessionId, control, 1);

        var press = engine.Process(ControlSignal.Physical(identity.SessionId, control, ControlSignalKind.Press, 10));
        Assert.True(engine.SetActiveLayer(identity.SessionId, "layer-2"));
        var release = engine.Process(ControlSignal.Physical(identity.SessionId, control, ControlSignalKind.Release, 11));

        Assert.Equal("layer-1", press.FrozenLayerId);
        Assert.Equal("layer-1", release.FrozenLayerId);
        Assert.Equal(EffectiveSourceMode.PassThrough, release.FrozenSourceMode);
        Assert.Equal(["CTRL", "F23"], output.DownKeys);
        Assert.Equal(["CTRL", "F23"], output.UpKeys);
        Assert.DoesNotContain("SHIFT", output.DownKeys);
        Assert.DoesNotContain("F22", output.DownKeys);
    }

    [Fact]
    public void CycleAndDepthAreRejectedBeforeOutput()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Tap("F24"), null)], maximumDepth: 2);
        TestProfiles.Activate(engine, control);
        var route = $"{TestProfiles.Session().Value}|{control.Value}";

        var cycle = engine.Process(new ControlSignal(TestProfiles.Session(), control, ControlSignalKind.Press, 10,
            InputInjectionMetadata.Physical, new ExecutionAncestry("cycle", ["upstream", route])));
        var depth = engine.Process(new ControlSignal(TestProfiles.Session(), control, ControlSignalKind.Press, 11,
            InputInjectionMetadata.Physical, new ExecutionAncestry("depth", ["one", "two"])));

        Assert.Equal(MappingDisposition.CycleRejected, cycle.Disposition);
        Assert.Equal(MappingDisposition.DepthRejected, depth.Disposition);
        Assert.Empty(engine.InputStates.GetPressedControls(TestProfiles.Session()));
        Assert.Empty(output.Events);
    }

    [Fact]
    public void AncestryRejectsAFeedbackCycleAcrossTwoMappings()
    {
        var output = new RecordingKeyboardOutput();
        var first = ControlId.FromRawInputKeyboard(0x004F);
        var second = ControlId.FromRawInputKeyboard(0x0050);
        var engine = TestProfiles.CreateEngine(output,
        [
            (first, KeyboardActionDefinition.Tap("F24"), null),
            (second, KeyboardActionDefinition.Tap("F23"), null)
        ]);
        TestProfiles.Activate(engine, first);

        engine.Process(ControlSignal.Physical(TestProfiles.Session(), first, ControlSignalKind.Press, 10));
        var firstAncestry = output.Events[^1].Request.Ancestry;
        engine.Process(ControlSignal.Physical(TestProfiles.Session(), first, ControlSignalKind.Release, 11));
        engine.Process(new ControlSignal(TestProfiles.Session(), second, ControlSignalKind.Press, 12,
            InputInjectionMetadata.Injected(0x1234, "test"), firstAncestry));
        var secondAncestry = output.Events[^1].Request.Ancestry;
        engine.Process(ControlSignal.Physical(TestProfiles.Session(), second, ControlSignalKind.Release, 13));

        var cycle = engine.Process(new ControlSignal(TestProfiles.Session(), first, ControlSignalKind.Press, 14,
            InputInjectionMetadata.Injected(0x1234, "test"), secondAncestry));

        Assert.Equal(2, secondAncestry.Depth);
        Assert.Equal(MappingDisposition.CycleRejected, cycle.Disposition);
        Assert.Equal(4, output.Events.Count);
    }

    [Fact]
    public void OutputRateGuardIsDeterministicAndWindowBounded()
    {
        var output = new RecordingKeyboardOutput();
        var clock = new FakeClock(10);
        var first = ControlId.FromRawInputKeyboard(0x004F);
        var second = ControlId.FromRawInputKeyboard(0x0050);
        var engine = TestProfiles.CreateEngine(output,
        [
            (first, KeyboardActionDefinition.Tap("F24"), null),
            (second, KeyboardActionDefinition.Tap("F23"), null)
        ], clock: clock, maximumTransitions: 2);
        TestProfiles.Activate(engine, first);

        Assert.Equal(MappingDisposition.Handled,
            engine.Process(ControlSignal.Physical(TestProfiles.Session(), first, ControlSignalKind.Press, 10)).Disposition);
        engine.Process(ControlSignal.Physical(TestProfiles.Session(), first, ControlSignalKind.Release, 11));
        clock.Timestamp = 12;
        Assert.Equal(MappingDisposition.RateLimited,
            engine.Process(ControlSignal.Physical(TestProfiles.Session(), second, ControlSignalKind.Press, 12)).Disposition);
        engine.Process(ControlSignal.Physical(TestProfiles.Session(), second, ControlSignalKind.Release, 13));

        clock.Timestamp = 1_010;
        Assert.Equal(MappingDisposition.Handled,
            engine.Process(ControlSignal.Physical(TestProfiles.Session(), first, ControlSignalKind.Press, 1_010)).Disposition);
        Assert.Equal(["F24", "F24"], output.DownKeys);
    }

    private static ControlBindingDefinition Binding(ControlId control, KeyboardActionDefinition press) => new()
    {
        ControlId = control,
        PressAction = press
    };

    private static MappingEngine CreateEngine(RecordingKeyboardOutput output, TappyProfile profile)
    {
        var engine = new MappingEngine(output, new MappingEngineOptions
        {
            SelfInjectionMarker = TestProfiles.Marker
        }, new FakeClock());
        engine.SetProfile(profile.CreateSnapshot());
        return engine;
    }

    private static void Activate(
        MappingEngine engine,
        ControllerSessionId sessionId,
        ControlId control,
        long timestamp)
    {
        engine.Activation.SelectCandidate(sessionId);
        engine.Process(ControlSignal.Physical(sessionId, control, ControlSignalKind.Press, timestamp));
        engine.Process(ControlSignal.Physical(sessionId, control, ControlSignalKind.Release, timestamp + 1));
        engine.Activation.Confirm();
    }

    private sealed class RejectingReleaseOutput : Tappy.Core.Output.IKeyboardOutput
    {
        internal bool RejectKeyUp { get; set; }

        internal int KeyUpAttempts { get; private set; }

        public void KeyDown(Tappy.Core.Output.KeyboardOutputRequest request)
        {
        }

        public void KeyUp(Tappy.Core.Output.KeyboardOutputRequest request)
        {
            KeyUpAttempts++;
            if (RejectKeyUp)
            {
                throw new InvalidOperationException("Simulated Windows output rejection.");
            }
        }
    }
}
