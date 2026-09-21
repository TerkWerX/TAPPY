using System.Collections.Concurrent;
using System.Threading.Channels;
using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Core.Profiles;
using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

public sealed class ControllerActionLifetimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CancellationCannotSnapshotBeforeAnInFlightKeyAcquisition()
    {
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var releaseEntered = new ManualResetEventSlim();
        var sink = new KeyboardSink
        {
            BeforeSend = batch =>
        {
            if (!batch[0].Flags.HasFlag(KeyboardInjectionFlags.KeyUp))
            {
                entered.Set();
                Assert.True(resume.Wait(Timeout));
            }
        }
        };
        using var output = Output(sink);
        Assert.True(output.Start(Request("held", ControllerActionSequenceMode.WhileHeld, Chord("Ctrl"))));
        Assert.True(entered.Wait(Timeout));
        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            releaseEntered.Set();
            try { released.SetResult(output.ReleaseOwner("held")); }
            catch (Exception error) { released.SetException(error); }
        })
        { IsBackground = true };
        try
        {
            thread.Start();
            Assert.True(releaseEntered.Wait(Timeout));
            // Both old and fixed implementations block on the dispatch lock here. The old
            // implementation had already taken its empty snapshot before entering this wait.
            Assert.True(SpinWait.SpinUntil(() => thread.ThreadState.HasFlag(ThreadState.WaitSleepJoin), Timeout));
        }
        finally
        {
            resume.Set();
        }
        Assert.True(await released.Task.WaitAsync(Timeout));
        Assert.True(output.ReleaseAll());
        Assert.Equal(["down:A2", "up:A2"], sink.Events);
    }

    [Fact]
    public async Task SharedModifiersSurviveBothMacroAndDirectOwners()
    {
        var sink = new KeyboardSink();
        var keyboard = new OwnedKeyboardOutput(new SendInputKeyboardOutput(sink));
        using var output = new WindowsControllerActionOutput(keyboard, new MidiSink());
        var first = ControlId.Create("test", "direct");
        var second = ControlId.Create("test", "sequence");
        var engine = Engine(keyboard, output,
            new ControlBindingDefinition { ControlId = first, PressAction = KeyboardActionDefinition.Hold("Ctrl", "A") },
            new ControlBindingDefinition
            {
                ControlId = second,
                PressSequence = Sequence(
                ControllerActionSequenceMode.WhileHeld, Chord("LeftCtrl", "B"), Delay(1))
            });
        Activate(engine, first);
        Press(engine, first);
        Press(engine, second);
        await sink.ReadBatchAsync();
        await sink.ReadBatchAsync();
        Release(engine, first);
        Assert.Equal(["down:A2", "down:41", "down:42", "up:41"], sink.Events);
        Release(engine, second);
        Assert.Equal(["down:A2", "down:41", "down:42", "up:41", "up:42", "up:A2"], sink.Events);
    }

    [Fact]
    public async Task ConcurrentMacrosRetainTheirSharedModifier()
    {
        var sink = new KeyboardSink();
        using var output = Output(sink);
        Assert.True(output.Start(Request("one", ControllerActionSequenceMode.WhileHeld, Chord("Ctrl", "A"), Delay(1))));
        await sink.ReadBatchAsync();
        Assert.True(output.Start(Request("two", ControllerActionSequenceMode.WhileHeld, Chord("Ctrl", "B"), Delay(1))));
        await sink.ReadBatchAsync();
        Assert.True(output.ReleaseOwner("one"));
        Assert.Equal(["down:A2", "down:41", "down:42", "up:41"], sink.Events);
        Assert.True(output.ReleaseOwner("two"));
        Assert.Equal(["down:A2", "down:41", "down:42", "up:41", "up:42", "up:A2"], sink.Events);
    }

    [Fact]
    public async Task FailedFinalCleanupRaisesFaultAndRetainsOwnershipForRetry()
    {
        var sink = new KeyboardSink { RejectUp = true };
        using var output = Output(sink);
        var fault = new TaskCompletionSource<ControllerActionOutputFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.Faulted += (_, error) => fault.TrySetResult(error);
        Assert.True(output.Start(Request("once", ControllerActionSequenceMode.RunOnce,
            new ControllerActionStepDefinition { Type = ControllerActionStepType.KeyDown, Keys = [new("Ctrl")] })));
        await fault.Task.WaitAsync(Timeout);
        Assert.False(output.ReleaseAll());
        Assert.False(output.Start(Request("once", ControllerActionSequenceMode.WhileHeld, Chord("A"))));
        sink.RejectUp = false;
        Assert.True(output.ReleaseAll());
        Assert.Equal(["down:A2", "up:A2"], sink.Events);
        Assert.True(output.Start(Request("once", ControllerActionSequenceMode.WhileHeld, Chord("A"))));
        Assert.True(output.ReleaseAll());
    }

    [Fact]
    public async Task RapidReleaseRunsAfterItsDelayedMidiPressAndBalancesNote()
    {
        var midi = new MidiSink();
        var delay = new ControlledDelay();
        var keyboard = new OwnedKeyboardOutput(new SendInputKeyboardOutput(new KeyboardSink()));
        using var output = new WindowsControllerActionOutput(keyboard, midi, delay.WaitAsync);
        var control = ControlId.Create("test", "midi");
        var engine = Engine(keyboard, output, new ControlBindingDefinition
        {
            ControlId = control,
            PressSequence = Sequence(ControllerActionSequenceMode.RunOnce, Delay(250), Midi("note:1:60:100")),
            ReleaseSequence = Sequence(ControllerActionSequenceMode.RunOnce, Midi("noteoff:1:60:0"))
        });
        Activate(engine, control);
        Press(engine, control);
        var completed = output.CurrentExecutionsCompletion;
        await delay.Entered.Task.WaitAsync(Timeout);
        Release(engine, control);
        Assert.Empty(midi.Events);
        delay.Resume.TrySetResult();
        await completed.WaitAsync(Timeout);
        Assert.Equal([MidiShortMessageKind.NoteOn, MidiShortMessageKind.NoteOff], midi.Events.Select(item => item.Kind));
    }

    [Fact]
    public async Task CompletedPressRetainsMidiUntilItsLaterFollowUp()
    {
        var midi = new MidiSink();
        using var output = Output(new KeyboardSink(), midi);
        var press = Request("press", ControllerActionSequenceMode.RunOnce, Midi("note:1:60:100"))
            with
        { RetainMidiUntilFollowUp = true };
        Assert.True(output.Start(press));
        var completed = output.CurrentExecutionsCompletion;
        await midi.ReadAsync();
        Assert.True(output.Start(Request("release", ControllerActionSequenceMode.RunOnce, Midi("noteoff:1:60:0"))
            with
        { PreviousOwnerId = "press" }));
        await completed.WaitAsync(Timeout);
        Assert.Equal([MidiShortMessageKind.NoteOn, MidiShortMessageKind.NoteOff], midi.Events.Select(item => item.Kind));
    }

    [Fact]
    public async Task CancellingBeforeDelayedMidiPressPreventsQueuedReleaseOutput()
    {
        var midi = new MidiSink();
        var delay = new ControlledDelay();
        using var output = new WindowsControllerActionOutput(new SendInputKeyboardOutput(new KeyboardSink()), midi, delay.WaitAsync);
        Assert.True(output.Start(Request("press", ControllerActionSequenceMode.RunOnce, Delay(250), Midi("note:1:60:100"))
            with
        { RetainMidiUntilFollowUp = true }));
        var completed = output.CurrentExecutionsCompletion;
        await delay.Entered.Task.WaitAsync(Timeout);
        Assert.True(output.Start(Request("release", ControllerActionSequenceMode.RunOnce, Midi("noteoff:1:60:0"))
            with
        { PreviousOwnerId = "press" }));
        Assert.True(output.ReleaseOwner("release"));
        delay.Resume.TrySetResult();
        await completed.WaitAsync(Timeout);
        Assert.Empty(midi.Events);
    }

    [Fact]
    public async Task CancelledRunOnceMidiAndConcurrentOwnersReleaseOnlyFinalNote()
    {
        var midi = new MidiSink();
        var delay = new ControlledDelay();
        using var output = new WindowsControllerActionOutput(new SendInputKeyboardOutput(new KeyboardSink()), midi, delay.WaitAsync);
        Assert.True(output.Start(Request("one", ControllerActionSequenceMode.WhileHeld, Midi("note:1:60:100"))));
        await midi.ReadAsync();
        Assert.True(output.Start(Request("two", ControllerActionSequenceMode.RunOnce, Midi("note:1:60:100"), Delay(500), Midi("noteoff:1:60:0"))));
        await delay.Entered.Task.WaitAsync(Timeout);
        Assert.True(output.ReleaseOwner("one"));
        Assert.Single(midi.Events);
        Assert.True(output.ReleaseOwner("two"));
        Assert.Equal([MidiShortMessageKind.NoteOn, MidiShortMessageKind.NoteOff], midi.Events.Select(item => item.Kind));
    }

    [Fact]
    public async Task CleanupFailureCallbackCanSynchronouslyRequestAnotherCleanup()
    {
        var sink = new KeyboardSink { RejectUp = true };
        using var output = Output(sink);
        var faultHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.Faulted += (_, _) =>
        {
            sink.RejectUp = false;
            Assert.True(Task.Run(output.ReleaseAll).Wait(Timeout));
            faultHandled.TrySetResult();
        };
        Assert.True(output.Start(Request("once", ControllerActionSequenceMode.RunOnce,
            new ControllerActionStepDefinition { Type = ControllerActionStepType.KeyDown, Keys = [new("Ctrl")] })));
        await faultHandled.Task.WaitAsync(Timeout);
        Assert.Equal(["down:A2", "up:A2"], sink.Events);
    }

    private static WindowsControllerActionOutput Output(KeyboardSink sink, MidiSink? midi = null) =>
        new(new SendInputKeyboardOutput(sink), midi ?? new MidiSink());

    private static ControllerActionStepDefinition Chord(params string[] keys) => new()
    {
        Type = ControllerActionStepType.KeyboardChord,
        Keys = keys.Select(key => new KeyboardOutputKey(key)).ToList()
    };
    private static ControllerActionStepDefinition Midi(string description) => new()
    {
        Type = ControllerActionStepType.Midi,
        Target = "fake-port",
        Value = description
    };
    private static ControllerActionStepDefinition Delay(int milliseconds) => new()
    {
        Type = ControllerActionStepType.Delay,
        DurationMs = milliseconds
    };
    private static ControllerActionSequenceDefinition Sequence(ControllerActionSequenceMode mode, params ControllerActionStepDefinition[] steps) =>
        new() { Mode = mode, Steps = [.. steps] };
    private static readonly ControllerSessionId Session = new("session");
    private static readonly ControllerIdentity Identity = new(Session, new ControllerPersistentId("controller"),
        ControllerIdentityConfidence.SerialExact, "Test");

    private static ControllerActionOutputRequest Request(string owner, ControllerActionSequenceMode mode, params ControllerActionStepDefinition[] steps)
    {
        var control = ControlId.Create("test", "one");
        var controller = ControllerProfile.Create(Identity, [control]);
        controller.Layers[0].Bindings.Add(new ControlBindingDefinition { ControlId = control, PressSequence = Sequence(mode, steps) });
        var sequence = new TappyProfile { Controllers = [controller] }.CreateSnapshot().Controllers[0].Layers[0].Bindings[0].PressSequence;
        return new(owner, "scope", sequence, InjectedInputMarker.Value, new ExecutionAncestry("test"));
    }

    private static MappingEngine Engine(OwnedKeyboardOutput keyboard, WindowsControllerActionOutput output, params ControlBindingDefinition[] bindings)
    {
        var controller = ControllerProfile.Create(Identity, bindings.Select(item => item.ControlId));
        controller.Layers[0].Bindings.AddRange(bindings);
        var engine = new MappingEngine(keyboard, new MappingEngineOptions { SelfInjectionMarker = InjectedInputMarker.Value }, actionOutput: output);
        engine.SetProfile(new TappyProfile { Controllers = [controller] }.CreateSnapshot());
        return engine;
    }
    private static void Activate(MappingEngine engine, ControlId control)
    {
        engine.Activation.SelectCandidate(Session);
        Press(engine, control);
        Release(engine, control);
        engine.Activation.Confirm();
    }
    private static void Press(MappingEngine engine, ControlId control) =>
        engine.Process(ControlSignal.Physical(Session, control, ControlSignalKind.Press, 10));
    private static void Release(MappingEngine engine, ControlId control) =>
        engine.Process(ControlSignal.Physical(Session, control, ControlSignalKind.Release, 20));

    private sealed class ControlledDelay
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task WaitAsync(int _, CancellationToken token)
        {
            Entered.TrySetResult();
            return Resume.Task.WaitAsync(token);
        }
    }

    private sealed class KeyboardSink : IKeyboardInputSink
    {
        private readonly Channel<KeyboardInjection[]> _batches = Channel.CreateUnbounded<KeyboardInjection[]>();
        internal ConcurrentQueue<string> Events { get; } = new();
        internal Action<IReadOnlyList<KeyboardInjection>>? BeforeSend { get; init; }
        internal volatile bool RejectUp;
        public int Send(IReadOnlyList<KeyboardInjection> inputs)
        {
            BeforeSend?.Invoke(inputs);
            if (RejectUp && inputs[0].Flags.HasFlag(KeyboardInjectionFlags.KeyUp))
            {
                return 0;
            }
            foreach (var input in inputs)
            {
                Events.Enqueue($"{(input.Flags.HasFlag(KeyboardInjectionFlags.KeyUp) ? "up" : "down")}:{input.VirtualKey:X2}");
            }
            _batches.Writer.TryWrite(inputs.ToArray());
            return inputs.Count;
        }
        internal Task<KeyboardInjection[]> ReadBatchAsync() => _batches.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
    }

    private sealed class MidiSink : IMidiOutput
    {
        private readonly Channel<MidiShortMessage> _messages = Channel.CreateUnbounded<MidiShortMessage>();
        internal ConcurrentQueue<MidiShortMessage> Events { get; } = new();
        public void Send(string? preferredDeviceName, MidiShortMessage message)
        {
            Events.Enqueue(message);
            _messages.Writer.TryWrite(message);
        }
        public void Reset() { }
        public void Dispose() { }
        internal Task<MidiShortMessage> ReadAsync() => _messages.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
    }
}
