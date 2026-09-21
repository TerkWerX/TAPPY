namespace Tappy.Core.Models;

/// <summary>
/// Lays out plain text onto a monochrome pixel buffer using FireOledFont.
/// Pure and device-agnostic — callers pass their own columns/rows (the Fire's
/// OLED is 128x64, but nothing here hardcodes that). A line that overflows
/// the given width is truncated, not wrapped; a line beyond the given height
/// is dropped. See docs/AKAI_FIRE.md.
/// </summary>
public static class FireOledTextRenderer
{
    private const int CharSpacing = 1;
    private const int LineSpacing = 1;

    public static bool[,] Render(IReadOnlyList<string> lines, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var buffer = new bool[columns, rows];
        var y = 0;
        foreach (var line in lines)
        {
            if (y >= rows) { break; }

            var x = 0;
            foreach (var character in line)
            {
                if (x + FireOledFont.GlyphWidth > columns) { break; }

                var glyph = FireOledFont.GetGlyph(character);
                for (var glyphY = 0; glyphY < FireOledFont.GlyphHeight && y + glyphY < rows; glyphY++)
                {
                    for (var glyphX = 0; glyphX < FireOledFont.GlyphWidth; glyphX++)
                    {
                        buffer[x + glyphX, y + glyphY] = glyph[glyphX, glyphY];
                    }
                }

                x += FireOledFont.GlyphWidth + CharSpacing;
            }

            y += FireOledFont.GlyphHeight + LineSpacing;
        }

        return buffer;
    }

    /// <summary>
    /// Renders one line at an integer pixel scale (each font pixel becomes a
    /// scale x scale block), sized exactly to fit the text — no fixed
    /// columns/rows, unlike Render. Used for "large" display content such as
    /// a scrolling wordmark, where the buffer is wider than the OLED and a
    /// window is extracted from it (see FireOledScroll).
    /// </summary>
    public static bool[,] RenderScaledLine(string text, int scale)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (scale < 1) { throw new ArgumentOutOfRangeException(nameof(scale)); }

        var charAdvance = (FireOledFont.GlyphWidth + CharSpacing) * scale;
        var width = text.Length == 0 ? 0 : charAdvance * text.Length - CharSpacing * scale;
        var height = FireOledFont.GlyphHeight * scale;
        var buffer = new bool[Math.Max(width, 0), height];
        var x = 0;
        foreach (var character in text)
        {
            var glyph = FireOledFont.GetGlyph(character);
            for (var glyphY = 0; glyphY < FireOledFont.GlyphHeight; glyphY++)
            {
                for (var glyphX = 0; glyphX < FireOledFont.GlyphWidth; glyphX++)
                {
                    if (!glyph[glyphX, glyphY]) { continue; }

                    for (var scaleY = 0; scaleY < scale; scaleY++)
                    {
                        for (var scaleX = 0; scaleX < scale; scaleX++)
                        {
                            buffer[x + glyphX * scale + scaleX, glyphY * scale + scaleY] = true;
                        }
                    }
                }
            }

            x += charAdvance;
        }

        return buffer;
    }

    /// <summary>
    /// Converts a solid bitmap into a hollow outline of the given stroke
    /// thickness: a lit pixel stays lit only if some pixel within thickness
    /// steps of it (in any of the 4 cardinal directions) is unlit or off the
    /// edge of the buffer — i.e. it is within thickness pixels of the
    /// shape's boundary. Pixels further than thickness from every edge (the
    /// solid interior) are cleared. Used for "fat hollow" display text: at a
    /// large RenderScaledLine scale there is enough resolution for the
    /// stroke itself to be several pixels thick, not just a 1px wire outline.
    /// </summary>
    public static bool[,] Outline(bool[,] source, int thickness)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (thickness < 1) { throw new ArgumentOutOfRangeException(nameof(thickness)); }

        var width = source.GetLength(0);
        var height = source.GetLength(1);
        var result = new bool[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (!source[x, y]) { continue; }

                var isInterior = true;
                for (var distance = 1; distance <= thickness && isInterior; distance++)
                {
                    if (!IsLit(source, width, height, x - distance, y) ||
                        !IsLit(source, width, height, x + distance, y) ||
                        !IsLit(source, width, height, x, y - distance) ||
                        !IsLit(source, width, height, x, y + distance))
                    {
                        isInterior = false;
                    }
                }

                result[x, y] = !isInterior;
            }
        }

        return result;
    }

    private static bool IsLit(bool[,] source, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height && source[x, y];
}
