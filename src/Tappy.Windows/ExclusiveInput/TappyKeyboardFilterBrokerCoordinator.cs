namespace Tappy.Windows.ExclusiveInput;

/// <summary>
/// Coordinates the protected primary and configurable secondary as a two-phase
/// broker operation. The secondary is never armed until the primary has already
/// acknowledged pass-through for the same generation.
/// </summary>
internal sealed class TappyKeyboardFilterBrokerCoordinator : IDisposable
{
    private readonly IReadOnlyList<ITappyKeyboardFilterDeviceSession> _primary;
    private readonly IReadOnlyList<ITappyKeyboardFilterDeviceSession> _secondary;
    private bool _faulted;
    private bool _disposed;

    public TappyKeyboardFilterBrokerCoordinator(
        ITappyKeyboardFilterDeviceSession primary,
        ITappyKeyboardFilterDeviceSession secondary)
        : this([primary], [secondary])
    {
    }

    public TappyKeyboardFilterBrokerCoordinator(
        IReadOnlyList<ITappyKeyboardFilterDeviceSession> primary,
        IReadOnlyList<ITappyKeyboardFilterDeviceSession> secondary)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);
        if (primary.Count == 0 || secondary.Count == 0)
        {
            throw new ArgumentException("Primary and secondary device groups must each contain at least one filter session.");
        }

        var all = primary.Concat(secondary).ToArray();
        if (all.Any(session => session is null) ||
            all.Distinct(ReferenceEqualityComparer.Instance).Count() != all.Length)
        {
            throw new ArgumentException("Every primary and secondary driver session must be non-null and distinct.");
        }

        _primary = primary.ToArray();
        _secondary = secondary.ToArray();
    }

    public bool IsArmed { get; private set; }

    public ulong PolicyGeneration { get; private set; }

    public void Arm(ulong policyGeneration, uint watchdogTimeoutMilliseconds)
    {
        ThrowIfUnavailable();
        if (IsArmed)
        {
            throw new InvalidOperationException("The exclusive keyboard pair is already armed.");
        }

        if (policyGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policyGeneration));
        }

        if (!TappyKeyboardFilterProtocol.IsValidWatchdog(watchdogTimeoutMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(watchdogTimeoutMilliseconds));
        }

        try
        {
            foreach (var session in _primary)
            {
                session.Begin();
            }

            foreach (var session in _secondary)
            {
                session.Begin();
            }

            foreach (var session in _primary)
            {
                session.SetPolicy(
                    policyGeneration,
                    KeyboardFilterRole.ProtectedPrimary,
                    KeyboardFilterMode.PassThrough,
                    0);
                RequirePrimaryAcknowledgement(session.QueryStatus(), policyGeneration);
            }

            foreach (var session in _secondary)
            {
                session.SetPolicy(
                    policyGeneration,
                    KeyboardFilterRole.ConfigurableSecondary,
                    KeyboardFilterMode.CaptureAndSuppress,
                    watchdogTimeoutMilliseconds);
                RequireSecondaryAcknowledgement(
                    session.QueryStatus(),
                    policyGeneration,
                    watchdogTimeoutMilliseconds);
            }

            PolicyGeneration = policyGeneration;
            IsArmed = true;
        }
        catch
        {
            FaultAndCloseBoth();
            throw;
        }
    }

    public void Heartbeat()
    {
        EnsureArmed();
        try
        {
            foreach (var session in _secondary)
            {
                session.Heartbeat(PolicyGeneration);
            }
        }
        catch
        {
            FaultAndCloseBoth();
            throw;
        }
    }

    public IReadOnlyList<KeyboardFilterEvent> ReadEvents(int maximumEvents)
    {
        EnsureArmed();
        try
        {
            if (maximumEvents is < 1 or > TappyKeyboardFilterProtocol.MaximumEventsPerRead)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEvents));
            }

            var events = new List<KeyboardFilterEvent>(maximumEvents);
            foreach (var session in _secondary)
            {
                var remaining = maximumEvents - events.Count;
                if (remaining == 0)
                {
                    break;
                }

                var batch = session.ReadEvents(remaining);
                if (batch.Count > remaining ||
                    batch.Any(item => item.PolicyGeneration != PolicyGeneration))
                {
                    throw new InvalidDataException(
                        "A secondary driver instance returned an oversized or stale event batch.");
                }

                events.AddRange(batch);
            }

            return events;
        }
        catch
        {
            FaultAndCloseBoth();
            throw;
        }
    }

    public void Stop()
    {
        ThrowIfUnavailable();
        if (!IsArmed)
        {
            return;
        }

        Exception? firstFailure = null;
        // Stop every suppressing instance before touching any protected-primary
        // instance, even when an individual request fails.
        foreach (var session in _secondary)
        {
            try
            {
                session.ForceFailOpen();
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        foreach (var session in _primary)
        {
            try
            {
                session.ForceFailOpen();
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        IsArmed = false;
        PolicyGeneration = 0;
        if (firstFailure is not null)
        {
            FaultAndCloseBoth();
            throw new IOException(
                "A driver fail-open command failed; both exclusive handles were closed instead.",
                firstFailure);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (IsArmed && !_faulted)
        {
            try
            {
                Stop();
            }
            catch (IOException)
            {
                // Stop closed both handles after the IOCTL failure.
            }
        }

        DisposeSessionsNoThrow();
        _disposed = true;
    }

    private static void RequirePrimaryAcknowledgement(
        KeyboardFilterInstanceStatus status,
        ulong policyGeneration)
    {
        if (status.ProtocolVersion != TappyKeyboardFilterProtocol.Version ||
            status.Role != KeyboardFilterRole.ProtectedPrimary ||
            status.EffectiveMode != KeyboardFilterMode.PassThrough ||
            status.WatchdogArmed ||
            status.PolicyGeneration != policyGeneration)
        {
            throw new InvalidDataException("The primary driver instance did not acknowledge protected pass-through.");
        }
    }

    private static void RequireSecondaryAcknowledgement(
        KeyboardFilterInstanceStatus status,
        ulong policyGeneration,
        uint watchdogTimeoutMilliseconds)
    {
        if (status.ProtocolVersion != TappyKeyboardFilterProtocol.Version ||
            status.Role != KeyboardFilterRole.ConfigurableSecondary ||
            status.EffectiveMode != KeyboardFilterMode.CaptureAndSuppress ||
            !status.WatchdogArmed ||
            status.WatchdogTimeoutMilliseconds != watchdogTimeoutMilliseconds ||
            status.PolicyGeneration != policyGeneration)
        {
            throw new InvalidDataException("The secondary driver instance did not acknowledge exclusive capture.");
        }
    }

    private void EnsureArmed()
    {
        ThrowIfUnavailable();
        if (!IsArmed)
        {
            throw new InvalidOperationException("The exclusive keyboard pair is not armed.");
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted)
        {
            throw new InvalidOperationException("The exclusive keyboard pair faulted and must be reopened.");
        }
    }

    private void FaultAndCloseBoth()
    {
        IsArmed = false;
        PolicyGeneration = 0;
        _faulted = true;
        DisposeSessionsNoThrow();
    }

    private void DisposeSessionsNoThrow()
    {
        foreach (var session in _secondary.Concat(_primary))
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
