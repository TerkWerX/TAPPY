using System.Buffers.Binary;

namespace Tappy.Windows.Input;

public static class RawInputPacketParser
{
    public const uint KeyboardDeviceType = 1;
    public const uint HidDeviceType = 2;

    private const int RawHidHeaderSize = 8;
    private const uint LogitechG13ReportSize = 8;
    private const byte LogitechG13ReportId = 1;

    public static bool TryParseKeyboard(
        ReadOnlySpan<byte> rawInput,
        int pointerSize,
        out RawKeyboardPacket packet)
    {
        packet = default;
        if (pointerSize is not (4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerSize), "Pointer size must be 4 or 8 bytes.");
        }

        var headerSize = pointerSize == 8 ? 24 : 16;
        const int keyboardSize = 16;
        if (rawInput.Length < headerSize + keyboardSize)
        {
            return false;
        }

        var type = BinaryPrimitives.ReadUInt32LittleEndian(rawInput);
        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(rawInput[4..]);
        if (type != KeyboardDeviceType || declaredSize < headerSize + keyboardSize || declaredSize > rawInput.Length)
        {
            return false;
        }

        var deviceHandle = pointerSize == 8
            ? unchecked((nint)BinaryPrimitives.ReadInt64LittleEndian(rawInput[8..]))
            : unchecked((nint)BinaryPrimitives.ReadInt32LittleEndian(rawInput[8..]));
        var keyboard = rawInput[headerSize..];

        packet = new RawKeyboardPacket(
            deviceHandle,
            BinaryPrimitives.ReadUInt16LittleEndian(keyboard),
            (RawKeyboardFlags)BinaryPrimitives.ReadUInt16LittleEndian(keyboard[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(keyboard[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(keyboard[6..]),
            BinaryPrimitives.ReadUInt32LittleEndian(keyboard[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(keyboard[12..]));
        return true;
    }

    /// <summary>
    /// Parses only the one-report, eight-byte shape used by the physical Logitech
    /// G13. This method deliberately returns decoded scalar state instead of raw
    /// report bytes. Device VID/PID and top-level usage validation remains the
    /// responsibility of the selected device provider.
    /// </summary>
    public static bool TryParseLogitechG13Candidate(
        ReadOnlySpan<byte> rawInput,
        int pointerSize,
        out RawHidInputPacket packet)
    {
        packet = default;
        if (pointerSize is not (4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerSize), "Pointer size must be 4 or 8 bytes.");
        }

        var headerSize = pointerSize == 8 ? 24 : 16;
        var requiredSize = headerSize + RawHidHeaderSize + checked((int)LogitechG13ReportSize);
        if (rawInput.Length != requiredSize)
        {
            return false;
        }

        var type = BinaryPrimitives.ReadUInt32LittleEndian(rawInput);
        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(rawInput[4..]);
        if (type != HidDeviceType || declaredSize != requiredSize)
        {
            return false;
        }

        var hid = rawInput[headerSize..];
        var reportSize = BinaryPrimitives.ReadUInt32LittleEndian(hid);
        var reportCount = BinaryPrimitives.ReadUInt32LittleEndian(hid[4..]);
        if (reportSize != LogitechG13ReportSize || reportCount != 1)
        {
            return false;
        }

        var report = hid[RawHidHeaderSize..];
        if (report[0] != LogitechG13ReportId)
        {
            return false;
        }

        ulong buttonBits = 0;
        for (var index = 0; index < 5; index++)
        {
            buttonBits |= (ulong)report[index + 3] << (index * 8);
        }

        var deviceHandle = pointerSize == 8
            ? unchecked((nint)BinaryPrimitives.ReadInt64LittleEndian(rawInput[8..]))
            : unchecked((nint)BinaryPrimitives.ReadInt32LittleEndian(rawInput[8..]));
        packet = new RawHidInputPacket(
            deviceHandle,
            report[0],
            report[1],
            report[2],
            buttonBits);
        return true;
    }
}
