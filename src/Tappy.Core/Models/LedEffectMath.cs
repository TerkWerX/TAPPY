namespace Tappy.Core.Models;

/// <summary>
/// Pure, deterministic per-cell color functions for the procedural LED effects.
/// Every function depends only on its inputs (elapsed time, cell position, effect
/// parameters) — no mutable state, no randomness — so the same inputs always
/// produce the same output for the player, tests, and any future preview renderer.
/// </summary>
public static class LedEffectMath
{
    public static (byte Red, byte Green, byte Blue) Evaluate(
        LedEffectDefinition effect, long elapsedMs, int column, int row, int columns, int rows) =>
        effect.Kind switch
        {
            LedEffectKind.RainbowWave => RainbowWave(elapsedMs, column, columns, effect.SpeedHz, effect.WaveDensity),
            LedEffectKind.ColorChase => ColorChase(elapsedMs, column, row, columns, rows, effect.SpeedHz, effect.Colors, effect.TrailLength),
            LedEffectKind.Breathe => Breathe(elapsedMs, effect.SpeedHz, effect.Colors),
            LedEffectKind.Sparkle => Sparkle(elapsedMs, column, row, effect.SpeedHz, effect.Colors, effect.Density),
            LedEffectKind.ColorWipe => ColorWipe(elapsedMs, column, row, columns, rows, effect.SpeedHz, effect.Colors),
            _ => (0, 0, 0),
        };

    private static (byte, byte, byte) RainbowWave(long elapsedMs, int column, int columns, double speedHz, double waveDensity)
    {
        var spatial = (column / (double)Math.Max(1, columns)) * waveDensity * 360.0;
        var temporal = elapsedMs * speedHz / 1000.0 * 360.0;
        return HsvToRgb(spatial + temporal, 1.0, 1.0);
    }

    private static (byte, byte, byte) ColorChase(
        long elapsedMs, int column, int row, int columns, int rows, double speedHz, IReadOnlyList<string> colors, int trailLength)
    {
        var total = Math.Max(1, columns * rows);
        var index = (row * columns) + column;
        var headIndex = (long)(elapsedMs * speedHz / 1000.0 * total) % total;
        var distanceBehindHead = ((headIndex - index) % total + total) % total;
        if (distanceBehindHead >= trailLength)
        {
            return (0, 0, 0);
        }

        var colorKey = colors.Count > 0 ? colors[index % colors.Count] : "Red";
        var brightness = (trailLength - distanceBehindHead) / (double)trailLength;
        return Scale(ResolveColor(colorKey), brightness);
    }

    private static (byte, byte, byte) Breathe(long elapsedMs, double speedHz, IReadOnlyList<string> colors)
    {
        var brightness = 0.5 + (0.5 * Math.Sin(2 * Math.PI * speedHz * elapsedMs / 1000.0));
        var colorKey = colors.Count > 0 ? colors[0] : "Red";
        return Scale(ResolveColor(colorKey), brightness);
    }

    private static (byte, byte, byte) Sparkle(
        long elapsedMs, int column, int row, double speedHz, IReadOnlyList<string> colors, double density)
    {
        var frame = (long)(elapsedMs * speedHz / 125.0);
        var lit = StableUnit(column, row, frame) < density;
        if (!lit || colors.Count == 0)
        {
            return (0, 0, 0);
        }

        var colorIndex = (int)(StableUnit(row, column, frame + 1) * colors.Count);
        colorIndex = Math.Clamp(colorIndex, 0, colors.Count - 1);
        return ResolveColor(colors[colorIndex]);
    }

    private static (byte, byte, byte) ColorWipe(
        long elapsedMs, int column, int row, int columns, int rows, double speedHz, IReadOnlyList<string> colors)
    {
        var total = Math.Max(1, columns * rows);
        var index = (row * columns) + column;
        var progress = elapsedMs * speedHz / 1000.0 * total;
        if (colors.Count == 0)
        {
            return (0, 0, 0);
        }

        var pass = (long)(progress / total);
        var colorIndex = (int)(pass % colors.Count);
        var positionInPass = progress % total;
        return index <= positionInPass ? ResolveColor(colors[colorIndex]) : ((byte)0, (byte)0, (byte)0);
    }

    /// <summary>
    /// Same 12-key-or-hex vocabulary as DeviceAwareControllerRuntime.FireRgb.
    /// Duplicated here (as a values-only palette table, not device logic) since
    /// Tappy.Core cannot reference Tappy.App.
    /// </summary>
    internal static (byte Red, byte Green, byte Blue) ResolveColor(string? colorKey)
    {
        var trimmed = colorKey?.Trim();
        if (TryParseHexColor(trimmed, out var red, out var green, out var blue))
        {
            return (red, green, blue);
        }

        return trimmed?.ToLowerInvariant() switch
        {
            "red" => (255, 0, 0),
            "amber" => (255, 170, 0),
            "orange" => (255, 90, 0),
            "yellow" => (255, 255, 0),
            "green" => (0, 255, 0),
            "teal" => (0, 255, 170),
            "blue" => (0, 90, 255),
            "purple" => (150, 0, 255),
            "pink" => (255, 0, 150),
            "gray" => (120, 120, 120),
            "white" => (255, 255, 255),
            _ => (0, 0, 0),
        };
    }

    internal static bool TryParseHexColor(string? value, out byte red, out byte green, out byte blue)
    {
        red = green = blue = 0;
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }

        try
        {
            red = Convert.ToByte(value.Substring(1, 2), 16);
            green = Convert.ToByte(value.Substring(3, 2), 16);
            blue = Convert.ToByte(value.Substring(5, 2), 16);
            return true;
        }
        catch (FormatException)
        {
            red = green = blue = 0;
            return false;
        }
    }

    private static (byte, byte, byte) Scale((byte Red, byte Green, byte Blue) color, double factor)
    {
        var clamped = Math.Clamp(factor, 0.0, 1.0);
        return (
            (byte)Math.Round(color.Red * clamped),
            (byte)Math.Round(color.Green * clamped),
            (byte)Math.Round(color.Blue * clamped));
    }

    /// <summary>Deterministic pseudo-random value in [0,1) from integer coordinates — no System.Random, so results are reproducible.</summary>
    private static double StableUnit(long a, long b, long c)
    {
        unchecked
        {
            var x = (ulong)a * 0x9E3779B97F4A7C15UL;
            x ^= (ulong)b + 0x9E3779B97F4A7C15UL + (x << 6) + (x >> 2);
            x ^= (ulong)c + 0x9E3779B97F4A7C15UL + (x << 6) + (x >> 2);
            x ^= x >> 33;
            x *= 0xFF51AFD7ED558CCDUL;
            x ^= x >> 33;
            x *= 0xC4CEB9FE1A85EC53UL;
            x ^= x >> 33;
            return (x & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }

    private static (byte, byte, byte) HsvToRgb(double hue, double saturation, double value)
    {
        var h = ((hue % 360.0) + 360.0) % 360.0;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
        var m = value - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return (
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
