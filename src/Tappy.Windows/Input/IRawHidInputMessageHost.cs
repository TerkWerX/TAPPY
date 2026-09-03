using System.Text.Json.Serialization;

namespace Tappy.Windows.Input;

/// <summary>
/// A decoded, ephemeral vendor HID input frame. Raw report bytes never leave the
/// native message boundary and every value is excluded from JSON serialization.
/// Device-specific providers must still validate the originating device identity.
/// </summary>
public readonly struct RawHidInputPacket
{
    public RawHidInputPacket(
        nint deviceHandle,
        byte reportId,
        byte joystickX,
        byte joystickY,
        ulong buttonBits)
    {
        DeviceHandle = deviceHandle;
        ReportId = reportId;
        JoystickX = joystickX;
        JoystickY = joystickY;
        ButtonBits = buttonBits;
    }

    [JsonIgnore]
    public nint DeviceHandle { get; }

    [JsonIgnore]
    public byte ReportId { get; }

    [JsonIgnore]
    public byte JoystickX { get; }

    [JsonIgnore]
    public byte JoystickY { get; }

    [JsonIgnore]
    public ulong ButtonBits { get; }
}

public sealed class RawHidInputPacketEventArgs(RawHidInputPacket packet) : EventArgs
{
    [JsonIgnore]
    public RawHidInputPacket Packet { get; } = packet;
}

/// <summary>
/// Raw Input host capability used by device-specific HID providers. Keeping this
/// separate preserves the existing keyboard-only host contract and test seams.
/// </summary>
public interface IRawHidInputMessageHost : IRawInputMessageHost
{
    event EventHandler<RawHidInputPacketEventArgs>? HidPacketReceived;
}
