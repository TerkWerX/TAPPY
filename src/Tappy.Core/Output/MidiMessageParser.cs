namespace Tappy.Core.Output;

public enum MidiShortMessageKind
{
    NoteOn,
    NoteOff,
    ControlChange,
    ProgramChange
}

public readonly record struct MidiShortMessage(
    MidiShortMessageKind Kind,
    int Channel,
    int Data1,
    int Data2,
    uint PackedValue)
{
    public bool IsNoteOn => Kind == MidiShortMessageKind.NoteOn && Data2 > 0;

    public MidiShortMessage ToNoteOff(int releaseVelocity = 0)
    {
        if (!IsNoteOn)
        {
            throw new InvalidOperationException("Only a MIDI note-on message can create a matching note-off.");
        }

        return MidiMessageParser.Create(MidiShortMessageKind.NoteOff, Channel, Data1, releaseVelocity);
    }
}

public static class MidiMessageParser
{
    public static MidiShortMessage Parse(string? description)
    {
        var parts = (description ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw FormatError();
        }

        var kind = parts[0].ToLowerInvariant() switch
        {
            "note" or "noteon" => MidiShortMessageKind.NoteOn,
            "noteoff" or "off" => MidiShortMessageKind.NoteOff,
            "cc" => MidiShortMessageKind.ControlChange,
            "pc" => MidiShortMessageKind.ProgramChange,
            _ => throw FormatError()
        };
        var expected = kind == MidiShortMessageKind.ProgramChange ? 3 : 4;
        if (parts.Length != expected)
        {
            throw FormatError();
        }

        var channel = Range(ParseNumber(parts[1], "channel"), 1, 16, "channel");
        return kind switch
        {
            MidiShortMessageKind.ProgramChange => Create(kind, channel,
                Range(ParseNumber(parts[2], "program"), 0, 127, "program")),
            MidiShortMessageKind.ControlChange => Create(kind, channel,
                Range(ParseNumber(parts[2], "controller"), 0, 127, "controller"),
                Range(ParseNumber(parts[3], "value"), 0, 127, "value")),
            MidiShortMessageKind.NoteOn => Create(kind, channel,
                Range(ParseNumber(parts[2], "note"), 0, 127, "note"),
                Range(ParseNumber(parts[3], "velocity"), 0, 127, "velocity")),
            _ => Create(kind, channel,
                Range(ParseNumber(parts[2], "note"), 0, 127, "note"),
                Range(ParseNumber(parts[3], "release velocity"), 0, 127, "release velocity"))
        };
    }

    public static MidiShortMessage Create(MidiShortMessageKind kind, int channel, int data1, int data2 = 0)
    {
        channel = Range(channel, 1, 16, "channel");
        data1 = Range(data1, 0, 127, "data 1");
        data2 = Range(data2, 0, 127, "data 2");
        var status = kind switch
        {
            MidiShortMessageKind.NoteOff => 0x80,
            MidiShortMessageKind.NoteOn => 0x90,
            MidiShortMessageKind.ControlChange => 0xB0,
            MidiShortMessageKind.ProgramChange => 0xC0,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var packed = (uint)(status | channel - 1 | data1 << 8 |
                            (kind == MidiShortMessageKind.ProgramChange ? 0 : data2 << 16));
        return new MidiShortMessage(kind, channel, data1, data2, packed);
    }

    private static int Range(int value, int minimum, int maximum, string label) =>
        value >= minimum && value <= maximum
            ? value
            : throw new ArgumentOutOfRangeException(label, value,
                $"MIDI {label} must be between {minimum} and {maximum}.");

    private static int ParseNumber(string value, string label) =>
        int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"Invalid MIDI {label}: {value}");

    private static ArgumentException FormatError() => new(
        "MIDI format must be note:channel:note:velocity, noteoff:channel:note:releaseVelocity, " +
        "cc:channel:controller:value, or pc:channel:program.");
}
