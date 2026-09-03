using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Core.Profiles;
using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

public sealed class WindowsControllerActionOutputTests
{
    [Fact]
    public void PowerShellStartInfoIsHiddenNoninteractiveAndHasNoPolicyBypass()
    {
        var step = Snapshot(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.PowerShellCommand,
            Target = "PowerShell 7",
            Value = "Get-Date"
        }).Steps.Single();

        var result = WindowsControllerActionOutput.BuildPowerShellStartInfo(step);

        Assert.Equal("pwsh.exe", result.FileName);
        Assert.False(result.UseShellExecute);
        Assert.True(result.CreateNoWindow);
        Assert.Contains("-NoProfile", result.ArgumentList);
        Assert.Contains("-NonInteractive", result.ArgumentList);
        Assert.DoesNotContain(result.ArgumentList,
            argument => argument.Contains("ExecutionPolicy", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Get-Date", result.ArgumentList[^1]);
    }

    [Fact]
    public void HeldDelaySequenceCanBeCancelledWithoutProducingNativeOutput()
    {
        using var output = new WindowsControllerActionOutput();
        var request = new ControllerActionOutputRequest(
            "owner",
            "scope",
            Snapshot(new ControllerActionStepDefinition
            {
                Type = ControllerActionStepType.Delay,
                DurationMs = 10_000
            }, ControllerActionSequenceMode.WhileHeld),
            InjectedInputMarker.Value,
            new ExecutionAncestry("test"));

        Assert.True(output.Start(request));
        Assert.False(output.Start(request));
        Assert.True(output.ReleaseOwner(request.OwnerId));
        Assert.True(output.ReleaseAll());
    }

    [Fact]
    public void SchedulerRejectsAnIncorrectInjectionMarker()
    {
        using var output = new WindowsControllerActionOutput();
        var request = new ControllerActionOutputRequest(
            "owner",
            "scope",
            Snapshot(new ControllerActionStepDefinition
            {
                Type = ControllerActionStepType.Delay,
                DurationMs = 1
            }),
            InjectedInputMarker.Value + 1,
            new ExecutionAncestry("test"));

        Assert.False(output.Start(request));
    }

    private static ControllerActionSequenceSnapshot Snapshot(
        ControllerActionStepDefinition step,
        ControllerActionSequenceMode mode = ControllerActionSequenceMode.RunOnce)
    {
        var control = ControlId.Create("test", "one");
        var identity = new ControllerIdentity(
            new ControllerSessionId("session"),
            new ControllerPersistentId("controller"),
            ControllerIdentityConfidence.SerialExact,
            "Test");
        var controller = ControllerProfile.Create(identity, [control]);
        controller.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            PressSequence = new ControllerActionSequenceDefinition
            {
                Mode = mode,
                Steps = [step]
            }
        });
        return new TappyProfile { Controllers = [controller] }
            .CreateSnapshot().Controllers[0].Layers[0].Bindings[0].PressSequence;
    }
}
