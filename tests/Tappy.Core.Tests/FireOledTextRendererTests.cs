using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class FireOledTextRendererTests
{
    [Fact]
    public void Render_of_a_single_character_matches_its_glyph_exactly_at_the_origin()
    {
        var buffer = FireOledTextRenderer.Render(["I"], columns: 20, rows: 10);
        var glyph = FireOledFont.GetGlyph('I');

        for (var x = 0; x < FireOledFont.GlyphWidth; x++)
        {
            for (var y = 0; y < FireOledFont.GlyphHeight; y++)
            {
                Assert.Equal(glyph[x, y], buffer[x, y]);
            }
        }

        // Nothing lit outside the one glyph's footprint.
        for (var x = FireOledFont.GlyphWidth; x < 20; x++)
        {
            for (var y = 0; y < 10; y++)
            {
                Assert.False(buffer[x, y]);
            }
        }
    }

    [Fact]
    public void Render_stacks_multiple_lines_top_to_bottom()
    {
        var buffer = FireOledTextRenderer.Render(["I", "I"], columns: 20, rows: 20);
        var glyph = FireOledFont.GetGlyph('I');
        const int secondLineY = FireOledFont.GlyphHeight + 1; // +1 line spacing

        for (var x = 0; x < FireOledFont.GlyphWidth; x++)
        {
            for (var y = 0; y < FireOledFont.GlyphHeight; y++)
            {
                Assert.Equal(glyph[x, y], buffer[x, secondLineY + y]);
            }
        }
    }

    [Fact]
    public void Render_truncates_a_line_that_overflows_the_available_width_instead_of_wrapping()
    {
        // Each character advances 6px (5 wide + 1 spacing); a 12px-wide buffer
        // fits exactly two characters (0-4 and 6-10) with no room for a third.
        var buffer = FireOledTextRenderer.Render(["ABC"], columns: 12, rows: 10);

        var expectedA = FireOledFont.GetGlyph('A');
        var expectedB = FireOledFont.GetGlyph('B');
        for (var x = 0; x < FireOledFont.GlyphWidth; x++)
        {
            for (var y = 0; y < FireOledFont.GlyphHeight; y++)
            {
                Assert.Equal(expectedA[x, y], buffer[x, y]);
                Assert.Equal(expectedB[x, y], buffer[x + 6, y]);
            }
        }
    }

    [Fact]
    public void Render_drops_a_line_that_does_not_fit_within_the_given_height_without_throwing()
    {
        // Only one line (8px tall including spacing) fits in a 10px buffer;
        // the second line must be silently dropped, not throw.
        var buffer = FireOledTextRenderer.Render(["A", "B"], columns: 20, rows: 10);

        var expectedA = FireOledFont.GetGlyph('A');
        for (var x = 0; x < FireOledFont.GlyphWidth; x++)
        {
            for (var y = 0; y < FireOledFont.GlyphHeight; y++)
            {
                Assert.Equal(expectedA[x, y], buffer[x, y]);
            }
        }
    }

    [Fact]
    public void Render_of_no_lines_produces_an_all_blank_buffer()
    {
        var buffer = FireOledTextRenderer.Render([], columns: 128, rows: 64);

        for (var x = 0; x < 128; x++)
        {
            for (var y = 0; y < 64; y++)
            {
                Assert.False(buffer[x, y]);
            }
        }
    }

    [Fact]
    public void RenderScaledLine_at_scale_1_matches_the_plain_font_exactly()
    {
        var buffer = FireOledTextRenderer.RenderScaledLine("I", scale: 1);
        var glyph = FireOledFont.GetGlyph('I');

        Assert.Equal(FireOledFont.GlyphWidth, buffer.GetLength(0));
        Assert.Equal(FireOledFont.GlyphHeight, buffer.GetLength(1));
        for (var x = 0; x < FireOledFont.GlyphWidth; x++)
        {
            for (var y = 0; y < FireOledFont.GlyphHeight; y++)
            {
                Assert.Equal(glyph[x, y], buffer[x, y]);
            }
        }
    }

    [Fact]
    public void RenderScaledLine_scales_every_dimension_by_the_given_factor()
    {
        const int scale = 3;
        var buffer = FireOledTextRenderer.RenderScaledLine("AB", scale);

        var expectedCharAdvance = (FireOledFont.GlyphWidth + 1) * scale;
        var expectedWidth = expectedCharAdvance * 2 - scale; // no trailing spacing after the last character
        Assert.Equal(expectedWidth, buffer.GetLength(0));
        Assert.Equal(FireOledFont.GlyphHeight * scale, buffer.GetLength(1));

        // Every lit source pixel of 'A' must appear as a solid scale x scale block.
        var glyphA = FireOledFont.GetGlyph('A');
        for (var gx = 0; gx < FireOledFont.GlyphWidth; gx++)
        {
            for (var gy = 0; gy < FireOledFont.GlyphHeight; gy++)
            {
                for (var sx = 0; sx < scale; sx++)
                {
                    for (var sy = 0; sy < scale; sy++)
                    {
                        Assert.Equal(glyphA[gx, gy], buffer[gx * scale + sx, gy * scale + sy]);
                    }
                }
            }
        }
    }

    [Fact]
    public void RenderScaledLine_of_an_empty_string_is_zero_width()
    {
        var buffer = FireOledTextRenderer.RenderScaledLine(string.Empty, scale: 4);

        Assert.Equal(0, buffer.GetLength(0));
    }

    [Fact]
    public void RenderScaledLine_rejects_a_scale_below_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FireOledTextRenderer.RenderScaledLine("X", scale: 0));

    [Fact]
    public void Outline_clears_the_interior_of_a_large_solid_block_but_keeps_the_border()
    {
        // A 10x10 solid square with a stroke thickness of 2: every pixel
        // within 2 steps of an edge stays lit, the deeper interior clears.
        var solid = new bool[10, 10];
        for (var x = 0; x < 10; x++) { for (var y = 0; y < 10; y++) { solid[x, y] = true; } }

        var outline = FireOledTextRenderer.Outline(solid, thickness: 2);

        // Border pixels (within 2 of any edge) stay lit.
        Assert.True(outline[0, 0]);
        Assert.True(outline[1, 5]);
        Assert.True(outline[5, 1]);
        Assert.True(outline[9, 9]);
        Assert.True(outline[8, 5]);
        // Deep interior pixels (at least 3 away from every edge) clear.
        Assert.False(outline[5, 5]);
        Assert.False(outline[3, 3]);
        Assert.False(outline[6, 6]);
    }

    [Fact]
    public void Outline_leaves_a_shape_thinner_than_the_stroke_fully_lit()
    {
        // A shape with no dimension wider than the requested thickness has
        // no true interior — every pixel is within `thickness` of some edge.
        var thinLine = new bool[3, 20];
        for (var y = 0; y < 20; y++) { thinLine[1, y] = true; }

        var outline = FireOledTextRenderer.Outline(thinLine, thickness: 3);

        for (var y = 0; y < 20; y++)
        {
            Assert.True(outline[1, y]);
        }
    }

    [Fact]
    public void Outline_of_an_all_blank_source_stays_blank()
    {
        var blank = new bool[10, 10];

        var outline = FireOledTextRenderer.Outline(blank, thickness: 2);

        for (var x = 0; x < 10; x++)
        {
            for (var y = 0; y < 10; y++)
            {
                Assert.False(outline[x, y]);
            }
        }
    }

    [Fact]
    public void Outline_rejects_a_thickness_below_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FireOledTextRenderer.Outline(new bool[5, 5], thickness: 0));
}
