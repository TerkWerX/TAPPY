using Tappy.Core.Input;

namespace Tappy.Windows.Input;

public enum MidiInputControlKind
{
    Note,
    ControlIncrease,
    ControlDecrease,
    ProgramChange,
    /// <summary>A relative/signed encoder CC decoded directly from its byte value,
    /// not compared against a previous absolute reading. See
    /// WinMmMidiInputProvider.SetRelativeControlChanges.</summary>
    RelativeControlIncrease,
    RelativeControlDecrease,
}

public sealed record MidiInputDeviceDescriptor(
    int DeviceId,
    string SessionId,
    string PersistentId,
    string DisplayName,
    ushort ManufacturerId,
    ushort ProductId,
    bool IsAmbiguous);

public sealed record MidiInput(
    int DeviceId,
    ControllerSessionId ControllerSessionId,
    string PersistentDeviceId,
    ControlId ControlId,
    MidiInputControlKind ControlKind,
    int Channel,
    int Number,
    int Value,
    string DisplayName,
    ControlSignal Signal)
{
    public long CaptureGeneration { get; init; }
}

public sealed class MidiCaptureLostEventArgs(MidiInputDeviceDescriptor descriptor, string reason) : EventArgs
{
    public MidiInputDeviceDescriptor Descriptor { get; } = descriptor;
    public string Reason { get; } = reason;
}

public sealed class MidiInputReceivedEventArgs(MidiInput input) : EventArgs
{
    public MidiInput Input { get; } = input;
}

public enum IncomingMidiMessageKind
{
    Unsupported,
    NoteOff,
    NoteOn,
    ControlChange,
    ProgramChange,
}

public readonly record struct IncomingMidiShortMessage(
    IncomingMidiMessageKind Kind,
    int Channel,
    int Data1,
    int Data2)
{
    public static IncomingMidiShortMessage Decode(uint packedMessage)
    {
        var status = (byte)(packedMessage & 0xFF);
        var data1 = (byte)((packedMessage >> 8) & 0x7F);
        var data2 = (byte)((packedMessage >> 16) & 0x7F);
        var channel = (status & 0x0F) + 1;
        var kind = status & 0xF0;
        return kind switch
        {
            0x80 => new IncomingMidiShortMessage(IncomingMidiMessageKind.NoteOff, channel, data1, data2),
            0x90 when data2 == 0 => new IncomingMidiShortMessage(IncomingMidiMessageKind.NoteOff, channel, data1, data2),
            0x90 => new IncomingMidiShortMessage(IncomingMidiMessageKind.NoteOn, channel, data1, data2),
            0xB0 => new IncomingMidiShortMessage(IncomingMidiMessageKind.ControlChange, channel, data1, data2),
            0xC0 => new IncomingMidiShortMessage(IncomingMidiMessageKind.ProgramChange, channel, data1, 0),
            _ => new IncomingMidiShortMessage(IncomingMidiMessageKind.Unsupported, channel, data1, data2),
        };
    }
}
