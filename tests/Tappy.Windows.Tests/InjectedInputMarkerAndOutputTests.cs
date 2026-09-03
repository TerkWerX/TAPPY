using Tappy.Core.Input;
using Tappy.Core.Output;
using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

public sealed class InjectedInputMarkerAndOutputTests
{
    [Fact]
    public void MarkerIsStableAndNonZeroForProcess()
    {
        var first = InjectedInputMarker.Value;
        var second = InjectedInputMarker.Value;

        Assert.NotEqual(0u, first);
        Assert.Equal(first, second);
        Assert.True(InjectedInputMarker.IsSelfInjected(first));
        Assert.False(InjectedInputMarker.IsSelfInjected(0));
    }

    [Fact]
    public void ChordDownAndCoreOrderedKeyUpAreHonoredAndTagged()
    {
        var sink = new RecordingKeyboardInputSink();
        var output = new SendInputKeyboardOutput(sink);
        var ancestry = new ExecutionAncestry("test");
        var down = new KeyboardOutputRequest(
            "owner",
            [new KeyboardOutputKey("CTRL"), new KeyboardOutputKey("A")],
            output.Marker,
            ancestry);
        // Core owns reversal and supplies A before Ctrl for release.
        var up = new KeyboardOutputRequest(
            "owner",
            [new KeyboardOutputKey("A"), new KeyboardOutputKey("CTRL")],
            output.Marker,
            ancestry);

        output.KeyDown(down);
        output.KeyUp(up);

        Assert.Equal(2, sink.Batches.Count);
        Assert.Equal(new ushort[] { 0xA2, 0x41 }, sink.Batches[0].Select(item => item.VirtualKey));
        Assert.Equal(new ushort[] { 0x41, 0xA2 }, sink.Batches[1].Select(item => item.VirtualKey));
        Assert.All(sink.Batches.SelectMany(batch => batch), item =>
            Assert.Equal(output.Marker, item.ExtraInformation));
        Assert.All(sink.Batches[0], item => Assert.False(item.Flags.HasFlag(KeyboardInjectionFlags.KeyUp)));
        Assert.All(sink.Batches[1], item => Assert.True(item.Flags.HasFlag(KeyboardInjectionFlags.KeyUp)));
    }

    [Fact]
    public void MismatchedEngineMarkerRefusesOutput()
    {
        var sink = new RecordingKeyboardInputSink();
        var output = new SendInputKeyboardOutput(sink);
        var wrongMarker = output.Marker == uint.MaxValue ? 1ul : output.Marker + 1ul;
        var request = new KeyboardOutputRequest(
            "owner",
            [new KeyboardOutputKey("F24")],
            wrongMarker,
            new ExecutionAncestry("test"));

        var exception = Assert.Throws<InvalidOperationException>(() => output.KeyDown(request));

        Assert.Contains("marker", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sink.Batches);
    }

    [Fact]
    public void ScanCodeTapIsBalancedExtendedAndTagged()
    {
        var sink = new RecordingKeyboardInputSink();
        var output = new SendInputKeyboardOutput(sink);

        output.TapScanCode(0x1C, extended: true);

        var batch = Assert.Single(sink.Batches);
        Assert.Collection(
            batch,
            down =>
            {
                Assert.Equal((ushort)0x1C, down.ScanCode);
                Assert.True(down.Flags.HasFlag(KeyboardInjectionFlags.ScanCode));
                Assert.True(down.Flags.HasFlag(KeyboardInjectionFlags.ExtendedKey));
                Assert.False(down.Flags.HasFlag(KeyboardInjectionFlags.KeyUp));
                Assert.Equal(output.Marker, down.ExtraInformation);
            },
            up =>
            {
                Assert.Equal((ushort)0x1C, up.ScanCode);
                Assert.True(up.Flags.HasFlag(KeyboardInjectionFlags.KeyUp));
                Assert.Equal(output.Marker, up.ExtraInformation);
            });
    }

    [Fact]
    public void PartialBatchAttemptsReleaseBeforeThrowing()
    {
        var sink = new RecordingKeyboardInputSink { NextResult = 1 };
        var output = new SendInputKeyboardOutput(sink);

        Assert.Throws<InvalidOperationException>(() =>
            output.Tap([new KeyboardOutputKey("CTRL"), new KeyboardOutputKey("A")]));

        Assert.Equal(2, sink.Batches.Count);
        Assert.All(sink.Batches[1], item => Assert.True(item.Flags.HasFlag(KeyboardInjectionFlags.KeyUp)));
    }

    [Fact]
    public void Windows_backend_translates_every_key_promised_by_the_portable_contract()
    {
        var sink = new RecordingKeyboardInputSink();
        var output = new SendInputKeyboardOutput(sink);
        var ancestry = new ExecutionAncestry("catalog-contract");

        foreach (var key in KeyboardOutputCapabilities.SupportedKeys)
        {
            var outputKey = new KeyboardOutputKey(key);
            output.KeyDown(new KeyboardOutputRequest(
                "catalog-contract", [outputKey], output.Marker, ancestry));
        }

        Assert.Equal(KeyboardOutputCapabilities.SupportedKeys.Count, sink.Batches.Count);
        Assert.All(sink.Batches, batch => Assert.Single(batch));
    }
}
