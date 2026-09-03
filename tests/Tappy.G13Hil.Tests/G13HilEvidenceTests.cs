using System.Text.Json;
using Tappy.Windows.Input;

namespace Tappy.G13Hil.Tests;

public sealed class G13HilEvidenceTests
{
    private const string PrivateSessionId = "PRIVATE_SESSION_ID";
    private const string PrivatePersistentId = "PRIVATE_PERSISTENT_ID";

    [Fact]
    public async Task WriterPersistsOnlyAggregateInputFunctionalSchema()
    {
        var descriptor = ExactDescriptor();
        var session = G13HilSessionTests.CreateCompleteCleanSession().Snapshot();
        var runtime = PassingRuntime();
        var evidence = G13HilEvidenceFactory.Create(
            G13HilOutcome.Passed,
            descriptor,
            session,
            runtime);
        var repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            $"Tappy.G13Hil.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "Tappy.slnx"), "<Solution />");

        try
        {
            var path = await G13HilEvidenceWriter.WriteAsync(repositoryRoot, evidence);
            var json = await File.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(json);
            var rootNames = document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal("g13.tappy-hil.json", Path.GetFileName(path));
            Assert.Equal(
                new[]
                {
                    "assertions",
                    "compatibilityTierClaimed",
                    "counts",
                    "device",
                    "durations",
                    "evidenceScope",
                    "outcome",
                    "product",
                    "schemaVersion",
                    "toolVersion",
                }.Order(StringComparer.Ordinal),
                rootNames);
            Assert.Equal(
                "input-functional",
                document.RootElement.GetProperty("evidenceScope").GetString());
            Assert.Equal(
                "none",
                document.RootElement.GetProperty("compatibilityTierClaimed").GetString());
            Assert.DoesNotContain(PrivateSessionId, json, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivatePersistentId, json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"prompt\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promptRevision", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("currentExpectedControl", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("instruction", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("displayName", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("controlId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rawReport", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("timestamp", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("startedUtc", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("endedUtc", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("isVerified", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void LifecycleInterruptionAfterCompleteRunCannotBecomePassedEvidence()
    {
        var runtime = PassingRuntime() with { LifecycleInterruptions = 1 };

        var evidence = G13HilEvidenceFactory.Create(
            G13HilOutcome.Aborted,
            ExactDescriptor(),
            G13HilSessionTests.CreateCompleteCleanSession().Snapshot(),
            runtime);

        Assert.Equal(G13HilOutcome.Aborted, evidence.Outcome);
        Assert.False(evidence.Assertions.FiniteInputRunCompleted);
        Assert.False(evidence.Assertions.NoLifecycleInterruption);
    }

    [Fact]
    public void PassedOutcomeRequiresEveryNamedAggregateAssertion()
    {
        var runtime = PassingRuntime() with { ProviderFaults = 1 };

        Assert.Throws<InvalidOperationException>(() => G13HilEvidenceFactory.Create(
            G13HilOutcome.Passed,
            ExactDescriptor(),
            G13HilSessionTests.CreateCompleteCleanSession().Snapshot(),
            runtime));
    }

    [Fact]
    public void DeviceSelectionAcceptsOnlyOneExactPhysicalProtocolDescriptor()
    {
        var exact = ExactDescriptor();
        var virtualKeyboard = exact with
        {
            SessionHandle = new nint(2),
            ProductId = 0xC232,
            Kind = RawInputDeviceKind.Keyboard,
            UsagePage = 0x0001,
            Usage = 0x0006,
        };

        var selected = G13HilDeviceSelection.From([virtualKeyboard, exact]);
        var ambiguous = G13HilDeviceSelection.From([exact, exact with { SessionHandle = new nint(3) }]);
        var missing = G13HilDeviceSelection.From([virtualKeyboard]);

        Assert.Equal(G13HilDeviceSelectionStatus.Selected, selected.Status);
        Assert.Same(exact, selected.Descriptor);
        Assert.Equal(G13HilDeviceSelectionStatus.MultiplePhysicalG13Controllers, ambiguous.Status);
        Assert.Equal(G13HilDeviceSelectionStatus.NoPhysicalG13, missing.Status);
    }

    [Fact]
    public void DeviceSelectionRejectsUnsanitizedFingerprint()
    {
        var result = G13HilDeviceSelection.From(
            [ExactDescriptor() with { PathFingerprintSha256 = "not-a-hash" }]);

        Assert.Equal(G13HilDeviceSelectionStatus.InvalidSanitizedDescriptor, result.Status);
        Assert.Null(result.Descriptor);
    }

    private static SanitizedDeviceDescriptor ExactDescriptor() =>
        new(
            new nint(1),
            PrivateSessionId,
            PrivatePersistentId,
            new string('A', 64),
            RawInputDeviceKind.Hid,
            LogitechG13Protocol.VendorId,
            LogitechG13Protocol.ProductId,
            LogitechG13Protocol.UsagePage,
            LogitechG13Protocol.Usage,
            "PRIVATE_DISPLAY_NAME")
        {
            Grouping = PhysicalDeviceGrouping.WindowsContainerId,
            MemberSessionHandles = [new nint(1)],
        };

    private static G13HilRuntimeFacts PassingRuntime() =>
        new(
            ExactPhysicalIdentity: true,
            SinglePhysicalController: true,
            CleanupCompleted: true,
            Disconnects: 0,
            ProviderFaults: 0,
            LifecycleInterruptions: 0,
            TimedOut: false,
            TotalDurationMs: 1234,
            HardTimeoutMs: 1_800_000);
}
