using Tappy.Core.Output;
using Tappy.Core.Safety;

namespace Tappy.Core.Tests;

public sealed class HeldOutputLedgerTests
{
    [Fact]
    public void SharedKeyStaysDownUntilItsFinalOwnerReleases()
    {
        var ledger = new HeldOutputLedger();
        var control = new KeyboardOutputKey("Ctrl");

        var first = ledger.Acquire("one", [control, new KeyboardOutputKey("A")]);
        var second = ledger.Acquire("two", [control, new KeyboardOutputKey("B")]);
        var firstRelease = ledger.ReleaseOwner("one");
        var secondRelease = ledger.ReleaseOwner("two");

        Assert.Equal(["CTRL", "A"], first.KeysDown.Select(key => key.Value));
        Assert.Equal(["B"], second.KeysDown.Select(key => key.Value));
        Assert.Equal(["A"], firstRelease.KeysUp.Select(key => key.Value));
        Assert.Equal(["CTRL", "B"], secondRelease.KeysUp.Select(key => key.Value));
    }

    [Fact]
    public void NestedOwnerAcquisitionsAndReleaseAllAreReferenceCounted()
    {
        var ledger = new HeldOutputLedger();
        var control = new KeyboardOutputKey("Control");

        ledger.Acquire("macro", [control]);
        Assert.Empty(ledger.Acquire("macro", [new KeyboardOutputKey("Ctrl")]).KeysDown);
        Assert.Empty(ledger.Release("macro", [control]).KeysUp);
        var final = ledger.ReleaseAll();

        Assert.Equal(["CTRL"], final.KeysUp.Select(key => key.Value));
        Assert.True(ledger.ReleaseAll().IsEmpty);
    }
}
