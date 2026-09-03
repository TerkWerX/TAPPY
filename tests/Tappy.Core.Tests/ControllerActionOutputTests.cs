using System.Text;
using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;

namespace Tappy.Core.Tests;

public sealed class ControllerActionOutputTests
{
    [Theory]
    [InlineData("note:1:60:127", MidiShortMessageKind.NoteOn, 0x007F3C90u)]
    [InlineData("noteoff:16:64:0", MidiShortMessageKind.NoteOff, 0x0000408Fu)]
    [InlineData("cc:2:7:100", MidiShortMessageKind.ControlChange, 0x006407B1u)]
    [InlineData("pc:10:42", MidiShortMessageKind.ProgramChange, 0x00002AC9u)]
    public void MidiParserValidatesAndPacksShortMessages(
        string value,
        MidiShortMessageKind expectedKind,
        uint expectedPacked)
    {
        var message = MidiMessageParser.Parse(value);

        Assert.Equal(expectedKind, message.Kind);
        Assert.Equal(expectedPacked, message.PackedValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("note:0:60:100")]
    [InlineData("note:17:60:100")]
    [InlineData("cc:1:7:128")]
    [InlineData("pc:1:128")]
    [InlineData("unknown:1:2:3")]
    public void MidiParserRejectsMalformedMessages(string value) =>
        Assert.ThrowsAny<ArgumentException>(() => MidiMessageParser.Parse(value));

    [Fact]
    public void OscPacketIsPaddedAndUsesTypedArguments()
    {
        var packet = OscPacketBuilder.Build("/tappy/control", "12,2.5,hello");
        var text = Encoding.UTF8.GetString(packet);

        Assert.Equal(0, packet.Length % 4);
        Assert.Contains("/tappy/control", text);
        Assert.Contains(",ifs", text);
    }

    [Fact]
    public void ActionSequenceRoundTripsThroughProfileSchemaTwo()
    {
        var control = ControlId.Create("test", "macro");
        var controller = ControllerProfile.Create(TestProfiles.Identity(), [control]);
        controller.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            Name = "Text then OSC",
            PressSequence = new ControllerActionSequenceDefinition
            {
                Name = "Text then OSC",
                Mode = ControllerActionSequenceMode.RepeatWhileHeld,
                Steps =
                [
                    new ControllerActionStepDefinition
                    {
                        Type = ControllerActionStepType.Text,
                        Value = "hello"
                    },
                    new ControllerActionStepDefinition
                    {
                        Type = ControllerActionStepType.Osc,
                        Target = "localhost",
                        Amount = 9000,
                        Value = "/tappy/test",
                        Arguments = "1,ready"
                    }
                ]
            }
        });
        var serializer = new Tappy.Core.Profiles.ProfileSerializer();

        var json = serializer.Serialize(new TappyProfile { Controllers = [controller] });
        var loaded = serializer.Deserialize(json);
        var sequence = loaded.Controllers[0].Layers[0].Bindings[0].PressSequence;

        Assert.Equal(TappyProfile.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(ControllerActionSequenceMode.RepeatWhileHeld, sequence.Mode);
        Assert.Equal([ControllerActionStepType.Text, ControllerActionStepType.Osc],
            sequence.Steps.Select(step => step.Type));
        Assert.Equal("localhost", sequence.Steps[1].Target);
        Assert.Equal(9000, sequence.Steps[1].Amount);
    }

    [Fact]
    public void HeldSequenceIsFrozenStartedAndReleasedWithPhysicalControl()
    {
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var actionOutput = new RecordingControllerActionOutput();
        var engine = CreateEngine(control, actionOutput,
            press: new ControllerActionSequenceDefinition
            {
                Name = "MIDI note",
                Mode = ControllerActionSequenceMode.WhileHeld,
                Steps = [new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Midi,
                    Value = "note:1:60:100"
                }]
            });
        TestProfiles.Activate(engine, control);

        var pressed = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 10));
        var released = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Release, 11));

        Assert.Equal(MappingDisposition.Handled, pressed.Disposition);
        Assert.Equal(MappingDisposition.Handled, released.Disposition);
        var started = Assert.Single(actionOutput.Started);
        Assert.Equal(TestProfiles.Session().Value, started.ScopeId);
        Assert.Equal("note:1:60:100", Assert.Single(started.Sequence.Steps).Value);
        Assert.Equal(started.OwnerId, Assert.Single(actionOutput.ReleasedOwners));
    }

    [Fact]
    public void ReleaseSequenceRunsOnlyOnPhysicalRelease()
    {
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var actionOutput = new RecordingControllerActionOutput();
        var engine = CreateEngine(control, actionOutput,
            release: ControllerActionSequenceDefinition.Once("OSC release",
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Osc,
                    Target = "127.0.0.1",
                    Amount = 8000,
                    Value = "/tappy/release"
                }));
        TestProfiles.Activate(engine, control);

        var pressed = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 10));
        Assert.Empty(actionOutput.Started);
        var released = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Release, 11));

        Assert.Equal(MappingDisposition.Tracked, pressed.Disposition);
        Assert.Equal(MappingDisposition.Handled, released.Disposition);
        Assert.Equal("/tappy/release", Assert.Single(actionOutput.Started).Sequence.Steps[0].Value);
    }

    [Fact]
    public void RehearsalSuppressesActionSequencesAndEmergencyStopCancelsAll()
    {
        var control = ControlId.FromRawInputKeyboard(0x004F);
        var actionOutput = new RecordingControllerActionOutput();
        var engine = CreateEngine(control, actionOutput,
            press: ControllerActionSequenceDefinition.Once("Text",
                new ControllerActionStepDefinition { Type = ControllerActionStepType.Text, Value = "hello" }));
        TestProfiles.Activate(engine, control);
        engine.SetRehearsalMode(true);

        var result = engine.Process(ControlSignal.Physical(
            TestProfiles.Session(), control, ControlSignalKind.Press, 10));
        var cleanup = engine.EmergencyStop();

        Assert.Equal(MappingDisposition.Rehearsal, result.Disposition);
        Assert.Empty(actionOutput.Started);
        Assert.True(cleanup.OutputReleaseSucceeded);
        Assert.True(actionOutput.ReleaseAllCount >= 1);
    }

    private static MappingEngine CreateEngine(
        ControlId control,
        IControllerActionOutput actionOutput,
        ControllerActionSequenceDefinition? press = null,
        ControllerActionSequenceDefinition? release = null)
    {
        var controller = ControllerProfile.Create(TestProfiles.Identity(), [control]);
        controller.Layers[0].Bindings.Add(new ControlBindingDefinition
        {
            ControlId = control,
            Name = "Action",
            PressSequence = press ?? new ControllerActionSequenceDefinition(),
            ReleaseSequence = release ?? new ControllerActionSequenceDefinition()
        });
        var engine = new MappingEngine(
            new RecordingKeyboardOutput(),
            new MappingEngineOptions { SelfInjectionMarker = TestProfiles.Marker },
            new FakeClock(),
            actionOutput);
        engine.SetProfile(new TappyProfile { Controllers = [controller] }.CreateSnapshot());
        return engine;
    }
}
