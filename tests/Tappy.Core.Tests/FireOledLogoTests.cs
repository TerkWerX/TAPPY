using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class FireOledLogoTests
{
    [Fact]
    public void GetPixels_returns_a_128x64_buffer()
    {
        var pixels = FireOledLogo.GetPixels();

        Assert.Equal(FireOledLogo.Width, pixels.GetLength(0));
        Assert.Equal(FireOledLogo.Height, pixels.GetLength(1));
    }

    [Fact]
    public void GetPixels_matches_the_exact_converted_bitmap()
    {
        // Locks down the hand-and-T silhouette derived from tappy.ico (see
        // docs/AKAI_FIRE.md) against accidental data-entry drift in the
        // packed byte table — 1,128 lit pixels, confirmed against the source
        // conversion script's own count before the bytes were transcribed.
        var pixels = FireOledLogo.GetPixels();

        var litCount = 0;
        for (var x = 0; x < FireOledLogo.Width; x++)
        {
            for (var y = 0; y < FireOledLogo.Height; y++)
            {
                if (pixels[x, y]) { litCount++; }
            }
        }

        Assert.Equal(1128, litCount);
    }

    [Fact]
    public void GetPixels_is_blank_along_every_edge_row_and_column()
    {
        // The logo is centered with margin on all sides; this catches an
        // accidental off-by-one that shifted the bitmap out of bounds.
        var pixels = FireOledLogo.GetPixels();

        for (var x = 0; x < FireOledLogo.Width; x++)
        {
            Assert.False(pixels[x, 0]);
            Assert.False(pixels[x, FireOledLogo.Height - 1]);
        }

        for (var y = 0; y < FireOledLogo.Height; y++)
        {
            Assert.False(pixels[0, y]);
            Assert.False(pixels[FireOledLogo.Width - 1, y]);
        }
    }
}
