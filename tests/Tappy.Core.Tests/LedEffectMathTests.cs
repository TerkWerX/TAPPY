using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class LedEffectMathTests
{
    private static LedEffectDefinition Effect(LedEffectKind kind, Action<LedEffectDefinition>? configure = null)
    {
        var effect = new LedEffectDefinition { Kind = kind };
        configure?.Invoke(effect);
        effect.Normalize();
        return effect;
    }

    [Theory]
    [InlineData(LedEffectKind.RainbowWave)]
    [InlineData(LedEffectKind.ColorChase)]
    [InlineData(LedEffectKind.Breathe)]
    [InlineData(LedEffectKind.Sparkle)]
    [InlineData(LedEffectKind.ColorWipe)]
    public void EvaluateIsDeterministicForTheSameInputs(LedEffectKind kind)
    {
        var effect = Effect(kind, e => e.Colors = ["Red", "Green", "Blue"]);
        var first = LedEffectMath.Evaluate(effect, 1234, 3, 1, 16, 4);
        var second = LedEffectMath.Evaluate(effect, 1234, 3, 1, 16, 4);
        Assert.Equal(first, second);
    }

    [Fact]
    public void RainbowWaveVariesByColumnAtAFixedInstant()
    {
        var effect = Effect(LedEffectKind.RainbowWave, e => e.WaveDensity = 1.0);
        var first = LedEffectMath.Evaluate(effect, 0, column: 0, row: 0, columns: 16, rows: 4);
        var opposite = LedEffectMath.Evaluate(effect, 0, column: 8, row: 0, columns: 16, rows: 4);
        Assert.NotEqual(first, opposite);
    }

    [Fact]
    public void ColorChaseHeadAdvancesMonotonicallyAndWraps()
    {
        var effect = Effect(LedEffectKind.ColorChase, e => { e.Colors = ["Red"]; e.SpeedHz = 1; e.TrailLength = 1; });

        // total = columns*rows = 4; head index = floor(elapsedMs/1000*speedHz*total).
        var atStart = LedEffectMath.Evaluate(effect, 0, column: 0, row: 0, columns: 4, rows: 1);
        var cellOneOffAtStart = LedEffectMath.Evaluate(effect, 0, column: 1, row: 0, columns: 4, rows: 1);
        Assert.NotEqual((byte)0, atStart.Red);
        Assert.Equal((0, 0, 0), cellOneOffAtStart);

        // A quarter second later the head has moved exactly one cell (1 * 4 / 1000 * 250 = 1).
        var afterQuarterSecond = LedEffectMath.Evaluate(effect, 250, column: 1, row: 0, columns: 4, rows: 1);
        var previousCellNowOff = LedEffectMath.Evaluate(effect, 250, column: 0, row: 0, columns: 4, rows: 1);
        Assert.NotEqual((byte)0, afterQuarterSecond.Red);
        Assert.Equal((0, 0, 0), previousCellNowOff);

        // A full second wraps the head exactly back to column 0.
        var afterFullSecond = LedEffectMath.Evaluate(effect, 1000, column: 0, row: 0, columns: 4, rows: 1);
        Assert.NotEqual((byte)0, afterFullSecond.Red);
    }

    [Fact]
    public void BreatheBrightnessIsPeriodic()
    {
        var effect = Effect(LedEffectKind.Breathe, e => { e.Colors = ["Red"]; e.SpeedHz = 1; });

        var atZero = LedEffectMath.Evaluate(effect, 0, 0, 0, 16, 4);
        var atQuarterPeriod = LedEffectMath.Evaluate(effect, 250, 0, 0, 16, 4);
        var atThreeQuarterPeriod = LedEffectMath.Evaluate(effect, 750, 0, 0, 16, 4);
        var atFullPeriod = LedEffectMath.Evaluate(effect, 1000, 0, 0, 16, 4);

        Assert.True(atQuarterPeriod.Red > atZero.Red, "Brightness should peak a quarter period in.");
        Assert.True(atThreeQuarterPeriod.Red < atZero.Red, "Brightness should trough three-quarters through.");
        // sin(2*pi) is not exactly 0 in floating point, so allow the one-byte
        // rounding slack that introduces rather than asserting exact equality.
        Assert.InRange(atFullPeriod.Red, (byte)Math.Max(0, atZero.Red - 1), (byte)Math.Min(255, atZero.Red + 1));
    }

    [Fact]
    public void SparkleDensityIsApproximatelyRespectedOverManyCells()
    {
        var effect = Effect(LedEffectKind.Sparkle, e => { e.Colors = ["Red"]; e.Density = 0.5; e.SpeedHz = 1; });

        var litCount = 0;
        const int columns = 16;
        const int rows = 8;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (LedEffectMath.Evaluate(effect, 0, column, row, columns, rows) != (0, 0, 0))
                {
                    litCount++;
                }
            }
        }

        var fraction = litCount / (double)(columns * rows);
        Assert.InRange(fraction, 0.3, 0.7);
    }

    [Fact]
    public void ColorWipeFillsInRasterOrder()
    {
        var effect = Effect(LedEffectKind.ColorWipe, e => { e.Colors = ["Red"]; e.SpeedHz = 1; });
        const int columns = 4;
        const int rows = 1;

        // total = 4; progress(t) = t/1000*speedHz*total. At t=0 only column 0 is lit.
        Assert.NotEqual((byte)0, LedEffectMath.Evaluate(effect, 0, 0, 0, columns, rows).Red);
        Assert.Equal((0, 0, 0), LedEffectMath.Evaluate(effect, 0, 3, 0, columns, rows));

        // At t=750ms, progress=3.0, so columns 0-3 are all lit (index <= 3).
        Assert.NotEqual((byte)0, LedEffectMath.Evaluate(effect, 750, 3, 0, columns, rows).Red);
    }

    [Fact]
    public void EvaluateResolvesNamedColorsToTheEstablishedFirePalette()
    {
        var effect = Effect(LedEffectKind.Breathe, e => { e.Colors = ["Red"]; e.SpeedHz = 1; });
        // At elapsedMs=250 (quarter period) brightness is 1.0 — full "Red" (255,0,0).
        var color = LedEffectMath.Evaluate(effect, 250, 0, 0, 16, 4);
        Assert.Equal((255, 0, 0), color);
    }

    [Fact]
    public void EvaluateResolvesHexColors()
    {
        var effect = Effect(LedEffectKind.Breathe, e => { e.Colors = ["#00FF00"]; e.SpeedHz = 1; });
        var color = LedEffectMath.Evaluate(effect, 250, 0, 0, 16, 4);
        Assert.Equal((0, 255, 0), color);
    }
}
