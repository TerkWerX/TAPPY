using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class KeyboardPacketNormalizerTests
{
    private static readonly SanitizedDeviceDescriptor Descriptor =
        DeviceDescriptorSanitizer.CreateKeyboard(new nint(42), @"\\?\HID#VID_046D&PID_C31C#PORT_A");

    [Theory]
    [InlineData(0x10, 0x2A, RawKeyboardFlags.Make, "LeftShift", "Left Shift")]
    [InlineData(0x10, 0x36, RawKeyboardFlags.Make, "RightShift", "Right Shift")]
    [InlineData(0x11, 0x1D, RawKeyboardFlags.Make, "LeftControl", "Left Ctrl")]
    [InlineData(0x11, 0x1D, RawKeyboardFlags.E0, "RightControl", "Right Ctrl")]
    [InlineData(0x12, 0x38, RawKeyboardFlags.Make, "LeftAlt", "Left Alt")]
    [InlineData(0x12, 0x38, RawKeyboardFlags.E0, "RightAlt", "Right Alt")]
    [InlineData(0x0D, 0x1C, RawKeyboardFlags.E0, "NumpadEnter", "Numpad Enter")]
    public void PreservesLeftRightAndExtendedIdentity(
        ushort virtualKey,
        ushort makeCode,
        RawKeyboardFlags flags,
        string expectedName,
        string expectedDisplayName)
    {
        var packet = new RawKeyboardPacket(
            Descriptor.SessionHandle,
            makeCode,
            flags,
            0,
            virtualKey,
            0x0100,
            99);

        var normalized = KeyboardPacketNormalizer.Normalize(packet, Descriptor, isRepeat: false, timestamp: 123);

        Assert.Equal(expectedName, normalized.Key.Name);
        Assert.Equal(expectedDisplayName, normalized.Key.DisplayName);
        Assert.Equal(makeCode, normalized.MakeCode);
        Assert.Equal(virtualKey, normalized.RawVirtualKey);
        Assert.Equal(99u, normalized.ExtraInformation);
        Assert.Equal(123, normalized.Signal.Timestamp);
        Assert.Equal(
            ControlId.FromRawInputKeyboard(makeCode, packet.IsE0, packet.IsE1),
            normalized.ControlId);
        Assert.Equal(normalized.ControlId, normalized.Signal.ControlId);
        Assert.Equal(normalized.ControllerSessionId, normalized.Signal.ControllerSessionId);
    }

    [Theory]
    [InlineData(0x47, 0x24, "Numpad7", "Numpad 7 / Home")]
    [InlineData(0x4F, 0x23, "Numpad1", "Numpad 1 / End")]
    [InlineData(0x52, 0x2D, "Numpad0", "Numpad 0 / Insert")]
    [InlineData(0x53, 0x2E, "NumpadDecimal", "Numpad Decimal / Delete")]
    public void UsesPhysicalScanCodeToDistinguishNumpadNavigationAliases(
        ushort makeCode,
        ushort virtualKey,
        string expectedName,
        string expectedDisplay)
    {
        var packet = new RawKeyboardPacket(
            Descriptor.SessionHandle,
            makeCode,
            RawKeyboardFlags.Make,
            0,
            virtualKey,
            0x0100,
            0);

        var normalized = KeyboardPacketNormalizer.Normalize(packet, Descriptor, false, 1);

        Assert.True(normalized.Key.IsNumpadKey);
        Assert.Equal(expectedName, normalized.Key.Name);
        Assert.Equal(expectedDisplay, normalized.Key.DisplayName);
    }

    [Fact]
    public void PreservesE1BreakAndRepeatShapeInCoreSignal()
    {
        var packet = new RawKeyboardPacket(
            Descriptor.SessionHandle,
            0x45,
            RawKeyboardFlags.E1 | RawKeyboardFlags.Break,
            0,
            0x13,
            0x0101,
            0x1234);

        var normalized = KeyboardPacketNormalizer.Normalize(packet, Descriptor, isRepeat: false, timestamp: 456);

        Assert.Equal(ExtendedKeyKind.E1, normalized.ExtendedKey);
        Assert.Equal(KeyTransition.Release, normalized.Transition);
        Assert.Equal(ControlSignalKind.Release, normalized.Signal.Kind);
        Assert.False(normalized.Signal.Injection.IsInjected);
        Assert.Equal(0x1234ul, normalized.Signal.Injection.ExtraInfo);
    }
}
