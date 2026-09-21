using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tappy.OutputWitness;

[JsonConverter(typeof(JsonStringEnumConverter<OutputWitnessOutcome>))]
internal enum OutputWitnessOutcome
{
    Passed,
    Failed,
    Aborted,
    TimedOut,
    Faulted,
}

internal sealed record OutputWitnessEvidenceExpectedKeys(
    string Original,
    string Output);

internal sealed record OutputWitnessEvidenceAssertions(
    bool ExplicitArmAcknowledged,
    bool FocusedConsoleOnlyAcknowledged,
    bool NoDeviceAttributionAcknowledged,
    bool TappyModeSetAcknowledged,
    bool FiniteScenarioCompleted,
    bool OriginalKeyDownRequirementSatisfied,
    bool OriginalKeyUpRequirementSatisfied,
    bool OriginalRepeatRequirementSatisfied,
    bool OutputTransitionRequirementSatisfied,
    bool NoUnexpectedOrDuplicateOutputTransitions,
    bool HeldUnplugRequirementSatisfied,
    bool PostConditionWindowRequirementSatisfied,
    bool FinalOutputReleased,
    bool NoConsoleReaderFault,
    bool WithinHardTimeout,
    bool ConsoleCleanupCompleted);

internal sealed record OutputWitnessEvidenceCounts(
    int OriginalKeyDownUnits,
    int OriginalKeyUpUnits,
    int OriginalRepeatUnits,
    int OriginalUnbalancedReleaseUnits,
    int OutputKeyDownUnits,
    int OutputKeyUpUnits,
    int OutputDuplicateDownUnits,
    int OutputUnbalancedReleaseUnits,
    int ConsoleReaderFaults);

internal sealed record OutputWitnessEvidenceFinalState(
    bool OriginalKeyHeld,
    bool OutputKeyHeld);

internal sealed record OutputWitnessEvidenceDurations(
    long TotalMs,
    long HardTimeoutMs,
    long PostConditionWindowRequiredMs,
    long PostConditionWindowObservedMs);

internal sealed record OutputWitnessEvidence(
    int SchemaVersion,
    string Product,
    string ToolVersion,
    string EvidenceScope,
    string CompatibilityTierClaimed,
    string DeviceSourceAttribution,
    string OperatorPrerequisite,
    string Scenario,
    OutputWitnessOutcome Outcome,
    OutputWitnessEvidenceExpectedKeys ExpectedKeys,
    OutputWitnessEvidenceAssertions Assertions,
    OutputWitnessEvidenceCounts Counts,
    OutputWitnessEvidenceFinalState FinalState,
    OutputWitnessEvidenceDurations Durations);

internal sealed record OutputWitnessRuntimeFacts(
    bool ExplicitlyArmed,
    bool FocusedConsoleOnlyAcknowledged,
    bool NoDeviceAttributionAcknowledged,
    bool TappyModeSetAcknowledged,
    bool CleanupCompleted,
    int ConsoleReaderFaults,
    bool TimedOut,
    long TotalDurationMs,
    long HardTimeoutMs);

internal static class OutputWitnessEvidenceFactory
{
    internal static OutputWitnessEvidence Create(
        OutputWitnessOutcome outcome,
        OutputWitnessOptions options,
        OutputWitnessSessionSnapshot session,
        OutputWitnessRuntimeFacts runtime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(runtime);
        if (!WitnessKeyCatalog.IsAllowedOriginal(options.OriginalKey) ||
            !WitnessKeyCatalog.IsAllowedOutput(options.OutputKey) ||
            options.Scenario != session.Scenario)
        {
            throw new InvalidOperationException("Evidence inputs are outside the witness allowlist.");
        }

        var originalUpSatisfied = options.Scenario switch
        {
            OutputWitnessScenario.HeldUnplug =>
                session.SourceKeyUpUnits == 0 && session.SourceKeyHeld,
            OutputWitnessScenario.ControllerBasic or
                OutputWitnessScenario.ControllerHeldUnplug =>
                session.SourceKeyDownUnits == 0 &&
                session.SourceKeyUpUnits == 0 &&
                !session.SourceKeyHeld,
            _ => session.SourceKeyUpUnits == 1 && !session.SourceKeyHeld,
        };
        var originalRepeatSatisfied = options.Scenario switch
        {
            OutputWitnessScenario.Basic => session.SourceRepeatUnits > 0,
            OutputWitnessScenario.ControllerBasic or
                OutputWitnessScenario.ControllerHeldUnplug =>
                session.SourceRepeatUnits == 0,
            _ => true,
        };
        var outputSatisfied = options.Scenario == OutputWitnessScenario.Rehearsal
            ? session.OutputKeyDownUnits == 0 && session.OutputKeyUpUnits == 0
            : session.OutputKeyDownUnits == 1 && session.OutputKeyUpUnits == 1;
        var heldUnplugSatisfied = options.Scenario is not
            (OutputWitnessScenario.HeldUnplug or
                OutputWitnessScenario.ControllerHeldUnplug) ||
            session.HeldUnplugStageReached &&
            session.HeldUnplugOutputReleaseObserved &&
            session.SourceKeyUpUnits == 0;
        var postConditionWindowSatisfied = options.Scenario == OutputWitnessScenario.Basic ||
            session.PostConditionWindowCompleted;

        var assertions = new OutputWitnessEvidenceAssertions(
            runtime.ExplicitlyArmed,
            runtime.FocusedConsoleOnlyAcknowledged,
            runtime.NoDeviceAttributionAcknowledged,
            runtime.TappyModeSetAcknowledged,
            outcome == OutputWitnessOutcome.Passed,
            session.OriginalKeyDownRequirementSatisfied,
            originalUpSatisfied,
            originalRepeatSatisfied,
            outputSatisfied,
            session.NoUnexpectedOrDuplicateOutputTransitions,
            heldUnplugSatisfied,
            postConditionWindowSatisfied,
            !session.OutputKeyHeld,
            runtime.ConsoleReaderFaults == 0,
            !runtime.TimedOut && runtime.TotalDurationMs <= runtime.HardTimeoutMs,
            runtime.CleanupCompleted);
        var allAssertionsPassed = assertions.GetType()
            .GetProperties()
            .All(property => property.GetValue(assertions) is true);
        if ((outcome == OutputWitnessOutcome.Passed) != allAssertionsPassed)
        {
            throw new InvalidOperationException(
                "Passed status must exactly match every aggregate witness assertion.");
        }

        return new OutputWitnessEvidence(
            SchemaVersion: 2,
            Product: "Tappy",
            ToolVersion: "0.3.0",
            EvidenceScope: EvidenceScope(options.Scenario),
            CompatibilityTierClaimed: "none",
            DeviceSourceAttribution: "none",
            OperatorPrerequisite: OperatorPrerequisite(options.Scenario),
            Scenario: OutputWitnessOptionParser.ScenarioName(options.Scenario),
            outcome,
            new OutputWitnessEvidenceExpectedKeys(
                OriginalKeyRequirement(options),
                options.OutputKey.Name),
            assertions,
            new OutputWitnessEvidenceCounts(
                session.SourceKeyDownUnits,
                session.SourceKeyUpUnits,
                session.SourceRepeatUnits,
                session.SourceUnbalancedReleaseUnits,
                session.OutputKeyDownUnits,
                session.OutputKeyUpUnits,
                session.OutputDuplicateDownUnits,
                session.OutputUnbalancedReleaseUnits,
                runtime.ConsoleReaderFaults),
            new OutputWitnessEvidenceFinalState(
                session.SourceKeyHeld,
                session.OutputKeyHeld),
            new OutputWitnessEvidenceDurations(
                runtime.TotalDurationMs,
                runtime.HardTimeoutMs,
                session.PostConditionWindowRequiredMs,
                session.PostConditionWindowObservedMs));
    }

    private static string OperatorPrerequisite(OutputWitnessScenario scenario) =>
        scenario switch
        {
            OutputWitnessScenario.Rehearsal => "operator-set-tappy-rehearsal-mode",
            OutputWitnessScenario.ControllerHeldUnplug =>
                "operator-held-controller-control-and-unplugged-device-in-tappy-normal-output-mode",
            _ => "operator-set-tappy-normal-output-mode",
        };

    private static string EvidenceScope(OutputWitnessScenario scenario) =>
        scenario is OutputWitnessScenario.ControllerBasic or
            OutputWitnessScenario.ControllerHeldUnplug
            ? "focused-console-controller-output-witness"
            : "focused-console-pass-through-and-output-witness";

    private static string OriginalKeyRequirement(OutputWitnessOptions options) =>
        options.Scenario is OutputWitnessScenario.ControllerBasic or
            OutputWitnessScenario.ControllerHeldUnplug
            ? $"none-required; {options.OriginalKey.Name} guard-must-remain-silent"
            : options.OriginalKey.Name;
}

internal static class OutputWitnessEvidenceWriter
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
        OutputWitnessEvidence evidence,
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
        var expectedPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!evidenceRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Evidence path escaped the repository root.");
        }

        var runDirectory = Path.Combine(evidenceRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var destination = Path.Combine(runDirectory, "output-witness.tappy-hil.json");
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
