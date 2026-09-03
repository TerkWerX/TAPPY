using System.Text.Json;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class LogitechG13DeviceDescriptorTests
{
    private static readonly Guid ContainerId =
        new("68B719B4-D7B6-4AB2-B371-F903AA005918");

    [Fact]
    public void AuthoritativeContainerProducesOneStableSanitizedG13Choice()
    {
        const string privatePathOne = @"\\?\HID#VID_046D&PID_C21C&COL01#PRIVATE_ONE";
        const string privatePathTwo = @"\\?\HID#VID_046D&PID_C21C&COL02#PRIVATE_TWO";
        var first = NativeLogitechG13DeviceEnumerator.CreateDescriptors(
        [
            new RawLogitechG13DeviceCandidate(new nint(41), privatePathOne, ContainerId),
            new RawLogitechG13DeviceCandidate(new nint(42), privatePathTwo, ContainerId),
        ]);
        var reconnect = NativeLogitechG13DeviceEnumerator.CreateDescriptors(
        [
            new RawLogitechG13DeviceCandidate(
                new nint(91),
                @"\\?\HID#VID_046D&PID_C21C&COL01#DIFFERENT_SESSION",
                ContainerId),
        ]);

        var descriptor = Assert.Single(first);
        var reconnected = Assert.Single(reconnect);
        Assert.Equal(PhysicalDeviceGrouping.WindowsContainerId, descriptor.Grouping);
        Assert.Equal(2, descriptor.InterfaceCount);
        Assert.Equal([new nint(41), new nint(42)], descriptor.MemberSessionHandles);
        Assert.Equal(descriptor.SessionHandle, reconnected.SessionHandle);
        Assert.Equal(descriptor.SessionId, reconnected.SessionId);
        Assert.Equal(descriptor.PersistentId, reconnected.PersistentId);
        Assert.Equal(RawInputDeviceKind.Hid, descriptor.Kind);
        Assert.Equal(LogitechG13Protocol.VendorId, descriptor.VendorId);
        Assert.Equal(LogitechG13Protocol.ProductId, descriptor.ProductId);
        Assert.Equal(LogitechG13Protocol.UsagePage, descriptor.UsagePage);
        Assert.Equal(LogitechG13Protocol.Usage, descriptor.Usage);

        var json = JsonSerializer.Serialize(descriptor);
        Assert.DoesNotContain(privatePathOne, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ContainerId.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(descriptor.SessionHandle.ToString(), json, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingContainerEvidenceNeverMergesInterfacesHeuristically()
    {
        var descriptors = NativeLogitechG13DeviceEnumerator.CreateDescriptors(
        [
            new RawLogitechG13DeviceCandidate(
                new nint(51),
                @"\\?\HID#VID_046D&PID_C21C#PORT_A",
                null),
            new RawLogitechG13DeviceCandidate(
                new nint(52),
                @"\\?\HID#VID_046D&PID_C21C#PORT_B",
                null),
        ]);

        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal(PhysicalDeviceGrouping.RawInputInterfaceFallback, descriptor.Grouping);
            Assert.Equal(1, descriptor.InterfaceCount);
        });
        Assert.NotEqual(descriptors[0].PersistentId, descriptors[1].PersistentId);
    }
}
