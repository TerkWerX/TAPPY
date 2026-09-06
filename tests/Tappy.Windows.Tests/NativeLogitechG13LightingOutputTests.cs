using Tappy.Windows.Input;
using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

public sealed class NativeLogitechG13LightingOutputTests
{
    [Fact]
    public void Backlight_report_has_the_verified_G13_feature_id_and_rgb_byte_order()
    {
        var report = NativeLogitechG13LightingOutput.CreateBacklightFeatureReport(12, 34, 56);

        Assert.Equal(0x07, LogitechG13Protocol.BacklightFeatureReportId);
        Assert.Equal(LogitechG13Protocol.BacklightFeatureReportSize, report.Length);
        Assert.Equal([0x07, 12, 34, 56, 0], report);
    }

    [Fact]
    public void Target_selection_returns_only_interfaces_belonging_to_the_confirmed_persistent_G13()
    {
        var firstContainer = Guid.NewGuid();
        var secondContainer = Guid.NewGuid();
        var candidates = new RawLogitechG13DeviceCandidate[]
        {
            new(new nint(11), @"\\?\HID#VID_046D&PID_C21C&COL01#FIRST", firstContainer),
            new(new nint(12), @"\\?\HID#VID_046D&PID_C21C&COL02#FIRST", firstContainer),
            new(new nint(21), @"\\?\HID#VID_046D&PID_C21C&COL01#SECOND", secondContainer),
        };
        var selected = NativeLogitechG13DeviceEnumerator.CreateDescriptors(candidates)
            .Single(device => device.MemberSessionHandles.Contains(new nint(11)));

        var paths = NativeLogitechG13LightingOutput.SelectTargetPaths(candidates, selected.PersistentId);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, path => Assert.Contains("FIRST", path, StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.Contains("SECOND", StringComparison.Ordinal));
    }
}
