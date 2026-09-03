using Tappy.Core.Input;

namespace Tappy.Windows.Input;

public static class KeyboardPacketNormalizer
{
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkReturn = 0x0D;

    public static NormalizedKeyboardInput Normalize(
        RawKeyboardPacket packet,
        SanitizedDeviceDescriptor descriptor,
        bool isRepeat,
        long timestamp)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var extended = packet.IsE1
            ? ExtendedKeyKind.E1
            : packet.IsE0
                ? ExtendedKeyKind.E0
                : ExtendedKeyKind.None;

        var metadata = ResolveMetadata(packet.VirtualKey, packet.MakeCode, extended);
        var sessionId = new ControllerSessionId(descriptor.SessionId);
        var controlId = ControlId.FromRawInputKeyboard(
            packet.MakeCode,
            packet.IsE0,
            packet.IsE1,
            usage: packet.MakeCode == 0 ? packet.VirtualKey : null);
        var transition = packet.IsBreak ? KeyTransition.Release : KeyTransition.Press;
        var signalKind = transition == KeyTransition.Release
            ? ControlSignalKind.Release
            : isRepeat
                ? ControlSignalKind.Repeat
                : ControlSignalKind.Press;
        var signal = new ControlSignal(
            sessionId,
            controlId,
            signalKind,
            timestamp,
            new InputInjectionMetadata(false, packet.ExtraInformation, "raw-input"));

        return new NormalizedKeyboardInput(
            packet.DeviceHandle,
            sessionId,
            descriptor.PersistentId,
            controlId,
            packet.MakeCode,
            packet.VirtualKey,
            extended,
            transition,
            isRepeat,
            metadata,
            packet.Message,
            packet.ExtraInformation,
            signal);
    }

    public static VirtualKeyMetadata ResolveMetadata(
        ushort virtualKey,
        ushort makeCode,
        ExtendedKeyKind extendedKey)
    {
        if (virtualKey == VkShift)
        {
            return makeCode == 0x36
                ? Metadata(0xA1, "RightShift", "Right Shift", true, false)
                : Metadata(0xA0, "LeftShift", "Left Shift", true, false);
        }

        if (virtualKey == VkControl)
        {
            return extendedKey == ExtendedKeyKind.E0
                ? Metadata(0xA3, "RightControl", "Right Ctrl", true, false)
                : Metadata(0xA2, "LeftControl", "Left Ctrl", true, false);
        }

        if (virtualKey == VkMenu)
        {
            return extendedKey == ExtendedKeyKind.E0
                ? Metadata(0xA5, "RightAlt", "Right Alt", true, false)
                : Metadata(0xA4, "LeftAlt", "Left Alt", true, false);
        }

        if (virtualKey == VkReturn && extendedKey == ExtendedKeyKind.E0)
        {
            return Metadata(virtualKey, "NumpadEnter", "Numpad Enter", false, true);
        }

        if (extendedKey == ExtendedKeyKind.None && TryResolveNumpadScanCode(makeCode, out var numpad))
        {
            return numpad with { VirtualKey = virtualKey };
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            var digit = virtualKey - 0x60;
            return Metadata(virtualKey, $"Numpad{digit}", $"Numpad {digit}", false, true);
        }

        return virtualKey switch
        {
            0x08 => Metadata(virtualKey, "Backspace", "Backspace", false, false),
            0x09 => Metadata(virtualKey, "Tab", "Tab", false, false),
            0x0D => Metadata(virtualKey, "Enter", "Enter", false, false),
            0x14 => Metadata(virtualKey, "CapsLock", "Caps Lock", false, false),
            0x1B => Metadata(virtualKey, "Escape", "Esc", false, false),
            0x20 => Metadata(virtualKey, "Space", "Space", false, false),
            0x21 => Metadata(virtualKey, "PageUp", "Page Up", false, false),
            0x22 => Metadata(virtualKey, "PageDown", "Page Down", false, false),
            0x23 => Metadata(virtualKey, "End", "End", false, false),
            0x24 => Metadata(virtualKey, "Home", "Home", false, false),
            0x25 => Metadata(virtualKey, "Left", "Left Arrow", false, false),
            0x26 => Metadata(virtualKey, "Up", "Up Arrow", false, false),
            0x27 => Metadata(virtualKey, "Right", "Right Arrow", false, false),
            0x28 => Metadata(virtualKey, "Down", "Down Arrow", false, false),
            0x2D => Metadata(virtualKey, "Insert", "Insert", false, false),
            0x2E => Metadata(virtualKey, "Delete", "Delete", false, false),
            >= 0x30 and <= 0x39 => Metadata(virtualKey, ((char)virtualKey).ToString(), ((char)virtualKey).ToString(), false, false),
            >= 0x41 and <= 0x5A => Metadata(virtualKey, ((char)virtualKey).ToString(), ((char)virtualKey).ToString(), false, false),
            >= 0x70 and <= 0x87 => Metadata(virtualKey, $"F{virtualKey - 0x6F}", $"F{virtualKey - 0x6F}", false, false),
            0x6A => Metadata(virtualKey, "NumpadMultiply", "Numpad *", false, true),
            0x6B => Metadata(virtualKey, "NumpadAdd", "Numpad +", false, true),
            0x6D => Metadata(virtualKey, "NumpadSubtract", "Numpad -", false, true),
            0x6E => Metadata(virtualKey, "NumpadDecimal", "Numpad Decimal", false, true),
            0x6F => Metadata(virtualKey, "NumpadDivide", "Numpad /", false, true),
            _ => Metadata(virtualKey, $"VK_{virtualKey:X4}", $"VK 0x{virtualKey:X4}", false, false),
        };
    }

    private static bool TryResolveNumpadScanCode(ushort makeCode, out VirtualKeyMetadata metadata)
    {
        metadata = makeCode switch
        {
            0x37 => Metadata(0x6A, "NumpadMultiply", "Numpad *", false, true),
            0x47 => Metadata(0x67, "Numpad7", "Numpad 7 / Home", false, true),
            0x48 => Metadata(0x68, "Numpad8", "Numpad 8 / Up", false, true),
            0x49 => Metadata(0x69, "Numpad9", "Numpad 9 / Page Up", false, true),
            0x4A => Metadata(0x6D, "NumpadSubtract", "Numpad -", false, true),
            0x4B => Metadata(0x64, "Numpad4", "Numpad 4 / Left", false, true),
            0x4C => Metadata(0x65, "Numpad5", "Numpad 5", false, true),
            0x4D => Metadata(0x66, "Numpad6", "Numpad 6 / Right", false, true),
            0x4E => Metadata(0x6B, "NumpadAdd", "Numpad +", false, true),
            0x4F => Metadata(0x61, "Numpad1", "Numpad 1 / End", false, true),
            0x50 => Metadata(0x62, "Numpad2", "Numpad 2 / Down", false, true),
            0x51 => Metadata(0x63, "Numpad3", "Numpad 3 / Page Down", false, true),
            0x52 => Metadata(0x60, "Numpad0", "Numpad 0 / Insert", false, true),
            0x53 => Metadata(0x6E, "NumpadDecimal", "Numpad Decimal / Delete", false, true),
            _ => null!,
        };

        return metadata is not null;
    }

    private static VirtualKeyMetadata Metadata(
        ushort virtualKey,
        string name,
        string displayName,
        bool isModifier,
        bool isNumpadKey) =>
        new(virtualKey, name, displayName, isModifier, isNumpadKey);
}
