namespace Tappy.OutputWitness.Tests;

public sealed class OutputWitnessSessionTests
{
    [Fact]
    public void BasicRequiresSourceRepeatAndExactlyOneBalancedOutputCycle()
    {
        var session = CreateSession(OutputWitnessScenario.Basic);

        Source(session, isDown: true);
        Output(session, isDown: true);
        Source(session, isDown: true, repeatCount: 3);
        Output(session, isDown: false);
        Assert.False(session.Snapshot().IsComplete);
        Source(session, isDown: false);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.IsComplete);
        Assert.True(snapshot.ScenarioAssertionsPassed);
        Assert.Equal(4, snapshot.SourceKeyDownUnits);
        Assert.Equal(3, snapshot.SourceRepeatUnits);
        Assert.Equal(1, snapshot.SourceKeyUpUnits);
        Assert.Equal(1, snapshot.OutputKeyDownUnits);
        Assert.Equal(1, snapshot.OutputKeyUpUnits);
        Assert.False(snapshot.SourceKeyHeld);
        Assert.False(snapshot.OutputKeyHeld);
    }

    [Fact]
    public void BasicFailsIfSourceIsReleasedBeforeAnyOsRepeat()
    {
        var session = CreateSession(OutputWitnessScenario.Basic);
        Source(session, isDown: true);
        Output(session, isDown: true);
        Source(session, isDown: false);
        Output(session, isDown: false);

        var snapshot = session.Snapshot();

        Assert.True(snapshot.HasFailed);
        Assert.True(snapshot.CanTerminateFailed);
        Assert.False(snapshot.ScenarioAssertionsPassed);
    }

    [Fact]
    public void DuplicateOrCoalescedOutputDownFailsExactOutputCardinality()
    {
        var session = CreateSession(OutputWitnessScenario.Basic);
        Source(session, isDown: true);
        Source(session, isDown: true, repeatCount: 2);
        Output(session, isDown: true, repeatCount: 2);
        Source(session, isDown: false);
        Output(session, isDown: false);

        var snapshot = session.Snapshot();

        Assert.True(snapshot.HasFailed);
        Assert.False(snapshot.NoUnexpectedOrDuplicateOutputTransitions);
        Assert.Equal(2, snapshot.OutputKeyDownUnits);
        Assert.Equal(1, snapshot.OutputDuplicateDownUnits);
    }

    [Fact]
    public void RehearsalCompletesOnlyAfterSourceCycleAndFixedQuietWindow()
    {
        var clock = 0L;
        var session = CreateSession(
            OutputWitnessScenario.Rehearsal,
            () => clock,
            timestampFrequency: 1000);
        Source(session, isDown: true);
        Source(session, isDown: false);

        clock = 1999;
        Assert.False(session.Snapshot().IsComplete);
        clock = 2000;
        var snapshot = session.Snapshot();

        Assert.True(snapshot.IsComplete);
        Assert.True(snapshot.PostConditionWindowCompleted);
        Assert.True(snapshot.ScenarioAssertionsPassed);
        Assert.Equal(0, snapshot.OutputKeyDownUnits);
        Assert.Equal(0, snapshot.OutputKeyUpUnits);
    }

    [Fact]
    public void AnySelectedOutputTransitionFailsRehearsalButWaitsForOutputRelease()
    {
        var session = CreateSession(OutputWitnessScenario.Rehearsal);
        Source(session, isDown: true);
        Source(session, isDown: false);
        Output(session, isDown: true);

        Assert.True(session.Snapshot().HasFailed);
        Assert.False(session.Snapshot().CanTerminateFailed);
        Output(session, isDown: false);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.CanTerminateFailed);
        Assert.False(snapshot.NoUnexpectedOrDuplicateOutputTransitions);
        Assert.False(snapshot.OutputKeyHeld);
    }

    [Fact]
    public void HeldUnplugRequiresOutputReleaseWhileSourceHasNoObservedRelease()
    {
        var clock = 0L;
        var session = CreateSession(
            OutputWitnessScenario.HeldUnplug,
            () => clock,
            timestampFrequency: 1000);
        Source(session, isDown: true);
        Output(session, isDown: true);
        Assert.Equal(OutputWitnessPhase.HeldUnplugReady, session.Snapshot().Phase);

        Source(session, isDown: true, repeatCount: 2);
        Output(session, isDown: false);
        Assert.Equal(
            OutputWitnessPhase.HeldUnplugObservationWindow,
            session.Snapshot().Phase);
        clock = 2000;
        var snapshot = session.Snapshot();

        Assert.True(snapshot.IsComplete);
        Assert.True(snapshot.HeldUnplugStageReached);
        Assert.True(snapshot.HeldUnplugOutputReleaseObserved);
        Assert.True(snapshot.PostConditionWindowCompleted);
        Assert.True(snapshot.SourceKeyHeld);
        Assert.False(snapshot.OutputKeyHeld);
        Assert.Equal(0, snapshot.SourceKeyUpUnits);
    }

    [Fact]
    public void HeldUnplugRejectsObservedSourceReleaseBeforeOutputRelease()
    {
        var session = CreateSession(OutputWitnessScenario.HeldUnplug);
        Source(session, isDown: true);
        Output(session, isDown: true);
        Source(session, isDown: false);
        Assert.Equal(OutputWitnessPhase.AwaitOutputReleaseAfterFailure, session.Snapshot().Phase);

        Output(session, isDown: false);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.HasFailed);
        Assert.True(snapshot.CanTerminateFailed);
        Assert.False(snapshot.HeldUnplugOutputReleaseObserved);
        Assert.False(snapshot.ScenarioAssertionsPassed);
    }

    [Fact]
    public void UnallowlistedConsoleKeyIsDiscardedWithoutAggregateTrace()
    {
        var session = CreateSession(OutputWitnessScenario.Basic);
        var before = session.Snapshot();

        session.Accept(new ConsoleKeyObservation(0x41, true, 9));
        session.Accept(new ConsoleKeyObservation(0x41, false, 1));
        var after = session.Snapshot();

        Assert.Equal(before, after);
    }

    [Fact]
    public void UnbalancedOutputReleaseFailsWithoutPretendingOutputIsHeld()
    {
        var session = CreateSession(OutputWitnessScenario.Basic);
        Output(session, isDown: false);
        var snapshot = session.Snapshot();

        Assert.True(snapshot.HasFailed);
        Assert.True(snapshot.CanTerminateFailed);
        Assert.Equal(1, snapshot.OutputUnbalancedReleaseUnits);
        Assert.False(snapshot.OutputKeyHeld);
    }

    private static OutputWitnessSession CreateSession(
        OutputWitnessScenario scenario,
        Func<long>? clock = null,
        long? timestampFrequency = null) =>
        new(
            scenario,
            WitnessKeyCatalog.DefaultOriginalKey,
            WitnessKeyCatalog.DefaultOutputKey,
            clock,
            timestampFrequency);

    private static void Source(
        OutputWitnessSession session,
        bool isDown,
        ushort repeatCount = 1) =>
        session.Accept(new ConsoleKeyObservation(
            WitnessKeyCatalog.DefaultOriginalKey.VirtualKeyCode,
            isDown,
            repeatCount));

    private static void Output(
        OutputWitnessSession session,
        bool isDown,
        ushort repeatCount = 1) =>
        session.Accept(new ConsoleKeyObservation(
            WitnessKeyCatalog.DefaultOutputKey.VirtualKeyCode,
            isDown,
            repeatCount));
}
