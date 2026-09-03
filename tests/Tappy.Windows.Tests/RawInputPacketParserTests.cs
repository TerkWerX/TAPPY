using System.Buffers.Binary;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class RawInputPacketParserTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(4)]
    public void ParsesKeyboardPacketWithoutLosingNativeFields(int pointerSize)
    {
        var bytes = CreatePacket(
            pointerSize,
            new nint(0x12345678),
            makeCode: 0x001D,
            flags: RawKeyboardFlags.Break | RawKeyboardFlags.E0,
            reserved: 0xBEEF,
            virtualKey: 0x0011,
            message: 0x0101,
            extraInformation: 0xA1B2C3D4);

        var parsed = RawInputPacketParser.TryParseKeyboard(bytes, pointerSize, out var packet);

        Assert.True(parsed);
        Assert.Equal(new nint(0x12345678), packet.DeviceHandle);
        Assert.Equal((ushort)0x001D, packet.MakeCode);
        Assert.Equal(RawKeyboardFlags.Break | RawKeyboardFlags.E0, packet.Flags);
        Assert.Equal((ushort)0xBEEF, packet.Reserved);
        Assert.Equal((ushort)0x0011, packet.VirtualKey);
        Assert.Equal(0x0101u, packet.Message);
        Assert.Equal(0xA1B2C3D4u, packet.ExtraInformation);
        Assert.True(packet.IsBreak);
        Assert.True(packet.IsE0);
        Assert.False(packet.IsE1);
    }

    [Fact]
    public void RejectsNonKeyboardRawInput()
    {
        var bytes = CreatePacket(8, new nint(1), 1, RawKeyboardFlags.Make, 0, 1, 0x100, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);

        Assert.False(RawInputPacketParser.TryParseKeyboard(bytes, 8, out _));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(4)]
    public void ParsesSingleEightByteG13ShapeIntoScalars(int pointerSize)
    {
        var bytes = CreateHidPacket(
            pointerSize,
            new nint(0x12345678),
            reportSize: 8,
            reportCount: 1,
            [1, 17, 239, 0x01, 0x02, 0x04, 0x08, 0x80]);

        var parsed = RawInputPacketParser.TryParseLogitechG13Candidate(
            bytes,
            pointerSize,
            out var packet);

        Assert.True(parsed);
        Assert.Equal(new nint(0x12345678), packet.DeviceHandle);
        Assert.Equal((byte)1, packet.ReportId);
        Assert.Equal((byte)17, packet.JoystickX);
        Assert.Equal((byte)239, packet.JoystickY);
        Assert.Equal(0x8008040201UL, packet.ButtonBits);
    }

    [Theory]
    [InlineData(7u, 1u)]
    [InlineData(9u, 1u)]
    [InlineData(8u, 0u)]
    [InlineData(8u, 2u)]
    public void RejectsMalformedOrMultiReportG13Payloads(uint reportSize, uint reportCount)
    {
        var reports = new byte[checked((int)(reportSize * Math.Max(reportCount, 1)))];
        if (reports.Length != 0)
        {
            reports[0] = 1;
        }

        var bytes = CreateHidPacket(8, new nint(12), reportSize, reportCount, reports);

        Assert.False(RawInputPacketParser.TryParseLogitechG13Candidate(bytes, 8, out _));
    }

    [Fact]
    public void RejectsWrongReportIdNonHidAndTrailingData()
    {
        var wrongId = CreateHidPacket(8, new nint(12), 8, 1, [2, 128, 128, 0, 0, 0, 0, 0]);
        var nonHid = (byte[])wrongId.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(nonHid, RawInputPacketParser.KeyboardDeviceType);
        var trailing = new byte[wrongId.Length + 1];
        wrongId.CopyTo(trailing, 0);

        Assert.False(RawInputPacketParser.TryParseLogitechG13Candidate(wrongId, 8, out _));
        Assert.False(RawInputPacketParser.TryParseLogitechG13Candidate(nonHid, 8, out _));
        Assert.False(RawInputPacketParser.TryParseLogitechG13Candidate(trailing, 8, out _));
    }

    [Fact]
    public void G13ParserRejectsTruncationAndInvalidPointerSize()
    {
        var bytes = CreateHidPacket(8, new nint(12), 8, 1, [1, 128, 128, 0, 0, 0, 0, 0]);

        Assert.False(RawInputPacketParser.TryParseLogitechG13Candidate(bytes[..^1], 8, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RawInputPacketParser.TryParseLogitechG13Candidate(bytes, 16, out _));
    }

    [Theory]
    [InlineData(39u, 8u, 1u)]
    [InlineData(41u, 8u, 1u)]
    [InlineData(40u, 8u, 2u)]
    [InlineData(40u, uint.MaxValue, uint.MaxValue)]
    public void G13ParserRejectsInconsistentDeclaredSizesWithoutArithmeticOverflow(
        uint declaredPacketSize,
        uint reportSize,
        uint reportCount)
    {
        var bytes = CreateHidPacket(8, new nint(12), 8, 1, [1, 128, 128, 0, 0, 0, 0, 0]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), declaredPacketSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), reportSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), reportCount);

        Assert.False(RawInputPacketParser.TryParseLogitechG13Candidate(bytes, 8, out _));
    }

    private static byte[] CreatePacket(
        int pointerSize,
        nint deviceHandle,
        ushort makeCode,
        RawKeyboardFlags flags,
        ushort reserved,
        ushort virtualKey,
        uint message,
        uint extraInformation)
    {
        var headerSize = pointerSize == 8 ? 24 : 16;
        var bytes = new byte[headerSize + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, RawInputPacketParser.KeyboardDeviceType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length));
        if (pointerSize == 8)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), deviceHandle.ToInt64());
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), deviceHandle.ToInt32());
        }

        var keyboard = bytes.AsSpan(headerSize);
        BinaryPrimitives.WriteUInt16LittleEndian(keyboard, makeCode);
        BinaryPrimitives.WriteUInt16LittleEndian(keyboard[2..], (ushort)flags);
        BinaryPrimitives.WriteUInt16LittleEndian(keyboard[4..], reserved);
        BinaryPrimitives.WriteUInt16LittleEndian(keyboard[6..], virtualKey);
        BinaryPrimitives.WriteUInt32LittleEndian(keyboard[8..], message);
        BinaryPrimitives.WriteUInt32LittleEndian(keyboard[12..], extraInformation);
        return bytes;
    }

    private static byte[] CreateHidPacket(
        int pointerSize,
        nint deviceHandle,
        uint reportSize,
        uint reportCount,
        ReadOnlySpan<byte> reports)
    {
        var headerSize = pointerSize == 8 ? 24 : 16;
        var bytes = new byte[headerSize + 8 + reports.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, RawInputPacketParser.HidDeviceType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length));
        if (pointerSize == 8)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), deviceHandle.ToInt64());
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), deviceHandle.ToInt32());
        }

        var hid = bytes.AsSpan(headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(hid, reportSize);
        BinaryPrimitives.WriteUInt32LittleEndian(hid[4..], reportCount);
        reports.CopyTo(hid[8..]);
        return bytes;
    }
}
