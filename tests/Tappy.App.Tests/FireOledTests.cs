using Tappy.App.Runtime;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    [Fact]
    public void FireOledBitPosition_maps_every_pixel_to_a_unique_in_bounds_bit()
    {
        // The Fire's OLED bit ordering is a documented non-linear tiling, not a
        // plain framebuffer scan (see docs/AKAI_FIRE.md). This does not re-derive
        // that formula; it verifies MY implementation of it is internally
        // consistent: every one of the 8,192 real pixels must land on its own
        // distinct bit, and never spill outside the 1,171-byte packed payload
        // (7 usable bits per byte, matching MIDI's 7-bit SysEx data limit).
        var seen = new HashSet<(int Index, int Bit)>();
        for (var y = 0; y < DeviceAwareControllerRuntime.FireOledHeight; y++)
        {
            for (var x = 0; x < DeviceAwareControllerRuntime.FireOledWidth; x++)
            {
                var position = DeviceAwareControllerRuntime.FireOledBitPosition(x, y);
                Assert.InRange(position.Index, 0, 1170);
                Assert.InRange(position.Bit, 0, 6);
                Assert.True(seen.Add(position), $"Pixel ({x},{y}) collided with an earlier pixel at {position}.");
            }
        }

        Assert.Equal(128 * 64, seen.Count);
    }

    [Fact]
    public void FireOledFrameSysEx_matches_the_documented_frame_format_for_a_blank_pattern()
    {
        // F0 47 7F 43 0E <lenHi> <lenLo> [00 07 00 7F][1,171 packed bytes] F7.
        // Sub-ID 0E = write OLED; length covers everything after the length
        // bytes up to (not including) F7 = 4 header bytes + 1,171 packed bytes.
        var packet = DeviceAwareControllerRuntime.FireOledFrameSysEx(
            new bool[DeviceAwareControllerRuntime.FireOledWidth, DeviceAwareControllerRuntime.FireOledHeight]);

        Assert.Equal(0xF0, packet[0]);
        Assert.Equal(0x47, packet[1]);
        Assert.Equal(0x7F, packet[2]);
        Assert.Equal(0x43, packet[3]);
        Assert.Equal(0x0E, packet[4]);
        var length = (packet[5] << 7) | packet[6];
        Assert.Equal(4 + 1171, length);
        Assert.Equal(0x00, packet[7]);
        Assert.Equal(0x07, packet[8]);
        Assert.Equal(0x00, packet[9]);
        Assert.Equal(0x7F, packet[10]);
        Assert.Equal(0xF7, packet[^1]);
        Assert.Equal(7 + length + 1, packet.Length);
        // A blank pattern must leave the whole packed payload at zero.
        Assert.All(packet[11..^1], b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(127, 0)]
    [InlineData(0, 63)]
    [InlineData(127, 63)]
    [InlineData(64, 32)]
    public void FireOledFrameSysEx_sets_exactly_one_bit_for_a_single_lit_pixel(int x, int y)
    {
        var pixels = new bool[DeviceAwareControllerRuntime.FireOledWidth, DeviceAwareControllerRuntime.FireOledHeight];
        pixels[x, y] = true;
        var packet = DeviceAwareControllerRuntime.FireOledFrameSysEx(pixels);
        var payload = packet[11..^1];

        var expected = DeviceAwareControllerRuntime.FireOledBitPosition(x, y);
        for (var i = 0; i < payload.Length; i++)
        {
            var expectedByte = i == expected.Index ? (byte)(1 << expected.Bit) : (byte)0;
            Assert.Equal(expectedByte, payload[i]);
        }
    }

    [Fact]
    public void FireOledFrameSysEx_rejects_a_wrong_sized_pixel_buffer() =>
        Assert.Throws<ArgumentException>(() => DeviceAwareControllerRuntime.FireOledFrameSysEx(new bool[10, 10]));

    [Fact]
    public void FireOledDiagnosticPattern_draws_a_border_and_a_top_left_corner_marker()
    {
        var pixels = DeviceAwareControllerRuntime.FireOledDiagnosticPattern();

        // Border: all four edges lit.
        Assert.True(pixels[0, 0]);
        Assert.True(pixels[127, 0]);
        Assert.True(pixels[0, 63]);
        Assert.True(pixels[127, 63]);
        Assert.True(pixels[64, 0]);
        Assert.True(pixels[64, 63]);
        Assert.True(pixels[0, 32]);
        Assert.True(pixels[127, 32]);

        // Top-left 8x8 corner marker, fully filled.
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.True(pixels[x, y], $"Expected the corner marker to cover ({x},{y}).");
            }
        }

        // Nowhere else is lit: interior and the opposite corners stay dark.
        Assert.False(pixels[64, 32]);
        Assert.False(pixels[8, 8]);
        Assert.False(pixels[120, 8]);
        Assert.False(pixels[8, 56]);
    }
}
