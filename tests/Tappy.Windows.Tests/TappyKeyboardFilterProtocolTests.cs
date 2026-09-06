using Tappy.Windows.ExclusiveInput;

namespace Tappy.Windows.Tests;

public sealed class TappyKeyboardFilterProtocolTests
{
    [Fact]
    public void Every_control_code_is_unique_buffered_and_requires_read_write_access()
    {
        uint[] codes =
        [
            TappyKeyboardFilterProtocol.QueryStatus,
            TappyKeyboardFilterProtocol.BeginAuthenticatedSession,
            TappyKeyboardFilterProtocol.SetInstancePolicy,
            TappyKeyboardFilterProtocol.Heartbeat,
            TappyKeyboardFilterProtocol.ReadEvents,
            TappyKeyboardFilterProtocol.ForceFailOpen
        ];

        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Equal(0u, code & 0x3u));
        Assert.All(codes, code => Assert.Equal(3u, (code >> 14) & 0x3u));
        Assert.All(codes, code => Assert.Equal(0x8000u, code >> 16));
        Assert.Equal(
            [0x8000E000u, 0x8000E004u, 0x8000E008u, 0x8000E00Cu, 0x8000E010u, 0x8000E014u],
            codes);
    }

    [Theory]
    [InlineData(249, false)]
    [InlineData(250, true)]
    [InlineData(1000, true)]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public void Watchdog_range_is_bounded(uint timeout, bool expected) =>
        Assert.Equal(expected, TappyKeyboardFilterProtocol.IsValidWatchdog(timeout));

    [Fact]
    public void Event_batches_require_strictly_increasing_sequences_and_real_scan_codes()
    {
        var valid = new[]
        {
            new KeyboardFilterEvent(10, 7, 0x1E, KeyboardFilterEventFlags.None, 0),
            new KeyboardFilterEvent(11, 7, 0x1E, KeyboardFilterEventFlags.Break, 0)
        };
        var duplicate = new[] { valid[0], valid[0] };
        var emptyScan = new[]
        {
            new KeyboardFilterEvent(12, 7, 0, KeyboardFilterEventFlags.None, 0)
        };

        Assert.True(TappyKeyboardFilterProtocol.IsValidEventBatch(valid));
        Assert.False(TappyKeyboardFilterProtocol.IsValidEventBatch(duplicate));
        Assert.False(TappyKeyboardFilterProtocol.IsValidEventBatch(emptyScan));
        Assert.False(TappyKeyboardFilterProtocol.IsValidEventBatch(
            [valid[0] with { Flags = (KeyboardFilterEventFlags)0x8000 }]));
    }

    [Fact]
    public void Protocol_rejects_unknown_versions()
    {
        Assert.True(TappyKeyboardFilterProtocol.IsSupportedVersion(1));
        Assert.False(TappyKeyboardFilterProtocol.IsSupportedVersion(0));
        Assert.False(TappyKeyboardFilterProtocol.IsSupportedVersion(2));
    }
}
