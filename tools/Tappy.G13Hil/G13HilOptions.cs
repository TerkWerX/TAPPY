namespace Tappy.G13Hil;

internal sealed record G13HilOptions(bool Armed, TimeSpan Timeout)
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    internal const int MinimumTimeoutMinutes = 5;

    internal const int MaximumTimeoutMinutes = 60;
}

internal enum G13HilParseDisposition
{
    Run,
    Help,
    Refused,
    Invalid,
}

internal sealed record G13HilParseResult(
    G13HilParseDisposition Disposition,
    G13HilOptions? Options,
    string? ErrorCode);

internal static class G13HilOptionParser
{
    internal static G13HilParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Any(argument => argument is "--help" or "-h" or "/?"))
        {
            return new G13HilParseResult(G13HilParseDisposition.Help, null, null);
        }

        var armed = false;
        var timeout = G13HilOptions.DefaultTimeout;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.Equals("--arm", StringComparison.OrdinalIgnoreCase))
            {
                if (armed)
                {
                    return Invalid("duplicate-arm");
                }

                armed = true;
                continue;
            }

            if (argument.Equals("--timeout-minutes", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= arguments.Count ||
                    !int.TryParse(arguments[index], out var minutes) ||
                    minutes is < G13HilOptions.MinimumTimeoutMinutes or > G13HilOptions.MaximumTimeoutMinutes)
                {
                    return Invalid("invalid-timeout");
                }

                timeout = TimeSpan.FromMinutes(minutes);
                continue;
            }

            return Invalid("unknown-option");
        }

        if (!armed)
        {
            return new G13HilParseResult(G13HilParseDisposition.Refused, null, "arm-required");
        }

        return new G13HilParseResult(
            G13HilParseDisposition.Run,
            new G13HilOptions(true, timeout),
            null);
    }

    private static G13HilParseResult Invalid(string errorCode) =>
        new(G13HilParseDisposition.Invalid, null, errorCode);
}
