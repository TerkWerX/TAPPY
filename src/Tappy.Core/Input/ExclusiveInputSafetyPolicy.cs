namespace Tappy.Core.Input;

/// <summary>
/// Describes how much trust Windows has placed in the keyboard-filter package.
/// The production application deliberately requires the strongest level even
/// though Windows permits narrower signing paths for some test scenarios.
/// </summary>
public enum ExclusiveInputDeploymentTrust
{
    None,
    DevelopmentTest,
    MicrosoftPreproduction,
    MicrosoftAttestation,
    MicrosoftHlkCertified
}

public enum ExclusiveInputActivationContext
{
    DevelopmentTest,
    Production
}

public sealed record ExclusiveKeyboardCandidate(
    string PersistentId,
    bool IsConnected,
    bool IsKeyboardClass,
    bool HasStableIdentity);

public sealed record ExclusiveInputBackendEvidence(
    bool DriverLoaded,
    bool BrokerAuthenticated,
    bool PrimaryPassThroughAcknowledged,
    bool SecondaryExclusiveAcknowledged,
    bool FailOpenWatchdogArmed,
    TimeSpan WatchdogTimeout,
    DateTimeOffset LastHeartbeatUtc,
    ExclusiveInputDeploymentTrust Trust,
    bool IsHvciCompatible,
    bool IsTestSigningEnabled,
    bool IsKernelDebuggingEnabled);

public sealed record ExclusiveInputActivationRequest(
    string PrimaryKeyboardPersistentId,
    string SecondaryKeyboardPersistentId,
    bool HasExplicitAdministratorConsent,
    bool MouseRecoveryAvailable,
    bool EmergencyStopAvailableOnPrimary,
    bool ProtectedApplicationActive,
    ExclusiveInputActivationContext Context);

public sealed record ExclusiveInputActivationDecision(
    bool Allowed,
    string Code,
    string Message)
{
    public static ExclusiveInputActivationDecision Permit() =>
        new(true, "ready", "The exact primary and secondary keyboards passed every exclusive-input safety gate.");

    public static ExclusiveInputActivationDecision Reject(string code, string message) =>
        new(false, code, message);
}

/// <summary>
/// Pure, deterministic activation gate shared by the future broker and UI.
/// Passing this policy is necessary but never sufficient to install a driver;
/// installation remains a separate, explicit administrator operation.
/// </summary>
public static class ExclusiveInputSafetyPolicy
{
    public static ExclusiveInputActivationDecision Evaluate(
        ExclusiveInputActivationRequest request,
        ExclusiveInputBackendEvidence backend,
        IReadOnlyCollection<ExclusiveKeyboardCandidate> keyboards,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(keyboards);

        if (!request.HasExplicitAdministratorConsent)
        {
            return Reject("administrator-consent-required",
                "Exclusive input requires a separate, explicit administrator-approved driver installation.");
        }

        var primaryId = request.PrimaryKeyboardPersistentId?.Trim() ?? string.Empty;
        var secondaryId = request.SecondaryKeyboardPersistentId?.Trim() ?? string.Empty;
        if (primaryId.Length == 0 || secondaryId.Length == 0)
        {
            return Reject("keyboard-role-missing",
                "Choose and physically verify both a protected primary keyboard and a configurable secondary keyboard.");
        }

        if (primaryId.Equals(secondaryId, StringComparison.OrdinalIgnoreCase))
        {
            return Reject("keyboard-roles-overlap",
                "The protected primary keyboard and configurable secondary keyboard must be different devices.");
        }

        var primaryMatches = FindMatches(keyboards, primaryId);
        var secondaryMatches = FindMatches(keyboards, secondaryId);
        if (primaryMatches.Count != 1 || secondaryMatches.Count != 1)
        {
            return Reject("keyboard-identity-ambiguous",
                "Each keyboard role must resolve to exactly one connected Windows device instance.");
        }

        var primary = primaryMatches[0];
        var secondary = secondaryMatches[0];
        if (!primary.IsConnected || !secondary.IsConnected)
        {
            return Reject("keyboard-disconnected",
                "Both the protected primary keyboard and configurable secondary keyboard must remain connected.");
        }

        if (!primary.IsKeyboardClass || !secondary.IsKeyboardClass)
        {
            return Reject("not-keyboard-class",
                "Exclusive keyboard filtering is limited to verified Windows keyboard-class device instances.");
        }

        if (!primary.HasStableIdentity || !secondary.HasStableIdentity)
        {
            return Reject("unstable-keyboard-identity",
                "Exclusive input requires stable per-instance identity; an ambiguous or session-only keyboard cannot be armed.");
        }

        if (!request.MouseRecoveryAvailable || !request.EmergencyStopAvailableOnPrimary)
        {
            return Reject("recovery-path-missing",
                "A working mouse/tray recovery path and an emergency stop on the protected primary keyboard are required.");
        }

        if (request.ProtectedApplicationActive)
        {
            return Reject("protected-application-active",
                "Exclusive input is disabled while an anti-cheat or other protected application is active.");
        }

        if (!backend.DriverLoaded || !backend.BrokerAuthenticated)
        {
            return Reject("backend-unavailable",
                "The signed keyboard filter and authenticated Tappy broker must both be available.");
        }

        if (!backend.IsHvciCompatible)
        {
            return Reject("hvci-not-verified",
                "The installed driver build has not passed Tappy's Memory Integrity/HVCI compatibility gate.");
        }

        if (!HasRequiredTrust(request.Context, backend.Trust))
        {
            return Reject("driver-trust-insufficient",
                request.Context == ExclusiveInputActivationContext.Production
                    ? "Production exclusive input requires a Microsoft-signed, HLK-certified Tappy driver package."
                    : "This development run requires at least an explicitly test-signed Tappy driver package.");
        }

        if (request.Context == ExclusiveInputActivationContext.Production &&
            (backend.IsTestSigningEnabled || backend.IsKernelDebuggingEnabled))
        {
            return Reject("unsafe-boot-configuration",
                "Production exclusive input is unavailable while Windows test-signing or kernel debugging is enabled.");
        }

        if (!backend.FailOpenWatchdogArmed ||
            backend.WatchdogTimeout < TimeSpan.FromMilliseconds(250) ||
            backend.WatchdogTimeout > TimeSpan.FromSeconds(2))
        {
            return Reject("watchdog-unavailable",
                "The driver must have an armed fail-open watchdog between 250 ms and 2 seconds.");
        }

        var heartbeatAge = nowUtc - backend.LastHeartbeatUtc;
        if (heartbeatAge < TimeSpan.Zero || heartbeatAge > backend.WatchdogTimeout)
        {
            return Reject("heartbeat-stale",
                "The broker heartbeat is missing or stale; the driver must remain in pass-through mode.");
        }

        if (!backend.PrimaryPassThroughAcknowledged || !backend.SecondaryExclusiveAcknowledged)
        {
            return Reject("driver-policy-not-acknowledged",
                "The driver has not acknowledged the exact primary pass-through and secondary exclusive roles.");
        }

        return ExclusiveInputActivationDecision.Permit();
    }

    private static List<ExclusiveKeyboardCandidate> FindMatches(
        IEnumerable<ExclusiveKeyboardCandidate> keyboards,
        string persistentId) =>
        keyboards
            .Where(candidate =>
                persistentId.Equals(candidate.PersistentId?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool HasRequiredTrust(
        ExclusiveInputActivationContext context,
        ExclusiveInputDeploymentTrust trust) =>
        context == ExclusiveInputActivationContext.Production
            ? trust == ExclusiveInputDeploymentTrust.MicrosoftHlkCertified
            : trust >= ExclusiveInputDeploymentTrust.DevelopmentTest;

    private static ExclusiveInputActivationDecision Reject(string code, string message) =>
        ExclusiveInputActivationDecision.Reject(code, message);
}
