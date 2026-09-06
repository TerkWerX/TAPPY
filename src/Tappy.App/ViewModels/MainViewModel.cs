using System.Collections.ObjectModel;
using Tappy.App.Runtime;
using Tappy.App.Services;

namespace Tappy.App.ViewModels;

public sealed record TileColorChoice(string Key, string Label, string Background, string Foreground);

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TileColorChoice[] AllTileColorChoices =
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
    internal const double LayoutCellWidth = 144;
    internal const double LayoutCellHeight = 116;
    internal const double DefaultTileWidth = 134;
    internal const double DefaultTileHeight = 106;
    private const double MinimumTileSize = 48;
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
    private ControlTileViewModel? _selectedControl;
    private string _selectedOutputKey = "F24";
    private string _identificationStatus = "Choose a device. Nothing is armed.";
    private string _mappingStatus = "No mapping selected.";
    private string _activeControllerLabel = "No controller confirmed";
    private string _activeLayerName = "Layer 1";
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

    public bool SnapLayoutToGrid
    {
        get => _snapLayoutToGrid;
        set => Set(ref _snapLayoutToGrid, value);
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
    public bool CanAssignSelectedControl => _selectedControl is not null;
    public bool SelectedControlHasAssignment =>
        _selectedControl is not null &&
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

    public void BeginIdentification()
    {
        if (SelectedDevice is null)
        {
            IdentificationStatus = "Choose a specific device first. Tappy will never choose one automatically.";
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
        if (!tile.IsGroupSelected)
        {
            ClearGroupSelection();
            SetGroupSelection(tile, true);
        }

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
        if (targets.Length == 0 && _selectedControl is not null)
        {
            targets = _selectedControl.CanUseHardwareColor ? [_selectedControl] : [];
        }

        if (targets.Length == 0)
        {
            MappingStatus = "Select a color-addressable controller control. Non-lighted and fixed-color controls keep their photographed appearance.";
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

    public async Task ApplyLayoutWorkspaceAsync(
        bool arrangeAsGrid,
        CancellationToken cancellationToken = default)
    {
        if (arrangeAsGrid)
        {
            LayoutRows = Math.Max(LayoutRows, (int)Math.Ceiling(Controls.Count / (double)LayoutColumns));
            for (var index = 0; index < Controls.Count; index++)
            {
                var tile = Controls[index];
                tile.X = (index % LayoutColumns) * LayoutCellWidth;
                tile.Y = (index / LayoutColumns) * LayoutCellHeight;
                tile.TileWidth = DefaultTileWidth;
                tile.TileHeight = DefaultTileHeight;
            }
        }

        RecalculateLayoutSurface();
        await SaveControllerLayoutAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task SaveControllerLayoutAsync(CancellationToken cancellationToken = default)
    {
        var current = _runtime.GetControllerLayout();
        if (current is null)
        {
            MappingStatus = "Confirm a controller before saving its layout.";
            return;
        }

        var natural = current.Controls.ToDictionary(control => control.ControlId, StringComparer.Ordinal);
        var placements = Controls
            .Where(tile => natural.ContainsKey(tile.ControlId))
            .Select(tile =>
            {
                var source = natural[tile.ControlId];
                return new ControllerControlPlacement(
                    tile.ControlId,
                    source.NaturalRow,
                    source.NaturalColumn,
                    tile.X,
                    tile.Y,
                    tile.TileWidth / DefaultTileWidth,
                    tile.TileHeight / DefaultTileHeight,
                    tile.ColorKey,
                    tile.AnalogRawAtMinimum,
                    tile.AnalogRawAtMaximum,
                    tile.AnalogRawAtCenter,
                    tile.EncoderDegreesPerStep,
                    tile.EncoderReversed);
            })
            .ToArray();
        var result = _runtime.UpdateControllerLayout(new ControllerLayoutWorkspace(
            LayoutColumns,
            LayoutRows,
            SnapLayoutToGrid,
            placements));
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

    public RuntimeOperation EmergencyStop(string reason)
    {
        var result = _runtime.EmergencyStop(reason);
        SynchronizeRuntimeModes();
        SynchronizeRuntimeActivation();
        Status = result.Message;
        _pendingVisuals.Clear();
        _illuminationStates.Clear();
        foreach (var tile in Controls)
        {
            tile.IsPressed = false;
            tile.IsIlluminated = false;
        }

        PressedSummary = "None";
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
            Devices.Add(device);
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
        }

        EnsurePhotoHotspot(tile);
        ApplyAnalogControlUpdate(tile, update);

        tile.IsPressed = update.IsPressed;
        UpdateIllumination(tile, update);
        tile.Action = update.AssignedAction;
        if (ReferenceEquals(tile, _selectedControl))
        {
            Raise(nameof(SelectedControlHasAssignment));
            Raise(nameof(AssignmentEditorButtonLabel));
            Raise(nameof(SelectedAnalogValueText));
            Raise(nameof(SelectedAnalogCalibrationText));
            Raise(nameof(SelectedEncoderCalibrationText));
        }
        if (update.IsPressed && !update.IsRepeat)
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
        MappingStatus = state.MappingStatus;
        Status = state.Status;
        EffectiveSourceLabel = state.EffectiveSourceLabel;
        SynchronizeTileColorChoices(state.IsConfirmed);
        SynchronizeControllerPhoto(state);
        SynchronizeControllerLayout(state.IsConfirmed);
        if (!state.IsConfirmed && !state.IsIdentificationCaptureActive)
        {
            ClearPressedVisuals();
        }
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
        var next = state.IsConfirmed
            ? ControllerPhotoCatalog.Find(
                state.ActiveControllerProviderId,
                state.ActiveControllerVendorId,
                state.ActiveControllerProductId,
                state.ActiveControllerLabel)
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
            return;
        }

        tile.X = placement.X ?? placement.NaturalColumn * LayoutCellWidth;
        tile.Y = placement.Y ?? placement.NaturalRow * LayoutCellHeight;
        tile.TileWidth = Math.Clamp(placement.Width * DefaultTileWidth, MinimumTileSize, 600);
        tile.TileHeight = Math.Clamp(placement.Height * DefaultTileHeight, MinimumTileSize, 400);
        tile.AnalogRawAtMinimum = placement.AnalogRawAtMinimum;
        tile.AnalogRawAtMaximum = placement.AnalogRawAtMaximum;
        tile.AnalogRawAtCenter = placement.AnalogRawAtCenter;
        tile.EncoderDegreesPerStep = placement.EncoderDegreesPerStep;
        tile.EncoderReversed = placement.EncoderReversed;
        ApplyTileColor(tile, UsesGlobalTileColor ? SelectedTileColor.Key : placement.ColorKey);
    }

    private void ApplyTileColor(ControlTileViewModel tile, string? colorKey)
    {
        if (!tile.CanUseHardwareColor)
        {
            colorKey = "Default";
        }

        var supportedKey = ResolveSupportedColorKey(colorKey);
        var choice = TileColorChoices.FirstOrDefault(item =>
                         string.Equals(item.Key, supportedKey, StringComparison.OrdinalIgnoreCase)) ??
                     TileColorChoices[0];
        tile.ColorKey = choice.Key;
        tile.TileBackground = choice.Background;
        tile.TileForeground = choice.Foreground;
    }

    private void SynchronizeTileColorChoices(bool isConfirmed)
    {
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

        SelectedTileColor = TileColorChoices[0];
        foreach (var tile in Controls)
        {
            SynchronizeTileColorCapability(tile);
        }
        Raise(nameof(HasSupportedTileColors));
        Raise(nameof(UsesGlobalTileColor));
        Raise(nameof(TileColorApplyButtonLabel));
        Raise(nameof(TileColorSyncButtonLabel));
        Raise(nameof(TileColorSyncToolTip));
        Raise(nameof(TileColorAvailabilityLabel));
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

    private void EnsurePhotoHotspot(ControlTileViewModel tile)
    {
        if (_activeControllerPhoto is null ||
            !_activeControllerPhoto.Hotspots.TryGetValue(tile.ControlId, out var definition))
        {
            return;
        }

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

    private void ClearPressedVisuals()
    {
        _pendingVisuals.Clear();
        _illuminationStates.Clear();
        foreach (var tile in Controls)
        {
            tile.IsPressed = false;
            tile.IsIlluminated = false;
        }

        PressedSummary = "None";
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
