namespace Tappy.OutputWitness.Tests;

public sealed class OutputWitnessApplicationTests
{
    private static readonly string[] FullyArmedArguments =
    [
        "--arm",
        "--ack-focused-console-only",
        "--ack-no-device-attribution",
        "--ack-tappy-mode-set",
    ];

    [Theory]
    [MemberData(nameof(RefusedArgumentSets))]
    public async Task MissingArmOrAnyAcknowledgmentNeverCallsRunner(string[] arguments)
    {
        var runnerCalled = false;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await OutputWitnessApplication.RunAsync(
            arguments,
            (_, _) =>
            {
                runnerCalled = true;
                return Task.FromResult(OutputWitnessExitCodes.Passed);
            },
            static () => true,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(OutputWitnessExitCodes.ArgumentsRefused, exitCode);
        Assert.False(runnerCalled);
        Assert.Contains("no console handle is opened", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Capture refused", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpAndUnsupportedPlatformNeverCallRunner()
    {
        var calls = 0;
        Task<int> Runner(OutputWitnessOptions _, CancellationToken __)
        {
            calls++;
            return Task.FromResult(OutputWitnessExitCodes.Passed);
        }

        using var output = new StringWriter();
        using var error = new StringWriter();
        var helpExit = await OutputWitnessApplication.RunAsync(
            ["--help", .. FullyArmedArguments],
            Runner,
            static () => true,
            output,
            error,
            CancellationToken.None);
        var platformExit = await OutputWitnessApplication.RunAsync(
            FullyArmedArguments,
            Runner,
            static () => false,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(OutputWitnessExitCodes.Passed, helpExit);
        Assert.Equal(OutputWitnessExitCodes.UnsupportedPlatform, platformExit);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task FullyAcknowledgedApplicationPassesCanonicalOptionsToRunner()
    {
        OutputWitnessOptions? observed = null;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await OutputWitnessApplication.RunAsync(
            [
                .. FullyArmedArguments,
                "--scenario", "held-unplug",
                "--original-key", "NumPad7",
                "--output-key", "F13",
                "--timeout-seconds", "45",
            ],
            (options, _) =>
            {
                observed = options;
                return Task.FromResult(OutputWitnessExitCodes.AssertionsFailed);
            },
            static () => true,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(OutputWitnessExitCodes.AssertionsFailed, exitCode);
        Assert.NotNull(observed);
        Assert.True(observed.IsFullyAcknowledged);
        Assert.Equal(OutputWitnessScenario.HeldUnplug, observed.Scenario);
        Assert.Equal("NumPad7", observed.OriginalKey.Name);
        Assert.Equal("F13", observed.OutputKey.Name);
        Assert.Equal(TimeSpan.FromSeconds(45), observed.Timeout);
        Assert.Contains("device-source attribution", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunnerFailureIsSanitized()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await OutputWitnessApplication.RunAsync(
            FullyArmedArguments,
            static (_, _) => throw new InvalidOperationException("PRIVATE_TYPED_CONTENT"),
            static () => true,
            output,
            error,
            CancellationToken.None);

        Assert.Equal(OutputWitnessExitCodes.InternalFailure, exitCode);
        Assert.DoesNotContain("PRIVATE_TYPED_CONTENT", error.ToString(), StringComparison.Ordinal);
    }

    public static TheoryData<string[]> RefusedArgumentSets => new()
    {
        Array.Empty<string>(),
        new[]
        {
            "--ack-focused-console-only",
            "--ack-no-device-attribution",
            "--ack-tappy-mode-set",
        },
        new[]
        {
            "--arm",
            "--ack-no-device-attribution",
            "--ack-tappy-mode-set",
        },
        new[]
        {
            "--arm",
            "--ack-focused-console-only",
            "--ack-tappy-mode-set",
        },
        new[]
        {
            "--arm",
            "--ack-focused-console-only",
            "--ack-no-device-attribution",
        },
    };
}

public sealed class OutputWitnessOptionParserTests
{
    private static readonly string[] RequiredArguments =
    [
        "--arm",
        "--ack-focused-console-only",
        "--ack-no-device-attribution",
        "--ack-tappy-mode-set",
    ];

    [Fact]
    public void ExactRequiredFlagsUseSafeFiniteDefaults()
    {
        var result = OutputWitnessOptionParser.Parse(RequiredArguments);

        Assert.Equal(OutputWitnessParseDisposition.Run, result.Disposition);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.IsFullyAcknowledged);
        Assert.Equal(OutputWitnessScenario.Basic, result.Options.Scenario);
        Assert.Equal("NumPad1", result.Options.OriginalKey.Name);
        Assert.Equal("F24", result.Options.OutputKey.Name);
        Assert.Equal(TimeSpan.FromSeconds(120), result.Options.Timeout);
    }

    [Theory]
    [InlineData("basic", 0)]
    [InlineData("rehearsal", 1)]
    [InlineData("held-unplug", 2)]
    public void ParserAcceptsEveryFiniteScenario(
        string value,
        int expected)
    {
        var result = OutputWitnessOptionParser.Parse(
            [.. RequiredArguments, "--scenario", value]);

        Assert.Equal(OutputWitnessParseDisposition.Run, result.Disposition);
        Assert.Equal((OutputWitnessScenario)expected, result.Options?.Scenario);
    }

    [Theory]
    [InlineData("--ARM")]
    [InlineData("--arm=true")]
    [InlineData("--ack-focused-console")]
    [InlineData("--ack-no-device-attribution=true")]
    [InlineData("--ack-tappy-mode-set=true")]
    public void NearMissSafetyFlagsAreInvalid(string replacement)
    {
        var arguments = RequiredArguments.ToArray();
        arguments[0] = replacement;

        var result = OutputWitnessOptionParser.Parse(arguments);

        Assert.NotEqual(OutputWitnessParseDisposition.Run, result.Disposition);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("--scenario", "other")]
    [InlineData("--original-key", "A")]
    [InlineData("--original-key", "Numpad1")]
    [InlineData("--output-key", "F12")]
    [InlineData("--output-key", "F25")]
    [InlineData("--timeout-seconds", "9")]
    [InlineData("--timeout-seconds", "301")]
    [InlineData("--timeout-seconds", "1.5")]
    public void ValuesOutsideFixedAllowlistsAreInvalid(string option, string value)
    {
        var result = OutputWitnessOptionParser.Parse(
            [.. RequiredArguments, option, value]);

        Assert.Equal(OutputWitnessParseDisposition.Invalid, result.Disposition);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("--arm")]
    [InlineData("--ack-focused-console-only")]
    [InlineData("--ack-no-device-attribution")]
    [InlineData("--ack-tappy-mode-set")]
    [InlineData("--scenario")]
    [InlineData("--original-key")]
    [InlineData("--output-key")]
    [InlineData("--timeout-seconds")]
    public void DuplicateOptionsAreInvalid(string duplicatedOption)
    {
        string[] arguments = duplicatedOption.StartsWith("--ack", StringComparison.Ordinal) ||
            duplicatedOption == "--arm"
            ? [.. RequiredArguments, duplicatedOption]
            : duplicatedOption switch
            {
                "--scenario" => [.. RequiredArguments, "--scenario", "basic", "--scenario", "basic"],
                "--original-key" => [.. RequiredArguments, "--original-key", "NumPad1", "--original-key", "NumPad1"],
                "--output-key" => [.. RequiredArguments, "--output-key", "F24", "--output-key", "F24"],
                _ => [.. RequiredArguments, "--timeout-seconds", "10", "--timeout-seconds", "10"],
            };

        var result = OutputWitnessOptionParser.Parse(arguments);

        Assert.Equal(OutputWitnessParseDisposition.Invalid, result.Disposition);
    }
}
