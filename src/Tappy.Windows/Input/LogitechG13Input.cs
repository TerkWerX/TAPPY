using System.Text.Json.Serialization;
using Tappy.Core.Input;

namespace Tappy.Windows.Input;

public enum LogitechG13Control
{
    G1,
    G2,
    G3,
    G4,
    G5,
    G6,
    G7,
    G8,
    G9,
    G10,
    G11,
    G12,
    G13,
    G14,
    G15,
    G16,
    G17,
    G18,
    G19,
    G20,
    G21,
    G22,
    LcdNextPage,
    LcdMenuLeft,
    LcdMenu2,
    LcdMenu3,
    LcdMenuRight,
    M1,
    M2,
    M3,
    Mr,
    JoystickLeftSide,
    JoystickBottomSide,
    JoystickPress,
    Lights,
    StickLeft,
    StickRight,
    StickUp,
    StickDown,
}

public sealed record LogitechG13AnalogState(byte X, byte Y)
{
    public static LogitechG13AnalogState Center { get; } = new(128, 128);
}

public sealed record LogitechG13ControlDefinition(
    LogitechG13Control Control,
    string DisplayName,
    int? ButtonBitIndex,
    ControlId ControlId);

/// <summary>
/// Normalized G13 control transition. It contains no HID report buffer, raw device
/// path, or Windows container identifier.
/// </summary>
public sealed record LogitechG13Input(
    [property: JsonIgnore]
    nint SessionDeviceHandle,
    ControllerSessionId ControllerSessionId,
    string PersistentDeviceId,
    ControlId ControlId,
    LogitechG13Control Control,
    string DisplayName,
    int? ButtonBitIndex,
    LogitechG13AnalogState AnalogState,
    ControlSignal Signal);

public sealed class LogitechG13InputReceivedEventArgs(LogitechG13Input input) : EventArgs
{
    public LogitechG13Input Input { get; } = input;
}

public sealed class LogitechG13AnalogChangedEventArgs(
    ControllerSessionId controllerSessionId,
    LogitechG13AnalogState state) : EventArgs
{
    public ControllerSessionId ControllerSessionId { get; } = controllerSessionId;

    public LogitechG13AnalogState State { get; } = state;
}

public sealed class LogitechG13DeviceChangedEventArgs(
    RawInputDeviceChangeKind kind,
    SanitizedDeviceDescriptor? descriptor,
    bool wasCaptureTarget) : EventArgs
{
    public RawInputDeviceChangeKind Kind { get; } = kind;

    public SanitizedDeviceDescriptor? Descriptor { get; } = descriptor;

    public bool WasCaptureTarget { get; } = wasCaptureTarget;
}

/// <summary>
/// Conservative fixed thresholds around the unsigned G13 stick center. Entering a
/// direction requires a large deflection; returning toward center releases sooner.
/// Low Y is up and high Y is down.
/// </summary>
public static class LogitechG13StickHysteresis
{
    public const byte NegativeEngageMaximum = 64;
    public const byte NegativeReleaseMinimum = 96;
    public const byte PositiveReleaseMaximum = 159;
    public const byte PositiveEngageMinimum = 191;
}
