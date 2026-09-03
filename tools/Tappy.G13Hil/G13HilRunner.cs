using System.Diagnostics;
using Tappy.Windows.Input;

namespace Tappy.G13Hil;

internal enum G13HilDeviceSelectionStatus
{
    Selected,
    NoPhysicalG13,
    MultiplePhysicalG13Controllers,
    InvalidSanitizedDescriptor,
}

internal sealed record G13HilDeviceSelection(
    G13HilDeviceSelectionStatus Status,
    SanitizedDeviceDescriptor? Descriptor)
{
    internal static G13HilDeviceSelection From(
        IReadOnlyList<SanitizedDeviceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var matches = descriptors.Where(IsExactPhysicalG13).ToArray();
        if (matches.Length == 0)
        {
            return new(G13HilDeviceSelectionStatus.NoPhysicalG13, null);
        }

        if (matches.Length != 1)
        {
            return new(G13HilDeviceSelectionStatus.MultiplePhysicalG13Controllers, null);
        }

        var descriptor = matches[0];
        if (descriptor.InterfaceCount <= 0 ||
            descriptor.PathFingerprintSha256.Length != 64 ||
            !descriptor.PathFingerprintSha256.All(Uri.IsHexDigit))
        {
            return new(G13HilDeviceSelectionStatus.InvalidSanitizedDescriptor, null);
        }

        return new(G13HilDeviceSelectionStatus.Selected, descriptor);
    }

    internal static bool IsExactPhysicalG13(SanitizedDeviceDescriptor descriptor) =>
        descriptor.Kind == RawInputDeviceKind.Hid &&
        descriptor.VendorId == LogitechG13Protocol.VendorId &&
        descriptor.ProductId == LogitechG13Protocol.ProductId &&
        descriptor.UsagePage == LogitechG13Protocol.UsagePage &&
        descriptor.Usage == LogitechG13Protocol.Usage;
}

internal enum G13HilTerminationReason
{
    None,
    Aborted,
    TimedOut,
    Disconnected,
    Faulted,
    LifecycleInterrupted,
    ConfirmationFailed,
}

internal sealed class G13HilTermination
{
    private readonly CancellationTokenSource _cancellation;
    private int _reason;

    internal G13HilTermination(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
    }

    internal G13HilTerminationReason Reason =>
        (G13HilTerminationReason)Volatile.Read(ref _reason);

    internal void Stop(G13HilTerminationReason reason)
    {
        if (reason == G13HilTerminationReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        if (Interlocked.CompareExchange(
            ref _reason,
            (int)reason,
            (int)G13HilTerminationReason.None) == (int)G13HilTerminationReason.None)
        {
            _cancellation.Cancel();
        }
    }
}

internal sealed class G13HilRunner
{
    private const int PollIntervalMilliseconds = 50;

    internal async Task<int> RunAsync(
        G13HilOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Armed)
        {
            throw new InvalidOperationException("The runner cannot start without explicit arming.");
        }

        var enumerator = new NativeLogitechG13DeviceEnumerator();
        var selection = G13HilDeviceSelection.From(enumerator.EnumerateControllers());
        if (selection.Status != G13HilDeviceSelectionStatus.Selected)
        {
            PrintSelectionFailure(selection.Status);
            return G13HilExitCodes.DeviceSelectionFailed;
        }

        var descriptor = selection.Descriptor!;
        Console.WriteLine("One exact physical G13 vendor collection was selected using its sanitized fingerprint.");
        Console.WriteLine($"Fingerprint: {descriptor.PathFingerprintSha256}");
        Console.WriteLine();

        var session = new G13HilSession();
        var stopwatch = Stopwatch.StartNew();
        using var runCancellation = new CancellationTokenSource();
        var termination = new G13HilTermination(runCancellation);
        using var externalCancellation = cancellationToken.Register(
            () => termination.Stop(G13HilTerminationReason.Aborted));
        using var timeoutTimer = new Timer(
            _ => termination.Stop(G13HilTerminationReason.TimedOut),
            null,
            options.Timeout,
            Timeout.InfiniteTimeSpan);

        var provider = new LogitechG13InputProvider(
            enumerator,
            new RawInputMessageHost());
        var providerStarted = false;
        var confirmationSucceeded = 0;
        var disconnects = 0;
        var faults = 0;
        var lifecycleInterruptions = 0;

        void ObserveNormalizedInput(object? sender, LogitechG13InputReceivedEventArgs eventArgs)
        {
            try
            {
                if (session.Phase == G13HilPhase.AwaitNeutral)
                {
                    if (provider.IsCaptureTargetNeutral)
                    {
                        session.MarkNeutralObserved();
                    }

                    return;
                }

                session.Accept(eventArgs.Input.Control, eventArgs.Input.Signal.Kind);
                var snapshot = session.Snapshot();
                if (snapshot.HandshakePassed &&
                    Volatile.Read(ref confirmationSucceeded) == 0)
                {
                    if (provider.SetConfirmedPersistentId(descriptor.PersistentId))
                    {
                        Volatile.Write(ref confirmationSucceeded, 1);
                    }
                    else
                    {
                        termination.Stop(G13HilTerminationReason.ConfirmationFailed);
                    }
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref faults);
                termination.Stop(G13HilTerminationReason.Faulted);
            }
        }

        void ObserveDeviceChange(object? sender, LogitechG13DeviceChangedEventArgs eventArgs)
        {
            if (eventArgs.WasCaptureTarget &&
                eventArgs.Kind is RawInputDeviceChangeKind.Removal or
                    RawInputDeviceChangeKind.MembershipChanged)
            {
                Interlocked.Increment(ref disconnects);
                termination.Stop(G13HilTerminationReason.Disconnected);
            }
        }

        void ObserveFault(object? sender, Exception exception)
        {
            _ = exception;
            Interlocked.Increment(ref faults);
            termination.Stop(G13HilTerminationReason.Faulted);
        }

        void ObserveLifecycle(object? sender, WindowsLifecycleSignalEventArgs eventArgs)
        {
            _ = eventArgs;
            Interlocked.Increment(ref lifecycleInterruptions);
            termination.Stop(G13HilTerminationReason.LifecycleInterrupted);
        }

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            termination.Stop(G13HilTerminationReason.Aborted);
        };

        provider.IdentificationInputReceived += ObserveNormalizedInput;
        provider.InputReceived += ObserveNormalizedInput;
        provider.DeviceChanged += ObserveDeviceChange;
        provider.Faulted += ObserveFault;
        provider.LifecycleChanged += ObserveLifecycle;
        Console.CancelKeyPress += cancelHandler;

        var cleanupCompleted = false;
        try
        {
            var providerInventory = provider.EnumerateControllers();
            var providerSelection = G13HilDeviceSelection.From(providerInventory);
            if (providerSelection.Status != G13HilDeviceSelectionStatus.Selected ||
                !string.Equals(
                    providerSelection.Descriptor!.PathFingerprintSha256,
                    descriptor.PathFingerprintSha256,
                    StringComparison.Ordinal))
            {
                termination.Stop(G13HilTerminationReason.Disconnected);
            }
            else if (!provider.SetCaptureTarget(descriptor.SessionHandle))
            {
                termination.Stop(G13HilTerminationReason.ConfirmationFailed);
            }
            else
            {
                await provider.StartAsync(runCancellation.Token).ConfigureAwait(false);
                providerStarted = true;
            }

            var lastPromptRevision = 0;
            var lastUnexpected = 0;
            var lastDuplicate = 0;
            var lastUnbalanced = 0;
            while (!runCancellation.IsCancellationRequested)
            {
                if (session.Phase == G13HilPhase.AwaitNeutral &&
                    provider.IsCaptureTargetNeutral)
                {
                    session.MarkNeutralObserved();
                }

                var snapshot = session.Snapshot();
                if (snapshot.Prompt.Revision != lastPromptRevision)
                {
                    Console.WriteLine(snapshot.Prompt.Instruction);
                    lastPromptRevision = snapshot.Prompt.Revision;
                }

                if (snapshot.UnexpectedTransitions != lastUnexpected)
                {
                    Console.WriteLine("Unexpected transition detected; it did not advance the prompt and only the aggregate count will be retained.");
                    lastUnexpected = snapshot.UnexpectedTransitions;
                }

                if (snapshot.DuplicateTransitions != lastDuplicate)
                {
                    Console.WriteLine("Duplicate transition detected; only the aggregate count will be retained.");
                    lastDuplicate = snapshot.DuplicateTransitions;
                }

                if (snapshot.UnbalancedTransitions != lastUnbalanced)
                {
                    Console.WriteLine("Unbalanced transition detected; only the aggregate count will be retained.");
                    lastUnbalanced = snapshot.UnbalancedTransitions;
                }

                if (snapshot.IsComplete)
                {
                    break;
                }

                await Task.Delay(
                    PollIntervalMilliseconds,
                    runCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            // The fixed termination reason is emitted after capture cleanup.
        }
        catch (Exception)
        {
            Interlocked.Increment(ref faults);
            termination.Stop(G13HilTerminationReason.Faulted);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            provider.IdentificationInputReceived -= ObserveNormalizedInput;
            provider.InputReceived -= ObserveNormalizedInput;
            provider.DeviceChanged -= ObserveDeviceChange;
            provider.Faulted -= ObserveFault;
            provider.LifecycleChanged -= ObserveLifecycle;
            cleanupCompleted = await CleanupProviderAsync(provider, providerStarted)
                .ConfigureAwait(false);
        }

        _ = timeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        stopwatch.Stop();
        var finalSnapshot = session.Snapshot();
        var reason = termination.Reason;
        var outcome = ToOutcome(reason, finalSnapshot, cleanupCompleted);
        var runtime = new G13HilRuntimeFacts(
            ExactPhysicalIdentity: G13HilDeviceSelection.IsExactPhysicalG13(descriptor),
            SinglePhysicalController: true,
            cleanupCompleted,
            Volatile.Read(ref disconnects),
            Volatile.Read(ref faults),
            Volatile.Read(ref lifecycleInterruptions),
            TimedOut: reason == G13HilTerminationReason.TimedOut,
            TotalDurationMs: stopwatch.ElapsedMilliseconds,
            HardTimeoutMs: checked((long)options.Timeout.TotalMilliseconds));

        if (outcome == G13HilOutcome.Passed && !AllRuntimeAndSessionAssertionsPass(finalSnapshot, runtime))
        {
            outcome = G13HilOutcome.Failed;
        }

        var evidence = G13HilEvidenceFactory.Create(outcome, descriptor, finalSnapshot, runtime);
        string evidencePath;
        try
        {
            evidencePath = await G13HilEvidenceWriter.WriteAsync(
                G13HilEvidenceWriter.FindRepositoryRoot(),
                evidence,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Capture was disarmed, but aggregate evidence could not be written.");
            return G13HilExitCodes.InternalFailure;
        }

        Console.WriteLine();
        Console.WriteLine($"Aggregate input-functional result: {outcome}. No full Tappy compatibility tier is claimed.");
        Console.WriteLine($"Evidence: {evidencePath}");
        Console.WriteLine($"Counts: {finalSnapshot.CompletedControlCycles} control cycles; " +
            $"{finalSnapshot.UnexpectedTransitions} unexpected; " +
            $"{finalSnapshot.DuplicateTransitions} duplicate; " +
            $"{finalSnapshot.UnbalancedTransitions} unbalanced.");

        return outcome switch
        {
            G13HilOutcome.Passed => G13HilExitCodes.Passed,
            G13HilOutcome.Failed => G13HilExitCodes.InputAssertionsFailed,
            _ => G13HilExitCodes.Interrupted,
        };
    }

    private static bool AllRuntimeAndSessionAssertionsPass(
        G13HilSessionSnapshot session,
        G13HilRuntimeFacts runtime) =>
        session.SessionAssertionsPassed &&
        runtime.ExactPhysicalIdentity &&
        runtime.SinglePhysicalController &&
        runtime.CleanupCompleted &&
        runtime.Disconnects == 0 &&
        runtime.ProviderFaults == 0 &&
        runtime.LifecycleInterruptions == 0 &&
        !runtime.TimedOut;

    private static G13HilOutcome ToOutcome(
        G13HilTerminationReason reason,
        G13HilSessionSnapshot session,
        bool cleanupCompleted) =>
        reason switch
        {
            G13HilTerminationReason.Aborted or G13HilTerminationReason.LifecycleInterrupted =>
                G13HilOutcome.Aborted,
            G13HilTerminationReason.TimedOut => G13HilOutcome.TimedOut,
            G13HilTerminationReason.Disconnected => G13HilOutcome.Disconnected,
            G13HilTerminationReason.Faulted => G13HilOutcome.Faulted,
            G13HilTerminationReason.ConfirmationFailed => G13HilOutcome.Failed,
            G13HilTerminationReason.None when session.SessionAssertionsPassed && cleanupCompleted =>
                G13HilOutcome.Passed,
            _ => G13HilOutcome.Failed,
        };

    private static async Task<bool> CleanupProviderAsync(
        LogitechG13InputProvider provider,
        bool providerStarted)
    {
        var succeeded = true;
        try
        {
            provider.ClearCaptureTarget();
        }
        catch (Exception)
        {
            succeeded = false;
        }

        if (providerStarted)
        {
            try
            {
                await provider.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                succeeded = false;
            }
        }

        try
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            succeeded = false;
        }

        return succeeded;
    }

    private static void PrintSelectionFailure(G13HilDeviceSelectionStatus status)
    {
        var message = status switch
        {
            G13HilDeviceSelectionStatus.NoPhysicalG13 =>
                "No exact physical Logitech G13 046D:C21C, FF00:0000 collection was found. No input capture started.",
            G13HilDeviceSelectionStatus.MultiplePhysicalG13Controllers =>
                "More than one physical Logitech G13 was found. Unambiguous single-device selection is required; no input capture started.",
            G13HilDeviceSelectionStatus.InvalidSanitizedDescriptor =>
                "The physical G13 descriptor failed sanitized identity validation. No input capture started.",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        Console.Error.WriteLine(message);
    }
}
