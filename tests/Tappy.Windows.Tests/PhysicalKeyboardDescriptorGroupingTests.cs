using System.Text.Json;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class PhysicalKeyboardDescriptorGroupingTests
{
    private static readonly Guid CompositeContainer =
        new("D511038E-2418-41EA-9A9E-8FDDC34AC62C");

    [Fact]
    public void SharedWindowsContainerProducesOneStableLogicalChoice()
    {
        var firstPass = NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors(
        [
            Candidate(401, @"\\?\HID#VID_1A2C&PID_2D43&MI_00&COL01#A#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}", CompositeContainer),
            Candidate(402, @"\\?\HID#VID_1A2C&PID_2D43&MI_00&COL02#B#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}", CompositeContainer),
            Candidate(403, @"\\?\HID#VID_1A2C&PID_2D43&MI_01&COL01#C#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}", CompositeContainer),
            Candidate(404, @"\\?\HID#VID_1A2C&PID_2D43&MI_01&COL02#D#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}", CompositeContainer),
        ]);

        var reconnect = NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors(
        [
            Candidate(901, @"\\?\HID#VID_1A2C&PID_2D43&MI_00&COL01#NEW_A#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}", CompositeContainer),
            Candidate(902, @"\\?\HID#VID_1A2C&PID_2D43&MI_00&COL02#NEW_B#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}", CompositeContainer),
        ]);

        var descriptor = Assert.Single(firstPass);
        var reconnected = Assert.Single(reconnect);
        Assert.Equal(PhysicalDeviceGrouping.WindowsContainerId, descriptor.Grouping);
        Assert.Equal(4, descriptor.InterfaceCount);
        Assert.Equal([new nint(401), new nint(402), new nint(403), new nint(404)], descriptor.MemberSessionHandles);
        Assert.Equal(descriptor.SessionHandle, reconnected.SessionHandle);
        Assert.Equal(descriptor.SessionId, reconnected.SessionId);
        Assert.Equal(descriptor.PersistentId, reconnected.PersistentId);
        Assert.Equal((ushort)0x1A2C, descriptor.VendorId);
        Assert.Equal((ushort)0x2D43, descriptor.ProductId);
    }

    [Fact]
    public void MissingContainerEvidenceNeverHeuristicallyMergesMatchingVidPid()
    {
        var descriptors = NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors(
        [
            Candidate(11, @"\\?\HID#VID_1A2C&PID_2D43#PORT_A", null),
            Candidate(12, @"\\?\HID#VID_1A2C&PID_2D43#PORT_B", null),
        ]);

        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal(PhysicalDeviceGrouping.RawInputInterfaceFallback, descriptor.Grouping);
            Assert.Equal(1, descriptor.InterfaceCount);
        });
        Assert.NotEqual(descriptors[0].PersistentId, descriptors[1].PersistentId);
    }

    [Fact]
    public void PublicDescriptorContainsOnlyHashedContainerEvidence()
    {
        const string privatePath = @"\\?\HID#VID_1A2C&PID_2D43#PRIVATE_SERIAL";
        var descriptor = Assert.Single(NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors(
        [
            Candidate(77, privatePath, CompositeContainer),
        ]));

        var json = JsonSerializer.Serialize(descriptor);

        Assert.DoesNotContain(privatePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE_SERIAL", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CompositeContainer.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9A-F]{64}$", descriptor.PathFingerprintSha256);
        Assert.DoesNotContain(descriptor.SessionHandle.ToString(), json, StringComparison.Ordinal);
    }

    [Fact]
    public void LogitechG13VirtualKeyboardIsNeverASelectableKeyboardDescriptor()
    {
        var descriptors = NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors(
        [
            new RawKeyboardDeviceCandidate(
                new nint(601),
                @"\\?\HID#VID_046D&PID_C232#SOFTWARE_EMULATED_G13_KEYBOARD",
                LogitechG13Protocol.VendorId,
                LogitechG13Protocol.VirtualKeyboardProductId,
                new Guid("12F07D48-6129-46D7-AC6B-A1D29781E4EF")),
            Candidate(602, @"\\?\HID#VID_1A2C&PID_2D43#PHYSICAL_K15", null),
        ]);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal((ushort)0x1A2C, descriptor.VendorId);
        Assert.Equal((ushort)0x2D43, descriptor.ProductId);
        Assert.DoesNotContain(descriptors, candidate =>
            candidate.VendorId == LogitechG13Protocol.VendorId &&
            candidate.ProductId == LogitechG13Protocol.VirtualKeyboardProductId);
    }

    private static RawKeyboardDeviceCandidate Candidate(
        long handle,
        string path,
        Guid? containerId) =>
        new(new nint(handle), path, 0x1A2C, 0x2D43, containerId);
}
