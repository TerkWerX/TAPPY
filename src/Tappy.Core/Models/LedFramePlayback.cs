namespace Tappy.Core.Models;

/// <summary>
/// Pure, deterministic frame-sequencing for hand-painted LED animations,
/// mirroring LedEffectMath's "same inputs always produce the same output"
/// contract. Depends only on the frame list, elapsed time, and loop flag — no
/// mutable state.
/// </summary>
public static class LedFramePlayback
{
    /// <summary>
    /// Which frame is showing at <paramref name="elapsedMs"/>, found by walking
    /// cumulative DurationMs. Wraps around when <paramref name="loop"/> is true;
    /// otherwise clamps to the last frame once elapsed time passes the total
    /// duration. Returns null for an empty frame list — there is nothing to show.
    /// </summary>
    public static int? GetActiveFrameIndex(IReadOnlyList<LedAnimationFrame> frames, long elapsedMs, bool loop)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            return null;
        }

        var totalDurationMs = 0L;
        foreach (var frame in frames)
        {
            totalDurationMs += frame.DurationMs;
        }

        if (totalDurationMs <= 0)
        {
            return 0;
        }

        var cursor = loop
            ? ((elapsedMs % totalDurationMs) + totalDurationMs) % totalDurationMs
            : Math.Clamp(elapsedMs, 0, totalDurationMs - 1);

        var accumulated = 0L;
        for (var index = 0; index < frames.Count; index++)
        {
            accumulated += frames[index].DurationMs;
            if (cursor < accumulated)
            {
                return index;
            }
        }

        return frames.Count - 1;
    }
}
