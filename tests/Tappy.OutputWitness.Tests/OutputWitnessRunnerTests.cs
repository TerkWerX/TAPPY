namespace Tappy.OutputWitness.Tests;

public sealed class OutputWitnessRunnerTests
{
    [Fact]
    public void LateTerminationCallbackRetainsReasonWithoutThrowingAfterCancellationIsDisposed()
    {
        var cancellation = new CancellationTokenSource();
        var termination = new OutputWitnessTermination(cancellation);
        cancellation.Dispose();

        termination.Stop(OutputWitnessTerminationReason.TimedOut);

        Assert.Equal(OutputWitnessTerminationReason.TimedOut, termination.Reason);
    }

    [Fact]
    public async Task DeterministicInputSeamCompletesWithoutNativeConsoleCapture()
    {
        var input = new FakeConsoleInputSource(
        [
            Source(isDown: true),
            new ConsoleKeyObservation(0x41, true, 20),
            Source(isDown: true, repeatCount: 2),
            Output(isDown: true),
            Source(isDown: false),
            Output(isDown: false),
        ]);
        OutputWitnessEvidence? writtenEvidence = null;
        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new OutputWitnessRunner(
            output,
            error,
            _ => input,
            static () => "unused-test-root",
            (root, evidence, _) =>
            {
                Assert.Equal("unused-test-root", root);
                writtenEvidence = evidence;
                return Task.FromResult("aggregate-test-evidence.json");
            });

        var exitCode = await runner.RunAsync(FullyArmedOptions());

        Assert.Equal(OutputWitnessExitCodes.Passed, exitCode);
        Assert.True(input.Flushed);
        Assert.True(input.Disposed);
        Assert.NotNull(writtenEvidence);
        Assert.Equal(3, writtenEvidence.Counts.OriginalKeyDownUnits);
        Assert.Equal(2, writtenEvidence.Counts.OriginalRepeatUnits);
        Assert.Equal(1, writtenEvidence.Counts.OutputKeyDownUnits);
        Assert.Equal(1, writtenEvidence.Counts.OutputKeyUpUnits);
        Assert.DoesNotContain("PRIVATE", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunnerDefenseInDepthRefusesBeforeCreatingInputSource()
    {
        var factoryCalls = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new OutputWitnessRunner(
            output,
            error,
            _ =>
            {
                factoryCalls++;
                return new FakeConsoleInputSource([]);
            },
            static () => throw new InvalidOperationException("must not resolve root"),
            static (_, _, _) => throw new InvalidOperationException("must not write evidence"));
        var invalidOptions = FullyArmedOptions() with
        {
            FocusedConsoleOnlyAcknowledged = false,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(invalidOptions));

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task FinalNonBlockingDrainRejectsExtraAllowlistedOutputAlreadyQueued()
    {
        var input = new FakeConsoleInputSource(
        [
            Source(isDown: true),
            Source(isDown: true, repeatCount: 2),
            Output(isDown: true),
            Source(isDown: false),
            Output(isDown: false),
            Output(isDown: true),
            Output(isDown: false),
        ]);
        OutputWitnessEvidence? writtenEvidence = null;
        using var output = new StringWriter();
        using var error = new StringWriter();
        var runner = new OutputWitnessRunner(
            output,
            error,
            _ => input,
            static () => "unused-test-root",
            (_, evidence, _) =>
            {
                writtenEvidence = evidence;
                return Task.FromResult("aggregate-test-evidence.json");
            });

        var exitCode = await runner.RunAsync(FullyArmedOptions());

        Assert.Equal(OutputWitnessExitCodes.AssertionsFailed, exitCode);
        Assert.NotNull(writtenEvidence);
        Assert.Equal(2, writtenEvidence.Counts.OutputKeyDownUnits);
        Assert.Equal(2, writtenEvidence.Counts.OutputKeyUpUnits);
        Assert.False(writtenEvidence.Assertions.NoUnexpectedOrDuplicateOutputTransitions);
        Assert.True(input.Disposed);
    }

    private static OutputWitnessOptions FullyArmedOptions() =>
        new(
            Armed: true,
            FocusedConsoleOnlyAcknowledged: true,
            NoDeviceAttributionAcknowledged: true,
            TappyModeSetAcknowledged: true,
            Timeout: OutputWitnessOptions.DefaultTimeout,
            Scenario: OutputWitnessScenario.Basic,
            WitnessKeyCatalog.DefaultOriginalKey,
            WitnessKeyCatalog.DefaultOutputKey);

    private static ConsoleKeyObservation Source(
        bool isDown,
        ushort repeatCount = 1) =>
        new(
            WitnessKeyCatalog.DefaultOriginalKey.VirtualKeyCode,
            isDown,
            repeatCount);

    private static ConsoleKeyObservation Output(bool isDown) =>
        new(
            WitnessKeyCatalog.DefaultOutputKey.VirtualKeyCode,
            isDown,
            1);

    private sealed class FakeConsoleInputSource(
        IEnumerable<ConsoleKeyObservation> observations) : IConsoleInputSource
    {
        private readonly Queue<ConsoleKeyObservation> _observations = new(observations);

        internal bool Flushed { get; private set; }

        internal bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }

        public void FlushPendingInput()
        {
            Flushed = true;
        }

        public bool TryRead(
            TimeSpan maximumWait,
            CancellationToken cancellationToken,
            out ConsoleKeyObservation observation)
        {
            _ = maximumWait;
            cancellationToken.ThrowIfCancellationRequested();
            if (_observations.TryDequeue(out observation))
            {
                return true;
            }

            observation = default;
            return false;
        }
    }
}
