namespace Tappy.OutputWitness;

internal static class Program
{
    private static Task<int> Main(string[] args) =>
        OutputWitnessApplication.RunAsync(
            args,
            static (options, cancellationToken) =>
                new OutputWitnessRunner(Console.Out, Console.Error)
                    .RunAsync(options, cancellationToken),
            OperatingSystem.IsWindows,
            Console.Out,
            Console.Error,
            CancellationToken.None);
}

internal static class OutputWitnessExitCodes
{
    internal const int Passed = 0;
    internal const int InternalFailure = 1;
    internal const int ArgumentsRefused = 2;
    internal const int UnsupportedPlatform = 3;
    internal const int AssertionsFailed = 5;
    internal const int Interrupted = 6;
}

internal static class OutputWitnessApplication
{
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        Func<OutputWitnessOptions, CancellationToken, Task<int>> runArmedAsync,
        Func<bool> isWindows,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runArmedAsync);
        ArgumentNullException.ThrowIfNull(isWindows);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parsed = OutputWitnessOptionParser.Parse(args);
        switch (parsed.Disposition)
        {
            case OutputWitnessParseDisposition.Help:
                PrintHelp(output);
                return OutputWitnessExitCodes.Passed;
            case OutputWitnessParseDisposition.Refused:
                error.WriteLine(
                    $"Capture refused ({parsed.ErrorCode}): exact arming and acknowledgment flags are required.");
                PrintHelp(output);
                return OutputWitnessExitCodes.ArgumentsRefused;
            case OutputWitnessParseDisposition.Invalid:
                error.WriteLine($"Invalid arguments ({parsed.ErrorCode}).");
                PrintHelp(output);
                return OutputWitnessExitCodes.ArgumentsRefused;
            case OutputWitnessParseDisposition.Run:
                break;
            default:
                throw new InvalidOperationException("Unsupported parser result.");
        }

        if (!isWindows())
        {
            error.WriteLine("The focused-console output witness requires Windows console input.");
            return OutputWitnessExitCodes.UnsupportedPlatform;
        }

        var options = parsed.Options!;
        output.WriteLine("Tappy finite focused-console pass-through/output witness");
        output.WriteLine(
            $"ARMED for scenario '{OutputWitnessOptionParser.ScenarioName(options.Scenario)}': " +
            $"only expected {options.OriginalKey.Name} and {options.OutputKey.Name} console key records are inspected.");
        output.WriteLine(
            "Scope: ordinary input in this console while it has focus. This is not Raw Input, a global hook, or device-source attribution.");
        output.WriteLine(
            "The operator-set Tappy mode is an acknowledged prerequisite, not something this witness can inspect or prove.");
        output.WriteLine(
            "The witness sends no output and cannot release Tappy-owned output. Ctrl+C stops it and restores its console state.");
        output.WriteLine();

        try
        {
            return await runArmedAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error.WriteLine(
                "The focused-console witness stopped safely because of an internal failure. Diagnostic details were not persisted.");
            return OutputWitnessExitCodes.InternalFailure;
        }
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine(
            "Usage: dotnet run --project tools/Tappy.OutputWitness -- --arm " +
            "--ack-focused-console-only --ack-no-device-attribution --ack-tappy-mode-set " +
            "[--scenario basic|rehearsal|held-unplug] [--original-key NumPad0..NumPad9] " +
            "[--output-key F13..F24] [--timeout-seconds 10..300]");
        output.WriteLine();
        output.WriteLine(
            "Without all four exact --arm/--ack-* flags, no console handle is opened, input mode changed, buffer flushed, or input captured.");
        output.WriteLine(
            "Defaults: scenario basic, original key NumPad1, output key F24, timeout 120 seconds. Keep this console focused and Num Lock on.");
        output.WriteLine();
        output.WriteLine(
            "basic: manually set Tappy to Normal output; hold the source through OS repeat, then release. " +
            "The source make/repeat/break and exactly one output make/break are required.");
        output.WriteLine(
            "rehearsal: manually set Tappy to Rehearsal Mode; press/release the source. " +
            "Source make/break and zero selected output transitions through a fixed 2-second quiet window are required.");
        output.WriteLine(
            "held-unplug: manually set Tappy to Normal output; hold the source, then unplug the selected controller while still holding it. " +
            "Exactly one output make/break with no observed source break through a fixed 2-second post-release window is required.");
        output.WriteLine();
        output.WriteLine(
            "Only aggregate allowlisted counts and final held state are written beneath " +
            "artifacts/hil/<random-run-id>/output-witness.tappy-hil.json. No typed content, event chronology, scan codes, raw paths, or other key identities are retained.");
        output.WriteLine(
            "Exit codes: 0=scenario passed/help; 1=internal/evidence failure; 2=arguments refused; " +
            "3=unsupported platform; 5=scenario assertions failed; 6=aborted or timed out.");
    }
}
