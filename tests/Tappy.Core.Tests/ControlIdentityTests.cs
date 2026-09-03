using Tappy.Core.Input;

namespace Tappy.Core.Tests;

public sealed class ControlIdentityTests
{
    [Fact]
    public void RawInputControlIdIsStableAndIncludesUsageScanAndExtension()
    {
        var id = ControlId.FromRawInputKeyboard(0x001D, usage: 0x00E0);

        Assert.Equal("raw-input:keyboard:up0007:u00e0:sc001d:base", id.Value);
        Assert.Equal(id, ControlId.FromRawInputKeyboard(0x001D, usage: 0x00E0));
    }

    [Fact]
    public void ScanAndE0E1DistinguishLeftRightAndExtendedKeys()
    {
        var leftControl = ControlId.FromRawInputKeyboard(0x001D, usage: 0x00E0);
        var rightControl = ControlId.FromRawInputKeyboard(0x001D, isE0: true, usage: 0x00E4);
        var basePauseScan = ControlId.FromRawInputKeyboard(0x0045);
        var e1PauseScan = ControlId.FromRawInputKeyboard(0x0045, isE1: true);

        Assert.NotEqual(leftControl, rightControl);
        Assert.EndsWith(":base", leftControl.Value);
        Assert.EndsWith(":e0", rightControl.Value);
        Assert.NotEqual(basePauseScan, e1PauseScan);
    }

    [Fact]
    public void NumpadEnterAndDigitsDoNotCollapseIntoMainKeyboardKeys()
    {
        var mainEnter = ControlId.FromRawInputKeyboard(0x001C, usage: 0x0028);
        var numpadEnter = ControlId.FromRawInputKeyboard(0x001C, isE0: true, usage: 0x0058);
        var topRowOne = ControlId.FromRawInputKeyboard(0x0002, usage: 0x001E);
        var numpadOne = ControlId.FromRawInputKeyboard(0x004F, usage: 0x0059);

        Assert.NotEqual(mainEnter, numpadEnter);
        Assert.NotEqual(topRowOne, numpadOne);
    }

    [Fact]
    public void IdentityKeepsSessionPersistentAndConfidenceSeparate()
    {
        var identity = TestProfiles.Identity(confidence: ControllerIdentityConfidence.PortBound);

        Assert.Equal("session-a", identity.SessionId.Value);
        Assert.Equal("controller-a", identity.PersistentId?.Value);
        Assert.Equal(ControllerIdentityConfidence.PortBound, identity.Confidence);
    }
}
