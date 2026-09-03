using System.Collections.ObjectModel;
using Tappy.App.Runtime;

namespace Tappy.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan MinimumIlluminationDuration = TimeSpan.FromMilliseconds(80);
    private readonly IControllerRuntime _runtime;
    private readonly Action<Action> _onUi;
    private readonly Action<TimeSpan, Action> _onUiAfter;
    private readonly VisualUpdateBuffer _pendingVisuals = new();
    private readonly Dictionary<string, ControlTileViewModel> _tiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IlluminationState> _illuminationStates = new(StringComparer.Ordinal);
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
    private string? _persistentStatusWarning;
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
        HarmlessOutputKeys = Enumerable.Range(13, 12).Select(number => $"F{number}").ToArray();
        _runtime.DevicesChanged += Runtime_OnDevicesChanged;
        _runtime.ControlChanged += Runtime_OnControlChanged;
        _runtime.StateChanged += Runtime_OnStateChanged;
    }

    public ObservableCollection<ControllerChoice> Devices { get; }
    public ObservableCollection<ControlTileViewModel> Controls { get; }
    public IReadOnlyList<string> HarmlessOutputKeys { get; }

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
            tile = new ControlTileViewModel
            {
                ControlId = update.ControlId,
                Label = update.DisplayLabel,
                Action = update.AssignedAction
            };
            _tiles.Add(update.ControlId, tile);
            Controls.Add(tile);
        }

        tile.IsPressed = update.IsPressed;
        UpdateIllumination(tile, update);
        tile.Action = update.AssignedAction;
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
        }

        Controls.Clear();
        _tiles.Clear();
        PressedSummary = "None";
        EventSummary = "No selected-device events retained";
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
        _runtime.DevicesChanged -= Runtime_OnDevicesChanged;
        _runtime.ControlChanged -= Runtime_OnControlChanged;
        _runtime.StateChanged -= Runtime_OnStateChanged;
        await _runtime.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record IlluminationState(long Generation, long StartedAtMilliseconds);
}
