using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class LedFramePlaybackTests
{
    [Fact]
    public void Empty_frame_list_has_no_active_frame()
    {
        Assert.Null(LedFramePlayback.GetActiveFrameIndex([], 0, loop: true));
        Assert.Null(LedFramePlayback.GetActiveFrameIndex([], 5000, loop: false));
    }

    [Fact]
    public void A_single_frame_stays_active_forever()
    {
        var frames = new List<LedAnimationFrame> { new() { DurationMs = 100 } };

        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 0, loop: true));
        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 50, loop: true));
        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 100_000, loop: true));
        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 100_000, loop: false));
    }

    [Fact]
    public void Multiple_frames_advance_at_their_cumulative_time_boundaries()
    {
        var frames = new List<LedAnimationFrame>
        {
            new() { DurationMs = 100 }, // [0, 100)
            new() { DurationMs = 200 }, // [100, 300)
            new() { DurationMs = 50 },  // [300, 350)
        };

        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 0, loop: true));
        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 99, loop: true));
        Assert.Equal(1, LedFramePlayback.GetActiveFrameIndex(frames, 100, loop: true));
        Assert.Equal(1, LedFramePlayback.GetActiveFrameIndex(frames, 299, loop: true));
        Assert.Equal(2, LedFramePlayback.GetActiveFrameIndex(frames, 300, loop: true));
        Assert.Equal(2, LedFramePlayback.GetActiveFrameIndex(frames, 349, loop: true));
    }

    [Fact]
    public void Looping_wraps_back_to_the_first_frame()
    {
        var frames = new List<LedAnimationFrame>
        {
            new() { DurationMs = 100 },
            new() { DurationMs = 200 },
        };

        // Total duration is 300ms; 300 + 50 = 350 should land where 50 alone would.
        Assert.Equal(LedFramePlayback.GetActiveFrameIndex(frames, 50, loop: true),
            LedFramePlayback.GetActiveFrameIndex(frames, 350, loop: true));
        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 300, loop: true));
        Assert.Equal(0, LedFramePlayback.GetActiveFrameIndex(frames, 900_000, loop: true));
    }

    [Fact]
    public void Non_looping_clamps_to_the_last_frame_once_elapsed_time_passes_the_end()
    {
        var frames = new List<LedAnimationFrame>
        {
            new() { DurationMs = 100 },
            new() { DurationMs = 200 },
        };

        Assert.Equal(1, LedFramePlayback.GetActiveFrameIndex(frames, 299, loop: false));
        Assert.Equal(1, LedFramePlayback.GetActiveFrameIndex(frames, 300, loop: false));
        Assert.Equal(1, LedFramePlayback.GetActiveFrameIndex(frames, 1_000_000, loop: false));
    }
}
