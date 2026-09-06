namespace Tappy.App.ViewModels;

public sealed class ControlTileViewModel : ObservableObject
{
    private bool _isPressed;
    private bool _isIlluminated;
    private bool _isSelected;
    private bool _isGroupSelected;
    private bool _canUseHardwareColor;
    private string _action = "Unassigned";
    private string _colorKey = "Default";
    private string _tileBackground = "#202936";
    private string _tileForeground = "#F7FAFC";
    private double _x;
    private double _y;
    private double _tileWidth = 134;
    private double _tileHeight = 106;
    private int? _analogRawValue;
    private int _analogRawAtMinimum;
    private int _analogRawAtMaximum = 127;
    private int? _analogRawAtCenter;
    private double _encoderDegreesPerStep = 15;
    private bool _encoderReversed;
    private double _encoderAngle;

    public required string ControlId { get; init; }
    public required string Label { get; init; }

    public double X
    {
        get => _x;
        set => Set(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, value);
    }

    public double TileWidth
    {
        get => _tileWidth;
        set => Set(ref _tileWidth, value);
    }

    public double TileHeight
    {
        get => _tileHeight;
        set => Set(ref _tileHeight, value);
    }

    public int? AnalogRawValue
    {
        get => _analogRawValue;
        set
        {
            if (Set(ref _analogRawValue, value))
            {
                Raise(nameof(HasAnalogValue));
                Raise(nameof(AnalogPosition));
                Raise(nameof(AnalogValueText));
            }
        }
    }

    public int AnalogRawAtMinimum
    {
        get => _analogRawAtMinimum;
        set
        {
            if (Set(ref _analogRawAtMinimum, value))
            {
                Raise(nameof(AnalogPosition));
                Raise(nameof(AnalogCalibrationText));
            }
        }
    }

    public int AnalogRawAtMaximum
    {
        get => _analogRawAtMaximum;
        set
        {
            if (Set(ref _analogRawAtMaximum, value))
            {
                Raise(nameof(AnalogPosition));
                Raise(nameof(AnalogCalibrationText));
            }
        }
    }

    public int? AnalogRawAtCenter
    {
        get => _analogRawAtCenter;
        set
        {
            if (Set(ref _analogRawAtCenter, value))
            {
                Raise(nameof(AnalogPosition));
                Raise(nameof(AnalogCalibrationText));
            }
        }
    }

    public double EncoderDegreesPerStep
    {
        get => _encoderDegreesPerStep;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, 0.25, 180) : 15;
            if (Set(ref _encoderDegreesPerStep, normalized))
            {
                Raise(nameof(EncoderCalibrationText));
            }
        }
    }

    public double EncoderAngle
    {
        get => _encoderAngle;
        private set => Set(ref _encoderAngle, value);
    }

    public bool EncoderReversed
    {
        get => _encoderReversed;
        set
        {
            if (Set(ref _encoderReversed, value))
            {
                Raise(nameof(EncoderCalibrationText));
            }
        }
    }

    public bool HasAnalogValue => AnalogRawValue is not null;

    public double AnalogPosition
    {
        get
        {
            if (AnalogRawValue is not { } raw || AnalogRawAtMinimum == AnalogRawAtMaximum)
            {
                return 0;
            }

            var position = (raw - (double)AnalogRawAtMinimum) /
                           (AnalogRawAtMaximum - (double)AnalogRawAtMinimum);
            position = Math.Clamp(position, 0, 1);
            if (AnalogRawAtCenter is not { } center)
            {
                return position;
            }

            var centerPosition = (center - (double)AnalogRawAtMinimum) /
                                 (AnalogRawAtMaximum - (double)AnalogRawAtMinimum);
            if (centerPosition is <= 0 or >= 1)
            {
                return position;
            }

            return position <= centerPosition
                ? 0.5 * position / centerPosition
                : 0.5 + (0.5 * (position - centerPosition) / (1 - centerPosition));
        }
    }

    public string AnalogValueText => AnalogRawValue is { } raw
        ? $"Raw {raw} · {AnalogPosition:P0}"
        : "Move the physical control to read its position";

    public string AnalogCalibrationText => AnalogRawAtCenter is { } center
        ? $"Ends {AnalogRawAtMinimum} → {AnalogRawAtMaximum}; center {center}"
        : $"Ends {AnalogRawAtMinimum} → {AnalogRawAtMaximum}";

    public string EncoderCalibrationText =>
        $"{EncoderDegreesPerStep:0.##}° per event · {(EncoderReversed ? "reversed" : "normal")}";

    public void ApplyEncoderDelta(double steps)
    {
        if (!double.IsFinite(steps) || steps == 0)
        {
            return;
        }

        var direction = EncoderReversed ? -1 : 1;
        var next = (EncoderAngle + (steps * EncoderDegreesPerStep * direction)) % 360;
        EncoderAngle = next < 0 ? next + 360 : next;
    }

    public void ResetEncoderAngle() => EncoderAngle = 0;

    public string AutomationName => $"Controller control {Label}, {Action}, {StateText}";

    public bool IsPressed
    {
        get => _isPressed;
        set
        {
            if (Set(ref _isPressed, value))
            {
                Raise(nameof(StateText));
                Raise(nameof(AutomationName));
            }
        }
    }

    public bool IsIlluminated
    {
        get => _isIlluminated;
        set => Set(ref _isIlluminated, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsGroupSelected
    {
        get => _isGroupSelected;
        set => Set(ref _isGroupSelected, value);
    }

    public string ColorKey
    {
        get => _colorKey;
        set
        {
            if (Set(ref _colorKey, string.IsNullOrWhiteSpace(value) ? "Default" : value))
            {
                Raise(nameof(HasCustomColor));
            }
        }
    }

    public bool CanUseHardwareColor
    {
        get => _canUseHardwareColor;
        set => Set(ref _canUseHardwareColor, value);
    }

    public bool HasCustomColor => CanUseHardwareColor &&
                                  !string.Equals(ColorKey, "Default", StringComparison.OrdinalIgnoreCase);

    public string TileBackground
    {
        get => _tileBackground;
        set => Set(ref _tileBackground, value);
    }

    public string TileForeground
    {
        get => _tileForeground;
        set => Set(ref _tileForeground, value);
    }

    public string Action
    {
        get => _action;
        set
        {
            if (Set(ref _action, value))
            {
                Raise(nameof(AutomationName));
            }
        }
    }

    public string StateText => IsPressed ? "Pressed" : "Released";
}
