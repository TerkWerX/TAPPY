namespace Tappy.Core.Models;

/// <summary>
/// Extracts a fixed-size, horizontally-wrapping window from a wider pixel
/// buffer — the "camera" for a scrolling marquee. Pure and stateless: the
/// caller tracks its own offset over time (see FireOledContentPlayer) and
/// passes it in each tick, the same way LedEffectMath takes elapsed time as
/// a parameter rather than owning a clock.
/// </summary>
public static class FireOledScroll
{
    /// <summary>
    /// Returns a windowWidth x windowHeight view into content, as if content
    /// repeated infinitely with a blank gap of gap pixels between copies,
    /// scrolled left by offsetX pixels (content appears to move left as
    /// offsetX increases). offsetX may be any integer, including negative.
    /// </summary>
    public static bool[,] ExtractWindow(bool[,] content, int windowWidth, int windowHeight, int offsetX, int gap)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (windowWidth < 0 || windowHeight < 0) { throw new ArgumentOutOfRangeException(nameof(windowWidth)); }
        if (gap < 0) { throw new ArgumentOutOfRangeException(nameof(gap)); }

        var window = new bool[windowWidth, windowHeight];
        var contentWidth = content.GetLength(0);
        var contentHeight = content.GetLength(1);
        var period = contentWidth + gap;
        if (period <= 0)
        {
            return window;
        }

        for (var x = 0; x < windowWidth; x++)
        {
            var sourceX = ((x + offsetX) % period + period) % period;
            if (sourceX >= contentWidth)
            {
                continue; // Currently in the blank gap between repeats.
            }

            for (var y = 0; y < windowHeight && y < contentHeight; y++)
            {
                window[x, y] = content[sourceX, y];
            }
        }

        return window;
    }
}
