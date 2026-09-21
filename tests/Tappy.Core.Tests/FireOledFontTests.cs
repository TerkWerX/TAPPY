using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class FireOledFontTests
{
    [Theory]
    [InlineData('A')]
    [InlineData('Z')]
    [InlineData('a')]
    [InlineData('z')]
    [InlineData('0')]
    [InlineData('9')]
    [InlineData('.')]
    [InlineData(':')]
    [InlineData('-')]
    [InlineData('/')]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData('\'')]
    public void GetGlyph_returns_a_5x7_glyph_with_at_least_one_lit_pixel(char character)
    {
        var glyph = FireOledFont.GetGlyph(character);

        Assert.Equal(FireOledFont.GlyphWidth, glyph.GetLength(0));
        Assert.Equal(FireOledFont.GlyphHeight, glyph.GetLength(1));
        var litPixels = 0;
        for (var x = 0; x < glyph.GetLength(0); x++)
        {
            for (var y = 0; y < glyph.GetLength(1); y++)
            {
                if (glyph[x, y]) { litPixels++; }
            }
        }

        Assert.True(litPixels > 0, $"Glyph '{character}' has no lit pixels.");
    }

    [Fact]
    public void GetGlyph_returns_an_all_blank_glyph_for_space()
    {
        var glyph = FireOledFont.GetGlyph(' ');

        for (var x = 0; x < glyph.GetLength(0); x++)
        {
            for (var y = 0; y < glyph.GetLength(1); y++)
            {
                Assert.False(glyph[x, y]);
            }
        }
    }

    [Theory]
    [InlineData('@')]
    [InlineData('é')] // accented character outside the font's covered set
    public void GetGlyph_falls_back_to_a_blank_glyph_for_unsupported_characters(char character)
    {
        var glyph = FireOledFont.GetGlyph(character);

        Assert.Equal(FireOledFont.GlyphWidth, glyph.GetLength(0));
        Assert.Equal(FireOledFont.GlyphHeight, glyph.GetLength(1));
        for (var x = 0; x < glyph.GetLength(0); x++)
        {
            for (var y = 0; y < glyph.GetLength(1); y++)
            {
                Assert.False(glyph[x, y]);
            }
        }
    }

    [Theory]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    [InlineData("0123456789")]
    [InlineData(".:-/()'")]
    public void GetGlyph_covers_every_character_in_the_documented_set(string characters)
    {
        foreach (var character in characters)
        {
            var glyph = FireOledFont.GetGlyph(character);
            var litPixels = 0;
            for (var x = 0; x < glyph.GetLength(0); x++)
            {
                for (var y = 0; y < glyph.GetLength(1); y++)
                {
                    if (glyph[x, y]) { litPixels++; }
                }
            }

            Assert.True(litPixels > 0, $"'{character}' unexpectedly rendered blank.");
        }
    }
}
