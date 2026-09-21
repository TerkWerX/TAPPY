using Tappy.App.Runtime;

namespace Tappy.App.Tests;

public sealed class ControllerChoiceLabelTests
{
    [Fact]
    public void Identical_models_have_distinct_stable_tags_without_exposing_device_paths()
    {
        var first = new ControllerChoice("session1", "private-path-one", "Targus PAUK10U number pad", "PortBound",
            VendorId: 0x05A4, ProductId: 0x9862);
        var second = first with { SessionId = "session2", PersistentId = "private-path-two" };
        Assert.NotEqual(first.DeviceTag, second.DeviceTag);
        Assert.Equal(first.DeviceTag, (first with { SessionId = "after-reconnect" }).DeviceTag);
        Assert.Contains("05A4:9862", first.Details);
        Assert.Contains("press/release", first.Details);
        Assert.DoesNotContain("private-path", first.Details);
    }

    [Fact]
    public void Receiver_infrared_and_ambiguous_sources_explain_their_limitations()
    {
        var receiver = new ControllerChoice("s", "p", "Logitech Unifying receiver", "PortBound",
            VendorId: 0x046D, ProductId: 0xC52B);
        Assert.Contains("Paired models are shown", receiver.Details);
        Assert.Contains("share typing protection", receiver.Details);
        var infrared = receiver with { VendorId = null, ProductId = null, DisplayName = "Microsoft eHome infrared remote input" };
        Assert.Contains("not another plugged-in USB keyboard", infrared.Details);
        Assert.Contains("cannot reliably distinguish", (receiver with { IdentityConfidence = "Ambiguous" }).Details);
    }
}
