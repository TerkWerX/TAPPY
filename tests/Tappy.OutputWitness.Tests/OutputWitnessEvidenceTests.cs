using System.Text.Json;

namespace Tappy.OutputWitness.Tests;

public sealed class OutputWitnessEvidenceTests
{
    private const string PrivatePathMarker = "PRIVATE_RAW_DEVICE_PATH_DO_NOT_STORE";

    [Theory]
    [InlineData(0, "operator-set-tappy-normal-output-mode")]
    [InlineData(1, "operator-set-tappy-rehearsal-mode")]
    [InlineData(2, "operator-set-tappy-normal-output-mode")]
    public void PassingEvidenceIsExplicitAboutOperatorPrerequisiteAndNoAttribution(
        int scenarioValue,
        string expectedPrerequisite)
    {
        var scenario = (OutputWitnessScenario)scenarioValue;
        var options = Options(scenario);
        var snapshot = PassingSnapshot(scenario);
        var evidence = OutputWitnessEvidenceFactory.Create(
            OutputWitnessOutcome.Passed,
            options,
            snapshot,
            PassingRuntime());

        Assert.Equal("focused-console-pass-through-and-output-witness", evidence.EvidenceScope);
        Assert.Equal("none", evidence.DeviceSourceAttribution);
        Assert.Equal("none", evidence.CompatibilityTierClaimed);
        Assert.Equal(expectedPrerequisite, evidence.OperatorPrerequisite);
        Assert.Equal(OutputWitnessOptionParser.ScenarioName(scenario), evidence.Scenario);
        Assert.All(
            evidence.Assertions.GetType().GetProperties(),
            property => Assert.True((bool)property.GetValue(evidence.Assertions)!));
    }

    [Fact]
    public async Task WriterUsesRandomIgnoredHilDirectoryAndStoresOnlyAggregateContract()
    {
        var evidence = OutputWitnessEvidenceFactory.Create(
            OutputWitnessOutcome.Passed,
            Options(OutputWitnessScenario.Basic),
            PassingSnapshot(OutputWitnessScenario.Basic),
            PassingRuntime());
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"Tappy.OutputWitness.Tests-{PrivatePathMarker}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "Tappy.slnx"), "<Solution />");

        try
        {
            var path = await OutputWitnessEvidenceWriter.WriteAsync(repositoryRoot, evidence);
            var json = await File.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(json);
            var runDirectory = Directory.GetParent(path)!;
            var evidenceRoot = runDirectory.Parent!;
            var rootProperties = document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal("output-witness.tappy-hil.json", Path.GetFileName(path));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "hil")),
                evidenceRoot.FullName);
            Assert.True(Guid.TryParseExact(runDirectory.Name, "N", out _));
            Assert.Equal(
                new[]
                {
                    "assertions",
                    "compatibilityTierClaimed",
                    "counts",
                    "deviceSourceAttribution",
                    "durations",
                    "evidenceScope",
                    "expectedKeys",
                    "finalState",
                    "operatorPrerequisite",
                    "outcome",
                    "product",
                    "scenario",
                    "schemaVersion",
                    "toolVersion",
                }.Order(StringComparer.Ordinal),
                rootProperties);
            Assert.DoesNotContain(PrivatePathMarker, json, StringComparison.Ordinal);
            Assert.DoesNotContain("timestamp", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("chronology", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sequence", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unicode", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("scanCode", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("virtualKeyCode", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("devicePath", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("containerId", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void PassingOutcomeRejectsCleanupOrReaderFaultContradictions()
    {
        var options = Options(OutputWitnessScenario.Basic);
        var snapshot = PassingSnapshot(OutputWitnessScenario.Basic);

        Assert.Throws<InvalidOperationException>(() =>
            OutputWitnessEvidenceFactory.Create(
                OutputWitnessOutcome.Passed,
                options,
                snapshot,
                PassingRuntime() with { CleanupCompleted = false }));
        Assert.Throws<InvalidOperationException>(() =>
            OutputWitnessEvidenceFactory.Create(
                OutputWitnessOutcome.Passed,
                options,
                snapshot,
                PassingRuntime() with { ConsoleReaderFaults = 1 }));
    }

    [Fact]
    public void EvidenceRejectsForgedKeyOutsideFixedAllowlist()
    {
        var options = Options(OutputWitnessScenario.Basic) with
        {
            OriginalKey = new WitnessKeySpec(PrivatePathMarker, 0x41),
        };

        Assert.Throws<InvalidOperationException>(() =>
            OutputWitnessEvidenceFactory.Create(
                OutputWitnessOutcome.Failed,
                options,
                PassingSnapshot(OutputWitnessScenario.Basic),
                PassingRuntime()));
    }

    [Fact]
    public async Task WriterRefusesDirectoryThatIsNotTappyRepositoryRoot()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"Tappy.OutputWitness.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                OutputWitnessEvidenceWriter.WriteAsync(
                    directory,
                    OutputWitnessEvidenceFactory.Create(
                        OutputWitnessOutcome.Passed,
                        Options(OutputWitnessScenario.Basic),
                        PassingSnapshot(OutputWitnessScenario.Basic),
                        PassingRuntime())));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static OutputWitnessOptions Options(OutputWitnessScenario scenario) =>
        new(
            Armed: true,
            FocusedConsoleOnlyAcknowledged: true,
            NoDeviceAttributionAcknowledged: true,
            TappyModeSetAcknowledged: true,
            Timeout: OutputWitnessOptions.DefaultTimeout,
            scenario,
            WitnessKeyCatalog.DefaultOriginalKey,
            WitnessKeyCatalog.DefaultOutputKey);

    private static OutputWitnessRuntimeFacts PassingRuntime() =>
        new(
            ExplicitlyArmed: true,
            FocusedConsoleOnlyAcknowledged: true,
            NoDeviceAttributionAcknowledged: true,
            TappyModeSetAcknowledged: true,
            CleanupCompleted: true,
            ConsoleReaderFaults: 0,
            TimedOut: false,
            TotalDurationMs: 2500,
            HardTimeoutMs: 120000);

    private static OutputWitnessSessionSnapshot PassingSnapshot(
        OutputWitnessScenario scenario)
    {
        var clock = 0L;
        var session = new OutputWitnessSession(
            scenario,
            WitnessKeyCatalog.DefaultOriginalKey,
            WitnessKeyCatalog.DefaultOutputKey,
            () => clock,
            timestampFrequency: 1000);
        void Source(bool down, ushort repeat = 1) =>
            session.Accept(new ConsoleKeyObservation(
                WitnessKeyCatalog.DefaultOriginalKey.VirtualKeyCode,
                down,
                repeat));
        void Output(bool down) =>
            session.Accept(new ConsoleKeyObservation(
                WitnessKeyCatalog.DefaultOutputKey.VirtualKeyCode,
                down,
                1));

        switch (scenario)
        {
            case OutputWitnessScenario.Basic:
                Source(true);
                Source(true, repeat: 2);
                Output(true);
                Source(false);
                Output(false);
                break;
            case OutputWitnessScenario.Rehearsal:
                Source(true);
                Source(false);
                clock = 2000;
                break;
            case OutputWitnessScenario.HeldUnplug:
                Source(true);
                Output(true);
                Output(false);
                clock = 2000;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var snapshot = session.Snapshot();
        Assert.True(snapshot.ScenarioAssertionsPassed);
        return snapshot;
    }
}
