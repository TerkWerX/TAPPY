using System.Globalization;

namespace Tappy.OutputWitness;

internal enum OutputWitnessScenario
{
    Basic,
    Rehearsal,
    HeldUnplug,
}

internal sealed record WitnessKeySpec(string Name, ushort VirtualKeyCode);

internal static class WitnessKeyCatalog
{
    private static readonly WitnessKeySpec[] OriginalKeys =
        Enumerable.Range(0, 10)
            .Select(number => new WitnessKeySpec($"NumPad{number}", checked((ushort)(0x60 + number))))
            .ToArray();

    private static readonly WitnessKeySpec[] OutputKeys =
        Enumerable.Range(13, 12)
            .Select(number => new WitnessKeySpec($"F{number}", checked((ushort)(0x7C + number - 13))))
            .ToArray();

    internal static WitnessKeySpec DefaultOriginalKey => OriginalKeys[1];

    internal static WitnessKeySpec DefaultOutputKey => OutputKeys[^1];

    internal static IReadOnlyList<WitnessKeySpec> AllowedOriginalKeys => OriginalKeys;

    internal static IReadOnlyList<WitnessKeySpec> AllowedOutputKeys => OutputKeys;

    internal static bool TryGetOriginal(string value, out WitnessKeySpec? key) =>
        TryGet(OriginalKeys, value, out key);

    internal static bool TryGetOutput(string value, out WitnessKeySpec? key) =>
        TryGet(OutputKeys, value, out key);

    internal static bool IsAllowedOriginal(WitnessKeySpec key) =>
        OriginalKeys.Contains(key);

    internal static bool IsAllowedOutput(WitnessKeySpec key) =>
        OutputKeys.Contains(key);

    private static bool TryGet(
        IReadOnlyList<WitnessKeySpec> keys,
        string value,
        out WitnessKeySpec? key)
    {
        key = keys.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.Ordinal));
        return key is not null;
    }
}

internal sealed record OutputWitnessOptions(
    bool Armed,
    bool FocusedConsoleOnlyAcknowledged,
    bool NoDeviceAttributionAcknowledged,
    bool TappyModeSetAcknowledged,
    TimeSpan Timeout,
    OutputWitnessScenario Scenario,
    WitnessKeySpec OriginalKey,
    WitnessKeySpec OutputKey)
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    internal const int MinimumTimeoutSeconds = 10;

    internal const int MaximumTimeoutSeconds = 300;

    internal bool IsFullyAcknowledged =>
        Armed &&
        FocusedConsoleOnlyAcknowledged &&
        NoDeviceAttributionAcknowledged &&
        TappyModeSetAcknowledged;
}

internal enum OutputWitnessParseDisposition
{
    Run,
    Help,
    Refused,
    Invalid,
}

internal sealed record OutputWitnessParseResult(
    OutputWitnessParseDisposition Disposition,
    OutputWitnessOptions? Options,
    string? ErrorCode);

internal static class OutputWitnessOptionParser
{
    internal static OutputWitnessParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Any(argument => argument is "--help" or "-h" or "/?"))
        {
            return new OutputWitnessParseResult(OutputWitnessParseDisposition.Help, null, null);
        }

        var armed = false;
        var focusedConsoleAcknowledged = false;
        var noAttributionAcknowledged = false;
        var tappyModeAcknowledged = false;
        var timeout = OutputWitnessOptions.DefaultTimeout;
        var scenario = OutputWitnessScenario.Basic;
        var originalKey = WitnessKeyCatalog.DefaultOriginalKey;
        var outputKey = WitnessKeyCatalog.DefaultOutputKey;
        var timeoutSeen = false;
        var scenarioSeen = false;
        var originalSeen = false;
        var outputSeen = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--arm":
                    if (armed)
                    {
                        return Invalid("duplicate-arm");
                    }

                    armed = true;
                    break;
                case "--ack-focused-console-only":
                    if (focusedConsoleAcknowledged)
                    {
                        return Invalid("duplicate-focused-console-acknowledgment");
                    }

                    focusedConsoleAcknowledged = true;
                    break;
                case "--ack-no-device-attribution":
                    if (noAttributionAcknowledged)
                    {
                        return Invalid("duplicate-no-attribution-acknowledgment");
                    }

                    noAttributionAcknowledged = true;
                    break;
                case "--ack-tappy-mode-set":
                    if (tappyModeAcknowledged)
                    {
                        return Invalid("duplicate-tappy-mode-acknowledgment");
                    }

                    tappyModeAcknowledged = true;
                    break;
                case "--timeout-seconds":
                    if (timeoutSeen ||
                        !TryTakeValue(arguments, ref index, out var timeoutValue) ||
                        !int.TryParse(
                            timeoutValue,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var timeoutSeconds) ||
                        timeoutSeconds is < OutputWitnessOptions.MinimumTimeoutSeconds or
                            > OutputWitnessOptions.MaximumTimeoutSeconds)
                    {
                        return Invalid("invalid-timeout");
                    }

                    timeoutSeen = true;
                    timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    break;
                case "--scenario":
                    if (scenarioSeen ||
                        !TryTakeValue(arguments, ref index, out var scenarioValue) ||
                        !TryParseScenario(scenarioValue, out scenario))
                    {
                        return Invalid("invalid-scenario");
                    }

                    scenarioSeen = true;
                    break;
                case "--original-key":
                    if (originalSeen ||
                        !TryTakeValue(arguments, ref index, out var originalValue) ||
                        !WitnessKeyCatalog.TryGetOriginal(originalValue, out var parsedOriginal))
                    {
                        return Invalid("invalid-original-key");
                    }

                    originalSeen = true;
                    originalKey = parsedOriginal!;
                    break;
                case "--output-key":
                    if (outputSeen ||
                        !TryTakeValue(arguments, ref index, out var outputValue) ||
                        !WitnessKeyCatalog.TryGetOutput(outputValue, out var parsedOutput))
                    {
                        return Invalid("invalid-output-key");
                    }

                    outputSeen = true;
                    outputKey = parsedOutput!;
                    break;
                default:
                    return Invalid("unknown-option");
            }
        }

        if (!armed)
        {
            return Refused("arm-required");
        }

        if (!focusedConsoleAcknowledged)
        {
            return Refused("focused-console-acknowledgment-required");
        }

        if (!noAttributionAcknowledged)
        {
            return Refused("no-attribution-acknowledgment-required");
        }

        if (!tappyModeAcknowledged)
        {
            return Refused("tappy-mode-acknowledgment-required");
        }

        return new OutputWitnessParseResult(
            OutputWitnessParseDisposition.Run,
            new OutputWitnessOptions(
                true,
                true,
                true,
                true,
                timeout,
                scenario,
                originalKey,
                outputKey),
            null);
    }

    internal static string ScenarioName(OutputWitnessScenario scenario) =>
        scenario switch
        {
            OutputWitnessScenario.Basic => "basic",
            OutputWitnessScenario.Rehearsal => "rehearsal",
            OutputWitnessScenario.HeldUnplug => "held-unplug",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static bool TryParseScenario(string value, out OutputWitnessScenario scenario)
    {
        scenario = value switch
        {
            "basic" => OutputWitnessScenario.Basic,
            "rehearsal" => OutputWitnessScenario.Rehearsal,
            "held-unplug" => OutputWitnessScenario.HeldUnplug,
            _ => (OutputWitnessScenario)(-1),
        };
        return Enum.IsDefined(scenario);
    }

    private static bool TryTakeValue(
        IReadOnlyList<string> arguments,
        ref int index,
        out string value)
    {
        if (++index >= arguments.Count)
        {
            value = string.Empty;
            return false;
        }

        value = arguments[index];
        return !string.IsNullOrEmpty(value);
    }

    private static OutputWitnessParseResult Refused(string errorCode) =>
        new(OutputWitnessParseDisposition.Refused, null, errorCode);

    private static OutputWitnessParseResult Invalid(string errorCode) =>
        new(OutputWitnessParseDisposition.Invalid, null, errorCode);
}
