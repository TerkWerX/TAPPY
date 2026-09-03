using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class InputStateAndActivationTests
{
    [Fact]
    public void StateTrackerKeepsSimultaneousControlsAndDevicesIndependent()
    {
        var tracker = new ControllerInputStateTracker();
        var firstDevice = new ControllerSessionId("first");
        var secondDevice = new ControllerSessionId("second");
        var a = ControlId.FromRawInputKeyboard(0x001E);
        var b = ControlId.FromRawInputKeyboard(0x0030);

        tracker.Apply(ControlSignal.Physical(firstDevice, a, ControlSignalKind.Press, 1));
        var simultaneous = tracker.Apply(ControlSignal.Physical(firstDevice, b, ControlSignalKind.Press, 2));
        tracker.Apply(ControlSignal.Physical(secondDevice, a, ControlSignalKind.Press, 3));

        Assert.Equal(2, simultaneous.PressedControls.Count);
        Assert.Equal([a, b], tracker.GetPressedControls(firstDevice));
        Assert.Equal([a], tracker.GetPressedControls(secondDevice));
        tracker.Disconnect(firstDevice);
        Assert.Empty(tracker.GetPressedControls(firstDevice));
        Assert.Equal([a], tracker.GetPressedControls(secondDevice));
    }

    [Fact]
    public void DuplicateMakeAndExplicitRepeatDoNotInventPhysicalTaps()
    {
        var tracker = new ControllerInputStateTracker();
        var session = TestProfiles.Session();
        var key = ControlId.FromRawInputKeyboard(0x004F);

        var press = tracker.Apply(ControlSignal.Physical(session, key, ControlSignalKind.Press, 1));
        var repeatedMake = tracker.Apply(ControlSignal.Physical(session, key, ControlSignalKind.Press, 2));
        var explicitRepeat = tracker.Apply(ControlSignal.Physical(session, key, ControlSignalKind.Repeat, 3));
        var release = tracker.Apply(ControlSignal.Physical(session, key, ControlSignalKind.Release, 4));
        var invalidRepeat = tracker.Apply(ControlSignal.Physical(session, key, ControlSignalKind.Repeat, 5));

        Assert.Equal(ControlSignalKind.Press, press.EffectiveKind);
        Assert.True(repeatedMake.IsRepeat);
        Assert.True(explicitRepeat.IsRepeat);
        Assert.Equal(ControlSignalKind.Release, release.EffectiveKind);
        Assert.False(invalidRepeat.Accepted);
    }

    [Fact]
    public void ActivationRequiresExplicitSelectIdentifyReleaseAndConfirm()
    {
        var tracker = new ControllerInputStateTracker();
        var gate = new ControllerActivationGate(tracker);
        var session = TestProfiles.Session();
        var key = ControlId.FromRawInputKeyboard(0x004F);

        Assert.Equal(ControllerActivationState.Idle, gate.State);
        Assert.Equal(ControllerActivationState.AwaitingIdentificationPress, gate.SelectCandidate(session));
        Assert.Throws<InvalidOperationException>(gate.Confirm);

        gate.Observe(tracker.Apply(ControlSignal.Physical(session, key, ControlSignalKind.Press, 1)));
        Assert.Equal(ControllerActivationState.AwaitingIdentificationRelease, gate.State);
        Assert.Throws<InvalidOperationException>(gate.Confirm);

        gate.Observe(tracker.Apply(ControlSignal.Physical(session, key, ControlSignalKind.Release, 2)));
        Assert.Equal(ControllerActivationState.AwaitingConfirmation, gate.State);
        gate.Confirm();

        Assert.True(gate.IsActive(session));
        Assert.Equal(ControllerActivationState.Active, gate.State);
    }

    [Fact]
    public void ActivationWaitsForEverySimultaneouslyHeldControlRegardlessOfReleaseOrder()
    {
        var tracker = new ControllerInputStateTracker();
        var gate = new ControllerActivationGate(tracker);
        var session = TestProfiles.Session();
        var identification = ControlId.FromRawInputKeyboard(0x004F);
        var simultaneouslyHeld = ControlId.FromRawInputKeyboard(0x0050);

        gate.SelectCandidate(session);
        gate.Observe(tracker.Apply(ControlSignal.Physical(
            session, identification, ControlSignalKind.Press, 1)));
        gate.Observe(tracker.Apply(ControlSignal.Physical(
            session, simultaneouslyHeld, ControlSignalKind.Press, 2)));
        gate.Observe(tracker.Apply(ControlSignal.Physical(
            session, identification, ControlSignalKind.Release, 3)));

        Assert.Equal(ControllerActivationState.AwaitingIdentificationRelease, gate.State);
        Assert.Throws<InvalidOperationException>(gate.Confirm);

        gate.Observe(tracker.Apply(ControlSignal.Physical(
            session, simultaneouslyHeld, ControlSignalKind.Release, 4)));

        Assert.Equal(ControllerActivationState.AwaitingConfirmation, gate.State);
        gate.Confirm();
        Assert.True(gate.IsActive(session));
    }

    [Fact]
    public void MappingEngineDiscardsUnselectedInputBeforeStateTracking()
    {
        var output = new RecordingKeyboardOutput();
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var engine = TestProfiles.CreateEngine(output,
            [(control, KeyboardActionDefinition.Tap("F24"), null)]);

        var result = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 1));

        Assert.Equal(MappingDisposition.UnselectedController, result.Disposition);
        Assert.Empty(engine.InputStates.GetPressedControls(TestProfiles.Session()));
        Assert.Empty(output.Events);
    }
}
