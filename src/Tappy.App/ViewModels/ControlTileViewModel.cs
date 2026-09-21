namespace Tappy.App.ViewModels;

/// <summary>
/// A rotary encoder's tile is really three logical controls (rotate-increase,
/// touch, rotate-decrease) rendered as one compact concentric cluster instead
/// of three separate tiles — see MainViewModel's encoder-cluster detection.
/// None means "an ordinary tile, not part of any cluster."
/// </summary>
public enum EncoderRingRole
{
    None,
    Center,
    IncreaseHalf,
    DecreaseHalf
}

public sealed class ControlTileViewModel : ObservableObject
{
    private bool _isPressed;
    private bool _canAssign = true;
    public bool CanAssign { get => _canAssign; set => Set(ref _canAssign, value); }
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
    private double _cornerRadius = 2;
    private string? _clusterId;
    private EncoderRingRole _ringRole = EncoderRingRole.None;
    private string _ringGlyph = string.Empty;
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

    /// <summary>
    /// 2 (today's plain-rectangle default, matching App.xaml's shared Button
    /// ControlTemplate) for most controls; a large constant for a control whose
    /// physical shape is round (knobs, some buttons) — WPF's Border clamps an
    /// over-large CornerRadius to the largest valid rounding for the box, so
    /// this renders as a full circle/pill regardless of the tile's current
    /// size and stays correct even if the tile is later resized, without
    /// needing to recompute per resize. Set once from the model's measured
    /// photo hotspot shape (see MainViewModel.EnsurePhotoHotspot), never
    /// inferred from TileWidth/TileHeight.
    /// </summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        set => Set(ref _cornerRadius, value);
    }

    /// <summary>
    /// Null for an ordinary tile. For a rotary encoder's three controls
    /// (increase/touch/decrease), all three share the same ClusterId — the
    /// key MainViewModel.PrepareGroupDrag uses to move them together, and
    /// the reason they're always positioned/sized identically (see
    /// MainViewModel.NaturalTileSize and ApplyControlPlacement).
    /// </summary>
    public string? ClusterId
    {
        get => _clusterId;
        set => Set(ref _clusterId, value);
    }

    public EncoderRingRole RingRole
    {
        get => _ringRole;
        set
        {
            if (Set(ref _ringRole, value))
            {
                Raise(nameof(IsEncoderCenter));
                Raise(nameof(IsEncoderIncreaseHalf));
                Raise(nameof(IsEncoderDecreaseHalf));
                Raise(nameof(IsEncoderClusterMember));
                Raise(nameof(RingZIndex));
            }
        }
    }

    /// <summary>"▲"/"▼" for an encoder half tile; blank otherwise — there's
    /// no room for the full Label/Action/State text in a thin half-moon.</summary>
    public string RingGlyph
    {
        get => _ringGlyph;
        set => Set(ref _ringGlyph, value);
    }

    public bool IsEncoderCenter => RingRole == EncoderRingRole.Center;
    public bool IsEncoderIncreaseHalf => RingRole == EncoderRingRole.IncreaseHalf;
    public bool IsEncoderDecreaseHalf => RingRole == EncoderRingRole.DecreaseHalf;
    public bool IsEncoderClusterMember => RingRole != EncoderRingRole.None;

    /// <summary>The center circle must paint over the increase/decrease
    /// half-disks' middle for the ring illusion to read correctly — see
    /// MainWindow.xaml's ItemContainerStyle.</summary>
    public int RingZIndex => RingRole switch
    {
        EncoderRingRole.Center => 2,
        EncoderRingRole.None => 0,
        _ => 1,
    };

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
