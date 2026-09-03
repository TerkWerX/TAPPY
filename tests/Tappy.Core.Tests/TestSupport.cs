using Tappy.Core.Abstractions;
using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;

namespace Tappy.Core.Tests;

internal sealed class FakeClock(long timestamp = 0, long frequency = 1_000) : IMonotonicClock
{
    public long Timestamp { get; set; } = timestamp;
    public long GetTimestamp() => Timestamp;
    public long TimestampFrequency { get; } = frequency;
}

internal sealed record RecordedKeyboardEvent(bool IsDown, KeyboardOutputRequest Request);

internal sealed class RecordingKeyboardOutput : IKeyboardOutput
{
    public List<RecordedKeyboardEvent> Events { get; } = [];

    public void KeyDown(KeyboardOutputRequest request) => Events.Add(new RecordedKeyboardEvent(true, Copy(request)));

    public void KeyUp(KeyboardOutputRequest request) => Events.Add(new RecordedKeyboardEvent(false, Copy(request)));

    public IReadOnlyList<string> DownKeys => Events.Where(item => item.IsDown)
        .SelectMany(item => item.Request.Keys).Select(key => key.Value).ToArray();

    public IReadOnlyList<string> UpKeys => Events.Where(item => !item.IsDown)
        .SelectMany(item => item.Request.Keys).Select(key => key.Value).ToArray();

    private static KeyboardOutputRequest Copy(KeyboardOutputRequest request) => new(
        request.OwnerId,
        request.Keys.ToArray(),
        request.InjectionMarker,
        new ExecutionAncestry(request.Ancestry.RootId, request.Ancestry.Nodes));
}

internal static class TestProfiles
{
    public const ulong Marker = 0x54505059;

    public static ControllerSessionId Session(string value = "session-a") => new(value);

    public static ControllerIdentity Identity(
        string session = "session-a",
        string persistent = "controller-a",
        ControllerIdentityConfidence confidence = ControllerIdentityConfidence.SerialExact) =>
        new(new ControllerSessionId(session), new ControllerPersistentId(persistent), confidence,
            "Test controller", vendorId: 0x1234, productId: 0x5678);

    public static MappingEngine CreateEngine(
        RecordingKeyboardOutput output,
        IEnumerable<(ControlId Control, KeyboardActionDefinition Press, KeyboardActionDefinition? Release)> bindings,
        FakeClock? clock = null,
        int maximumDepth = 8,
        int maximumTransitions = 200,
        EffectiveSourceMode effectiveSourceMode = EffectiveSourceMode.PassThrough,
        RequestedSourceMode requestedSourceMode = RequestedSourceMode.PassThrough,
        int layerCount = 3)
    {
        var identity = Identity();
        var bindingArray = bindings.ToArray();
        var controller = ControllerProfile.Create(identity, bindingArray.Select(binding => binding.Control), layerCount);
        controller.SourceMode.Requested = requestedSourceMode;
        controller.SourceMode.Effective = effectiveSourceMode;
        foreach (var binding in bindingArray)
        {
            controller.Layers[0].Bindings.Add(new ControlBindingDefinition
            {
                ControlId = binding.Control,
                Name = binding.Control.Value,
                PressAction = binding.Press,
                ReleaseAction = binding.Release ?? new KeyboardActionDefinition()
            });
        }

        var engine = new MappingEngine(output, new MappingEngineOptions
        {
            SelfInjectionMarker = Marker,
            MaximumAncestryDepth = maximumDepth,
            MaximumOutputTransitionsPerWindow = maximumTransitions,
            OutputRateWindow = TimeSpan.FromSeconds(1)
        }, clock ?? new FakeClock());
        engine.SetProfile(new TappyProfile { Controllers = [controller] }.CreateSnapshot());
        return engine;
    }

    public static void Activate(MappingEngine engine, ControlId identificationControl, long timestamp = 1)
    {
        var session = Session();
        Assert.Equal(ControllerActivationState.AwaitingIdentificationPress,
            engine.Activation.SelectCandidate(session));
        Assert.Equal(MappingDisposition.ActivationPending,
            engine.Process(ControlSignal.Physical(session, identificationControl, ControlSignalKind.Press, timestamp)).Disposition);
        Assert.Equal(MappingDisposition.ActivationPending,
            engine.Process(ControlSignal.Physical(session, identificationControl, ControlSignalKind.Release, timestamp + 1)).Disposition);
        Assert.Equal(ControllerActivationState.AwaitingConfirmation, engine.Activation.State);
        engine.Activation.Confirm();
        Assert.True(engine.Activation.IsActive(session));
    }
}
