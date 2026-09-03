using System.Diagnostics;

namespace Tappy.OutputWitness;

internal enum OutputWitnessTerminationReason
{
    None,
    Aborted,
    TimedOut,
    Faulted,
}

internal sealed class OutputWitnessTermination
{
    private readonly CancellationTokenSource _cancellation;
    private int _reason;

    internal OutputWitnessTermination(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
    }

    internal OutputWitnessTerminationReason Reason =>
        (OutputWitnessTerminationReason)Volatile.Read(ref _reason);

    internal void Stop(OutputWitnessTerminationReason reason)
    {
        if (reason == OutputWitnessTerminationReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        if (Interlocked.CompareExchange(
                ref _reason,
                (int)reason,
                (int)OutputWitnessTerminationReason.None) ==
            (int)OutputWitnessTerminationReason.None)
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A native console control callback or timer callback can already
                // be queued while the finite run is closing. The fixed reason is
                // still retained, and a late callback must never crash the process.
            }
        }
    }
}

internal sealed class OutputWitnessRunner
{
    private static readonly TimeSpan ConsolePollInterval = TimeSpan.FromMilliseconds(50);

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<OutputWitnessOptions, IConsoleInputSource> _inputSourceFactory;
    private readonly Func<string> _repositoryRootProvider;
    private readonly Func<
        string,
        OutputWitnessEvidence,
        CancellationToken,
        Task<string>> _evidenceWriter;

    internal OutputWitnessRunner(TextWriter output, TextWriter error)
        : this(
            output,
            error,
            static options => new WindowsConsoleInputSource(
                options.OriginalKey,
                options.OutputKey),
            OutputWitnessEvidenceWriter.FindRepositoryRoot,
            OutputWitnessEvidenceWriter.WriteAsync)
    {
    }

    internal OutputWitnessRunner(
        TextWriter output,
        TextWriter error,
        Func<OutputWitnessOptions, IConsoleInputSource> inputSourceFactory,
        Func<string> repositoryRootProvider,
        Func<string, OutputWitnessEvidence, CancellationToken, Task<string>> evidenceWriter)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _inputSourceFactory = inputSourceFactory ??
            throw new ArgumentNullException(nameof(inputSourceFactory));
        _repositoryRootProvider = repositoryRootProvider ??
            throw new ArgumentNullException(nameof(repositoryRootProvider));
        _evidenceWriter = evidenceWriter ?? throw new ArgumentNullException(nameof(evidenceWriter));
    }

    internal async Task<int> RunAsync(
        OutputWitnessOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateArmedOptions(options);

        var session = new OutputWitnessSession(
            options.Scenario,
            options.OriginalKey,
            options.OutputKey);
        var stopwatch = Stopwatch.StartNew();
        using var runCancellation = new CancellationTokenSource();
        var termination = new OutputWitnessTermination(runCancellation);
        using var externalCancellation = cancellationToken.Register(
            () => termination.Stop(OutputWitnessTerminationReason.Aborted));
        var timeoutTimer = new Timer(
            _ => termination.Stop(OutputWitnessTerminationReason.TimedOut),
            null,
            options.Timeout,
            Timeout.InfiniteTimeSpan);

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            termination.Stop(OutputWitnessTerminationReason.Aborted);
        };

        IConsoleInputSource? inputSource = null;
        var cleanupCompleted = true;
        var consoleReaderFaults = 0;
        var lastPhase = (OutputWitnessPhase)(-1);
        Console.CancelKeyPress += cancelHandler;
        try
        {
            inputSource = _inputSourceFactory(options);
            inputSource.FlushPendingInput();

            while (!runCancellation.IsCancellationRequested)
            {
                var snapshot = session.Snapshot();
                if (snapshot.Phase != lastPhase)
                {
                    PrintPrompt(options, snapshot);
                    lastPhase = snapshot.Phase;
                }

                if (snapshot.IsComplete)
                {
                    // Drain records already queued at the observation-window edge
                    // before accepting the aggregate result.
                    if (inputSource.TryRead(
                            TimeSpan.Zero,
                            runCancellation.Token,
                            out var finalObservation))
                    {
                        session.Accept(finalObservation);
                        continue;
                    }

                    break;
                }

                if (snapshot.CanTerminateFailed)
                {
                    break;
                }

                if (inputSource.TryRead(
                        ConsolePollInterval,
                        runCancellation.Token,
                        out var observation))
                {
                    session.Accept(observation);
                }
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            // The fixed termination reason is converted after console cleanup.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Interlocked.Increment(ref consoleReaderFaults);
            termination.Stop(OutputWitnessTerminationReason.Faulted);
            _error.WriteLine(
                "Focused console input failed safely; no exception details or input content were persisted.");
        }
        finally
        {
            _ = timeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            await timeoutTimer.DisposeAsync().ConfigureAwait(false);
            Console.CancelKeyPress -= cancelHandler;
            if (inputSource is not null)
            {
                try
                {
                    inputSource.Dispose();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    cleanupCompleted = false;
                }
            }
        }

        stopwatch.Stop();
        var finalSnapshot = session.Snapshot();
        var outcome = DetermineOutcome(
            termination.Reason,
            finalSnapshot,
            cleanupCompleted,
            Volatile.Read(ref consoleReaderFaults));
        var runtime = new OutputWitnessRuntimeFacts(
            options.Armed,
            options.FocusedConsoleOnlyAcknowledged,
            options.NoDeviceAttributionAcknowledged,
            options.TappyModeSetAcknowledged,
            cleanupCompleted,
            Volatile.Read(ref consoleReaderFaults),
            termination.Reason == OutputWitnessTerminationReason.TimedOut,
            stopwatch.ElapsedMilliseconds,
            checked((long)options.Timeout.TotalMilliseconds));

        OutputWitnessEvidence evidence;
        try
        {
            evidence = OutputWitnessEvidenceFactory.Create(
                outcome,
                options,
                finalSnapshot,
                runtime);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _error.WriteLine("The aggregate witness result was internally inconsistent and was not written.");
            return OutputWitnessExitCodes.InternalFailure;
        }

        string evidencePath;
        try
        {
            evidencePath = await _evidenceWriter(
                _repositoryRootProvider(),
                evidence,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _error.WriteLine(
                "Console capture was disarmed, but aggregate evidence could not be written.");
            return OutputWitnessExitCodes.InternalFailure;
        }

        _output.WriteLine();
        _output.WriteLine(
            $"Aggregate focused-console witness result: {outcome}. No device attribution or compatibility tier is claimed.");
        _output.WriteLine($"Evidence: {evidencePath}");
        _output.WriteLine(
            $"Allowlisted counts: source down={finalSnapshot.SourceKeyDownUnits}, " +
            $"repeat={finalSnapshot.SourceRepeatUnits}, up={finalSnapshot.SourceKeyUpUnits}; " +
            $"output down={finalSnapshot.OutputKeyDownUnits}, up={finalSnapshot.OutputKeyUpUnits}.");
        _output.WriteLine(
            $"Final expected-key state: source held={finalSnapshot.SourceKeyHeld}; " +
            $"output held={finalSnapshot.OutputKeyHeld}.");

        return outcome switch
        {
            OutputWitnessOutcome.Passed => OutputWitnessExitCodes.Passed,
            OutputWitnessOutcome.Failed => OutputWitnessExitCodes.AssertionsFailed,
            OutputWitnessOutcome.Aborted or OutputWitnessOutcome.TimedOut =>
                OutputWitnessExitCodes.Interrupted,
            OutputWitnessOutcome.Faulted => OutputWitnessExitCodes.InternalFailure,
            _ => throw new InvalidOperationException("Unsupported witness outcome."),
        };
    }

    private void PrintPrompt(
        OutputWitnessOptions options,
        OutputWitnessSessionSnapshot snapshot)
    {
        var instruction = snapshot.Phase switch
        {
            OutputWitnessPhase.AwaitOriginalPress when
                options.Scenario == OutputWitnessScenario.HeldUnplug =>
                $"Press and keep holding {options.OriginalKey.Name}; wait for {options.OutputKey.Name} down before unplugging.",
            OutputWitnessPhase.AwaitOriginalPress =>
                $"Press and hold {options.OriginalKey.Name} while this console remains focused.",
            OutputWitnessPhase.AwaitOriginalRepeat =>
                $"Keep holding {options.OriginalKey.Name} until ordinary OS repeat is observed.",
            OutputWitnessPhase.AwaitOriginalRelease =>
                $"Release {options.OriginalKey.Name} once; keep this console focused.",
            OutputWitnessPhase.AwaitRequiredOutput =>
                $"The source cycle was observed; waiting for exactly one balanced {options.OutputKey.Name} output cycle.",
            OutputWitnessPhase.HeldUnplugReady =>
                $"Keep {options.OriginalKey.Name} physically held and unplug the selected controller now; waiting for synthetic {options.OutputKey.Name} up.",
            OutputWitnessPhase.RehearsalQuietWindow =>
                $"Keep the console focused during the fixed quiet window; any {options.OutputKey.Name} transition fails Rehearsal evidence.",
            OutputWitnessPhase.HeldUnplugObservationWindow =>
                $"Keep {options.OriginalKey.Name} physically held while the fixed post-release observation window completes.",
            OutputWitnessPhase.AwaitOutputReleaseAfterFailure =>
                $"A failure is recorded while {options.OutputKey.Name} is held; unplug or use Tappy Emergency stop and wait for its key-up.",
            OutputWitnessPhase.Complete =>
                "All scenario aggregates passed; console capture is being disarmed.",
            OutputWitnessPhase.Failed =>
                "A scenario assertion failed; console capture is being disarmed.",
            _ => throw new InvalidOperationException("Unsupported witness phase."),
        };

        _output.WriteLine(instruction);
        _output.WriteLine(
            $"Current expected-key state: source held={snapshot.SourceKeyHeld}; output held={snapshot.OutputKeyHeld}.");
    }

    private static OutputWitnessOutcome DetermineOutcome(
        OutputWitnessTerminationReason terminationReason,
        OutputWitnessSessionSnapshot session,
        bool cleanupCompleted,
        int consoleReaderFaults) =>
        terminationReason switch
        {
            OutputWitnessTerminationReason.Aborted => OutputWitnessOutcome.Aborted,
            OutputWitnessTerminationReason.TimedOut => OutputWitnessOutcome.TimedOut,
            OutputWitnessTerminationReason.Faulted => OutputWitnessOutcome.Faulted,
            OutputWitnessTerminationReason.None when
                session.ScenarioAssertionsPassed &&
                cleanupCompleted &&
                consoleReaderFaults == 0 => OutputWitnessOutcome.Passed,
            OutputWitnessTerminationReason.None => OutputWitnessOutcome.Failed,
            _ => throw new InvalidOperationException("Unsupported witness termination reason."),
        };

    private static void ValidateArmedOptions(OutputWitnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsFullyAcknowledged ||
            options.Timeout < TimeSpan.FromSeconds(OutputWitnessOptions.MinimumTimeoutSeconds) ||
            options.Timeout > TimeSpan.FromSeconds(OutputWitnessOptions.MaximumTimeoutSeconds) ||
            !WitnessKeyCatalog.IsAllowedOriginal(options.OriginalKey) ||
            !WitnessKeyCatalog.IsAllowedOutput(options.OutputKey) ||
            !Enum.IsDefined(options.Scenario))
        {
            throw new InvalidOperationException(
                "The runner cannot open console input without exact validated arming options.");
        }
    }
}
