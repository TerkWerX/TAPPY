namespace Tappy.G13Hil;

internal static class Program
{
    private static Task<int> Main(string[] args) =>
        G13HilApplication.RunAsync(
            args,
            static (options, cancellationToken) =>
                new G13HilRunner().RunAsync(options, cancellationToken),
            OperatingSystem.IsWindows,
            Console.Out,
            Console.Error,
            CancellationToken.None);
}

internal static class G13HilExitCodes
{
    internal const int Passed = 0;
    internal const int InternalFailure = 1;
    internal const int ArgumentsRefused = 2;
    internal const int UnsupportedPlatform = 3;
    internal const int DeviceSelectionFailed = 4;
    internal const int InputAssertionsFailed = 5;
    internal const int Interrupted = 6;
}

internal static class G13HilApplication
{
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        Func<G13HilOptions, CancellationToken, Task<int>> runArmedAsync,
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

        var parsed = G13HilOptionParser.Parse(args);
        switch (parsed.Disposition)
        {
            case G13HilParseDisposition.Help:
                PrintHelp(output);
                return G13HilExitCodes.Passed;
            case G13HilParseDisposition.Refused:
                error.WriteLine("Capture refused: this verifier runs only with the explicit --arm flag.");
                PrintHelp(output);
                return G13HilExitCodes.ArgumentsRefused;
            case G13HilParseDisposition.Invalid:
                error.WriteLine($"Invalid arguments ({parsed.ErrorCode}).");
                PrintHelp(output);
                return G13HilExitCodes.ArgumentsRefused;
            case G13HilParseDisposition.Run:
                break;
            default:
                throw new InvalidOperationException("Unsupported parser result.");
        }

        if (!isWindows())
        {
            error.WriteLine("The G13 hardware verifier requires Windows Raw Input.");
            return G13HilExitCodes.UnsupportedPlatform;
        }

        output.WriteLine("Tappy Logitech G13 finite input/control hardware verifier");
        output.WriteLine("ARMED: input capture will start only for one exact 046D:C21C, FF00:0000 controller.");
        output.WriteLine("Scope: input-functional only; this does not certify Tappy output, pass-through, performance, or a full compatibility tier.");
        output.WriteLine("This verifier reads normalized controls only. It sends no device output reports and runs no mapped actions.");
        output.WriteLine("Press Ctrl+C at any time to abort; abort, fault, timeout, or unplug disarms capture and releases held state.");
        output.WriteLine();

        try
        {
            return await runArmedAsync(parsed.Options!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error.WriteLine("The verifier stopped safely because of an internal failure. Diagnostic details were not persisted.");
            return G13HilExitCodes.InternalFailure;
        }
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("Usage: dotnet run --project tools/Tappy.G13Hil -- --arm [--timeout-minutes 5..60]");
        output.WriteLine();
        output.WriteLine("Without the exact --arm flag, the tool performs no device enumeration, input registration, or input capture.");
        output.WriteLine("An armed run is finite (30 minutes by default), requires exactly one physical Logitech G13,");
        output.WriteLine("and writes only aggregate input-functional evidence beneath artifacts/hil/<random-run-id>/g13.tappy-hil.json.");
        output.WriteLine("No raw reports, device paths, container IDs, control chronology, or typed text are retained.");
        output.WriteLine();
        output.WriteLine("Exit codes: 0=input assertions passed/help; 1=internal/evidence failure; 2=arguments refused;");
        output.WriteLine("3=unsupported platform; 4=device selection failed; 5=input assertions failed; 6=interrupted/fault/timeout/unplug.");
    }
}
