namespace Tappy.G13Hil.Tests;

public sealed class G13HilApplicationTests
{
    [Fact]
    public async Task NoArgumentsRefuseBeforeArmedRunnerCanEnumerate()
    {
        var runnerCalls = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await G13HilApplication.RunAsync(
            [],
            (_, _) =>
            {
                runnerCalls++;
                return Task.FromResult(G13HilExitCodes.Passed);
            },
            () => true,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(G13HilExitCodes.ArgumentsRefused, exitCode);
        Assert.Equal(2, exitCode);
        Assert.Equal(0, runnerCalls);
        Assert.Contains("no device enumeration", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit --arm", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpAndUnsupportedPlatformNeverCallArmedRunner()
    {
        var runnerCalls = 0;
        Task<int> Runner(G13HilOptions _, CancellationToken __)
        {
            runnerCalls++;
            return Task.FromResult(G13HilExitCodes.Passed);
        }

        var helpExit = await G13HilApplication.RunAsync(
            ["--help", "--arm"],
            Runner,
            () => true,
            TextWriter.Null,
            TextWriter.Null,
            CancellationToken.None);
        var platformExit = await G13HilApplication.RunAsync(
            ["--arm"],
            Runner,
            () => false,
            TextWriter.Null,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(G13HilExitCodes.Passed, helpExit);
        Assert.Equal(G13HilExitCodes.UnsupportedPlatform, platformExit);
        Assert.Equal(0, runnerCalls);
    }

    [Fact]
    public async Task ArmedApplicationPassesFiniteOptionsToRunner()
    {
        G13HilOptions? observed = null;
        var exitCode = await G13HilApplication.RunAsync(
            ["--arm", "--timeout-minutes", "12"],
            (options, _) =>
            {
                observed = options;
                return Task.FromResult(G13HilExitCodes.InputAssertionsFailed);
            },
            () => true,
            TextWriter.Null,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(G13HilExitCodes.InputAssertionsFailed, exitCode);
        Assert.NotNull(observed);
        Assert.True(observed.Armed);
        Assert.Equal(TimeSpan.FromMinutes(12), observed.Timeout);
    }

    [Fact]
    public async Task ArmedRunnerFailureUsesSanitizedInternalExitCode()
    {
        using var error = new StringWriter();
        var exitCode = await G13HilApplication.RunAsync(
            ["--arm"],
            (_, _) => throw new InvalidOperationException("PRIVATE_DEVICE_PATH"),
            () => true,
            TextWriter.Null,
            error,
            CancellationToken.None);

        Assert.Equal(G13HilExitCodes.InternalFailure, exitCode);
        Assert.DoesNotContain("PRIVATE_DEVICE_PATH", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExitCodesAreStableAndDistinct()
    {
        Assert.Equal(
            [0, 1, 2, 3, 4, 5, 6],
            new[]
            {
                G13HilExitCodes.Passed,
                G13HilExitCodes.InternalFailure,
                G13HilExitCodes.ArgumentsRefused,
                G13HilExitCodes.UnsupportedPlatform,
                G13HilExitCodes.DeviceSelectionFailed,
                G13HilExitCodes.InputAssertionsFailed,
                G13HilExitCodes.Interrupted,
            });
    }
}

public sealed class G13HilOptionParserTests
{
    [Theory]
    [InlineData("--arm=true")]
    [InlineData("--watch")]
    [InlineData("--arm", "--arm")]
    [InlineData("--arm", "--timeout-minutes")]
    [InlineData("--arm", "--timeout-minutes", "4")]
    [InlineData("--arm", "--timeout-minutes", "61")]
    [InlineData("--arm", "--timeout-minutes", "not-a-number")]
    public void RejectsAmbiguousOrUnboundedArguments(params string[] arguments)
    {
        var result = G13HilOptionParser.Parse(arguments);

        Assert.Equal(G13HilParseDisposition.Invalid, result.Disposition);
        Assert.Null(result.Options);
    }

    [Fact]
    public void ExactArmUsesFiniteDefaultTimeout()
    {
        var result = G13HilOptionParser.Parse(["--arm"]);

        Assert.Equal(G13HilParseDisposition.Run, result.Disposition);
        Assert.Equal(G13HilOptions.DefaultTimeout, result.Options?.Timeout);
        Assert.True(result.Options?.Armed);
    }
}
