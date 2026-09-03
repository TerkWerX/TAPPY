namespace Tappy.Windows.Input;

[Flags]
public enum RawKeyboardFlags : ushort
{
    Make = 0,
    Break = 0x0001,
    E0 = 0x0002,
    E1 = 0x0004,
}

/// <summary>
/// Lossless managed representation of the keyboard portion of a WM_INPUT packet.
/// The device handle is session-scoped and is never persisted or included in a
/// support report.
/// </summary>
public readonly record struct RawKeyboardPacket(
    nint DeviceHandle,
    ushort MakeCode,
    RawKeyboardFlags Flags,
    ushort Reserved,
    ushort VirtualKey,
    uint Message,
    uint ExtraInformation)
{
    public bool IsBreak => (Flags & RawKeyboardFlags.Break) != 0;

    public bool IsE0 => (Flags & RawKeyboardFlags.E0) != 0;

    public bool IsE1 => (Flags & RawKeyboardFlags.E1) != 0;
}
