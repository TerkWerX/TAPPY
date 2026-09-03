using System.Reflection;
using System.Text.Json;
using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Windows;
using Tappy.Windows.Output;
using Tappy.Windows.Profiles;

namespace Tappy.App.Runtime;

public static class ReadinessSmokeRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var resultPath = GetOption(args, "--result");
        if (string.IsNullOrWhiteSpace(resultPath) || !Path.IsPathFullyQualified(resultPath))
        {
            return 2;
        }

        var checks = new List<SmokeCheck>();
        var output = new CountingKeyboardOutput();
        try
        {
            var dataRoot = Environment.GetEnvironmentVariable("TAPPY_SMOKE_DATA_ROOT");
            if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
            {
                throw new InvalidOperationException("TAPPY_SMOKE_DATA_ROOT must be an isolated absolute directory.");
            }

            var fullDataRoot = Path.GetFullPath(dataRoot);
            var applicationRoot = Path.GetFullPath(AppContext.BaseDirectory);
            if (IsWithin(fullDataRoot, applicationRoot))
            {
                throw new InvalidOperationException("Readiness smoke data cannot be written inside the published application directory.");
            }

            var registryPassed = ValidateRuntimeData(applicationRoot);
            checks.Add(new SmokeCheck("controller-registry", registryPassed));

            var (profilePassed, snapshot, identity, controlId) = await ProfileRoundTripAsync(
                fullDataRoot, cancellationToken).ConfigureAwait(false);
            checks.Add(new SmokeCheck("profile-round-trip", profilePassed));

            var rehearsalPassed = ExerciseRehearsal(snapshot, identity, controlId, output);
            checks.Add(new SmokeCheck("rehearsal-no-output", rehearsalPassed));

            var doctorPassed = registryPassed && profilePassed && rehearsalPassed &&
                               output.InjectedInputCount == 0 &&
                               ProductIdentity.ProductName == "Tappy" &&
                               ProductIdentity.AppUserModelId == "TerkWerX.Tappy" &&
                               ProductIdentity.LocalDataFolderName == "Tappy" &&
                               !ProductIdentity.SingleInstanceMutexName.Contains("Foot", StringComparison.OrdinalIgnoreCase);
            checks.Add(new SmokeCheck("tappy-doctor", doctorPassed));
        }
        catch (Exception exception)
        {
            var required = new[]
            {
                "controller-registry", "profile-round-trip", "rehearsal-no-output", "tappy-doctor"
            };
            foreach (var name in required.Where(name => checks.All(check => check.Name != name)))
            {
                checks.Add(new SmokeCheck(name, false, exception.GetType().Name));
            }
        }

        var ordered = new[]
        {
            "controller-registry", "profile-round-trip", "rehearsal-no-output", "tappy-doctor"
        }.Select(name => checks.First(check => check.Name == name)).ToArray();
        var result = new SmokeResult(
            1,
            "Tappy",
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0",
            ordered.All(check => check.Passed),
            output.InjectedInputCount,
            ordered);

        var resultDirectory = Path.GetDirectoryName(resultPath);
        if (!string.IsNullOrWhiteSpace(resultDirectory))
        {
            Directory.CreateDirectory(resultDirectory);
        }

        await File.WriteAllTextAsync(
            resultPath,
            JsonSerializer.Serialize(result, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        return result.Ready ? 0 : 1;
    }

    private static bool ValidateRuntimeData(string applicationRoot)
    {
        var registryPath = Path.Combine(applicationRoot, "ControllerPacks", "controller_registry.json");
        var publishersPath = Path.Combine(applicationRoot, "ControllerPacks", "trusted-publishers.json");
        using var registry = JsonDocument.Parse(File.ReadAllText(registryPath));
        using var publishers = JsonDocument.Parse(File.ReadAllText(publishersPath));
        var registryRoot = registry.RootElement;
        var publisherRoot = publishers.RootElement;
        return registryRoot.GetProperty("schema_version").GetInt32() == 1 &&
               registryRoot.GetProperty("product").GetString() == "Tappy" &&
               registryRoot.GetProperty("rendering").GetProperty("mode").GetString() == "code" &&
               !registryRoot.GetProperty("rendering").GetProperty("external_artwork").GetBoolean() &&
               registryRoot.GetProperty("fallback_layout").GetProperty("kind").GetString() == "generated-grid" &&
               publisherRoot.GetProperty("schema_version").GetInt32() == 1 &&
               publisherRoot.GetProperty("product").GetString() == "Tappy";
    }

    private static async Task<(bool Passed, Tappy.Core.Profiles.TappyProfileSnapshot Snapshot,
        ControllerIdentity Identity, ControlId ControlId)> ProfileRoundTripAsync(
        string dataRoot,
        CancellationToken cancellationToken)
    {
        var sessionId = new ControllerSessionId("smoke-session");
        var persistentId = new ControllerPersistentId("smoke-controller");
        var identity = new ControllerIdentity(
            sessionId, persistentId, ControllerIdentityConfidence.PortBound,
            "Readiness smoke controller");
        var controlId = ControlId.FromRawInputKeyboard(0x4F);
        var controller = ControllerProfile.Create(identity, [controlId]);
        controller.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = controlId,
            Name = "Hold F24 until release",
            PressAction = KeyboardActionDefinition.Hold("F24")
        });
        var profile = new TappyProfile
        {
            Name = "Readiness smoke",
            Controllers = [controller]
        };
        var store = new AtomicProfileStore(dataRoot);
        await store.SaveAsync("readiness-smoke", profile.CreateSnapshot(), cancellationToken).ConfigureAwait(false);
        var loaded = await store.LoadAsync("readiness-smoke", cancellationToken).ConfigureAwait(false);
        var loadedController = loaded.FindController("smoke-controller");
        var passed = loaded.SchemaVersion == TappyProfile.CurrentSchemaVersion &&
                     loaded.Name == "Readiness smoke" &&
                     loadedController is not null &&
                     loadedController.Layers.Count == 3 &&
                     loadedController.Layout.Rows.SelectMany(row => row.Controls)
                         .Any(control => control.ControlId == controlId) &&
                     loadedController.Layers[0].FindBinding(controlId)?.PressAction.Keys
                         .SequenceEqual([new KeyboardOutputKey("F24")]) == true;
        return (passed, loaded, identity, controlId);
    }

    private static bool ExerciseRehearsal(
        Tappy.Core.Profiles.TappyProfileSnapshot profile,
        ControllerIdentity identity,
        ControlId controlId,
        CountingKeyboardOutput output)
    {
        var engine = new MappingEngine(output, new MappingEngineOptions
        {
            SelfInjectionMarker = InjectedInputMarker.Value
        });
        engine.SetProfile(profile);
        engine.SetRehearsalMode(true);
        engine.Activation.SelectCandidate(identity.SessionId);
        _ = engine.Process(ControlSignal.Physical(identity.SessionId, controlId, ControlSignalKind.Press, 1));
        _ = engine.Process(ControlSignal.Physical(identity.SessionId, controlId, ControlSignalKind.Release, 2));
        engine.Activation.Confirm();
        AssertConnected(engine.ConnectController(identity));
        var press = engine.Process(ControlSignal.Physical(identity.SessionId, controlId, ControlSignalKind.Press, 3));
        var release = engine.Process(ControlSignal.Physical(identity.SessionId, controlId, ControlSignalKind.Release, 4));
        return press.Disposition == MappingDisposition.Rehearsal &&
               release.Disposition == MappingDisposition.Rehearsal &&
               output.InjectedInputCount == 0;
    }

    private static void AssertConnected(bool connected)
    {
        if (!connected)
        {
            throw new InvalidOperationException("The smoke controller did not reconnect to its reloaded profile.");
        }
    }

    private static string? GetOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static bool IsWithin(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." ||
               (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative));
    }

    private sealed class CountingKeyboardOutput : IKeyboardOutput
    {
        public int InjectedInputCount { get; private set; }

        public void KeyDown(KeyboardOutputRequest request) =>
            InjectedInputCount += request.Keys.Count;

        public void KeyUp(KeyboardOutputRequest request) =>
            InjectedInputCount += request.Keys.Count;
    }

    private sealed record SmokeCheck(string Name, bool Passed, string? Detail = null);

    private sealed record SmokeResult(
        int SchemaVersion,
        string Product,
        string Version,
        bool Ready,
        int InjectedInputCount,
        IReadOnlyList<SmokeCheck> Checks);
}
