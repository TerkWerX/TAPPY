using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class WindowsDeviceContainerIdResolverTests
{
    [Theory]
    [InlineData(
        @"\\?\hid#vid_05a4&pid_9862#7&2f1c3b8a&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}",
        "884b96c3-56ef-11d1-bc8c-00a0c91405dd")]
    // WinMM MIDI device interface paths (via DRV_QUERYDEVICEINTERFACE) carry a
    // trailing KS reference string after the closing brace - confirmed against
    // real Akai Fire/APC MINI hardware, e.g.
    // "...#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global". Parsing must stop at
    // the closing brace, not the end of the string. See D-026 in DECISIONS.md.
    [InlineData(
        @"\\?\usb#vid_09e8&pid_0043&mi_00#9&2310eeda&0&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global",
        "6994ad04-93ef-11d0-a3cc-00a0c9223196")]
    [InlineData(
        @"\\?\usb#vid_09e8&pid_0028&mi_00#6&17b6db37&0&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global",
        "6994ad04-93ef-11d0-a3cc-00a0c9223196")]
    public void TryReadTrailingInterfaceClass_reads_the_guid_even_with_a_trailing_reference_string(
        string path, string expectedGuid)
    {
        var found = WindowsDeviceContainerIdResolver.TryReadTrailingInterfaceClass(path, out var interfaceClass);

        Assert.True(found);
        Assert.Equal(Guid.Parse(expectedGuid), interfaceClass);
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_05a4&pid_9862#7&2f1c3b8a&0&0000")] // no interface class at all
    [InlineData(@"\\?\usb#vid_09e8&pid_0043&mi_00#9&2310eeda&0&0000#{not-a-guid}\global")] // unparsable content
    [InlineData(@"\\?\usb#vid_09e8&pid_0043&mi_00#9&2310eeda&0&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196")] // missing closing brace
    [InlineData("")]
    public void TryReadTrailingInterfaceClass_rejects_malformed_paths(string path) =>
        Assert.False(WindowsDeviceContainerIdResolver.TryReadTrailingInterfaceClass(path, out _));
}
