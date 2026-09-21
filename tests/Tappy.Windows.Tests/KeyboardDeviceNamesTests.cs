using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class KeyboardDeviceNamesTests
{
    [Fact]
    public void Reported_product_names_improve_unknown_models_without_changing_identity_or_overriding_known_receivers()
    {
        const string path = @"\\?\HID#VID_9999&PID_0001#PRIVATE_PORT";
        var generic = DeviceDescriptorSanitizer.CreateKeyboard((nint)1, path);
        var named = DeviceDescriptorSanitizer.CreateKeyboard((nint)1, path, reportedProduct: "Wireless Office Keyboard");
        Assert.Equal("Wireless Office Keyboard — keyboard input", named.DisplayName);
        Assert.Equal(generic.PersistentId, named.PersistentId);
        Assert.Equal("Logitech Unifying receiver", KeyboardDeviceNames.Resolve(0x046D, 0xC52B, [], "USB Receiver"));
        Assert.Equal("Keyboard input (model unknown)", KeyboardDeviceNames.Resolve(0x9999, 1, [], path));
        Assert.Equal("Office keyboard — keyboard input", KeyboardDeviceNames.Resolve(0x9999, 1, [], "Office\n keyboard\u202e"));
    }

    [Fact]
    public void Grouped_interfaces_require_consistent_product_names_and_keep_stable_device_identity()
    {
        var container = Guid.NewGuid();
        var a = new RawKeyboardDeviceCandidate((nint)1, @"\\?\HID#VID_9999&PID_0001#A", 0x9999, 1, container, "Office keyboard");
        var b = a with { SessionHandle = (nint)2, RawDevicePath = @"\\?\HID#VID_9999&PID_0001#B" };
        var named = Assert.Single(NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors([a, b]));
        Assert.Equal("Office keyboard — keyboard input", named.DisplayName);
        var conflicting = Assert.Single(NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors([a, b with { ReportedProduct = "Other keyboard" }]));
        Assert.Equal("Keyboard input (model unknown)", conflicting.DisplayName);
        Assert.Equal(named.PersistentId, conflicting.PersistentId);
    }
    [Theory]
    [InlineData(0x05A4, 0x9862, "Targus PAUK10U number pad")]
    [InlineData(0x1532, 0x0201, "Razer Tartarus keypad")]
    [InlineData(0x046D, 0xC52B, "Logitech Unifying receiver")]
    [InlineData(0x1A2C, 0x2D43, "USB gaming keypad (Freewolf K15 candidate)")]
    [InlineData(0x9999, 0x0001, "Keyboard input (model unknown)")]
    public void Known_models_have_readable_names_without_guessing_unknown_models(int vendor, int product, string name)
    {
        var device = DeviceDescriptorSanitizer.CreateKeyboard((nint)1,
            $@"\\?\HID#VID_{vendor:X4}&PID_{product:X4}#PORT");
        Assert.Equal(name, device.DisplayName);
        Assert.Contains($"vid-{vendor:X4}:pid-{product:X4}", device.PersistentId);
    }

    [Fact]
    public void Infrared_name_comes_from_interfaces_not_the_shared_system_container()
    {
        var infrared = @"\\?\HID#IrDeviceV2&Col05#PORT";
        Assert.Equal("Microsoft eHome infrared remote input",
            DeviceDescriptorSanitizer.CreateKeyboard((nint)1, infrared).DisplayName);
        var container = new Guid("00000000-0000-0000-FFFF-FFFFFFFFFFFF");
        var grouped = DeviceDescriptorSanitizer.CreateKeyboardGroup(container, [
            new RawKeyboardDeviceCandidate((nint)1, infrared, null, null, container),
            new RawKeyboardDeviceCandidate((nint)2, infrared.Replace("Col05", "Col06"), null, null, container)]);
        Assert.Equal("Microsoft eHome infrared remote input", grouped.DisplayName);
        Assert.Equal("Keyboard input (model unknown)", KeyboardDeviceNames.Resolve(null, null,
            [infrared, @"\\?\HID#OTHER#PORT"]));
        Assert.Equal("Keyboard input (model unknown)", KeyboardDeviceNames.Resolve(null, null, []));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Infrared_is_filtered_before_grouping_without_hiding_other_keyboards(bool hasContainer)
    {
        Guid? container = hasContainer ? new Guid("00000000-0000-0000-FFFF-FFFFFFFFFFFF") : null;
        var infrared = @"\\?\HID#IrDeviceV2&Col05#PORT";
        var keyboard = @"\\?\HID#VID_046D&PID_C52B#KEYBOARD";
        var choices = NativeRawInputDeviceEnumerator.CreatePhysicalDescriptors([
            new RawKeyboardDeviceCandidate((nint)1, infrared, null, null, container),
            new RawKeyboardDeviceCandidate((nint)2, infrared.Replace("Col05", "Col06"), null, null, container),
            new RawKeyboardDeviceCandidate((nint)3, infrared.Replace("Col05", "Col07"), null, null, container),
            new RawKeyboardDeviceCandidate((nint)4, keyboard, 0x046D, 0xC52B, container)]);
        var choice = Assert.Single(choices);
        Assert.Equal("Logitech Unifying receiver", choice.DisplayName);
        Assert.Equal(new nint[] { (nint)4 }, choice.MemberSessionHandles);
        Assert.False(KeyboardInterfacePolicy.IsSelectable(infrared.ToUpperInvariant(), null, null));
        Assert.True(KeyboardInterfacePolicy.IsSelectable(@"\\?\HID#UNKNOWN#PORT", null, null));
        Assert.True(KeyboardInterfacePolicy.IsSelectable(@"\\?\HID#VID_05A4&PID_9862#PAD", 0x05A4, 0x9862));
        Assert.True(KeyboardInterfacePolicy.IsSelectable(@"\\?\HID#VID_1532&PID_0201#PAD", 0x1532, 0x0201));
    }
}
