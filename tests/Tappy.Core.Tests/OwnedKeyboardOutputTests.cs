using Tappy.Core.Input;
using Tappy.Core.Output;

namespace Tappy.Core.Tests;

public sealed class OwnedKeyboardOutputTests
{
    [Fact]
    public void OwnersShareCanonicalModifiersAndReleaseOnlyTheirOwnKeys()
    {
        var sink = new RecordingKeyboardOutput();
        var output = new OwnedKeyboardOutput(sink);
        output.KeyDown(Request("first", "Ctrl", "A"));
        output.KeyDown(Request("second", "LeftCtrl", "B"));
        output.ReleaseOwner("first");
        Assert.Equal(["CTRL", "A", "B"], sink.DownKeys);
        Assert.Equal(["A"], sink.UpKeys);
        output.ReleaseOwner("second");
        Assert.Equal(["A", "B", "CTRL"], sink.UpKeys);
    }

    [Fact]
    public void FailedReleaseRemainsOwnedUntilRetrySucceeds()
    {
        var sink = new RejectingOutput();
        var output = new OwnedKeyboardOutput(sink);
        output.KeyDown(Request("first", "Ctrl"));
        Assert.Throws<InvalidOperationException>(() => output.ReleaseOwner("first"));
        Assert.True(output.HasOwner("first"));
        sink.RejectRelease = false;
        output.ReleaseOwner("first");
        Assert.False(output.HasOwner("first"));
        Assert.Equal(2, sink.ReleaseAttempts);
    }

    private static KeyboardOutputRequest Request(string owner, params string[] keys) =>
        new(owner, keys.Select(key => new KeyboardOutputKey(key)).ToArray(),
            TestProfiles.Marker, new ExecutionAncestry("test"));

    private sealed class RejectingOutput : IKeyboardOutput
    {
        internal bool RejectRelease { get; set; } = true;
        internal int ReleaseAttempts { get; private set; }
        public void KeyDown(KeyboardOutputRequest request) { }
        public void KeyUp(KeyboardOutputRequest request)
        {
            ReleaseAttempts++;
            if (RejectRelease)
            {
                throw new InvalidOperationException("Native release rejected");
            }
        }
    }
}
