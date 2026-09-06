using Tappy.Core.Input;

namespace Tappy.Core.Tests;

public sealed class ExclusiveInputSafetyPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Production_allows_only_exact_distinct_keyboards_after_every_gate_acknowledges()
    {
        var decision = Evaluate();

        Assert.True(decision.Allowed);
        Assert.Equal("ready", decision.Code);
    }

    [Fact]
    public void Primary_keyboard_can_never_also_be_the_suppressed_keyboard()
    {
        var request = GoodRequest() with { SecondaryKeyboardPersistentId = "primary" };

        var decision = Evaluate(request: request);

        Assert.False(decision.Allowed);
        Assert.Equal("keyboard-roles-overlap", decision.Code);
    }

    [Theory]
    [InlineData(false, true, "recovery-path-missing")]
    [InlineData(true, false, "recovery-path-missing")]
    public void Mouse_and_primary_keyboard_recovery_are_both_mandatory(
        bool mouseRecovery,
        bool emergencyStop,
        string expectedCode)
    {
        var request = GoodRequest() with
        {
            MouseRecoveryAvailable = mouseRecovery,
            EmergencyStopAvailableOnPrimary = emergencyStop
        };

        var decision = Evaluate(request: request);

        Assert.False(decision.Allowed);
        Assert.Equal(expectedCode, decision.Code);
    }

    [Fact]
    public void Ambiguous_duplicate_identity_fails_closed_to_pass_through()
    {
        var keyboards = GoodKeyboards().Append(
            new ExclusiveKeyboardCandidate("secondary", true, true, true)).ToArray();

        var decision = Evaluate(keyboards: keyboards);

        Assert.False(decision.Allowed);
        Assert.Equal("keyboard-identity-ambiguous", decision.Code);
    }

    [Fact]
    public void Session_only_identity_cannot_enter_exclusive_mode()
    {
        var keyboards =
            new[]
            {
                new ExclusiveKeyboardCandidate("primary", true, true, true),
                new ExclusiveKeyboardCandidate("secondary", true, true, false)
            };

        var decision = Evaluate(keyboards: keyboards);

        Assert.False(decision.Allowed);
        Assert.Equal("unstable-keyboard-identity", decision.Code);
    }

    [Fact]
    public void Production_rejects_attestation_only_package_under_stricter_tappy_policy()
    {
        var backend = GoodBackend() with
        {
            Trust = ExclusiveInputDeploymentTrust.MicrosoftAttestation
        };

        var decision = Evaluate(backend: backend);

        Assert.False(decision.Allowed);
        Assert.Equal("driver-trust-insufficient", decision.Code);
    }

    [Fact]
    public void Development_accepts_explicit_test_package_but_protected_apps_still_block_it()
    {
        var request = GoodRequest() with
        {
            Context = ExclusiveInputActivationContext.DevelopmentTest,
            ProtectedApplicationActive = true
        };
        var backend = GoodBackend() with
        {
            Trust = ExclusiveInputDeploymentTrust.DevelopmentTest,
            IsTestSigningEnabled = true
        };

        var decision = Evaluate(request, backend);

        Assert.False(decision.Allowed);
        Assert.Equal("protected-application-active", decision.Code);
    }

    [Fact]
    public void Stale_heartbeat_never_leaves_suppression_armed()
    {
        var backend = GoodBackend() with
        {
            WatchdogTimeout = TimeSpan.FromSeconds(1),
            LastHeartbeatUtc = Now - TimeSpan.FromMilliseconds(1001)
        };

        var decision = Evaluate(backend: backend);

        Assert.False(decision.Allowed);
        Assert.Equal("heartbeat-stale", decision.Code);
    }

    [Fact]
    public void Production_rejects_test_signing_or_kernel_debugging()
    {
        var testSigning = Evaluate(backend: GoodBackend() with { IsTestSigningEnabled = true });
        var kernelDebugging = Evaluate(backend: GoodBackend() with { IsKernelDebuggingEnabled = true });

        Assert.Equal("unsafe-boot-configuration", testSigning.Code);
        Assert.Equal("unsafe-boot-configuration", kernelDebugging.Code);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Both_driver_role_acknowledgements_are_required(bool primaryAck, bool secondaryAck)
    {
        var backend = GoodBackend() with
        {
            PrimaryPassThroughAcknowledged = primaryAck,
            SecondaryExclusiveAcknowledged = secondaryAck
        };

        var decision = Evaluate(backend: backend);

        Assert.False(decision.Allowed);
        Assert.Equal("driver-policy-not-acknowledged", decision.Code);
    }

    private static ExclusiveInputActivationDecision Evaluate(
        ExclusiveInputActivationRequest? request = null,
        ExclusiveInputBackendEvidence? backend = null,
        IReadOnlyCollection<ExclusiveKeyboardCandidate>? keyboards = null) =>
        ExclusiveInputSafetyPolicy.Evaluate(
            request ?? GoodRequest(),
            backend ?? GoodBackend(),
            keyboards ?? GoodKeyboards(),
            Now);

    private static ExclusiveInputActivationRequest GoodRequest() => new(
        "primary",
        "secondary",
        HasExplicitAdministratorConsent: true,
        MouseRecoveryAvailable: true,
        EmergencyStopAvailableOnPrimary: true,
        ProtectedApplicationActive: false,
        Context: ExclusiveInputActivationContext.Production);

    private static ExclusiveInputBackendEvidence GoodBackend() => new(
        DriverLoaded: true,
        BrokerAuthenticated: true,
        PrimaryPassThroughAcknowledged: true,
        SecondaryExclusiveAcknowledged: true,
        FailOpenWatchdogArmed: true,
        WatchdogTimeout: TimeSpan.FromSeconds(1),
        LastHeartbeatUtc: Now - TimeSpan.FromMilliseconds(100),
        Trust: ExclusiveInputDeploymentTrust.MicrosoftHlkCertified,
        IsHvciCompatible: true,
        IsTestSigningEnabled: false,
        IsKernelDebuggingEnabled: false);

    private static IReadOnlyCollection<ExclusiveKeyboardCandidate> GoodKeyboards() =>
        new[]
        {
            new ExclusiveKeyboardCandidate("primary", true, true, true),
            new ExclusiveKeyboardCandidate("secondary", true, true, true)
        };
}
