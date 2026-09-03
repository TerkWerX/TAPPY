using System.Globalization;
using Tappy.Core.Input;

namespace Tappy.Windows.Input;

internal enum LogitechG13AxisDirection
{
    Neutral,
    Negative,
    Positive,
}

internal readonly record struct LogitechG13DecodedTransition(
    LogitechG13ControlDefinition Definition,
    ControlSignalKind Kind);

internal sealed class LogitechG13ReportDecoder
{
    private const ulong ControllableButtonMask =
        ((1UL << 22) - 1) |
        (0x1FUL << 24) |
        (0x7FUL << 29) |
        (1UL << 37);

    private ulong _buttons;
    private LogitechG13AxisDirection _xDirection;
    private LogitechG13AxisDirection _yDirection;

    internal static IReadOnlyList<LogitechG13ControlDefinition> Controls { get; } =
        CreateDefinitions();

    internal bool HasObservedFrame { get; private set; }

    internal LogitechG13AnalogState AnalogState { get; private set; } =
        LogitechG13AnalogState.Center;

    internal IReadOnlyList<LogitechG13DecodedTransition> Process(
        RawHidInputPacket packet,
        out bool analogChanged)
    {
        if (packet.ReportId != LogitechG13Protocol.InputReportId)
        {
            analogChanged = false;
            return [];
        }

        var transitions = new List<LogitechG13DecodedTransition>();
        var newButtons = packet.ButtonBits & ControllableButtonMask;
        var changedButtons = _buttons ^ newButtons;
        foreach (var definition in Controls.Where(definition => definition.ButtonBitIndex is not null))
        {
            var bit = definition.ButtonBitIndex!.Value;
            var mask = 1UL << bit;
            if ((changedButtons & mask) == 0)
            {
                continue;
            }

            transitions.Add(new LogitechG13DecodedTransition(
                definition,
                (newButtons & mask) != 0 ? ControlSignalKind.Press : ControlSignalKind.Release));
        }

        var newXDirection = NextDirection(_xDirection, packet.JoystickX);
        var newYDirection = NextDirection(_yDirection, packet.JoystickY);
        AddAxisTransitions(
            transitions,
            _xDirection,
            newXDirection,
            LogitechG13Control.StickLeft,
            LogitechG13Control.StickRight);
        AddAxisTransitions(
            transitions,
            _yDirection,
            newYDirection,
            LogitechG13Control.StickUp,
            LogitechG13Control.StickDown);

        var nextAnalog = new LogitechG13AnalogState(packet.JoystickX, packet.JoystickY);
        analogChanged = HasObservedFrame && nextAnalog != AnalogState;
        AnalogState = nextAnalog;
        _buttons = newButtons;
        _xDirection = newXDirection;
        _yDirection = newYDirection;
        HasObservedFrame = true;
        return transitions;
    }

    internal void Reset()
    {
        _buttons = 0;
        _xDirection = LogitechG13AxisDirection.Neutral;
        _yDirection = LogitechG13AxisDirection.Neutral;
        AnalogState = LogitechG13AnalogState.Center;
        HasObservedFrame = false;
    }

    private static LogitechG13AxisDirection NextDirection(
        LogitechG13AxisDirection current,
        byte value) =>
        current switch
        {
            LogitechG13AxisDirection.Negative when value >= LogitechG13StickHysteresis.PositiveEngageMinimum =>
                LogitechG13AxisDirection.Positive,
            LogitechG13AxisDirection.Negative when value >= LogitechG13StickHysteresis.NegativeReleaseMinimum =>
                LogitechG13AxisDirection.Neutral,
            LogitechG13AxisDirection.Positive when value <= LogitechG13StickHysteresis.NegativeEngageMaximum =>
                LogitechG13AxisDirection.Negative,
            LogitechG13AxisDirection.Positive when value <= LogitechG13StickHysteresis.PositiveReleaseMaximum =>
                LogitechG13AxisDirection.Neutral,
            LogitechG13AxisDirection.Neutral when value <= LogitechG13StickHysteresis.NegativeEngageMaximum =>
                LogitechG13AxisDirection.Negative,
            LogitechG13AxisDirection.Neutral when value >= LogitechG13StickHysteresis.PositiveEngageMinimum =>
                LogitechG13AxisDirection.Positive,
            _ => current,
        };

    private static void AddAxisTransitions(
        ICollection<LogitechG13DecodedTransition> transitions,
        LogitechG13AxisDirection previous,
        LogitechG13AxisDirection next,
        LogitechG13Control negativeControl,
        LogitechG13Control positiveControl)
    {
        if (previous == next)
        {
            return;
        }

        if (previous != LogitechG13AxisDirection.Neutral)
        {
            transitions.Add(new LogitechG13DecodedTransition(
                Find(previous == LogitechG13AxisDirection.Negative ? negativeControl : positiveControl),
                ControlSignalKind.Release));
        }

        if (next != LogitechG13AxisDirection.Neutral)
        {
            transitions.Add(new LogitechG13DecodedTransition(
                Find(next == LogitechG13AxisDirection.Negative ? negativeControl : positiveControl),
                ControlSignalKind.Press));
        }
    }

    private static LogitechG13ControlDefinition Find(LogitechG13Control control) =>
        Controls.Single(definition => definition.Control == control);

    private static IReadOnlyList<LogitechG13ControlDefinition> CreateDefinitions()
    {
        var definitions = new List<LogitechG13ControlDefinition>(39);
        for (var index = 0; index < 22; index++)
        {
            var control = LogitechG13Control.G1 + index;
            definitions.Add(Button(control, $"G{index + 1}", index));
        }

        definitions.Add(Button(LogitechG13Control.LcdNextPage, "LCD next page", 24));
        definitions.Add(Button(LogitechG13Control.LcdMenuLeft, "LCD menu 1 (left)", 25));
        definitions.Add(Button(LogitechG13Control.LcdMenu2, "LCD menu 2", 26));
        definitions.Add(Button(LogitechG13Control.LcdMenu3, "LCD menu 3", 27));
        definitions.Add(Button(LogitechG13Control.LcdMenuRight, "LCD menu 4 (right)", 28));
        definitions.Add(Button(LogitechG13Control.M1, "M1", 29));
        definitions.Add(Button(LogitechG13Control.M2, "M2", 30));
        definitions.Add(Button(LogitechG13Control.M3, "M3", 31));
        definitions.Add(Button(LogitechG13Control.Mr, "MR", 32));
        definitions.Add(Button(LogitechG13Control.JoystickLeftSide, "Joystick left side", 33));
        definitions.Add(Button(LogitechG13Control.JoystickBottomSide, "Joystick bottom side", 34));
        definitions.Add(Button(LogitechG13Control.JoystickPress, "Joystick press", 35));
        definitions.Add(Button(LogitechG13Control.Lights, "Lights", 37));
        definitions.Add(Axis(LogitechG13Control.StickLeft, "Stick left", "x", "negative"));
        definitions.Add(Axis(LogitechG13Control.StickRight, "Stick right", "x", "positive"));
        definitions.Add(Axis(LogitechG13Control.StickUp, "Stick up", "y", "negative"));
        definitions.Add(Axis(LogitechG13Control.StickDown, "Stick down", "y", "positive"));
        return definitions.AsReadOnly();
    }

    private static LogitechG13ControlDefinition Button(
        LogitechG13Control control,
        string displayName,
        int bit)
    {
        var physicalIdentity = string.Create(
            CultureInfo.InvariantCulture,
            $"g13:upff00:u0000:r01:bit{bit:D2}:{control.ToString().ToLowerInvariant()}");
        return new(
            control,
            displayName,
            bit,
            ControlId.Create("raw-hid-g13", physicalIdentity));
    }

    private static LogitechG13ControlDefinition Axis(
        LogitechG13Control control,
        string displayName,
        string axis,
        string direction) =>
        new(
            control,
            displayName,
            null,
            ControlId.Create(
                "raw-hid-g13",
                $"g13:upff00:u0000:r01:axis-{axis}:{direction}:{control.ToString().ToLowerInvariant()}"));
}
