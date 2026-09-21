using System.Collections.ObjectModel;
using Tappy.App.Runtime;
using Tappy.App.Services;

namespace Tappy.App.ViewModels;

public sealed record TileColorChoice(string Key, string Label, string Background, string Foreground);
public sealed record TartarusKeymapChoice(int Number, string Label);

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    internal static readonly TileColorChoice[] AllTileColorChoices =
    [
        new("Default", "Off / default", "#202936", "#F7FAFC"),
        new("Red", "Red", "#8F2636", "#FFFFFF"),
        new("Amber", "Amber", "#B87814", "#171B22"),
        new("Orange", "Orange", "#9A4A12", "#FFFFFF"),
        new("Yellow", "Yellow", "#E0B52B", "#171B22"),
        new("Green", "Green", "#26734D", "#FFFFFF"),
        new("Teal", "Teal", "#147C78", "#FFFFFF"),
        new("Blue", "Blue", "#285F9E", "#FFFFFF"),
        new("Purple", "Purple", "#67479B", "#FFFFFF"),
        new("Pink", "Pink", "#9B3F75", "#FFFFFF"),
        new("Gray", "Gray", "#586273", "#FFFFFF"),
        new("White", "White", "#EEF2F6", "#17202C"),
    ];
    private static readonly TimeSpan MinimumIlluminationDuration = TimeSpan.FromMilliseconds(80);
    // Must comfortably fit the largest grid-controller natural tile the
    // default (unsaved) layout ever places at this pitch without a gap —
    // the Fire's pads, at PhotoShapeTileScale (81*1.5 = 121.5px tall), are
    // the tallest control placed edge-to-edge with no explicit gap column
    // (an encoder cluster, at EncoderClusterTileScale, is smaller still).
    // See NaturalTileSize.
    internal const double LayoutCellWidth = 170;
    internal const double LayoutCellHeight = 170;
    internal const double DefaultTileWidth = 134;
    internal const double DefaultTileHeight = 106;
    private const double MinimumTileSize = 48;
    // Real physical size differences (an encoder knob is legitimately bigger
    // than a button) carry straight through at this one uniform scale, rather
    // than normalizing every tile to the same footprint — a bigger control on
    // the real unit becomes a bigger tile, not just a differently-shaped one
    // at the same size.
    private const double PhotoShapeTileScale = 1.5;
    // A rotary encoder's three controls (increase/touch/decrease) render as
    // one compact concentric cluster, not three side-by-side tiles, so this
    // stays at the raw measured knob diameter instead of PhotoShapeTileScale
    // — scaling it up too would defeat the point of combining them.
    private const double EncoderClusterTileScale = 1.0;
    private const double SnapStep = 12;
    private const double NeighborSnapDistance = 10;
    private readonly IControllerRuntime _runtime;
    private readonly Action<Action> _onUi;
    private readonly Action<TimeSpan, Action> _onUiAfter;
    private readonly VisualUpdateBuffer _pendingVisuals = new();
    private readonly Dictionary<string, ControlTileViewModel> _tiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControllerPhotoHotspotViewModel> _photoHotspotsByVisualId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IlluminationState> _illuminationStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControllerControlPlacement> _layoutPlacements = new(StringComparer.Ordinal);
    private ControllerChoice? _selectedDevice;
    private string _typingKeyboardStatus = "Typing keyboard: not set";
    public string TypingKeyboardStatus { get => _typingKeyboardStatus; internal set => Set(ref _typingKeyboardStatus, value); }
    private ControlTileViewModel? _selectedControl;
    private string _selectedOutputKey = "F24";
    private string _identificationStatus = "Choose a device. Nothing is armed.";
    private string _mappingStatus = "No mapping selected.";
    private string _activeControllerLabel = "No controller confirmed";
    private string _activeLayerName = "Layer 1";
    private string _inputBankLabel = string.Empty;
    private bool? _numLockOn;
    private int? _tartarusKeymap;
    private bool _canChangeTartarusKeymap;
    private bool _tartarusLedBusy;
    private bool _tartarusFollowKeymaps;
    private int? _tartarusVerifiedLedMask;
    private string? _tartarusLedError;
    private FireOledDisplayMode _oledDisplayMode = FireOledDisplayMode.Off;
    public bool HasTartarusKeymaps => _tartarusKeymap is >= 1 and <= 8;
    public bool CanChangeTartarusKeymap => _canChangeTartarusKeymap && !_tartarusLedBusy;
    public bool TartarusFollowKeymaps { get => _tartarusFollowKeymaps; set => Set(ref _tartarusFollowKeymaps, value); }

    /// <summary>
    /// Read every tick by the tab's FireOledContentPlayer — no side effect on
    /// set. The Fire's OLED reverts to black on its own a few seconds after
    /// its last update, so switching away from any mode (including back to
    /// Off) needs no explicit "clear" action; the display goes dark by
    /// itself once the periodic sends for the old mode stop.
    /// </summary>
    public FireOledDisplayMode OledDisplayMode { get => _oledDisplayMode; set => Set(ref _oledDisplayMode, value); }
    public IReadOnlyList<TartarusKeymapChoice> TartarusKeymaps { get; } =
    [new(1, "1 · Yellow"), new(2, "2 · Green"), new(3, "3 · Blue"), new(4, "4 · Yellow + green"),
     new(5, "5 · Yellow + blue"), new(6, "6 · Green + blue"), new(7, "7 · All three"), new(8, "8 · All off")];
    public int SelectedTartarusKeymap => _tartarusKeymap ?? 0;
    private int TartarusIndicatorMask => HasTartarusKeymaps ? RazerTartarusLayout.IndicatorMask(_tartarusKeymap!.Value) : 0;
    public string TartarusYellowLed => (TartarusIndicatorMask & 1) != 0 ? "#FFE45C" : "#30343A";
    public string TartarusGreenLed => (TartarusIndicatorMask & 2) != 0 ? "#53EF70" : "#30343A";
    public string TartarusBlueLed => (TartarusIndicatorMask & 4) != 0 ? "#49B8FF" : "#30343A";
    public string TartarusIndicatorStatus => _tartarusLedBusy ? "Synchronizing this Tartarus's bank indicators…" :
        _tartarusLedError ?? (_tartarusVerifiedLedMask == TartarusIndicatorMask
            ? $"Keymap {SelectedTartarusKeymap} LEDs confirmed by device readback at last sync."
            : $"Tappy keymap {SelectedTartarusKeymap} preview — physical LEDs not synchronized.");
    public async Task SyncTartarusIndicatorsAsync()
    {
        if (!CanChangeTartarusKeymap) { return; }
        _tartarusLedBusy = true; _tartarusLedError = null;
        Raise(nameof(CanChangeTartarusKeymap)); Raise(nameof(TartarusIndicatorStatus));
        try
        {
            var result = await _runtime.SyncTartarusIndicatorsAsync().ConfigureAwait(true);
            if (!_isDisposed && _canChangeTartarusKeymap)
            {
                MappingStatus = result.Message;
                _tartarusLedError = result.Succeeded ? null : result.Message;
                // A failed operation must be explicitly retried, not repeated on every bank change.
                if (!result.Succeeded) { TartarusFollowKeymaps = false; }
            }
        }
        finally
        {
            _tartarusLedBusy = false;
            Raise(nameof(CanChangeTartarusKeymap)); Raise(nameof(TartarusIndicatorStatus));
        }
    }
    public async Task SelectTartarusKeymapAsync(int bank)
    {
        if (bank == _tartarusKeymap || !CanChangeTartarusKeymap) { return; }
        var result = _runtime.SelectTartarusKeymap(bank);
        MappingStatus = result.Message;
        Raise(nameof(SelectedTartarusKeymap));
        if (result.Succeeded)
        {
            if (TartarusFollowKeymaps) { await SyncTartarusIndicatorsAsync().ConfigureAwait(true); }
            try
            {
                var saved = await _runtime.SaveProfileAsync().ConfigureAwait(true);
                MappingStatus = saved.Succeeded ? $"Tartarus keymap {bank} selected and saved." : saved.Message;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { MappingStatus = "The keymap changed, but the profile could not be saved. Use Save profile to retry. " + exception.Message; }
        }
    }
    public string InputBankLabel { get => _inputBankLabel; private set => Set(ref _inputBankLabel, value); }
    public bool HasInputBank => !string.IsNullOrEmpty(InputBankLabel);
    public string NumLockLedBrush => _numLockOn switch { true => "#48FF63", false => "#122C19", null => "#696969" };
    public string NumLockLedDescription => _numLockOn switch
    {
        true => "Last detected numeric report — not live LED readback",
        false => "Last detected navigation report — not live LED readback",
        null => "No numeric or navigation report detected yet"
    };
    private string _pressedSummary = "None";
    private string _eventSummary = "No selected-device events retained";
    private string _status = "Tappy is not listening to a controller.";
    private string _effectiveSourceLabel = "Effective: Pass-through";
    private string _controllerPhotoName = string.Empty;
    private string _controllerPhotoId = string.Empty;
    private string _controllerPhotoSource = string.Empty;
    private double _controllerPhotoWidth = 1;
    private double _controllerPhotoHeight = 1;
    private int _layoutColumns = 7;
    private int _layoutRows = 6;
    private bool _snapLayoutToGrid = true;
    private bool _isLayoutLocked;
    private double _zoom = 1.0;
    private double _photoPaneWidth = 420;
    private TileColorChoice _selectedTileColor;
    private ControllerLedColorCapability? _controllerLedCapability;
    private double _layoutSurfaceWidth = LayoutCellWidth * 7;
    private double _layoutSurfaceHeight = LayoutCellHeight * 6;
    private string? _persistentStatusWarning;
    private ControllerPhotoDefinition? _activeControllerPhoto;
    private bool _canConfirmController;
    private bool _isIdentificationCaptureActive;
    private bool _isRehearsal = true;
    private volatile bool _isDisposed;
    private long _illuminationGeneration;
    private int _visualFlushScheduled;

    public MainViewModel(
        IControllerRuntime runtime,
        Action<Action> onUi,
        Action<TimeSpan, Action>? onUiAfter = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _onUi = onUi ?? throw new ArgumentNullException(nameof(onUi));
        _onUiAfter = onUiAfter ?? ((_, action) => _onUi(action));
        Devices = [];
        Controls = [];
        ControllerPhotoHotspots = [];
        HarmlessOutputKeys = Enumerable.Range(13, 12).Select(number => $"F{number}").ToArray();
        LayoutGridChoices = Enumerable.Range(1, 40).ToArray();
        TileColorChoices = [AllTileColorChoices[0]];
        _selectedTileColor = TileColorChoices[0];
        _runtime.DevicesChanged += Runtime_OnDevicesChanged;
        _runtime.ControlChanged += Runtime_OnControlChanged;
        _runtime.StateChanged += Runtime_OnStateChanged;
    }

    public ObservableCollection<ControllerChoice> Devices { get; }
    internal Func<ControllerChoice, string?>? IdentificationGuard { get; set; }
    internal Func<string?>? NormalOutputGuard { get; set; }
    public ObservableCollection<ControlTileViewModel> Controls { get; }
    public ObservableCollection<ControllerPhotoHotspotViewModel> ControllerPhotoHotspots { get; }
    public IReadOnlyList<string> HarmlessOutputKeys { get; }
    public IReadOnlyList<int> LayoutGridChoices { get; }
    public ObservableCollection<TileColorChoice> TileColorChoices { get; }

    public TileColorChoice SelectedTileColor
    {
        get => _selectedTileColor;
        set => Set(ref _selectedTileColor, value ?? TileColorChoices[0]);
    }

    public int GroupSelectedCount => Controls.Count(tile => tile.IsGroupSelected);
    public string GroupSelectionLabel => $"{GroupSelectedCount} selected";
    public bool HasSupportedTileColors => _controllerLedCapability?.SupportedColorKeys.Count > 1;
    public bool UsesGlobalTileColor => _controllerLedCapability?.Topology == ControllerLightingTopology.Global;
    public bool HasFreeformColorSupport => _controllerLedCapability?.SupportsFreeformColor == true;
    public bool HasAnimatableLighting => _controllerLedCapability is { } capability &&
                                          LedGridAdapterCatalog.Find(capability.ModelId) is not null;
    public bool HasOledDisplay => _controllerLedCapability?.ModelId == MidiControllerModelCatalog.AkaiFireV1Id;
    public string TileColorApplyButtonLabel => UsesGlobalTileColor ? "Preview whole device" : "Color selected";
    public string TileColorSyncButtonLabel => UsesGlobalTileColor ? "Sync G13 lighting" : "Sync supported LEDs";
    public string TileColorSyncToolTip => UsesGlobalTileColor
        ? "Send the chosen whole-device color only to the confirmed Logitech G13. Its physical keys share one RGB backlight color."
        : "Send saved square colors only when Tappy has a verified lighting protocol for this exact controller model.";
    public string TileColorAvailabilityLabel => _controllerLedCapability?.DisplayName ??
                                                "No verified addressable colors for this controller";

    public int LayoutColumns
    {
        get => _layoutColumns;
        set => Set(ref _layoutColumns, Math.Clamp(value, 1, 40));
    }

    public int LayoutRows
    {
        get => _layoutRows;
        set => Set(ref _layoutRows, Math.Clamp(value, 1, 40));
    }

    // Per-controller, not per-window: each device tab has its own MainViewModel,
    // so zoom and photo-pane width naturally stay independent per confirmed
    // controller rather than leaking across tabs.
    public double Zoom
    {
        get => _zoom;
        set => Set(ref _zoom, Math.Clamp(value, 0.25, 2.0));
    }

    public double PhotoPaneWidth
    {
        get => _photoPaneWidth;
        set => Set(ref _photoPaneWidth, Math.Clamp(value, 240, 1200));
    }

    public bool SnapLayoutToGrid
    {
        get => _snapLayoutToGrid;
        set => Set(ref _snapLayoutToGrid, value);
    }

    // Per-controller, like Zoom/SnapLayoutToGrid above — once a layout is
    // arranged the way the owner wants it, locking it stops accidental
    // drags/resizes without affecting selection or color actions.
    public bool IsLayoutLocked
    {
        get => _isLayoutLocked;
        set => Set(ref _isLayoutLocked, value);
    }

    public double LayoutSurfaceWidth
    {
        get => _layoutSurfaceWidth;
        private set => Set(ref _layoutSurfaceWidth, value);
    }

    public double LayoutSurfaceHeight
    {
        get => _layoutSurfaceHeight;
        private set => Set(ref _layoutSurfaceHeight, value);
    }

    public ControllerChoice? SelectedDevice
    {
        get => _selectedDevice;
        set => Set(ref _selectedDevice, value);
    }

    public string SelectedOutputKey
    {
        get => _selectedOutputKey;
        set => Set(ref _selectedOutputKey, value);
    }

    public bool IsRehearsal
    {
        get => _isRehearsal;
        set
        {
            if (!value && NormalOutputGuard?.Invoke() is { } refusal)
            {
                MappingStatus = refusal;
                Raise(nameof(IsRehearsal));
                return;
            }
            if (_isRehearsal == value && _runtime.IsRehearsal == value)
            {
                return;
            }

            var mappingStatusBeforeRequest = MappingStatus;
            _runtime.IsRehearsal = value;
            var effective = _runtime.IsRehearsal;
            if (!Set(ref _isRehearsal, effective) && effective != value)
            {
                // The target control already changed before the two-way binding
                // called this setter. Notify it to snap back to the effective mode.
                Raise(nameof(IsRehearsal));
            }

            var immediateStatus = effective == value
                ? effective
                    ? "Rehearsal Mode is on. Recognition continues; every output is suppressed."
                    : "Normal output is armed only for the deliberately confirmed controller."
                : effective
                    ? "Normal output was refused. Rehearsal Mode remains on because Tappy needs attention; review the status and restart Tappy before rearming."
                    : "Rehearsal Mode could not be enabled. Use Emergency stop and review Tappy's status before continuing.";
            if (string.Equals(MappingStatus, mappingStatusBeforeRequest, StringComparison.Ordinal))
            {
                MappingStatus = immediateStatus;
            }
        }
    }

    public bool CanConfirmController
    {
        get => _canConfirmController;
        private set => Set(ref _canConfirmController, value);
    }

    public bool IsIdentificationCaptureActive
    {
        get => _isIdentificationCaptureActive;
        private set => Set(ref _isIdentificationCaptureActive, value);
    }

    private bool _isWaitingForReconnect;
    private bool _hasActiveDeviceTab;
    public bool HasActiveDeviceTab { get => _hasActiveDeviceTab; private set => Set(ref _hasActiveDeviceTab, value); }
    public bool IsWaitingForReconnect
    {
        get => _isWaitingForReconnect;
        private set => Set(ref _isWaitingForReconnect, value);
    }

    public string IdentificationStatus
    {
        get => _identificationStatus;
        private set => Set(ref _identificationStatus, value);
    }

    public string MappingStatus
    {
        get => _mappingStatus;
        private set => Set(ref _mappingStatus, value);
    }

    public string ActiveControllerLabel
    {
        get => _activeControllerLabel;
        private set => Set(ref _activeControllerLabel, value);
    }

    public string ActiveLayerName
    {
        get => _activeLayerName;
        private set => Set(ref _activeLayerName, value);
    }

    public string SelectedControlLabel => _selectedControl?.Label ?? "None";
    public bool CanAssignSelectedControl => _selectedControl is { CanAssign: true } &&
        _selectedControl.ControlId != TargusKeypadLayout.TripleZeroReference;
    public bool SelectedControlHasAssignment =>
        _selectedControl is { CanAssign: true } &&
        !string.Equals(_selectedControl.Action, "Unassigned", StringComparison.OrdinalIgnoreCase);
    public string AssignmentEditorButtonLabel =>
        SelectedControlHasAssignment ? "Edit assignment…" : "Build an assignment…";
    public bool CanCalibrateSelectedControl => SelectedPhotoHotspotDefinition?.Kind is
        ControllerPhotoControlKind.Fader or ControllerPhotoControlKind.Potentiometer;
    public bool CanTuneSelectedEncoder => SelectedPhotoHotspotDefinition?.Kind ==
                                          ControllerPhotoControlKind.RotaryEncoder;
    public string SelectedAnalogValueText => _selectedControl?.AnalogValueText ??
                                             "Move the physical control to read its position";
    public string SelectedAnalogCalibrationText => _selectedControl?.AnalogCalibrationText ?? string.Empty;
    public string SelectedEncoderCalibrationText => _selectedControl?.EncoderCalibrationText ?? string.Empty;

    public bool HasControllerPhoto => _activeControllerPhoto is not null;

    public string ControllerPhotoName
    {
        get => _controllerPhotoName;
        private set => Set(ref _controllerPhotoName, value);
    }

    public string ControllerPhotoId
    {
        get => _controllerPhotoId;
        private set => Set(ref _controllerPhotoId, value);
    }

    public string ControllerPhotoSource
    {
        get => _controllerPhotoSource;
        private set => Set(ref _controllerPhotoSource, value);
    }

    public double ControllerPhotoWidth
    {
        get => _controllerPhotoWidth;
        private set => Set(ref _controllerPhotoWidth, value);
    }

    public double ControllerPhotoHeight
    {
        get => _controllerPhotoHeight;
        private set => Set(ref _controllerPhotoHeight, value);
    }

    public string PressedSummary
    {
        get => _pressedSummary;
        private set => Set(ref _pressedSummary, value);
    }

    public string EventSummary
    {
        get => _eventSummary;
        private set => Set(ref _eventSummary, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, IncludePersistentStatusWarning(value));
    }

    public string EffectiveSourceLabel
    {
        get => _effectiveSourceLabel;
        private set => Set(ref _effectiveSourceLabel, value);
    }

    public bool IsOutputStateConfirmedSafe => _runtime.IsOutputStateConfirmedSafe;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _runtime.IsRehearsal = true;
        await _runtime.InitializeAsync(cancellationToken).ConfigureAwait(true);
        SynchronizeRuntimeModes();
        SynchronizeRuntimeActivation();
        ReplaceDevices();
    }

    public void RefreshDevices()
    {
        Status = "Refreshing Windows Raw Input controller devices…";
        _runtime.RefreshDevices();
    }

    public void PollRememberedReconnect()
    {
        if (!_isDisposed) { _runtime.PollRememberedReconnect(); }
    }

    public void BeginIdentification()
    {
        if (SelectedDevice is null)
        {
            IdentificationStatus = "Choose a specific device first. Tappy will never choose one automatically.";
            return;
        }

        if (IdentificationGuard?.Invoke(SelectedDevice) is { } refusal)
        {
            IdentificationStatus = refusal;
            return;
        }
        var result = _runtime.BeginIdentification(SelectedDevice);
        IdentificationStatus = result.Message;
        SynchronizeRuntimeModes();
        SynchronizeRuntimeActivation();
        if (result.Succeeded)
        {
            ClearControls();
        }
    }

    public void ConfirmController()
    {
        ClearControls();
        var result = _runtime.ConfirmController();
        IdentificationStatus = result.Message;
        SynchronizeRuntimeModes();
        SynchronizeRuntimeActivation();
        if (!result.Succeeded)
        {
            return;
        }
    }

    public void SelectControl(ControlTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (_selectedControl is not null)
        {
            _selectedControl.IsSelected = false;
        }

        _selectedControl = tile;
        tile.IsSelected = true;
        Raise(nameof(SelectedIsCombination));
        Raise(nameof(SelectedControlLabel));
        Raise(nameof(CanAssignSelectedControl));
        Raise(nameof(SelectedControlHasAssignment));
        Raise(nameof(AssignmentEditorButtonLabel));
        Raise(nameof(CanCalibrateSelectedControl));
        Raise(nameof(CanTuneSelectedEncoder));
        Raise(nameof(SelectedAnalogValueText));
        Raise(nameof(SelectedAnalogCalibrationText));
        Raise(nameof(SelectedEncoderCalibrationText));
    }

    public async Task CaptureSelectedAnalogMinimumAsync(CancellationToken cancellationToken = default) =>
        await CaptureSelectedAnalogPointAsync(AnalogCalibrationPoint.Minimum, cancellationToken).ConfigureAwait(true);

    public async Task CaptureSelectedAnalogCenterAsync(CancellationToken cancellationToken = default) =>
        await CaptureSelectedAnalogPointAsync(AnalogCalibrationPoint.Center, cancellationToken).ConfigureAwait(true);

    public async Task CaptureSelectedAnalogMaximumAsync(CancellationToken cancellationToken = default) =>
        await CaptureSelectedAnalogPointAsync(AnalogCalibrationPoint.Maximum, cancellationToken).ConfigureAwait(true);

    public async Task ResetSelectedAnalogCalibrationAsync(CancellationToken cancellationToken = default)
    {
        var targets = SelectedAnalogVisualTiles();
        if (targets.Count == 0)
        {
            MappingStatus = "Select a photographed fader or potentiometer before resetting its calibration.";
            return;
        }

        foreach (var target in targets)
        {
            target.AnalogRawAtMinimum = 0;
            target.AnalogRawAtMaximum = 127;
            target.AnalogRawAtCenter = null;
        }

        Raise(nameof(SelectedAnalogCalibrationText));
        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
        MappingStatus = "Restored the standard 0 → 127 motion range and saved it for this controller.";
    }

    public async Task TuneSelectedEncoderAsync(double multiplier, CancellationToken cancellationToken = default)
    {
        var targets = SelectedEncoderVisualTiles();
        if (targets.Count == 0 || !double.IsFinite(multiplier) || multiplier <= 0)
        {
            MappingStatus = "Select a photographed endless rotary encoder before changing its response.";
            return;
        }

        foreach (var target in targets)
        {
            target.EncoderDegreesPerStep = Math.Clamp(target.EncoderDegreesPerStep * multiplier, 0.25, 180);
        }

        Raise(nameof(SelectedEncoderCalibrationText));
        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
        MappingStatus = $"Saved rotary response at {_selectedControl!.EncoderDegreesPerStep:0.##}° per input event.";
    }

    public async Task ReverseSelectedEncoderAsync(CancellationToken cancellationToken = default)
    {
        var targets = SelectedEncoderVisualTiles();
        if (targets.Count == 0)
        {
            MappingStatus = "Select a photographed endless rotary encoder before reversing its direction.";
            return;
        }

        var reversed = !targets[0].EncoderReversed;
        foreach (var target in targets)
        {
            target.EncoderReversed = reversed;
        }

        Raise(nameof(SelectedEncoderCalibrationText));
        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
        MappingStatus = $"Saved the rotary encoder's {(reversed ? "reversed" : "normal")} visual direction.";
    }

    public void ResetSelectedEncoderAngle()
    {
        var targets = SelectedEncoderVisualTiles();
        foreach (var target in targets)
        {
            target.ResetEncoderAngle();
        }

        MappingStatus = targets.Count == 0
            ? "Select a photographed endless rotary encoder before resetting its visual line."
            : "Reset the endless encoder's visual reference line to 0°.";
    }

    public ControllerActionAssignment? GetSelectedControllerAction() =>
        _selectedControl is null ? null : _runtime.GetControllerAction(_selectedControl.ControlId);

    public ControllerAssignmentEditContext? BeginSelectedAssignmentEdit()
    {
        var context = _selectedControl is null ? null : _runtime.BeginAssignmentEdit(_selectedControl.ControlId);
        if (context is null)
        {
            MappingStatus = "Confirm the controller and select its control before editing an assignment.";
        }

        return context;
    }

    public async Task ReorderControlsAsync(
        ControlTileViewModel source,
        ControlTileViewModel target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var sourceIndex = Controls.IndexOf(source);
        var targetIndex = Controls.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        Controls.Move(sourceIndex, targetIndex);
        Controls.Move(targetIndex > sourceIndex ? targetIndex - 1 : targetIndex + 1, sourceIndex);
        var order = Controls.Select(tile => tile.ControlId).ToArray();
        var result = _runtime.ReorderControls(order);
        if (!result.Succeeded)
        {
            var currentSourceIndex = Controls.IndexOf(source);
            var currentTargetIndex = Controls.IndexOf(target);
            Controls.Move(currentSourceIndex, currentTargetIndex);
            Controls.Move(currentTargetIndex > currentSourceIndex ? currentTargetIndex - 1 : currentTargetIndex + 1,
                currentSourceIndex);
            MappingStatus = result.Message;
            return;
        }

        try
        {
            var saved = await _runtime.SaveProfileAsync(cancellationToken).ConfigureAwait(true);
            MappingStatus = saved.Succeeded
                ? "Square arrangement saved for this controller."
                : saved.Message;
        }
        catch (Exception exception)
        {
            MappingStatus = $"The squares moved for this session, but the arrangement could not be saved: {exception.Message}";
        }
    }

    public void MoveControl(ControlTileViewModel tile, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (SnapLayoutToGrid)
        {
            x = SnapCoordinate(x, tile, horizontal: true);
            y = SnapCoordinate(y, tile, horizontal: false);
        }

        tile.X = Math.Max(0, x);
        tile.Y = Math.Max(0, y);
        EnsureLayoutSurface(tile.X + tile.TileWidth + 10, tile.Y + tile.TileHeight + 10);
    }

    public IReadOnlyList<ControlTileViewModel> PrepareGroupDrag(ControlTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.IsGroupSelected)
        {
            return Controls.Where(item => item.IsGroupSelected).ToArray();
        }

        // An encoder's three tiles (increase/touch/decrease) are drawn as one
        // concentric cluster sharing one bounding box — dragging any one of
        // them must move all three together, or the cluster would visually
        // fall apart. This doesn't touch IsGroupSelected/the checkbox UI, so
        // it stays invisible plumbing rather than lighting up the
        // group-select toolbar for an ordinary encoder drag.
        if (tile.ClusterId is { } clusterId)
        {
            return Controls.Where(item => item.ClusterId == clusterId).ToArray();
        }

        ClearGroupSelection();
        SetGroupSelection(tile, true);
        return Controls.Where(item => item.IsGroupSelected).ToArray();
    }

    public void MoveControlGroup(
        ControlTileViewModel anchor,
        double proposedX,
        double proposedY,
        IReadOnlyDictionary<string, (double X, double Y)> origins)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(origins);
        if (!origins.TryGetValue(anchor.ControlId, out var anchorOrigin))
        {
            return;
        }

        var movingIds = origins.Keys.ToHashSet(StringComparer.Ordinal);
        if (SnapLayoutToGrid)
        {
            proposedX = SnapCoordinate(proposedX, anchor, horizontal: true, movingIds);
            proposedY = SnapCoordinate(proposedY, anchor, horizontal: false, movingIds);
        }

        var deltaX = proposedX - anchorOrigin.X;
        var deltaY = proposedY - anchorOrigin.Y;
        deltaX = Math.Max(deltaX, -origins.Values.Min(point => point.X));
        deltaY = Math.Max(deltaY, -origins.Values.Min(point => point.Y));
        foreach (var tile in Controls.Where(item => movingIds.Contains(item.ControlId)))
        {
            var origin = origins[tile.ControlId];
            tile.X = origin.X + deltaX;
            tile.Y = origin.Y + deltaY;
        }

        EnsureLayoutSurface(
            Controls.Select(tile => tile.X + tile.TileWidth + 10).DefaultIfEmpty(0).Max(),
            Controls.Select(tile => tile.Y + tile.TileHeight + 10).DefaultIfEmpty(0).Max());
    }

    public void SetGroupSelection(ControlTileViewModel tile, bool selected)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.IsGroupSelected == selected)
        {
            return;
        }

        tile.IsGroupSelected = selected;
        Raise(nameof(GroupSelectedCount));
        Raise(nameof(GroupSelectionLabel));
    }

    public void SelectAllControls()
    {
        foreach (var tile in Controls)
        {
            tile.IsGroupSelected = true;
        }

        Raise(nameof(GroupSelectedCount));
        Raise(nameof(GroupSelectionLabel));
    }

    public void ClearGroupSelection()
    {
        foreach (var tile in Controls)
        {
            tile.IsGroupSelected = false;
        }

        Raise(nameof(GroupSelectedCount));
        Raise(nameof(GroupSelectionLabel));
    }

    public void SelectControlsInArea(
        double left,
        double top,
        double right,
        double bottom,
        IReadOnlySet<string>? preservedControlIds = null)
    {
        foreach (var tile in Controls)
        {
            tile.IsGroupSelected = preservedControlIds?.Contains(tile.ControlId) == true;
        }

        foreach (var tile in Controls.Where(tile =>
                     tile.X < right && tile.X + tile.TileWidth > left &&
                     tile.Y < bottom && tile.Y + tile.TileHeight > top))
        {
            tile.IsGroupSelected = true;
        }

        Raise(nameof(GroupSelectedCount));
        Raise(nameof(GroupSelectionLabel));
    }

    public async Task ApplySelectedTileColorAsync(CancellationToken cancellationToken = default)
    {
        if (!HasSupportedTileColors)
        {
            MappingStatus = "This controller has no verified addressable color palette. Tappy will not offer colors that the hardware cannot reproduce.";
            return;
        }

        if (UsesGlobalTileColor)
        {
            foreach (var tile in Controls)
            {
                ApplyTileColor(tile, SelectedTileColor.Key);
            }

            await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
            MappingStatus = $"Applied {SelectedTileColor.Label} to the complete G13 preview and saved it. The physical G13 has one shared RGB zone; use Sync G13 lighting to send it.";
            return;
        }

        var selectedTargets = Controls.Where(tile => tile.IsGroupSelected).ToArray();
        var targets = selectedTargets.Where(tile => tile.CanUseHardwareColor).ToArray();
        // Only fall back to the single focused control when nothing is checked at
        // all. If the user explicitly checked a group that just turned out to have
        // no addressable members, silently recoloring some unrelated tile that
        // still happens to hold focus is more surprising than doing nothing.
        if (selectedTargets.Length == 0 && _selectedControl is not null)
        {
            targets = _selectedControl.CanUseHardwareColor ? [_selectedControl] : [];
        }

        if (targets.Length == 0)
        {
            MappingStatus = selectedTargets.Length > 0
                ? $"None of the {selectedTargets.Length} selected squares are color-addressable on this controller. Non-lighted and fixed-color controls keep their photographed appearance."
                : "Select a color-addressable controller control. Non-lighted and fixed-color controls keep their photographed appearance.";
            return;
        }

        foreach (var tile in targets)
        {
            ApplyTileColor(tile, SelectedTileColor.Key);
        }

        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
        var skipped = Math.Max(0, selectedTargets.Length - targets.Length);
        MappingStatus = $"Applied {SelectedTileColor.Label} to {targets.Length} color-addressable square{(targets.Length == 1 ? string.Empty : "s")} and saved the controller layout." +
                        (skipped > 0 ? $" Kept {skipped} non-addressable control{(skipped == 1 ? string.Empty : "s")} unchanged." : string.Empty);
    }

    /// <summary>
    /// Bulk-sets TileWidth/TileHeight for every checked (group-selected)
    /// tile, falling back to the single focused control when nothing is
    /// checked — the same selection-resolution rule
    /// ApplySelectedTileColorAsync already establishes for "Color
    /// selected." Excludes any encoder cluster member
    /// (ControlTileViewModel.IsEncoderClusterMember): resizing one third of
    /// a concentric ring cluster independently would desync the shared
    /// bounding box the three parts depend on, the same reasoning D-038
    /// used to hide their per-tile resize Thumb.
    /// </summary>
    public async Task ResizeGroupSelectionAsync(
        double width, double height, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height))
        {
            return;
        }

        var clampedWidth = Math.Clamp(width, MinimumTileSize, 600);
        var clampedHeight = Math.Clamp(height, MinimumTileSize, 400);

        var selectedTargets = Controls.Where(tile => tile.IsGroupSelected).ToArray();
        var targets = selectedTargets.Where(tile => !tile.IsEncoderClusterMember).ToArray();
        if (selectedTargets.Length == 0 && _selectedControl is not null)
        {
            targets = _selectedControl.IsEncoderClusterMember ? [] : [_selectedControl];
        }

        if (targets.Length == 0)
        {
            MappingStatus = selectedTargets.Length > 0
                ? $"None of the {selectedTargets.Length} selected squares can be resized independently — an encoder's ring cluster shares one size with its other parts."
                : "Select one or more squares to resize.";
            return;
        }

        foreach (var tile in targets)
        {
            tile.TileWidth = clampedWidth;
            tile.TileHeight = clampedHeight;
            EnsureLayoutSurface(tile.X + tile.TileWidth + 10, tile.Y + tile.TileHeight + 10);
        }

        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
        var skipped = Math.Max(0, selectedTargets.Length - targets.Length);
        MappingStatus = $"Resized {targets.Length} square{(targets.Length == 1 ? string.Empty : "s")} to {clampedWidth:0}×{clampedHeight:0} and saved the controller layout." +
                        (skipped > 0 ? $" Kept {skipped} encoder ring part{(skipped == 1 ? string.Empty : "s")} unchanged." : string.Empty);
    }

    public void SyncTileColorsToHardware()
    {
        if (!HasSupportedTileColors)
        {
            MappingStatus = "This controller has no verified addressable lighting protocol, so Tappy sent nothing.";
            return;
        }

        IReadOnlyDictionary<string, string> colors;
        if (UsesGlobalTileColor)
        {
            foreach (var tile in Controls)
            {
                ApplyTileColor(tile, SelectedTileColor.Key);
            }

            colors = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["whole-device"] = SelectedTileColor.Key,
            };
        }
        else
        {
            colors = Controls.Where(tile => tile.CanUseHardwareColor)
                .ToDictionary(tile => tile.ControlId, tile => tile.ColorKey, StringComparer.Ordinal);
        }

        var result = _runtime.SyncControllerLedColors(colors);
        MappingStatus = result.Message;
    }

    /// <summary>
    /// Sends a fixed diagnostic pattern to a confirmed Fire's OLED — the very
    /// first real use of this SysEx sub-command against hardware. Deliberately
    /// not a general drawing API yet; see DeviceAwareControllerRuntime.
    /// SendFireOledDiagnosticPattern and docs/AKAI_FIRE.md.
    /// </summary>
    public void SendOledTestPattern()
    {
        if (!HasOledDisplay)
        {
            MappingStatus = "This controller has no Tappy-supported OLED display.";
            return;
        }

        var result = _runtime.SendFireOledDiagnosticPattern();
        MappingStatus = result.Message;
    }

    public void ResizeControl(ControlTileViewModel tile, double horizontalChange, double verticalChange)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var width = Math.Clamp(tile.TileWidth + horizontalChange, MinimumTileSize, 600);
        var height = Math.Clamp(tile.TileHeight + verticalChange, MinimumTileSize, 400);
        if (SnapLayoutToGrid)
        {
            width = Math.Max(MinimumTileSize, Math.Round(width / SnapStep) * SnapStep);
            height = Math.Max(MinimumTileSize, Math.Round(height / SnapStep) * SnapStep);
        }

        tile.TileWidth = width;
        tile.TileHeight = height;
        EnsureLayoutSurface(tile.X + tile.TileWidth + 10, tile.Y + tile.TileHeight + 10);
    }

    public void EnsureLayoutSurface(double width, double height)
    {
        if (double.IsFinite(width))
        {
            LayoutSurfaceWidth = Math.Max(LayoutSurfaceWidth, Math.Max(LayoutColumns * LayoutCellWidth, width));
        }

        if (double.IsFinite(height))
        {
            LayoutSurfaceHeight = Math.Max(LayoutSurfaceHeight, Math.Max(LayoutRows * LayoutCellHeight, height));
        }
    }

    /// <summary>
    /// The tight bounding box of the currently placed tiles — deliberately
    /// NOT LayoutSurfaceWidth/Height, which is a grow-only high-water mark
    /// (see EnsureLayoutSurface) that also grows to match the viewport's own
    /// size on every resize (ControllerLayoutViewport_OnSizeChanged) and
    /// never shrinks back down from ordinary dragging. Fitting/filling the
    /// viewport to that stale, often-viewport-sized number was a near
    /// no-op regardless of how compact the actual arrangement was; this is
    /// what Fit/Fill to view should measure against instead.
    /// </summary>
    public (double Width, double Height) GetLayoutContentBounds() =>
        Controls.Count == 0
            ? (LayoutSurfaceWidth, LayoutSurfaceHeight)
            : (Controls.Max(tile => tile.X + tile.TileWidth) + 10, Controls.Max(tile => tile.Y + tile.TileHeight) + 10);

    public async Task ArrangeLayoutAsGridAsync(CancellationToken cancellationToken = default)
    {
        LayoutRows = Math.Max(LayoutRows, (int)Math.Ceiling(Controls.Count / (double)LayoutColumns));
        for (var index = 0; index < Controls.Count; index++)
        {
            var tile = Controls[index];
            tile.X = (index % LayoutColumns) * LayoutCellWidth;
            tile.Y = (index / LayoutColumns) * LayoutCellHeight;
            var natural = NaturalTileSize(tile);
            tile.TileWidth = natural.Width;
            tile.TileHeight = natural.Height;
        }

        RecalculateLayoutSurface();
        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
    }

    // Pushes the viewmodel's current layout state (tile placements, Zoom,
    // PhotoPaneWidth, ...) into the runtime's in-memory layout only — no disk
    // write. This must happen synchronously and immediately whenever any of that
    // state changes, not just when SaveControllerLayoutAsync eventually runs:
    // Runtime_OnStateChanged fires on essentially any runtime event (a control
    // press, a bank change, ...) and re-reads GetControllerLayout() to resync this
    // viewmodel. If Zoom/PhotoPaneWidth were only pushed into the runtime at the
    // end of a debounce/await, any such event in that window would stomp the
    // viewmodel's newer value back to the runtime's still-stale one.
    private RuntimeOperation? ApplyControllerLayoutToRuntime()
    {
        var current = _runtime.GetControllerLayout();
        if (current is null)
        {
            return null;
        }

        var runtimeControls = current.Controls.ToDictionary(control => control.ControlId, StringComparer.Ordinal);
        var placements = Controls
            .Where(tile => runtimeControls.ContainsKey(tile.ControlId))
            .Select(tile =>
            {
                var source = runtimeControls[tile.ControlId];
                var naturalSize = NaturalTileSize(tile);
                return new ControllerControlPlacement(
                    tile.ControlId,
                    source.NaturalRow,
                    source.NaturalColumn,
                    tile.X,
                    tile.Y,
                    tile.TileWidth / naturalSize.Width,
                    tile.TileHeight / naturalSize.Height,
                    tile.ColorKey,
                    tile.AnalogRawAtMinimum,
                    tile.AnalogRawAtMaximum,
                    tile.AnalogRawAtCenter,
                    tile.EncoderDegreesPerStep,
                    tile.EncoderReversed);
            })
            .ToArray();
        return _runtime.UpdateControllerLayout(new ControllerLayoutWorkspace(
            LayoutColumns,
            LayoutRows,
            SnapLayoutToGrid,
            placements,
            Zoom,
            PhotoPaneWidth,
            IsLayoutLocked));
    }

    // Call immediately (synchronously, same call as the property change) whenever
    // Zoom or PhotoPaneWidth changes outside of a full SaveControllerLayoutAsync,
    // e.g. on every zoom-wheel tick, before any debounced disk save runs. See
    // ApplyControllerLayoutToRuntime for why this cannot wait.
    public void SyncLayoutPresentationToRuntime() => ApplyControllerLayoutToRuntime();

    public async Task SaveControllerLayoutAsync(CancellationToken cancellationToken = default)
    {
        var result = ApplyControllerLayoutToRuntime();
        if (result is null)
        {
            MappingStatus = "Confirm a controller before saving its layout.";
            return;
        }

        if (!result.Succeeded)
        {
            MappingStatus = result.Message;
            return;
        }

        try
        {
            var saved = await _runtime.SaveProfileAsync(cancellationToken).ConfigureAwait(true);
            MappingStatus = saved.Succeeded
                ? "Freeform square positions and sizes saved for this controller."
                : saved.Message;
        }
        catch (Exception exception)
        {
            MappingStatus = $"The layout changed for this session, but could not be saved: {exception.Message}";
        }
    }

    private double SnapCoordinate(
        double proposed,
        ControlTileViewModel moving,
        bool horizontal,
        IReadOnlySet<string>? excludedControlIds = null)
    {
        var snapped = Math.Round(proposed / SnapStep) * SnapStep;
        var movingSize = horizontal ? moving.TileWidth : moving.TileHeight;
        var candidates = Controls
            .Where(tile => !ReferenceEquals(tile, moving) &&
                           (excludedControlIds is null || !excludedControlIds.Contains(tile.ControlId)))
            .SelectMany(tile =>
            {
                var start = horizontal ? tile.X : tile.Y;
                var size = horizontal ? tile.TileWidth : tile.TileHeight;
                return new[]
                {
                    start,
                    start + size + 10,
                    start - movingSize - 10,
                    start + size - movingSize,
                };
            })
            .Select(candidate => new { Value = candidate, Distance = Math.Abs(candidate - proposed) })
            .Where(candidate => candidate.Distance <= NeighborSnapDistance)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        return candidates?.Value ?? snapped;
    }

    public void AssignMapping()
    {
        if (_selectedControl is null)
        {
            MappingStatus = "Press a confirmed controller control or click a control tile first.";
            return;
        }

        var result = _runtime.AssignMapping(_selectedControl.ControlId, SelectedOutputKey);
        MappingStatus = result.Message;
        if (result.Succeeded)
        {
            _selectedControl.Action = $"Hold {SelectedOutputKey} until release";
            Raise(nameof(SelectedControlHasAssignment));
            Raise(nameof(AssignmentEditorButtonLabel));
        }
    }

    public void AssignKeyboardMapping(KeyboardMappingAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (_selectedControl is null)
        {
            MappingStatus = "Press a confirmed controller control or click a control tile first.";
            return;
        }

        var result = _runtime.AssignKeyboardMapping(_selectedControl.ControlId, assignment);
        MappingStatus = result.Message;
        if (result.Succeeded)
        {
            _selectedControl.Action = assignment.Name;
            Raise(nameof(SelectedControlHasAssignment));
            Raise(nameof(AssignmentEditorButtonLabel));
        }
    }

    public void AssignControllerAction(ControllerActionAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (_selectedControl is null)
        {
            MappingStatus = "Press a confirmed controller control or click a control tile first.";
            return;
        }

        var result = _runtime.AssignControllerAction(_selectedControl.ControlId, assignment);
        MappingStatus = result.Message;
        if (result.Succeeded)
        {
            _selectedControl.Action = assignment.Name;
            Raise(nameof(SelectedControlHasAssignment));
            Raise(nameof(AssignmentEditorButtonLabel));
        }
    }

    public async Task SaveProfileAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runtime.SaveProfileAsync(cancellationToken).ConfigureAwait(true);
        MappingStatus = result.Message;
    }

    public RuntimeOperation AssignKeyboardMapping(
        ControllerAssignmentTarget target,
        KeyboardMappingAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(assignment);
        var result = _runtime.AssignKeyboardMapping(target, assignment);
        CompleteTargetedAssignment(target, assignment.Name, result);
        return result;
    }

    public RuntimeOperation AssignControllerAction(
        ControllerAssignmentTarget target,
        ControllerActionAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(assignment);
        var result = _runtime.AssignControllerAction(target, assignment);
        CompleteTargetedAssignment(target, assignment.Name, result);
        return result;
    }

    private void CompleteTargetedAssignment(
        ControllerAssignmentTarget target,
        string name,
        RuntimeOperation result)
    {
        MappingStatus = result.Message;
        var visualId = target.ControlId;
        if (result.Succeeded && _tiles.TryGetValue(visualId, out var tile))
        {
            tile.Action = name;
            Raise(nameof(SelectedControlHasAssignment));
            Raise(nameof(AssignmentEditorButtonLabel));
        }
    }

    // Windows shutdown synchronously waits for persistence on the dispatcher.
    // Return the runtime's context-free task without any UI continuation.
    internal Task<RuntimeOperation> SaveProfileForShutdownAsync(CancellationToken cancellationToken = default) =>
        _runtime.SaveProfileAsync(cancellationToken);

    public RuntimeOperation EmergencyStop(string reason)
    {
        var result = _runtime.EmergencyStop(reason);
        SynchronizeRuntimeModes();
        SynchronizeRuntimeActivation();
        Status = result.Message;
        ClearControls();
        return result;
    }

    public void ReportStatus(string message)
    {
        Status = string.IsNullOrWhiteSpace(message) ? "Tappy is ready." : message.Trim();
    }

    public void ReportPersistentStatusWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _persistentStatusWarning = message.Trim();
        Status = _status;
    }

    private void Runtime_OnDevicesChanged(object? sender, EventArgs e) => _onUi(ReplaceDevices);

    private void ReplaceDevices()
    {
        var selectedSession = SelectedDevice?.SessionId;
        var selectedProvider = SelectedDevice?.ProviderId;
        Devices.Clear();
        foreach (var device in _runtime.Devices)
        {
            var name = TargusKeypadLayout.Matches(device.ProviderId, device.VendorId, device.ProductId)
                ? "Targus PAUK10U number pad" : device.DisplayName;
            var sameModelCount = _runtime.Devices.Count(other => other.ProviderId == device.ProviderId &&
                other.VendorId == device.VendorId && other.ProductId == device.ProductId && other.DisplayName == device.DisplayName);
            if (sameModelCount > 1)
            {
                name += $" · ID {device.DeviceTag}";
            }
            if (device.IsProtectedForTyping) { name += " · protected typing"; }
            Devices.Add(device with { DisplayName = name });
        }

        SelectedDevice = selectedSession is null
            ? null
            : Devices.FirstOrDefault(device =>
                device.SessionId == selectedSession &&
                device.ProviderId == selectedProvider);
        Status = Devices.Count == 0
            ? "No supported Raw Input controller devices are currently available."
            : $"{Devices.Count} controller device(s) available. None is selected automatically.";
    }

    private void Runtime_OnControlChanged(object? sender, RuntimeControlUpdate update)
    {
        if (_isDisposed)
        {
            return;
        }

        _pendingVisuals.Enqueue(update);
        if (Interlocked.Exchange(ref _visualFlushScheduled, 1) == 0)
        {
            _onUi(FlushVisuals);
        }
    }

    private void FlushVisuals()
    {
        try
        {
            var batch = _pendingVisuals.Drain();
            foreach (var update in batch.Updates)
            {
                ApplyControlUpdate(update);
            }

            if (batch.WasCompacted && batch.Updates.Count > 0)
            {
                EventSummary += "; visual backlog compacted with final states preserved";
            }
        }
        finally
        {
            Interlocked.Exchange(ref _visualFlushScheduled, 0);
            if (_pendingVisuals.HasPending && Interlocked.Exchange(ref _visualFlushScheduled, 1) == 0)
            {
                _onUi(FlushVisuals);
            }
        }
    }

    private void ApplyControlUpdate(RuntimeControlUpdate update)
    {
        if (!_tiles.TryGetValue(update.ControlId, out var tile))
        {
            // The runtime may have just appended a learned report with an explicit
            // collision-free position. Refresh before creating its tile instead of
            // letting a stale cache fall back to collection-index coordinates.
            if (_runtime.GetControllerLayout() is { } workspace)
            {
                foreach (var placement in workspace.Controls)
                { _layoutPlacements[placement.ControlId] = placement; }
            }
            var index = Controls.Count;
            tile = new ControlTileViewModel
            {
                ControlId = update.ControlId,
                Label = update.DisplayLabel,
                Action = update.AssignedAction,
                X = (index % LayoutColumns) * LayoutCellWidth,
                Y = (index / LayoutColumns) * LayoutCellHeight,
            };
            _tiles.Add(update.ControlId, tile);
            Controls.Add(tile);
            SynchronizeTileColorCapability(tile);
            ApplyControlPlacement(tile);
            RecalculateLayoutSurface();
        }

        EnsurePhotoHotspot(tile);
        ApplyAnalogControlUpdate(tile, update);

        tile.IsPressed = update.IsPressed;
        tile.CanAssign = update.CanAssign;
        UpdateIllumination(tile, update);
        tile.Action = update.AssignedAction;
        if (ReferenceEquals(tile, _selectedControl))
        {
            Raise(nameof(SelectedControlHasAssignment));
            Raise(nameof(AssignmentEditorButtonLabel));
            Raise(nameof(CanAssignSelectedControl));
            Raise(nameof(SelectedAnalogValueText));
            Raise(nameof(SelectedAnalogCalibrationText));
            Raise(nameof(SelectedEncoderCalibrationText));
        }
        if (update.IsPressed && !update.IsRepeat && !update.IsSnapshot)
        {
            SelectControl(tile);
        }

        var down = Controls.Where(item => item.IsPressed).Select(item => item.Label).ToArray();
        PressedSummary = down.Length == 0 ? "None" : string.Join(", ", down);
        if (!update.IsSnapshot)
        {
            EventSummary = $"Aggregate selected-device events: {update.AggregateEventCount}; simultaneous: {update.SimultaneousCount}; last: {(update.IsRepeat ? "repeat" : update.IsPressed ? "press" : "release")}";
        }
    }

    private void UpdateIllumination(ControlTileViewModel tile, RuntimeControlUpdate update)
    {
        if (update.IsPressed)
        {
            if (!update.IsRepeat || !_illuminationStates.ContainsKey(update.ControlId))
            {
                _illuminationStates[update.ControlId] = new IlluminationState(
                    ++_illuminationGeneration,
                    Environment.TickCount64);
            }

            tile.IsIlluminated = true;
            return;
        }

        if (!_illuminationStates.TryGetValue(update.ControlId, out var state))
        {
            tile.IsIlluminated = false;
            return;
        }

        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - state.StartedAtMilliseconds));
        var remaining = MinimumIlluminationDuration - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            _illuminationStates.Remove(update.ControlId);
            tile.IsIlluminated = false;
            return;
        }

        _onUiAfter(remaining, () => ClearIllumination(update.ControlId, state.Generation));
    }

    private void ClearIllumination(string controlId, long generation)
    {
        if (_isDisposed ||
            !_illuminationStates.TryGetValue(controlId, out var state) ||
            state.Generation != generation ||
            !_tiles.TryGetValue(controlId, out var tile) ||
            tile.IsPressed)
        {
            return;
        }

        _illuminationStates.Remove(controlId);
        tile.IsIlluminated = false;
    }

    private void Runtime_OnStateChanged(object? sender, RuntimeState state) => _onUi(() =>
    {
        SynchronizeRuntimeModes();
        CanConfirmController = state.CanConfirm;
        IsIdentificationCaptureActive = state.IsIdentificationCaptureActive;
        IdentificationStatus = state.IdentificationStatus;
        ActiveControllerLabel = state.ActiveControllerLabel;
        ActiveLayerName = state.ActiveLayerName;
        _bankEndpoint = _runtime.GetBankEndpoint();
        Raise(nameof(BankChoices)); Raise(nameof(SelectedBankId)); Raise(nameof(CanChangeBank));
        _tartarusKeymap = state.TartarusKeymap;
        _tartarusVerifiedLedMask = state.TartarusVerifiedLedMask;
        _tartarusLedError = null;
        _canChangeTartarusKeymap = state.IsConfirmed && HasTartarusKeymaps;
        if (!_canChangeTartarusKeymap) { TartarusFollowKeymaps = false; }
        Raise(nameof(HasTartarusKeymaps)); Raise(nameof(CanChangeTartarusKeymap)); Raise(nameof(SelectedTartarusKeymap));
        Raise(nameof(TartarusYellowLed)); Raise(nameof(TartarusGreenLed)); Raise(nameof(TartarusBlueLed));
        Raise(nameof(TartarusIndicatorStatus));
        InputBankLabel = state.InputBankLabel ?? string.Empty;
        _numLockOn = state.NumLockOn;
        Raise(nameof(HasInputBank));
        Raise(nameof(NumLockLedBrush));
        Raise(nameof(NumLockLedDescription));
        MappingStatus = state.MappingStatus;
        Status = state.Status;
        EffectiveSourceLabel = state.EffectiveSourceLabel;
        var hasPresentation = state.IsConfirmed || state.IsRememberedReconnect || state.IsLifecyclePaused;
        SynchronizeTileColorChoices(hasPresentation);
        SynchronizeControllerPhoto(state);
        SynchronizeControllerLayout(hasPresentation);
        if (state.IsLifecyclePaused && state.RetainedControls is { } retainedControls)
        {
            // A queued lock state may have cleared coalesced visuals before this
            // sleep state reaches the UI. Its own released snapshot restores them.
            foreach (var update in retainedControls) { ApplyControlUpdate(update); }
        }
        if (!hasPresentation && !state.IsIdentificationCaptureActive)
        {
            // Disarming clears the live presentation, not the saved controller profile.
            ClearControls();
        }
        // Publish visibility only after the disconnected presentation is cleared.
        IsWaitingForReconnect = state.IsWaitingForReconnect;
        HasActiveDeviceTab = hasPresentation || state.IsIdentificationCaptureActive;
    });

    private void SynchronizeRuntimeModes()
    {
        var rehearsal = _runtime.IsRehearsal;
        if (_isRehearsal != rehearsal)
        {
            _isRehearsal = rehearsal;
            Raise(nameof(IsRehearsal));
        }
    }

    private void SynchronizeRuntimeActivation()
    {
        CanConfirmController = _runtime.CanConfirmController;
        IsIdentificationCaptureActive = _runtime.IsIdentificationCaptureActive;
    }

    private void SynchronizeControllerPhoto(RuntimeState state)
    {
        var next = state.IsConfirmed || state.IsRememberedReconnect || state.IsLifecyclePaused
            ? ControllerPhotoCatalog.Find(
                state.ActiveControllerProviderId,
                state.ActiveControllerVendorId,
                state.ActiveControllerProductId,
                state.ActiveControllerLabel,
                state.ActiveControllerPresentationModelId)
            : null;
        if (string.Equals(_activeControllerPhoto?.Id, next?.Id, StringComparison.Ordinal))
        {
            return;
        }

        _activeControllerPhoto = next;
        ControllerPhotoId = next?.Id ?? string.Empty;
        ControllerPhotoName = next?.AccessibleName ?? string.Empty;
        ControllerPhotoSource = next?.AssetUri ?? string.Empty;
        ControllerPhotoWidth = next?.Width ?? 1;
        ControllerPhotoHeight = next?.Height ?? 1;
        foreach (var hotspot in _photoHotspotsByVisualId.Values)
        {
            hotspot.Dispose();
        }
        ControllerPhotoHotspots.Clear();
        _photoHotspotsByVisualId.Clear();
        Raise(nameof(HasControllerPhoto));
        Raise(nameof(CanCalibrateSelectedControl));
        Raise(nameof(CanTuneSelectedEncoder));
        foreach (var tile in Controls)
        {
            EnsurePhotoHotspot(tile);
        }
    }

    private void SynchronizeControllerLayout(bool isConfirmed)
    {
        var workspace = isConfirmed ? _runtime.GetControllerLayout() : null;
        _layoutPlacements.Clear();
        if (workspace is null)
        {
            return;
        }

        LayoutColumns = workspace.GridColumns;
        LayoutRows = workspace.GridRows;
        SnapLayoutToGrid = workspace.SnapToGrid;
        Zoom = workspace.Zoom;
        PhotoPaneWidth = workspace.PhotoPaneWidth;
        IsLayoutLocked = workspace.IsLayoutLocked;
        foreach (var placement in workspace.Controls)
        {
            _layoutPlacements[placement.ControlId] = placement;
        }

        if (UsesGlobalTileColor)
        {
            var savedGlobalColor = workspace.Controls
                .Select(placement => ResolveSupportedColorKey(placement.ColorKey))
                .FirstOrDefault(color => !string.Equals(color, "Default", StringComparison.OrdinalIgnoreCase)) ??
                "Default";
            SelectedTileColor = TileColorChoices.First(choice =>
                string.Equals(choice.Key, savedGlobalColor, StringComparison.OrdinalIgnoreCase));
        }

        var adjustedColorCount = 0;
        foreach (var tile in Controls)
        {
            ApplyControlPlacement(tile);
            if (_layoutPlacements.TryGetValue(tile.ControlId, out var placement) &&
                !string.Equals(placement.ColorKey, tile.ColorKey, StringComparison.OrdinalIgnoreCase))
            {
                adjustedColorCount++;
            }
        }

        RecalculateLayoutSurface();
        if (adjustedColorCount > 0 && _controllerLedCapability is not null)
        {
            MappingStatus = $"Adjusted {adjustedColorCount} earlier square color{(adjustedColorCount == 1 ? string.Empty : "s")} to the {_controllerLedCapability.DisplayName} hardware palette. Save the profile to keep the migration.";
        }
    }

    private void ApplyControlPlacement(ControlTileViewModel tile)
    {
        if (!_layoutPlacements.TryGetValue(tile.ControlId, out var placement))
        {
            // No saved placement yet (a brand-new control) — start at this
            // control's own correctly-shaped size rather than the plain
            // rectangular default.
            var (naturalWidth, naturalHeight) = NaturalTileSize(tile);
            tile.TileWidth = naturalWidth;
            tile.TileHeight = naturalHeight;
            return;
        }

        tile.X = placement.X ?? placement.NaturalColumn * LayoutCellWidth;
        tile.Y = placement.Y ?? placement.NaturalRow * LayoutCellHeight;
        var natural = NaturalTileSize(tile);
        tile.TileWidth = Math.Clamp(placement.Width * natural.Width, MinimumTileSize, 600);
        tile.TileHeight = Math.Clamp(placement.Height * natural.Height, MinimumTileSize, 400);
        tile.AnalogRawAtMinimum = placement.AnalogRawAtMinimum;
        tile.AnalogRawAtMaximum = placement.AnalogRawAtMaximum;
        tile.AnalogRawAtCenter = placement.AnalogRawAtCenter;
        tile.EncoderDegreesPerStep = placement.EncoderDegreesPerStep;
        tile.EncoderReversed = placement.EncoderReversed;
        ApplyTileColor(tile, UsesGlobalTileColor ? SelectedTileColor.Key : placement.ColorKey);
    }

    /// <summary>
    /// Live preview for an in-progress LED animation tick — reuses the same
    /// per-tile color application every one-off apply already uses, but
    /// deliberately never calls SaveControllerLayoutAsync. An animation can tick
    /// up to ~15 times a second; treating every tick as a profile edit would turn
    /// playback into a stream of disk writes.
    /// </summary>
    internal void ApplyAnimationPreviewColors(IReadOnlyDictionary<string, string> colorsByControlId)
    {
        foreach (var tile in Controls)
        {
            if (colorsByControlId.TryGetValue(tile.ControlId, out var color))
            {
                ApplyTileColor(tile, color);
            }
        }
    }

    private void ApplyTileColor(ControlTileViewModel tile, string? colorKey)
    {
        if (!tile.CanUseHardwareColor)
        {
            colorKey = "Default";
        }

        var supportedKey = ResolveSupportedColorKey(colorKey);
        var choice = IsHexColor(supportedKey)
            ? BuildCustomColorChoice(supportedKey)
            : TileColorChoices.FirstOrDefault(item =>
                  string.Equals(item.Key, supportedKey, StringComparison.OrdinalIgnoreCase)) ??
              TileColorChoices[0];
        tile.ColorKey = choice.Key;
        tile.TileBackground = choice.Background;
        tile.TileForeground = choice.Foreground;
    }

    private void SynchronizeTileColorChoices(bool isConfirmed)
    {
        var previousModelId = _controllerLedCapability?.ModelId;
        var previousCustomSelection = HasFreeformColorSupport && IsHexColor(_selectedTileColor.Key)
            ? _selectedTileColor.Key
            : null;
        _controllerLedCapability = isConfirmed ? _runtime.GetControllerLedColorCapability() : null;
        var supportedKeys = HasSupportedTileColors
            ? _controllerLedCapability!.SupportedColorKeys
            : ["Default"];
        TileColorChoices.Clear();
        foreach (var key in supportedKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var choice = AllTileColorChoices.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (choice is not null)
            {
                TileColorChoices.Add(choice);
            }
        }

        if (TileColorChoices.Count == 0)
        {
            TileColorChoices.Add(AllTileColorChoices[0]);
        }

        // A custom color the user just picked but has not yet applied/synced must
        // survive an unrelated runtime event resyncing this list — otherwise it is
        // the same "stomping" bug class as the per-tab zoom fix (see D-019):
        // background noise would silently discard an armed, unapplied selection.
        if (previousCustomSelection is not null && HasFreeformColorSupport &&
            _controllerLedCapability?.ModelId == previousModelId)
        {
            var restored = BuildCustomColorChoice(previousCustomSelection);
            TileColorChoices.Add(restored);
            SelectedTileColor = restored;
        }
        else
        {
            SelectedTileColor = TileColorChoices[0];
        }

        foreach (var tile in Controls)
        {
            SynchronizeTileColorCapability(tile);
        }
        Raise(nameof(HasSupportedTileColors));
        Raise(nameof(UsesGlobalTileColor));
        Raise(nameof(HasFreeformColorSupport));
        Raise(nameof(HasAnimatableLighting));
        Raise(nameof(HasOledDisplay));
        Raise(nameof(TileColorApplyButtonLabel));
        Raise(nameof(TileColorSyncButtonLabel));
        Raise(nameof(TileColorSyncToolTip));
        Raise(nameof(TileColorAvailabilityLabel));
    }

    /// <summary>
    /// Applies an arbitrary "#RRGGBB" color picked in the custom color dialog.
    /// Only the Akai Fire currently advertises freeform color support; other
    /// controllers keep their fixed, physically-verified named palette.
    /// </summary>
    public void SelectCustomColor(string hex)
    {
        if (!HasFreeformColorSupport || !IsHexColor(hex))
        {
            return;
        }

        var choice = BuildCustomColorChoice(hex);
        var existingIndex = -1;
        for (var i = 0; i < TileColorChoices.Count; i++)
        {
            if (IsHexColor(TileColorChoices[i].Key)) { existingIndex = i; break; }
        }

        if (existingIndex >= 0) { TileColorChoices[existingIndex] = choice; }
        else { TileColorChoices.Add(choice); }
        SelectedTileColor = choice;
    }

    internal static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    internal static bool TryParseHexColor(string? value, out byte red, out byte green, out byte blue)
    {
        red = green = blue = 0;
        if (!IsHexColor(value))
        {
            return false;
        }

        red = Convert.ToByte(value!.Substring(1, 2), 16);
        green = Convert.ToByte(value.Substring(3, 2), 16);
        blue = Convert.ToByte(value.Substring(5, 2), 16);
        return true;
    }

    internal static TileColorChoice BuildCustomColorChoice(string hex)
    {
        TryParseHexColor(hex, out var red, out var green, out var blue);
        var luminance = (0.299 * red + 0.587 * green + 0.114 * blue) / 255.0;
        var foreground = luminance > 0.55 ? "#171B22" : "#F7FAFC";
        return new TileColorChoice(hex, hex, hex, foreground);
    }

    private void SynchronizeTileColorCapability(ControlTileViewModel tile)
    {
        tile.CanUseHardwareColor = _controllerLedCapability?.Topology switch
        {
            ControllerLightingTopology.Global => true,
            ControllerLightingTopology.PerControl =>
                _controllerLedCapability.ColorAddressableControlIds.Contains(tile.ControlId),
            _ => false,
        };
    }

    private string ResolveSupportedColorKey(string? colorKey)
    {
        var requested = string.IsNullOrWhiteSpace(colorKey) ? "Default" : colorKey.Trim();
        if (HasFreeformColorSupport && IsHexColor(requested))
        {
            return requested;
        }

        if (TileColorChoices.Any(choice => string.Equals(choice.Key, requested, StringComparison.OrdinalIgnoreCase)))
        {
            return requested;
        }

        if (_controllerLedCapability?.ModelId == MidiControllerModelCatalog.AkaiApcMiniV1Id)
        {
            return requested.ToLowerInvariant() switch
            {
                "teal" => "Green",
                "pink" => "Red",
                "orange" or "yellow" or "white" => "Amber",
                _ => "Default",
            };
        }

        return "Default";
    }

    private void RecalculateLayoutSurface()
    {
        LayoutSurfaceWidth = LayoutColumns * LayoutCellWidth;
        LayoutSurfaceHeight = LayoutRows * LayoutCellHeight;
        EnsureLayoutSurface(
            Controls.Select(tile => tile.X + tile.TileWidth + 10).DefaultIfEmpty(0).Max(),
            Controls.Select(tile => tile.Y + tile.TileHeight + 10).DefaultIfEmpty(0).Max());
    }

    /// <summary>
    /// The tile size a control gets before any user resizing — the real
    /// measured photo hotspot's width/height scaled up by
    /// PhotoShapeTileScale, or the plain DefaultTileWidth/Height for a
    /// control with no measured photo data. Saved placements store a
    /// multiplier against this (see ApplyControlPlacement/
    /// ApplyControllerLayoutToRuntime), so an untouched tile's saved 1.0x
    /// automatically follows this shape, and a tile the owner explicitly
    /// resized to e.g. 1.5x stays 1.5x its own correct shape.
    /// Scoped to grid-style LED controllers (HasAnimatableLighting — Fire/APC
    /// MINI) only: Targus, the Microsoft Number Pad, and other keyboard-class
    /// devices also carry real measured photo hotspots (tightly packed key
    /// geometry, not a spaced-out pad grid), so applying this to them would
    /// scale their tiles into overlapping each other.
    /// </summary>
    private (double Width, double Height) NaturalTileSize(ControlTileViewModel tile)
    {
        if (!HasAnimatableLighting || _activeControllerPhoto is null ||
            !_activeControllerPhoto.Hotspots.TryGetValue(tile.ControlId, out var definition))
        {
            return (DefaultTileWidth, DefaultTileHeight);
        }

        var scale = TryGetEncoderClusterRole(definition) is null ? PhotoShapeTileScale : EncoderClusterTileScale;
        return (definition.Width * scale, definition.Height * scale);
    }

    /// <summary>
    /// Which of a rotary encoder's three overlapping tiles this hotspot is,
    /// or null for an ordinary (non-encoder) control. Reuses
    /// ControllerPhotoCatalog's existing VisualId aliasing — the
    /// clockwise/increase hotspot is the canonical entry (no VisualId, so
    /// EffectiveVisualId == its own ControlId, Kind == RotaryEncoder); the
    /// counter-clockwise/decrease hotspot (Kind == RotaryEncoder) and the
    /// touch hotspot (Kind == Button) both point their VisualId back at it —
    /// no new authoring, just reading the shape already measured for the
    /// photo locator. Deliberately independent of tile.ClusterId/RingRole
    /// (which EnsurePhotoHotspot sets from this same result) so sizing works
    /// correctly even when called before EnsurePhotoHotspot has run for a
    /// brand-new tile.
    /// </summary>
    private EncoderRingRole? TryGetEncoderClusterRole(ControllerPhotoHotspotDefinition definition)
    {
        if (_activeControllerPhoto is null ||
            !_activeControllerPhoto.Hotspots.TryGetValue(definition.EffectiveVisualId, out var canonical) ||
            canonical.Kind != ControllerPhotoControlKind.RotaryEncoder)
        {
            return null;
        }

        if (string.Equals(definition.ControlId, definition.EffectiveVisualId, StringComparison.Ordinal))
        {
            return EncoderRingRole.IncreaseHalf;
        }

        return definition.Kind == ControllerPhotoControlKind.RotaryEncoder
            ? EncoderRingRole.DecreaseHalf
            : EncoderRingRole.Center;
    }

    private void EnsurePhotoHotspot(ControlTileViewModel tile)
    {
        if (_activeControllerPhoto is null ||
            !_activeControllerPhoto.Hotspots.TryGetValue(tile.ControlId, out var definition))
        {
            return;
        }

        // Reuses the same measured photo shape the locator hotspot renders
        // with — a knob/round button (Ellipse) gets a tile that looks round
        // too, instead of every tile being the same plain rectangle
        // regardless of what the physical control actually looks like.
        tile.CornerRadius = definition.Shape == ControllerPhotoHotspotShape.Ellipse ? 999 : 2;

        var clusterRole = TryGetEncoderClusterRole(definition);
        tile.ClusterId = clusterRole is null ? null : definition.EffectiveVisualId;
        tile.RingRole = clusterRole ?? EncoderRingRole.None;
        tile.RingGlyph = clusterRole switch
        {
            EncoderRingRole.IncreaseHalf => "▲",
            EncoderRingRole.DecreaseHalf => "▼",
            _ => string.Empty,
        };

        if (_photoHotspotsByVisualId.TryGetValue(definition.EffectiveVisualId, out var existing))
        {
            existing.AddTile(tile);
            return;
        }

        var hotspot = new ControllerPhotoHotspotViewModel(tile, definition);
        _photoHotspotsByVisualId.Add(definition.EffectiveVisualId, hotspot);
        ControllerPhotoHotspots.Add(hotspot);
    }

    private ControllerPhotoHotspotDefinition? SelectedPhotoHotspotDefinition =>
        _selectedControl is not null &&
        _activeControllerPhoto?.Hotspots.TryGetValue(_selectedControl.ControlId, out var definition) == true
            ? definition
            : null;

    private void ApplyAnalogControlUpdate(ControlTileViewModel tile, RuntimeControlUpdate update)
    {
        if (_activeControllerPhoto?.Hotspots.GetValueOrDefault(tile.ControlId) is not { } definition ||
            definition.Kind == ControllerPhotoControlKind.Button)
        {
            return;
        }

        foreach (var target in AnalogVisualTiles(definition.EffectiveVisualId))
        {
            if (update.AnalogRawValue is { } rawValue)
            {
                target.AnalogRawValue = rawValue;
            }

            if (definition.Kind == ControllerPhotoControlKind.RotaryEncoder &&
                update.AnalogDelta is { } delta)
            {
                target.ApplyEncoderDelta(delta);
            }
        }
    }

    private async Task CaptureSelectedAnalogPointAsync(
        AnalogCalibrationPoint point,
        CancellationToken cancellationToken)
    {
        var targets = SelectedAnalogVisualTiles();
        var raw = _selectedControl?.AnalogRawValue;
        if (targets.Count == 0 || raw is null)
        {
            MappingStatus = "Move the selected physical fader or potentiometer first, then capture this calibration point.";
            return;
        }

        foreach (var target in targets)
        {
            switch (point)
            {
                case AnalogCalibrationPoint.Minimum:
                    target.AnalogRawAtMinimum = raw.Value;
                    break;
                case AnalogCalibrationPoint.Center:
                    target.AnalogRawAtCenter = raw.Value;
                    break;
                case AnalogCalibrationPoint.Maximum:
                    target.AnalogRawAtMaximum = raw.Value;
                    break;
            }
        }

        Raise(nameof(SelectedAnalogCalibrationText));
        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
        MappingStatus = $"Captured raw value {raw.Value} as the {point.ToString().ToLowerInvariant()} position and saved it for this physical controller.";
    }

    private IReadOnlyList<ControlTileViewModel> SelectedAnalogVisualTiles()
    {
        var definition = SelectedPhotoHotspotDefinition;
        return definition?.Kind is ControllerPhotoControlKind.Fader or ControllerPhotoControlKind.Potentiometer
            ? AnalogVisualTiles(definition.EffectiveVisualId)
            : [];
    }

    private IReadOnlyList<ControlTileViewModel> SelectedEncoderVisualTiles()
    {
        var definition = SelectedPhotoHotspotDefinition;
        return definition?.Kind == ControllerPhotoControlKind.RotaryEncoder
            ? AnalogVisualTiles(definition.EffectiveVisualId)
            : [];
    }

    private IReadOnlyList<ControlTileViewModel> AnalogVisualTiles(string visualId) =>
        _activeControllerPhoto?.Hotspots.Values
            .Where(item => string.Equals(item.EffectiveVisualId, visualId, StringComparison.Ordinal))
            .Select(item => _tiles.GetValueOrDefault(item.ControlId))
            .Where(item => item is not null)
            .Cast<ControlTileViewModel>()
            .Distinct()
            .ToArray() ?? [];

    private string IncludePersistentStatusWarning(string message)
    {
        var status = string.IsNullOrWhiteSpace(message) ? "Tappy is ready." : message.Trim();
        return string.IsNullOrWhiteSpace(_persistentStatusWarning) ||
               status.Contains(_persistentStatusWarning, StringComparison.Ordinal)
            ? status
            : $"{status} {_persistentStatusWarning}";
    }

    private void ClearControls()
    {
        _pendingVisuals.Clear();
        _illuminationStates.Clear();
        if (_selectedControl is not null)
        {
            _selectedControl.IsSelected = false;
            _selectedControl = null;
            Raise(nameof(SelectedControlLabel));
            Raise(nameof(CanAssignSelectedControl));
            Raise(nameof(SelectedControlHasAssignment));
            Raise(nameof(AssignmentEditorButtonLabel));
            Raise(nameof(CanCalibrateSelectedControl));
            Raise(nameof(CanTuneSelectedEncoder));
            Raise(nameof(SelectedAnalogValueText));
            Raise(nameof(SelectedAnalogCalibrationText));
            Raise(nameof(SelectedEncoderCalibrationText));
        }

        Controls.Clear();
        _tiles.Clear();
        _layoutPlacements.Clear();
        foreach (var hotspot in _photoHotspotsByVisualId.Values)
        {
            hotspot.Dispose();
        }
        ControllerPhotoHotspots.Clear();
        _photoHotspotsByVisualId.Clear();
        PressedSummary = "None";
        EventSummary = "No selected-device events retained";
        Raise(nameof(GroupSelectedCount));
        Raise(nameof(GroupSelectionLabel));
    }

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        _pendingVisuals.Clear();
        _illuminationStates.Clear();
        foreach (var hotspot in _photoHotspotsByVisualId.Values)
        {
            hotspot.Dispose();
        }
        _photoHotspotsByVisualId.Clear();
        _runtime.DevicesChanged -= Runtime_OnDevicesChanged;
        _runtime.ControlChanged -= Runtime_OnControlChanged;
        _runtime.StateChanged -= Runtime_OnStateChanged;
        await _runtime.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record IlluminationState(long Generation, long StartedAtMilliseconds);

    private enum AnalogCalibrationPoint
    {
        Minimum,
        Center,
        Maximum
    }
}
