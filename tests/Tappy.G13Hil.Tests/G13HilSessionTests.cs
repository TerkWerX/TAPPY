using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.G13Hil.Tests;

public sealed class G13HilSessionTests
{
    private static readonly LogitechG13Control[][] SimultaneousSets =
    [
        [LogitechG13Control.G1, LogitechG13Control.G2],
        [LogitechG13Control.G3, LogitechG13Control.G4, LogitechG13Control.G5, LogitechG13Control.G6],
        [
            LogitechG13Control.G7,
            LogitechG13Control.M1,
            LogitechG13Control.JoystickPress,
            LogitechG13Control.StickRight,
        ],
    ];

    [Fact]
    public void CleanSequenceCoversEveryCodeDefinedControlAndAggregateAssertion()
    {
        var clock = 0L;
        var session = new G13HilSession(
            timestampProvider: () => clock,
            timestampFrequency: 1000);

        clock += 100;
        session.MarkNeutralObserved();
        Cycle(session, LogitechG13Control.G1, 1);
        clock += 200;
        CompleteAllControlPrompts(session);
        clock += 300;
        CompleteSimultaneousSets(session);
        clock += 400;
        CompleteDuplicateSweep(session);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.IsComplete);
        Assert.True(snapshot.SessionAssertionsPassed);
        Assert.True(snapshot.NeutralObserved);
        Assert.True(snapshot.HandshakePassed);
        Assert.True(snapshot.AllControlsPassed);
        Assert.True(snapshot.StickDirectionsPassed);
        Assert.True(snapshot.SimultaneousSetsPassed);
        Assert.True(snapshot.DuplicateSweepCompleted);
        Assert.True(snapshot.DuplicateSuppressionPassed);
        Assert.True(snapshot.ExpectedControlGatingPassed);
        Assert.True(snapshot.BalancedTransitions);
        Assert.Equal(39, snapshot.CodeDefinedControlCount);
        Assert.Equal(78, snapshot.CompletedControlCycles);
        Assert.Equal(3, snapshot.SimultaneousSetsPassedCount);
        Assert.Equal(4, snapshot.StickDirectionsPassedCount);
        Assert.Equal(snapshot.AcceptedPresses, snapshot.AcceptedReleases);
        Assert.Equal(0, snapshot.UnexpectedTransitions);
        Assert.Equal(0, snapshot.DuplicateTransitions);
        Assert.Equal(0, snapshot.UnbalancedTransitions);
        Assert.Equal(100, snapshot.NeutralDurationMs);
        Assert.Equal(200, snapshot.ControlsDurationMs);
        Assert.Equal(300, snapshot.SimultaneousDurationMs);
        Assert.Equal(400, snapshot.DuplicateSweepDurationMs);
    }

    [Fact]
    public void DuplicateAndUnbalancedSignalsRemainFatalEvenWhenExpectedPromptCompletes()
    {
        var session = new G13HilSession();
        session.MarkNeutralObserved();
        var originalRevision = session.Snapshot().Prompt.Revision;

        session.Accept(LogitechG13Control.G1, ControlSignalKind.Release);
        session.Accept(LogitechG13Control.G1, ControlSignalKind.Press);
        session.Accept(LogitechG13Control.G1, ControlSignalKind.Press);
        session.Accept(LogitechG13Control.G1, ControlSignalKind.Repeat);

        var beforeBalancedRelease = session.Snapshot();
        Assert.Equal(G13HilPhase.Handshake, beforeBalancedRelease.Prompt.Phase);
        Assert.Equal(originalRevision, beforeBalancedRelease.Prompt.Revision);
        Assert.Equal(0, beforeBalancedRelease.UnexpectedTransitions);
        Assert.Equal(2, beforeBalancedRelease.DuplicateTransitions);
        Assert.Equal(1, beforeBalancedRelease.UnbalancedTransitions);

        session.Accept(LogitechG13Control.G1, ControlSignalKind.Release);
        var after = session.Snapshot();
        Assert.Equal(G13HilPhase.Controls, after.Prompt.Phase);
        Assert.True(after.ExpectedControlGatingPassed);
        Assert.False(after.DuplicateSuppressionPassed);
        Assert.False(after.BalancedTransitions);
    }

    [Fact]
    public void Balanced_wrong_control_retries_prompt_without_poisoning_clean_run()
    {
        var session = new G13HilSession();
        session.MarkNeutralObserved();
        var originalRevision = session.Snapshot().Prompt.Revision;

        Cycle(session, LogitechG13Control.G2, 1);
        var retry = session.Snapshot();

        Assert.Equal(G13HilPhase.Handshake, retry.Prompt.Phase);
        Assert.True(retry.Prompt.Revision > originalRevision);
        Assert.Equal(1, retry.PromptRetries);
        Assert.Equal(0, retry.UnexpectedTransitions);

        Cycle(session, LogitechG13Control.G1, 1);
        CompleteAllControlPrompts(session);
        CompleteSimultaneousSets(session);
        CompleteDuplicateSweep(session);
        var complete = session.Snapshot();

        Assert.True(complete.SessionAssertionsPassed);
        Assert.True(complete.ExpectedControlGatingPassed);
        Assert.Equal(1, complete.PromptRetries);
    }

    [Fact]
    public void SimultaneousPromptRequiresActualOverlapButAllowsBalancedRetry()
    {
        var session = PrepareThroughIndividualControls();

        Cycle(session, LogitechG13Control.G1, 1);
        Assert.Equal(G13HilPhase.SimultaneousSets, session.Phase);
        Assert.Equal(1, session.Snapshot().PromptRetries);

        CompleteSimultaneousSets(session);
        CompleteDuplicateSweep(session);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.SessionAssertionsPassed);
        Assert.Equal(1, snapshot.PromptRetries);
        Assert.Equal(snapshot.AcceptedPresses, snapshot.AcceptedReleases);
    }

    [Fact]
    public void PrematureSweepReleaseCanBeRetriedWithoutPoisoningCleanAttempt()
    {
        var session = PrepareThroughIndividualControls();
        CompleteSimultaneousSets(session);

        session.Accept(LogitechG13Control.G1, ControlSignalKind.Press);
        Cycle(session, LogitechG13Control.StickLeft, 1);
        session.Accept(LogitechG13Control.G1, ControlSignalKind.Release);
        Assert.Equal(G13HilPhase.DuplicateSweep, session.Phase);

        CompleteDuplicateSweep(session);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.IsComplete);
        Assert.True(snapshot.DuplicateSweepCompleted);
        Assert.True(snapshot.DuplicateSuppressionPassed);
        Assert.True(snapshot.SessionAssertionsPassed);
        Assert.Equal(1, snapshot.PromptRetries);
    }

    [Fact]
    public void Perpendicular_stick_crossings_are_tolerated_during_horizontal_duplicate_sweep()
    {
        var session = PrepareThroughIndividualControls();
        CompleteSimultaneousSets(session);

        session.Accept(LogitechG13Control.G1, ControlSignalKind.Press);
        for (var index = 0; index < G13HilSession.RequiredSweepCyclesPerDirection; index++)
        {
            Cycle(session, LogitechG13Control.StickLeft, 1);
            Cycle(session, LogitechG13Control.StickUp, 1);
            Cycle(session, LogitechG13Control.StickRight, 1);
            Cycle(session, LogitechG13Control.StickDown, 1);
        }

        session.Accept(LogitechG13Control.G1, ControlSignalKind.Release);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.IsComplete);
        Assert.True(snapshot.SessionAssertionsPassed);
        Assert.True(snapshot.DuplicateSweepCompleted);
        Assert.True(snapshot.DuplicateSuppressionPassed);
        Assert.True(snapshot.ExpectedControlGatingPassed);
        Assert.Equal(0, snapshot.UnexpectedTransitions);
    }

    [Fact]
    public void NeutralMarkIsIdempotentAndInputBeforeNeutralIsIgnored()
    {
        var session = new G13HilSession();
        session.Accept(LogitechG13Control.G1, ControlSignalKind.Press);
        session.Accept(LogitechG13Control.G1, ControlSignalKind.Release);
        Assert.Equal(G13HilPhase.AwaitNeutral, session.Phase);

        session.MarkNeutralObserved();
        var revision = session.Snapshot().Prompt.Revision;
        session.MarkNeutralObserved();

        Assert.Equal(G13HilPhase.Handshake, session.Phase);
        Assert.Equal(revision, session.Snapshot().Prompt.Revision);
        Assert.Equal(0, session.Snapshot().AcceptedPresses);
    }

    internal static G13HilSession CreateCompleteCleanSession()
    {
        var session = PrepareThroughIndividualControls();
        CompleteSimultaneousSets(session);
        CompleteDuplicateSweep(session);
        return session;
    }

    private static G13HilSession PrepareThroughIndividualControls()
    {
        var session = new G13HilSession();
        session.MarkNeutralObserved();
        Cycle(session, LogitechG13Control.G1, 1);
        CompleteAllControlPrompts(session);
        Assert.Equal(G13HilPhase.SimultaneousSets, session.Phase);
        return session;
    }

    private static void CompleteAllControlPrompts(G13HilSession session)
    {
        foreach (var definition in LogitechG13InputProvider.SupportedControls)
        {
            Cycle(session, definition.Control, G13HilSession.RequiredCyclesPerControl);
        }
    }

    private static void CompleteSimultaneousSets(G13HilSession session)
    {
        foreach (var controls in SimultaneousSets)
        {
            foreach (var control in controls)
            {
                session.Accept(control, ControlSignalKind.Press);
            }

            foreach (var control in controls.AsEnumerable().Reverse())
            {
                session.Accept(control, ControlSignalKind.Release);
            }
        }
    }

    private static void CompleteDuplicateSweep(G13HilSession session)
    {
        session.Accept(LogitechG13Control.G1, ControlSignalKind.Press);
        for (var index = 0; index < G13HilSession.RequiredSweepCyclesPerDirection; index++)
        {
            Cycle(session, LogitechG13Control.StickLeft, 1);
            Cycle(session, LogitechG13Control.StickRight, 1);
        }

        session.Accept(LogitechG13Control.G1, ControlSignalKind.Release);
    }

    private static void Cycle(
        G13HilSession session,
        LogitechG13Control control,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            session.Accept(control, ControlSignalKind.Press);
            session.Accept(control, ControlSignalKind.Release);
        }
    }
}
