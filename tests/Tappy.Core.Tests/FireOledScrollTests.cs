using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class FireOledScrollTests
{
    private static bool[,] StripedContent(int width, int height)
    {
        // Column x is lit only in row (x % height), a distinctive pattern
        // that makes any horizontal misalignment obvious.
        var content = new bool[width, height];
        for (var x = 0; x < width; x++)
        {
            content[x, x % height] = true;
        }

        return content;
    }

    [Fact]
    public void ExtractWindow_at_zero_offset_returns_the_content_verbatim()
    {
        var content = StripedContent(width: 10, height: 4);

        var window = FireOledScroll.ExtractWindow(content, windowWidth: 10, windowHeight: 4, offsetX: 0, gap: 0);

        for (var x = 0; x < 10; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                Assert.Equal(content[x, y], window[x, y]);
            }
        }
    }

    [Fact]
    public void ExtractWindow_shifts_content_left_as_offset_increases()
    {
        var content = StripedContent(width: 10, height: 4);

        var window = FireOledScroll.ExtractWindow(content, windowWidth: 10, windowHeight: 4, offsetX: 3, gap: 0);

        // With a zero gap the content wraps immediately, so window column x
        // shows source column (x + 3) % 10.
        for (var x = 0; x < 10; x++)
        {
            var sourceX = (x + 3) % 10;
            for (var y = 0; y < 4; y++)
            {
                Assert.Equal(content[sourceX, y], window[x, y]);
            }
        }
    }

    [Fact]
    public void ExtractWindow_handles_a_negative_offset_by_wrapping()
    {
        var content = StripedContent(width: 10, height: 4);

        var window = FireOledScroll.ExtractWindow(content, windowWidth: 10, windowHeight: 4, offsetX: -1, gap: 0);

        for (var x = 0; x < 10; x++)
        {
            var sourceX = ((x - 1) % 10 + 10) % 10;
            for (var y = 0; y < 4; y++)
            {
                Assert.Equal(content[sourceX, y], window[x, y]);
            }
        }
    }

    [Fact]
    public void ExtractWindow_leaves_the_gap_blank_between_repeats()
    {
        var content = new bool[5, 3];
        for (var x = 0; x < 5; x++) { for (var y = 0; y < 3; y++) { content[x, y] = true; } }

        // period = content width (5) + gap (2) = 7. Columns 5 and 6 of each
        // period are the blank gap.
        var window = FireOledScroll.ExtractWindow(content, windowWidth: 7, windowHeight: 3, offsetX: 0, gap: 2);

        for (var y = 0; y < 3; y++)
        {
            Assert.True(window[0, y]);
            Assert.True(window[4, y]);
            Assert.False(window[5, y]);
            Assert.False(window[6, y]);
        }
    }

    [Fact]
    public void ExtractWindow_tiles_multiple_repeats_across_a_wide_window()
    {
        var content = new bool[3, 2];
        content[0, 0] = true; // a single marker pixel at the start of each repeat

        // period = 3 (no gap); a 9px window should show the marker 3 times.
        var window = FireOledScroll.ExtractWindow(content, windowWidth: 9, windowHeight: 2, offsetX: 0, gap: 0);

        Assert.True(window[0, 0]);
        Assert.True(window[3, 0]);
        Assert.True(window[6, 0]);
        Assert.False(window[1, 0]);
        Assert.False(window[4, 0]);
        Assert.False(window[7, 0]);
    }

    [Fact]
    public void ExtractWindow_of_empty_content_with_no_gap_is_all_blank()
    {
        var content = new bool[0, 4];

        var window = FireOledScroll.ExtractWindow(content, windowWidth: 8, windowHeight: 4, offsetX: 0, gap: 0);

        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                Assert.False(window[x, y]);
            }
        }
    }
}
