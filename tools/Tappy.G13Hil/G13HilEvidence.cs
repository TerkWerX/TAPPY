using System.Text.Json;
using System.Text.Json.Serialization;
using Tappy.Windows.Input;

namespace Tappy.G13Hil;

[JsonConverter(typeof(JsonStringEnumConverter<G13HilOutcome>))]
internal enum G13HilOutcome
{
    Passed,
    Failed,
    Aborted,
    TimedOut,
    Disconnected,
    Faulted,
}

internal sealed record G13HilEvidenceDevice(
    string FingerprintSha256,
    string VendorId,
    string ProductId,
    string UsagePage,
    string Usage,
    string Grouping,
    int InterfaceCount);

internal sealed record G13HilEvidenceAssertions(
    bool FiniteInputRunCompleted,
    bool ExactPhysicalIdentity,
    bool SinglePhysicalController,
    bool InitialNeutralObserved,
    bool PressReleaseHandshake,
    bool EveryCodeDefinedControl,
    bool TwoBalancedCyclesPerControl,
    bool AllStickDirections,
    bool SimultaneousSets,
    bool DuplicateSuppressionDuringStickSweep,
    bool ExpectedControlGating,
    bool GloballyBalancedTransitions,
    bool NoDisconnect,
    bool NoProviderFault,
    bool NoLifecycleInterruption,
    bool WithinHardTimeout,
    bool CaptureCleanupCompleted);

internal sealed record G13HilEvidenceCounts(
    int CodeDefinedControls,
    int RequiredCyclesPerControl,
    int CompletedControlCycles,
    int AcceptedPresses,
    int AcceptedReleases,
    int UnexpectedTransitions,
    int DuplicateTransitions,
    int UnbalancedTransitions,
    int PromptRetries,
    int StickDirectionsRequired,
    int StickDirectionsPassed,
    int SimultaneousSetsRequired,
    int SimultaneousSetsPassed,
    int Disconnects,
    int ProviderFaults,
    int LifecycleInterruptions);

internal sealed record G13HilEvidenceDurations(
    long TotalMs,
    long NeutralMs,
    long HandshakeMs,
    long ControlsMs,
    long SimultaneousSetsMs,
    long DuplicateSweepMs,
    long HardTimeoutMs);

internal sealed record G13HilEvidence(
    int SchemaVersion,
    string Product,
    string ToolVersion,
    string EvidenceScope,
    string CompatibilityTierClaimed,
    G13HilOutcome Outcome,
    G13HilEvidenceDevice Device,
    G13HilEvidenceAssertions Assertions,
    G13HilEvidenceCounts Counts,
    G13HilEvidenceDurations Durations);

internal sealed record G13HilRuntimeFacts(
    bool ExactPhysicalIdentity,
    bool SinglePhysicalController,
    bool CleanupCompleted,
    int Disconnects,
    int ProviderFaults,
    int LifecycleInterruptions,
    bool TimedOut,
    long TotalDurationMs,
    long HardTimeoutMs);

internal static class G13HilEvidenceFactory
{
    internal static G13HilEvidence Create(
        G13HilOutcome outcome,
        SanitizedDeviceDescriptor descriptor,
        G13HilSessionSnapshot session,
        G13HilRuntimeFacts runtime)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(runtime);

        var assertions = new G13HilEvidenceAssertions(
            outcome == G13HilOutcome.Passed,
            runtime.ExactPhysicalIdentity,
            runtime.SinglePhysicalController,
            session.NeutralObserved,
            session.HandshakePassed,
            session.AllControlsPassed,
            session.CompletedControlCycles ==
                session.CodeDefinedControlCount * session.RequiredCyclesPerControl,
            session.StickDirectionsPassed,
            session.SimultaneousSetsPassed,
            session.DuplicateSweepCompleted && session.DuplicateSuppressionPassed,
            session.ExpectedControlGatingPassed,
            session.BalancedTransitions,
            runtime.Disconnects == 0,
            runtime.ProviderFaults == 0,
            runtime.LifecycleInterruptions == 0,
            !runtime.TimedOut,
            runtime.CleanupCompleted);
        var allAssertionsPassed = assertions.GetType()
            .GetProperties()
            .All(property => property.GetValue(assertions) is true);
        if ((outcome == G13HilOutcome.Passed) != allAssertionsPassed)
        {
            throw new InvalidOperationException("Passed status must exactly match the aggregate assertion result.");
        }

        return new G13HilEvidence(
            SchemaVersion: 1,
            Product: "Tappy",
            ToolVersion: "0.1.0",
            EvidenceScope: "input-functional",
            CompatibilityTierClaimed: "none",
            outcome,
            new G13HilEvidenceDevice(
                descriptor.PathFingerprintSha256,
                LogitechG13Protocol.VendorId.ToString("X4"),
                LogitechG13Protocol.ProductId.ToString("X4"),
                LogitechG13Protocol.UsagePage.ToString("X4"),
                LogitechG13Protocol.Usage.ToString("X4"),
                descriptor.Grouping.ToString(),
                descriptor.InterfaceCount),
            assertions,
            new G13HilEvidenceCounts(
                session.CodeDefinedControlCount,
                session.RequiredCyclesPerControl,
                session.CompletedControlCycles,
                session.AcceptedPresses,
                session.AcceptedReleases,
                session.UnexpectedTransitions,
                session.DuplicateTransitions,
                session.UnbalancedTransitions,
                session.PromptRetries,
                StickDirectionsRequired: 4,
                session.StickDirectionsPassedCount,
                session.SimultaneousSetsRequired,
                session.SimultaneousSetsPassedCount,
                runtime.Disconnects,
                runtime.ProviderFaults,
                runtime.LifecycleInterruptions),
            new G13HilEvidenceDurations(
                runtime.TotalDurationMs,
                session.NeutralDurationMs,
                session.HandshakeDurationMs,
                session.ControlsDurationMs,
                session.SimultaneousDurationMs,
                session.DuplicateSweepDurationMs,
                runtime.HardTimeoutMs));
    }
}

internal static class G13HilEvidenceWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tappy.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Tappy repository root was not found.");
    }

    internal static async Task<string> WriteAsync(
        string repositoryRoot,
        G13HilEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(evidence);

        var root = Path.GetFullPath(repositoryRoot);
        if (!File.Exists(Path.Combine(root, "Tappy.slnx")))
        {
            throw new InvalidOperationException("Evidence root is not a Tappy repository.");
        }

        var evidenceRoot = Path.GetFullPath(Path.Combine(root, "artifacts", "hil"));
        var expectedPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!evidenceRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Evidence path escaped the repository root.");
        }

        var runDirectory = Path.Combine(evidenceRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var destination = Path.Combine(runDirectory, "g13.tappy-hil.json");
        var temporary = destination + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    evidence,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
