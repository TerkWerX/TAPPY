using System.Collections.ObjectModel;
using System.Globalization;
using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Core.Profiles;
using Tappy.App.Services;
using Tappy.Windows.Diagnostics;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;
using Tappy.Windows.Output;
using Tappy.Windows.Profiles;

namespace Tappy.App.Runtime;

/// <summary>
/// Composes keyboard Raw Input, the physical Logitech G13 vendor HID provider,
/// and Windows MIDI input with the platform-neutral mapping engine.
/// Identification events have a separate target-only path and can never execute
/// a binding.
/// </summary>
public sealed partial class DeviceAwareControllerRuntime : IControllerRuntime
{
    private const string DefaultProfileId = "default";
    private const string KeyboardProviderId = "raw-input";
    private const string LogitechG13ProviderId = "raw-hid-g13";
    private const string MidiProviderId = WinMmMidiInputProvider.ProviderId;
    private const string OutputSafetyFailureStatus =
        "Needs attention: Windows rejected a Tappy output transition, so Tappy cannot confirm that every owned output is released. Mapping output is disarmed and source input remains fail-open pass-through; restart Tappy before rearming.";
    private const string OutputSafetyFailureMappingStatus =
        "Tappy could not confirm a safe output state. Rehearsal Mode remains on; verify the output state in a harmless target and restart Tappy before rearming.";
    private readonly object _gate = new();
    private readonly RawInputKeyboardProvider _keyboardProvider;
    private readonly LogitechG13InputProvider? _logitechG13Provider;
    private readonly WinMmMidiInputProvider? _midiProvider;
    private readonly IWindowsLifecycleSignalSource? _applicationLifecycleSource;
    private readonly AtomicProfileStore _profileStore;
    private readonly MappingEngine _engine;
    private readonly IControllerActionOutput _actionOutput;
    private readonly WinMmMidiOutput? _controllerLedOutput;
    private readonly ILogitechG13LightingOutput? _logitechG13LightingOutput;
    private readonly IRazerTartarusIndicatorOutput? _tartarusIndicatorOutput;
    private int? _tartarusVerifiedLedMask;
    private long _tartarusLedConfirmationGeneration;
    private long _tartarusKeymapRevision;
    private bool _tartarusLedSyncPending;
    private readonly Action? _beforeEngineProcess;
    private readonly Action? _beforeDisposeGate;
    private readonly InputDiagnosticAggregate _diagnostics = new();
    private readonly TargusBankState _targusBank = new();
    private readonly List<RuntimeInputDevice> _devices = [];
    private readonly HashSet<ControlId> _observedControls = [];
    private readonly Dictionary<ControlId, string> _controlLabels = [];
    private TappyProfile _editableProfile = new();
    private RuntimeInputDevice? _candidate;
    private RuntimeInputDevice? _confirmed;
    private RuntimeInputDevice? _rememberedReconnect;
    private bool _isRememberedReconnect;
    private string? _startupRestorePersistentId;
    private LifecycleResumeSession? _lifecycleResume;
    private bool _suspended;
    private bool _sessionLocked;
    private long _confirmationGeneration;
    private bool _isRehearsal = true;
    private bool _initialized;
    private bool _disposed;
    private bool _outputSafetyNeedsAttention;
    private string? _optionalCapabilityWarning;
    private long _aggregateEventCount;
    private string? _protectedTypingKeyboardId;
    private bool _blockAllKeyboardControllers;

    private bool IsProtectedKeyboard(RuntimeInputDevice? device) => device?.ProviderId == KeyboardProviderId &&
        (_blockAllKeyboardControllers || device.PersistentId == _protectedTypingKeyboardId);

    public ControllerChoice? GetVerifiedTypingKeyboard()
    {
        lock (_gate)
        {
            return !_disposed && !_suspended && !_sessionLocked && !_outputSafetyNeedsAttention &&
                _candidate?.ProviderId == KeyboardProviderId && !_isRememberedReconnect &&
                CanConfirmController && IsCaptureTargetNeutral(_candidate) ? ToChoice(_candidate) : null;
        }
    }

    public void SetTypingKeyboardProtection(string? persistentId, bool blockAllKeyboards = false)
    {
        bool mustStop;
        lock (_gate)
        {
            _protectedTypingKeyboardId = persistentId;
            _blockAllKeyboardControllers = blockAllKeyboards;
            mustStop = IsProtectedKeyboard(_confirmed) || IsProtectedKeyboard(_candidate) ||
                IsProtectedKeyboard(_rememberedReconnect) || IsProtectedKeyboard(_lifecycleResume?.Device);
        }
        if (mustStop) { EmergencyStop("Typing keyboard protected: controller capture stopped; saved assignments retained."); }
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public ControllerChoice? ReservedDevice
    {
        get
        {
            lock (_gate)
            {
                var device = _confirmed ?? _candidate ?? _rememberedReconnect ?? _lifecycleResume?.Device;
                return device is null ? null : new ControllerChoice(device.SessionId, device.PersistentId,
                    device.DisplayName, device.IsAmbiguous ? "Ambiguous" : "PortBound",
                    device.ProviderId, device.VendorId, device.ProductId);
            }
        }
    }

    public DeviceAwareControllerRuntime(
        string? dataRoot = null,
        IWindowsLifecycleSignalSource? applicationLifecycleSource = null)
        : this(CreateProductionProviders(), CreateProductionOutputs(),
            new AtomicProfileStore(dataRoot), applicationLifecycleSource)
    {
    }

    private DeviceAwareControllerRuntime(
        ProductionProviders providers,
        ProductionOutputs outputs,
        AtomicProfileStore profileStore,
        IWindowsLifecycleSignalSource? applicationLifecycleSource)
        : this(
            providers.Keyboard,
            providers.LogitechG13,
            outputs.Keyboard,
            profileStore,
            applicationLifecycleSource,
            actionOutput: outputs.Actions,
            midiInputProvider: providers.Midi,
            controllerLedOutput: outputs.ControllerLeds,
            logitechG13LightingOutput: outputs.LogitechG13Lighting,
            tartarusIndicatorOutput: new NativeRazerTartarusIndicatorOutput())
    {
    }

    internal DeviceAwareControllerRuntime(
        RawInputKeyboardProvider provider,
        IKeyboardOutput keyboardOutput,
        AtomicProfileStore profileStore,
        IWindowsLifecycleSignalSource? applicationLifecycleSource = null,
        Action? beforeEngineProcess = null,
        Action? beforeDisposeGate = null,
        IControllerActionOutput? actionOutput = null,
        WinMmMidiInputProvider? midiInputProvider = null,
        WinMmMidiOutput? controllerLedOutput = null,
        ILogitechG13LightingOutput? logitechG13LightingOutput = null,
        IRazerTartarusIndicatorOutput? tartarusIndicatorOutput = null)
        : this(
            provider,
            null,
            keyboardOutput,
            profileStore,
            applicationLifecycleSource,
            beforeEngineProcess,
            beforeDisposeGate,
            actionOutput,
            midiInputProvider,
            controllerLedOutput,
            logitechG13LightingOutput,
            tartarusIndicatorOutput)
    {
    }

    internal DeviceAwareControllerRuntime(
        RawInputKeyboardProvider keyboardProvider,
        LogitechG13InputProvider? logitechG13Provider,
        IKeyboardOutput keyboardOutput,
        AtomicProfileStore profileStore,
        IWindowsLifecycleSignalSource? applicationLifecycleSource = null,
        Action? beforeEngineProcess = null,
        Action? beforeDisposeGate = null,
        IControllerActionOutput? actionOutput = null,
        WinMmMidiInputProvider? midiInputProvider = null,
        WinMmMidiOutput? controllerLedOutput = null,
        ILogitechG13LightingOutput? logitechG13LightingOutput = null,
        IRazerTartarusIndicatorOutput? tartarusIndicatorOutput = null)
    {
        _keyboardProvider = keyboardProvider ?? throw new ArgumentNullException(nameof(keyboardProvider));
        _logitechG13Provider = logitechG13Provider;
        _midiProvider = midiInputProvider;
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _actionOutput = actionOutput ?? NullControllerActionOutput.Instance;
        _controllerLedOutput = controllerLedOutput;
        _logitechG13LightingOutput = logitechG13LightingOutput;
        _tartarusIndicatorOutput = tartarusIndicatorOutput;
        _beforeEngineProcess = beforeEngineProcess;
        _beforeDisposeGate = beforeDisposeGate;
        _applicationLifecycleSource = ReferenceEquals(applicationLifecycleSource, keyboardProvider) ||
                                      ReferenceEquals(applicationLifecycleSource, logitechG13Provider)
            ? null
            : applicationLifecycleSource;
        _engine = new MappingEngine(
            keyboardOutput ?? throw new ArgumentNullException(nameof(keyboardOutput)),
            new MappingEngineOptions
            {
                SelfInjectionMarker = InjectedInputMarker.Value,
                MaximumAncestryDepth = 8,
                MaximumOutputTransitionsPerWindow = 200,
                OutputRateWindow = TimeSpan.FromSeconds(1)
            },
            actionOutput: _actionOutput);
        _engine.SetRehearsalMode(true);
        _actionOutput.Faulted += ActionOutput_OnFaulted;

        _keyboardProvider.IdentificationInputReceived += KeyboardProvider_OnIdentificationInputReceived;
        _keyboardProvider.InputReceived += KeyboardProvider_OnInputReceived;
        _keyboardProvider.DeviceChanged += KeyboardProvider_OnDeviceChanged;
        _keyboardProvider.LifecycleChanged += Provider_OnLifecycleChanged;
        _keyboardProvider.Faulted += Provider_OnFaulted;
        if (_logitechG13Provider is not null)
        {
            _logitechG13Provider.IdentificationInputReceived += LogitechG13Provider_OnIdentificationInputReceived;
            _logitechG13Provider.InputReceived += LogitechG13Provider_OnInputReceived;
            _logitechG13Provider.DeviceChanged += LogitechG13Provider_OnDeviceChanged;
            _logitechG13Provider.AvailabilityChanged += LogitechG13Provider_OnAvailabilityChanged;
            // Production providers share the keyboard provider's native host. It is
            // the sole lifecycle/fault authority so one Windows message cannot
            // trigger cleanup twice.
        }
        if (_midiProvider is not null)
        {
            _midiProvider.IdentificationInputReceived += MidiProvider_OnIdentificationInputReceived;
            _midiProvider.InputReceived += MidiProvider_OnInputReceived;
            _midiProvider.CaptureLost += MidiProvider_OnCaptureLost;
            _midiProvider.Faulted += Provider_OnFaulted;
        }
        if (_applicationLifecycleSource is not null)
        {
            _applicationLifecycleSource.LifecycleChanged += Provider_OnLifecycleChanged;
        }
    }

    public event EventHandler? DevicesChanged;
    public event EventHandler<RuntimeControlUpdate>? ControlChanged;
    public event EventHandler<RuntimeState>? StateChanged;

    public IReadOnlyList<ControllerChoice> Devices
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<ControllerChoice>(
                    _devices.Select(ToChoice).ToArray());
            }
        }
    }

    public bool IsRehearsal
    {
        get
        {
            lock (_gate)
            {
                return _isRehearsal;
            }
        }
        set
        {
            bool needsAttention;
            lock (_gate)
            {
                if (_outputSafetyNeedsAttention)
                {
                    _isRehearsal = true;
                    _ = _engine.SetRehearsalMode(true);
                }
                else if (_lifecycleResume is { HasSlept: true } paused)
                {
                    _lifecycleResume = paused with { Rehearsal = value };
                    _isRehearsal = value;
                    RecordCleanupResultLocked(_engine.SetRehearsalMode(true));
                }
                else if (_isRememberedReconnect || _suspended || _sessionLocked)
                {
                    _isRehearsal = true;
                    RecordCleanupResultLocked(_engine.SetRehearsalMode(true));
                }
                else
                {
                    _isRehearsal = value;
                    RecordCleanupResultLocked(_engine.SetRehearsalMode(value));
                }

                needsAttention = _outputSafetyNeedsAttention;
            }

            if (needsAttention)
            {
                RaiseOutputSafetyFailureState();
            }
            else
            {
                RaiseState(mappingStatus: IsRehearsal
                    ? "Rehearsal Mode is on. Recognition continues; output is suppressed."
                    : "Normal output is enabled for the confirmed controller only.");
            }
        }
    }

    public bool CanConfirmController =>
        !_isRememberedReconnect && _engine.Activation.State == ControllerActivationState.AwaitingConfirmation;

    public bool IsIdentificationCaptureActive
    {
        get
        {
            lock (_gate)
            {
                return _candidate is not null;
            }
        }
    }

    public bool IsOutputStateConfirmedSafe
    {
        get
        {
            lock (_gate)
            {
                return !_outputSafetyNeedsAttention;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        TappyProfile profile;
        string loadStatus;
        try
        {
            var recovered = await _profileStore.LoadWithRecoveryAsync(
                DefaultProfileId, cancellationToken).ConfigureAwait(false);
            profile = recovered.Snapshot.ToEditableProfile();
            loadStatus = recovered.RecoveryState == ProfileRecoveryState.Primary
                ? "Saved profile loaded. Choose and physically identify its controller to arm it."
                : "The last-known-good profile was loaded. Review it before normal output.";
        }
        catch (FileNotFoundException)
        {
            profile = new TappyProfile();
            loadStatus = "No saved profile exists yet. Nothing is armed.";
        }

        EnforceAvailableSourceModes(profile);
        lock (_gate)
        {
            _editableProfile = profile;
            _engine.SetProfile(profile.CreateSnapshot());
            // Non-null only when Normalize() already confirmed a matching saved
            // controller exists; resolved against live devices in RefreshDevices().
            _startupRestorePersistentId = profile.RememberedReconnectPersistentId;
        }

        // Profile/recovery work is intentionally complete before native listening.
        await _keyboardProvider.StartAsync(cancellationToken).ConfigureAwait(false);
        if (_logitechG13Provider is not null)
        {
            await _logitechG13Provider.StartAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                UpdateOptionalCapabilityWarningLocked();
            }
        }
        if (_midiProvider is not null)
        {
            await _midiProvider.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        RefreshDevices();
        _initialized = true;
        bool restoreStarted;
        lock (_gate) { restoreStarted = _isRememberedReconnect; }
        // A silent startup restore (see RefreshDevices/TryStartRememberedReconnect)
        // already raised its own, more accurate status; do not stomp it here.
        if (!restoreStarted)
        {
            RaiseState(status: loadStatus);
        }
    }

    public void RefreshDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var devices = EnumerateRuntimeDevices();
        RuntimeControlUpdate[] releasedUpdates = [];
        ControllerSessionId? disconnectedSession = null;
        var captureWasLost = false;
        var outputReleaseSucceeded = true;
        lock (_gate)
        {
            if (_confirmed is not null &&
                !devices.Any(item => item.HasSameLiveIdentity(_confirmed)))
            {
                releasedUpdates = CaptureReleasedUpdatesLocked();
                disconnectedSession = new ControllerSessionId(_confirmed.SessionId);
                captureWasLost = true;
                _rememberedReconnect = _confirmed.IsAmbiguous ? null : _confirmed;
                _confirmed = null;
                _candidate = null;
                _isRememberedReconnect = false;
            }
            else if (_candidate is not null &&
                     (!devices.Any(item => item.HasSameLiveIdentity(_candidate)) ||
                      (_isRememberedReconnect &&
                       devices.Count(item => item.ProviderId == _candidate.ProviderId &&
                           item.PersistentId == _candidate.PersistentId) != 1)))
            {
                disconnectedSession = new ControllerSessionId(_candidate.SessionId);
                captureWasLost = true;
                _candidate = null;
                _isRememberedReconnect = false;
            }
            else if (_startupRestorePersistentId is { Length: > 0 } startupId && _rememberedReconnect is null &&
                     _confirmed is null && _candidate is null)
            {
                // One saved identity, checked opportunistically on every refresh (not
                // just the first) so a device that enumerates a moment after Tappy
                // starts is still picked up. PersistentId alone is the full identity
                // claim here (it already hashes vendor/product/container); unlike the
                // mid-session path above, there is no prior live descriptor to compare
                // the other fields against.
                var startupMatches = devices.Where(item =>
                    item.PersistentId == startupId && !item.IsAmbiguous).ToArray();
                if (startupMatches.Length == 1)
                {
                    _rememberedReconnect = startupMatches[0];
                    _startupRestorePersistentId = null;
                }
            }

            if (captureWasLost)
            {
                if (!_suspended && !_sessionLocked) { _lifecycleResume = null; }
                _isRehearsal = true;
                outputReleaseSucceeded = RecordCleanupResultLocked(
                    _engine.SetRehearsalMode(true));
                _engine.Activation.Reset();
            }

            _devices.Clear();
            _devices.AddRange(devices
                .OrderBy(item => item.DisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.ProviderId, StringComparer.Ordinal));
        }

        if (disconnectedSession is { } session)
        {
            var cleanup = _engine.DisconnectController(session);
            lock (_gate)
            {
                outputReleaseSucceeded = RecordCleanupResultLocked(cleanup) && !_outputSafetyNeedsAttention;
            }
            ClearCaptureTargets();
        }

        PublishReleasedUpdates(releasedUpdates);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        if (captureWasLost)
        {
            if (outputReleaseSucceeded)
            {
                RaiseState(
                    identificationStatus: "Controller disconnected. Its remembered layout will return when the same device reconnects; output stays in Rehearsal Mode.",
                    mappingStatus: "Rehearsal Mode was restored and all Tappy-owned output was released.",
                    status: "Needs attention: selected controller disappeared; fail-open pass-through remains.",
                    activeControllerLabel: "No controller confirmed");
            }
            else
            {
                RaiseOutputSafetyFailureState(
                    identificationStatus: "The selected controller disappeared and Windows rejected an owned-output release. Restart Tappy before rearming.",
                    activeControllerLabel: "No controller confirmed");
            }
        }
        else
        {
            TryStartRememberedReconnect();
        }
    }

    // USB reconnect stays in rehearsal. Sleep recovery restores the user's previous
    // mode only after resume/unlock, identity verification and fresh neutral input.
    public void PollRememberedReconnect()
    {
        // Friendly-name discovery completes in the background. Consume on the normal
        // UI poll, without treating a label update as a device arrival or re-confirmation.
        if (!_disposed && _initialized && !_suspended && !_sessionLocked && _keyboardProvider.ConsumeMetadataChange())
        {
            try { RefreshDevices(); }
            catch (Exception exception) { Provider_OnFaulted(this, exception); }
        }
        lock (_gate)
        {
            // _startupRestorePersistentId also gates the check in, not just
            // _rememberedReconnect: a saved device that hasn't enumerated yet (e.g.
            // Tappy started slightly ahead of Windows finishing USB enumeration)
            // must still be retried on the next few polls, not just once at startup.
            if (_disposed || !_initialized || _suspended || _sessionLocked ||
                (_rememberedReconnect is null && _startupRestorePersistentId is null) ||
                _outputSafetyNeedsAttention || _confirmed is not null ||
                (_candidate is not null && !_isRememberedReconnect))
            {
                return;
            }
        }

        try
        {
            RefreshDevices();
            lock (_gate)
            {
                if (_isRememberedReconnect && _candidate is not null &&
                    IsCaptureTargetNeutral(_candidate))
                {
                    _ = ConfirmController();
                }
            }
        }
        catch (Exception exception)
        {
            Provider_OnFaulted(this, exception);
        }
    }

    private void TryStartRememberedReconnect()
    {
        lock (_gate)
        {
            if (_disposed || _suspended || _sessionLocked || _outputSafetyNeedsAttention || _rememberedReconnect is not { } remembered ||
                _confirmed is not null || _candidate is not null || IsProtectedKeyboard(remembered))
            {
                return;
            }

            var matches = _devices.Where(item => item.ProviderId == remembered.ProviderId &&
                item.PersistentId == remembered.PersistentId).ToArray();
            if (matches.Length != 1 || matches[0].IsAmbiguous ||
                matches[0].VendorId != remembered.VendorId || matches[0].ProductId != remembered.ProductId ||
                matches[0].UsagePage != remembered.UsagePage || matches[0].Usage != remembered.Usage ||
                FindEditableController(remembered.PersistentId) is not { } controller)
            {
                return;
            }

            _isRehearsal = _lifecycleResume is { HasSlept: true } paused ? paused.Rehearsal : true;
            if (!RecordCleanupResultLocked(_engine.SetRehearsalMode(true))) { return; }
            if (!SetCaptureTarget(matches[0])) { return; }
            _candidate = matches[0];
            _isRememberedReconnect = true;
            _aggregateEventCount = 0;
            _engine.Activation.SelectCandidate(new ControllerSessionId(_candidate.SessionId));
            PublishReleasedUpdates(CreateControlUpdatesLocked(controller, isSnapshot: true));
            RaiseState(
                identificationStatus: "Remembered controller reconnected. Waiting for its controls to be released; no identification gesture is needed.",
                mappingStatus: _lifecycleResume is { HasSlept: true }
                    ? "Waking: saved layout and mode retained. Output waits for all controls to be released."
                    : "Saved layout restored. Rehearsal Mode remains on; no output will resume automatically.",
                status: "Restoring remembered controller. Release all controls to finish reconnecting.");
        }
    }

    public RuntimeOperation BeginIdentification(ControllerChoice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);

        RuntimeInputDevice? descriptor;
        RuntimeControlUpdate[] releasedUpdates;
        string? failure = null;
        var outputSafetyFailure = false;
        lock (_gate)
        {
            if ((_suspended || _sessionLocked) && !_outputSafetyNeedsAttention)
            {
                return RuntimeOperation.Failed("Wake and unlock Windows before identifying a controller.");
            }
            if (_outputSafetyNeedsAttention)
            {
                return RuntimeOperation.Failed(
                    "An earlier Tappy-owned output release was rejected. Restart Tappy before identifying and rearming a controller.");
            }

            descriptor = _devices.FirstOrDefault(item =>
                item.ProviderId == device.ProviderId &&
                item.SessionId == device.SessionId);
            if (descriptor is null)
            {
                return RuntimeOperation.Failed("That device is no longer present. Refresh the list and choose it again.");
            }

            if (IsProtectedKeyboard(descriptor))
            {
                return RuntimeOperation.Failed("This keyboard is protected for typing. It cannot run Tappy assignments. Clear or change typing-keyboard protection first.");
            }

            releasedUpdates = CaptureReleasedUpdatesLocked();
            _lifecycleResume = null;
            _rememberedReconnect = null;
            _isRememberedReconnect = false;
            _startupRestorePersistentId = null;
            if (_confirmed is not null)
            {
                if (!RecordCleanupResultLocked(
                        _engine.DisconnectController(new ControllerSessionId(_confirmed.SessionId))))
                {
                    failure = "Windows rejected a Tappy-owned output release. Identification remains disarmed.";
                    outputSafetyFailure = true;
                }
            }

            _engine.Activation.Reset();
            _isRehearsal = true;
            _engine.SetRehearsalMode(true);
            _candidate = descriptor;
            _confirmed = null;
            _targusBank.Reset();
            _observedControls.Clear();
            _controlLabels.Clear();
            _aggregateEventCount = 0;
            ClearCaptureTargets();
            if (failure is not null)
            {
                _candidate = null;
            }
            else if (!SetCaptureTarget(descriptor))
            {
                _candidate = null;
                failure = descriptor.ProviderId == MidiProviderId
                    ? "Nothing was armed because Windows could not open that MIDI input. Close any application that has exclusive use of the MIDI port, click Refresh, and try again."
                    : "Nothing was armed. Release every keyboard and controller control, then try again with the mouse; refresh the device list if everything is already neutral.";
            }
            else
            {
                _engine.Activation.SelectCandidate(new ControllerSessionId(descriptor.SessionId));
            }
        }

        PublishReleasedUpdates(releasedUpdates);
        if (failure is not null)
        {
            if (outputSafetyFailure)
            {
                RaiseOutputSafetyFailureState(
                    identificationStatus: $"{failure} Restart Tappy before rearming.",
                    activeControllerLabel: "No controller confirmed");
            }
            else
            {
                RaiseState(
                    identificationStatus: failure,
                    status: "Needs attention: identification could not be armed.");
            }

            return RuntimeOperation.Failed(failure);
        }

        RaiseState(
            identificationStatus: "Identification is visibly armed for only this chosen device. Release all its controls, then press and release one control.",
            mappingStatus: "Rehearsal Mode is on. Recognition continues; output is suppressed.",
            status: "Identification capture is active; focused Tappy buttons ignore keyboard activation and assignments remain suspended.");
        return RuntimeOperation.Ok(
            "Identification is armed for this device only. Release all controls, then press and release one control.");
    }

    public RuntimeOperation ConfirmController()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RuntimeInputDevice descriptor;
        ControllerProfile controller;
        RuntimeControlUpdate[] restoredControls;
        lock (_gate)
        {
            if ((_suspended || _sessionLocked) && !_outputSafetyNeedsAttention)
            {
                return RuntimeOperation.Failed("Wake and unlock Windows before confirming a controller.");
            }
            if (_outputSafetyNeedsAttention)
            {
                return RuntimeOperation.Failed(
                    "An earlier Tappy-owned output release was rejected. Restart Tappy before confirming a controller.");
            }

            if (_candidate is null ||
                (!_isRememberedReconnect && _engine.Activation.State != ControllerActivationState.AwaitingConfirmation) ||
                !IsCaptureTargetNeutral(_candidate))
            {
                return RuntimeOperation.Failed(
                    "Confirmation is unavailable until the selected device completes one press and release and all its controls are up.");
            }

            descriptor = _candidate;
            if (IsProtectedKeyboard(descriptor))
            { return RuntimeOperation.Failed("This keyboard is protected for typing; controller confirmation is blocked."); }
            var sleepResume = _isRememberedReconnect && _lifecycleResume is { HasSlept: true };
            var resumeInRehearsal = !sleepResume || _lifecycleResume!.Rehearsal;
            if (!SetConfirmedPersistentId(descriptor, descriptor.PersistentId))
            {
                return RuntimeOperation.Failed("The device changed or is not neutral. Identification remains unconfirmed.");
            }

            if (_isRememberedReconnect)
            {
                if (!_engine.Activation.TryRestoreRememberedCandidate())
                {
                    return RuntimeOperation.Failed("Release every control before the remembered controller resumes.");
                }
                _isRehearsal = true;
                _engine.SetRehearsalMode(true);
            }
            else
            {
                _engine.Activation.Confirm();
            }
            var identity = ToIdentity(descriptor);
            var midiModel = MidiControllerModelCatalog.Find(descriptor.ProviderId, descriptor.DisplayName);
            if (descriptor.ProviderId == MidiProviderId)
            {
                // Always set, even to empty: capture state resets on disconnect/reconfirm,
                // so a stale relative-CC set from a previous model must not leak forward.
                _midiProvider?.SetRelativeControlChanges(midiModel?.RelativeControlChangeNumbers ?? []);
            }
            var targus = TargusKeypadLayout.Matches(descriptor.ProviderId, descriptor.VendorId, descriptor.ProductId);
            var microsoftPad = descriptor.PresentationModelId == KeyboardPresentationModels.MicrosoftBluetoothNumberPad;
            var tartarus = RazerTartarusLayout.Matches(descriptor.ProviderId, descriptor.VendorId, descriptor.ProductId);
            var existingController = FindEditableController(descriptor.PersistentId);
            controller = existingController ?? ControllerProfile.Create(
                             identity,
                             descriptor.ProviderId == LogitechG13ProviderId
                                 ? LogitechG13InputProvider.SupportedControls.Select(item => item.ControlId)
                                 : midiModel is not null
                                     ? midiModel.Controls.Select(item => item.ControlId)
                                 : null,
                             defaultLayerCount: tartarus ? 8
                                 : midiModel?.Id == MidiControllerModelCatalog.AkaiFireV1Id ? 4
                                 : 3);
            controller.Identity = identity;
            controller.DisplayName = descriptor.DisplayName;
            if (existingController is null && midiModel is null && !targus && !microsoftPad && !tartarus &&
                descriptor.ProviderId != LogitechG13ProviderId)
            {
                // Preserve the generated grid's six-column starting point, then grow
                // by appending controls without reflowing any saved workspace.
                controller.Layout.GridColumns = 6;
            }
            if (descriptor.ProviderId == LogitechG13ProviderId)
            {
                controller.Layout = MergeCodeDefinedLayout(
                    controller.Layout,
                    CreateLogitechG13Layout());
            }
            else if (midiModel is not null)
            {
                controller.Layout = MergeCodeDefinedLayout(
                    controller.Layout,
                    midiModel.Layout);
                if (midiModel.Id == MidiControllerModelCatalog.AkaiFireV1Id)
                {
                    // A Fire confirmed and saved before the bank count grew to
                    // 4 (matching its own physical Modes/Bank LED, which has
                    // exactly four positions) would otherwise stay stuck at 3
                    // forever — defaultLayerCount only applies to a brand new
                    // profile. Same pattern as the Tartarus 8-layer ensure below.
                    for (var index = 0; controller.Layers.Count < 4; index++)
                    {
                        var layer = InputLayerDefinition.Create(index);
                        if (controller.Layers.All(l => !string.Equals(l.Id, layer.Id, StringComparison.OrdinalIgnoreCase)))
                        { controller.Layers.Add(layer); }
                    }
                }
            }
            else if (targus)
            {
                controller.Layout = TargusKeypadLayout.MergeSavedLayout(controller.Layout);
            }
            else if (microsoftPad)
            {
                controller.Layout = MicrosoftNumberPadLayout.MergeSavedLayout(controller.Layout);
            }
            else if (tartarus)
            {
                controller.Layout = RazerTartarusLayout.MergeSavedLayout(controller.Layout);
                for (var index = 0; controller.Layers.Count < 8; index++)
                {
                    var layer = InputLayerDefinition.Create(index);
                    if (controller.Layers.All(l => !string.Equals(l.Id, layer.Id, StringComparison.OrdinalIgnoreCase)))
                    { controller.Layers.Add(layer); }
                }
            }
            EnsureCombinationTiles(controller);
            ApplyAvailableSourceMode(controller);
            if (!_editableProfile.Controllers.Contains(controller))
            {
                _editableProfile.Controllers.Add(controller);
            }

            controller.Normalize();
            _observedControls.Clear();
            _controlLabels.Clear();
            foreach (var layoutControl in controller.Layout.Rows.SelectMany(row => row.Controls))
            {
                if (layoutControl.ControlId is { } controlId)
                {
                    _observedControls.Add(controlId);
                    _controlLabels[controlId] = string.IsNullOrWhiteSpace(layoutControl.Label)
                        ? controlId.Value
                        : layoutControl.Label;
                }
            }

            _confirmed = descriptor;
            _confirmationGeneration++;
            _bankConnectionToken = Guid.NewGuid().ToString("N");
            _candidate = null;
            _rememberedReconnect = null;
            _isRememberedReconnect = false;
            _startupRestorePersistentId = null;
            // Eligible for a silent startup restore next launch if this same
            // unambiguous device is still connected then; see InitializeAsync.
            _editableProfile.RememberedReconnectPersistentId =
                descriptor.IsAmbiguous ? null : descriptor.PersistentId;
            RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot()));
            _engine.ConnectController(identity);
            if (sleepResume && !_outputSafetyNeedsAttention)
            {
                _isRehearsal = resumeInRehearsal;
                RecordCleanupResultLocked(_engine.SetRehearsalMode(resumeInRehearsal));
            }
            _lifecycleResume = null;
            restoredControls = CreateControlUpdatesLocked(controller, isSnapshot: true);
        }

        PublishReleasedUpdates(restoredControls);
        RaiseState(
            identificationStatus: "Controller confirmed for this session. Only its events can reach mappings.",
            status: descriptor.ProviderId == MidiProviderId
                ? "MIDI controller ready. Its controls can run any Tappy assignment."
                : controller.SourceMode.Effective == EffectiveSourceMode.PassThrough
                ? "Controller ready in Device-aware pass-through."
                : "Needs attention: the requested source backend is unavailable, so mappings remain disarmed and source input passes through.",
            activeControllerLabel: descriptor.DisplayName,
            activeLayerName: controller.Layers.First(layer => layer.Id == controller.ActiveLayerId).Name,
            sourceLabel: SourceLabel(controller));
        return RuntimeOperation.Ok(descriptor.ProviderId == MidiProviderId
            ? "MIDI controller confirmed. Its controls can now be assigned to any Tappy action."
            : "Controller confirmed. Original input remains pass-through.");
    }

    public RuntimeOperation SelectTartarusKeymap(int bank)
    {
        RuntimeControlUpdate[] updates;
        lock (_gate)
        {
            if (_disposed || _confirmed is null || _isRememberedReconnect || _suspended || _sessionLocked ||
                _outputSafetyNeedsAttention || !RazerTartarusLayout.Matches(_confirmed.ProviderId, _confirmed.VendorId, _confirmed.ProductId) ||
                bank is < 1 or > 8 || FindEditableController(_confirmed.PersistentId) is not { } controller || controller.Layers.Count < bank)
            { return RuntimeOperation.Failed("Confirm the original Razer Tartarus before choosing a keymap."); }
            var session = new ControllerSessionId(_confirmed.SessionId);
            if (_engine.InputStates.GetPressedControls(session).Count != 0)
            { return RuntimeOperation.Failed("Release the Tartarus controls before changing keymaps."); }
            var layer = controller.Layers[bank - 1];
            if (!_engine.SetActiveLayer(session, layer.Id))
            { return RuntimeOperation.Failed("The Tartarus session is no longer connected."); }
            controller.ActiveLayerId = layer.Id;
            _tartarusKeymapRevision++;
            _tartarusVerifiedLedMask = null;
            updates = CreateControlUpdatesLocked(controller, isSnapshot: true);
        }
        PublishReleasedUpdates(updates);
        RaiseState(mappingStatus: $"Tartarus keymap {bank} selected. Assignments in other keymaps are preserved.");
        return RuntimeOperation.Ok($"Tartarus keymap {bank} selected. Save profile to keep this selection.");
    }

    public async Task<RuntimeOperation> SyncTartarusIndicatorsAsync()
    {
        string persistentId;
        long generation, revision;
        int mask;
        lock (_gate)
        {
            if (_tartarusIndicatorOutput is null)
            { return RuntimeOperation.Failed("Tartarus LED synchronization is unavailable in this session. No indicator commands were sent."); }
            if (!CanSyncTartarusLocked() || _tartarusLedSyncPending ||
                FindEditableController(_confirmed!.PersistentId) is not { } controller)
            { return RuntimeOperation.Failed("Confirm an available original Tartarus before syncing its bank LEDs. A sync may already be running."); }
            var bank = controller.Layers.FindIndex(l => l.Id == controller.ActiveLayerId) + 1;
            if (bank is < 1 or > 8) { return RuntimeOperation.Failed("This bank has no Tartarus indicator pattern."); }
            persistentId = _confirmed.PersistentId;
            generation = _confirmationGeneration; revision = _tartarusKeymapRevision;
            mask = RazerTartarusLayout.IndicatorMask(bank);
            _tartarusVerifiedLedMask = null;
            _tartarusLedSyncPending = true;
        }
        bool IsCurrent()
        {
            lock (_gate)
            {
                return CanSyncTartarusLocked() && _confirmed!.PersistentId == persistentId &&
                _confirmationGeneration == generation && _tartarusKeymapRevision == revision;
            }
        }
        RuntimeOperation result;
        try
        {
            var actual = await _tartarusIndicatorOutput.SynchronizeAsync(persistentId, mask, IsCurrent).ConfigureAwait(false);
            lock (_gate)
            {
                if (!IsCurrent()) { return RuntimeOperation.Failed("The controller or keymap changed during LED sync; the old result was discarded."); }
                if (actual != mask) { throw new IOException("Indicator readback differs from this keymap."); }
                _tartarusVerifiedLedMask = actual;
                _tartarusLedConfirmationGeneration = generation;
            }
            result = RuntimeOperation.Ok("Tartarus bank LEDs synchronized and confirmed by device readback.");
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or UnauthorizedAccessException)
        { result = RuntimeOperation.Failed("Tartarus LEDs are not confirmed synchronized. " + error.Message); }
        finally { lock (_gate) { _tartarusLedSyncPending = false; } }
        if (IsCurrent()) { RaiseState(mappingStatus: result.Message); }
        return result;
    }

    private bool CanSyncTartarusLocked() => !_disposed && _confirmed is not null && !_suspended && !_sessionLocked &&
        !_isRememberedReconnect && !_outputSafetyNeedsAttention && !IsProtectedKeyboard(_confirmed) &&
        RazerTartarusLayout.Matches(_confirmed.ProviderId, _confirmed.VendorId, _confirmed.ProductId);

    public RuntimeOperation AssignMapping(string controlId, string outputKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(controlId))
        {
            return RuntimeOperation.Failed("Choose a physical control first.");
        }

        if (!IsHarmlessOutputKey(outputKey))
        {
            return RuntimeOperation.Failed("The first milestone permits only F13 through F24 as harmless starter outputs.");
        }

        return AssignKeyboardMapping(
            controlId,
            KeyboardMappingAssignment.HoldUntilRelease($"Hold {outputKey} until release", [outputKey]));
    }

    public RuntimeOperation AssignKeyboardMapping(string controlId, KeyboardMappingAssignment assignment)
    {
        lock (_gate)
        {
            var report = AssignmentReportLocked(controlId);
            return report is null ? RuntimeOperation.Failed("This hardware-mode/reference control has no reliable independent assignment signal.")
                : AssignKeyboardMappingReport(report, assignment);
        }
    }

    private RuntimeOperation AssignKeyboardMappingReport(string controlId, KeyboardMappingAssignment assignment)
    {
        if (controlId == TargusKeypadLayout.TripleZeroReference)
        { return RuntimeOperation.Failed("The 000 key's signal must be verified before it can have an independent assignment."); }
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(controlId))
        {
            return RuntimeOperation.Failed("Choose a physical control first.");
        }

        ArgumentNullException.ThrowIfNull(assignment);
        var pressAction = CreateKeyboardAction(assignment.PressMode, assignment.PressKeys);
        var releaseAction = CreateKeyboardAction(assignment.ReleaseMode, assignment.ReleaseKeys);
        if (pressAction is null || releaseAction is null)
        {
            return RuntimeOperation.Failed(
                "That shortcut contains an unsupported key or exceeds Tappy's eight-key chord safety limit.");
        }

        if (releaseAction.Mode == KeyboardActionMode.HoldUntilRelease)
        {
            return RuntimeOperation.Failed("A release action must tap once; it cannot begin a held output.");
        }

        if (pressAction.Mode == KeyboardActionMode.None && releaseAction.Mode == KeyboardActionMode.None)
        {
            return RuntimeOperation.Failed("Choose a keyboard action before assigning this control.");
        }

        var bindingName = string.IsNullOrWhiteSpace(assignment.Name)
            ? FormatKeyboardBinding(pressAction, releaseAction)
            : assignment.Name.Trim();

        lock (_gate)
        {
            if (_outputSafetyNeedsAttention)
            {
                return RuntimeOperation.Failed(
                    "An earlier Tappy-owned output release was rejected. Rehearsal Mode remains on; restart Tappy before assigning or arming output.");
            }

            if (_confirmed is null)
            {
                return RuntimeOperation.Failed("Select, identify, release, and confirm a spare controller before assigning it.");
            }

            var controller = FindEditableController(_confirmed.PersistentId);
            if (controller is null)
            {
                return RuntimeOperation.Failed("The confirmed controller has no profile. Nothing was changed.");
            }

            var layer = controller.Layers.First(item => item.Id == controller.ActiveLayerId);
            var id = new ControlId(controlId);
            if (CombinationRequiresRunOnce(controller, id) && pressAction.Mode == KeyboardActionMode.HoldUntilRelease)
            { return RuntimeOperation.Failed("This button participates in a combination. Choose Run once when pressed, not hold/repeat; combination gestures execute on release."); }
            if (_confirmed.ProviderId == LogitechG13ProviderId &&
                !LogitechG13InputProvider.SupportedControls.Any(item => item.ControlId == id) && !controller.Combinations.Any(c => c.ControlId == id))
            {
                return RuntimeOperation.Failed("That control does not belong to the confirmed Logitech G13 layout.");
            }

            var binding = layer.Bindings.FirstOrDefault(item => item.ControlId == id);
            if (binding is null)
            {
                binding = new ControlBindingDefinition { ControlId = id };
                layer.Bindings.Add(binding);
            }

            binding.Name = bindingName;
            binding.Enabled = true;
            binding.PressAction = pressAction;
            binding.ReleaseAction = releaseAction;
            binding.PressSequence = new ControllerActionSequenceDefinition();
            binding.ReleaseSequence = new ControllerActionSequenceDefinition();
            EnsureObservedLayout(controller, id);
            if (!RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot())))
            {
                _isRehearsal = true;
                _ = _engine.SetRehearsalMode(true);
                return RuntimeOperation.Failed(
                    "The mapping was updated, but Windows rejected a prior Tappy-owned output release. Rehearsal Mode remains on; restart Tappy before arming output.");
            }
            _engine.ConnectController(controller.Identity);
        }

        return RuntimeOperation.Ok(
            $"Mapped this control to {bindingName}. Rehearsal Mode currently {(IsRehearsal ? "suppresses" : "allows")} output.");
    }

    public ControllerAssignmentEditContext? BeginAssignmentEdit(string controlId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_confirmed is null || string.IsNullOrWhiteSpace(controlId) || controlId == TargusKeypadLayout.TripleZeroReference ||
                FindEditableController(_confirmed.PersistentId) is not { } controller ||
                !controller.Layout.Rows.SelectMany(row => row.Controls)
                    .Any(control => control.ControlId?.Value == controlId))
            {
                return null;
            }

            var report = AssignmentReportLocked(controlId);
            if (report is null) { return null; }
            EnsureObservedLayout(controller, new ControlId(report));
            return new ControllerAssignmentEditContext(
                new ControllerAssignmentTarget(_confirmed.SessionId, _confirmed.PersistentId,
                    _confirmed.ProviderId, controller.ActiveLayerId, report, _confirmationGeneration),
                GetControllerActionReport(report));
        }
    }

    public RuntimeOperation AssignKeyboardMapping(
        ControllerAssignmentTarget target, KeyboardMappingAssignment assignment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            return IsCurrentAssignmentTargetLocked(target)
                ? AssignKeyboardMappingReport(target.ControlId, assignment)
                : RuntimeOperation.Failed("The controller or layer changed while this assignment was being edited. " +
                    "Nothing was changed; your draft remains open.");
        }
    }

    public RuntimeOperation AssignControllerAction(
        ControllerAssignmentTarget target, ControllerActionAssignment assignment)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            return IsCurrentAssignmentTargetLocked(target)
                ? AssignControllerActionReport(target.ControlId, assignment)
                : RuntimeOperation.Failed("The controller or layer changed while this assignment was being edited. " +
                    "Nothing was changed; your draft remains open.");
        }
    }

    private bool IsCurrentAssignmentTargetLocked(ControllerAssignmentTarget target) =>
        _confirmed is not null && target.ConfirmationGeneration == _confirmationGeneration &&
        (!IsTargus(_confirmed) || _targusBank.AssignmentReport(target.ControlId) is not null) &&
        target.ControllerSessionId == _confirmed.SessionId &&
        target.ControllerPersistentId == _confirmed.PersistentId && target.ProviderId == _confirmed.ProviderId &&
        FindEditableController(_confirmed.PersistentId) is { } controller &&
        target.LayerId == controller.ActiveLayerId &&
        controller.Layout.Rows.SelectMany(row => row.Controls)
            .Any(control => control.ControlId?.Value == target.ControlId);

    public ControllerActionAssignment? GetControllerAction(string controlId)
    {
        lock (_gate)
        {
            var report = AssignmentReportLocked(controlId);
            return report is null ? null : GetControllerActionReport(report);
        }
    }

    private ControllerActionAssignment? GetControllerActionReport(string controlId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(controlId))
        {
            return null;
        }

        lock (_gate)
        {
            if (_confirmed is null)
            {
                return null;
            }

            var controller = FindEditableController(_confirmed.PersistentId);
            var layer = controller?.Layers.FirstOrDefault(item => item.Id == controller.ActiveLayerId);
            var binding = layer?.Bindings.FirstOrDefault(item =>
                item.Enabled && item.ControlId.Value == controlId);
            if (binding is null)
            {
                return null;
            }

            var press = binding.PressSequence?.Clone() ?? new ControllerActionSequenceDefinition();
            var release = binding.ReleaseSequence?.Clone() ?? new ControllerActionSequenceDefinition();
            if (press.IsEmpty && binding.PressAction is { Mode: not KeyboardActionMode.None } pressAction)
            {
                press = KeyboardActionToSequence(binding.Name, pressAction);
            }

            if (release.IsEmpty && binding.ReleaseAction is { Mode: not KeyboardActionMode.None } releaseAction)
            {
                release = KeyboardActionToSequence(binding.Name, releaseAction);
                release.Mode = ControllerActionSequenceMode.RunOnce;
            }

            if (press.IsEmpty && release.IsEmpty)
            {
                return null;
            }

            return new ControllerActionAssignment(binding.Name, press, release);
        }
    }

    public RuntimeOperation ReorderControls(IReadOnlyList<string> orderedControlIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(orderedControlIds);
        lock (_gate)
        {
            if (_confirmed is null)
            {
                return RuntimeOperation.Failed("Confirm a controller before rearranging its squares.");
            }

            var controller = FindEditableController(_confirmed.PersistentId);
            if (controller is null)
            {
                return RuntimeOperation.Failed("The confirmed controller has no editable layout.");
            }

            var current = GetControlsInPresentationOrder(controller);
            var requested = orderedControlIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new ControlId(value))
                .ToArray();
            if (requested.Length != current.Count ||
                requested.Distinct().Count() != requested.Length ||
                !requested.ToHashSet().SetEquals(current))
            {
                return RuntimeOperation.Failed("The controller layout changed while the square was being moved. Refresh and try again.");
            }

            var retainedAliases = controller.Layout.Rows.SelectMany(row => row.Controls)
                .Where(key => key.ControlId is not null).Select(key => key.ControlId!.Value)
                .Where(id => !requested.Contains(id));
            ApplyPresentationOrder(controller.Layout, requested.Concat(retainedAliases).ToArray());
            controller.Normalize();
            if (!RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot())))
            {
                return RuntimeOperation.Failed(
                    "The square order changed locally, but Tappy could not safely publish the updated profile. Restart before arming output.");
            }

            _engine.ConnectController(controller.Identity);
        }

        return RuntimeOperation.Ok("Controller square arrangement updated. Save the profile to keep it for this device.");
    }

    public ControllerLayoutWorkspace? GetControllerLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var displayed = PresentedControllerLocked();
            if (displayed is null || FindEditableController(displayed.PersistentId) is not { } controller)
            {
                return null;
            }

            var controls = controller.Layout.Rows
                .SelectMany((row, rowIndex) => row.Controls.Select((control, columnIndex) =>
                    (Control: control, Row: rowIndex, Column: columnIndex)))
                .Where(item => item.Control.ControlId is not null)
                .Where(item => !IsTargus(displayed) || !TargusKeypadLayout.IsHiddenPresentationReport(item.Control.ControlId!.Value.Value))
                .Select(item => new ControllerControlPlacement(
                    item.Control.ControlId!.Value.Value,
                    item.Row,
                    item.Column,
                    item.Control.X,
                    item.Control.Y,
                    item.Control.Width,
                    item.Control.Height,
                    item.Control.ColorKey,
                    item.Control.AnalogRawAtMinimum,
                    item.Control.AnalogRawAtMaximum,
                    item.Control.AnalogRawAtCenter,
                    item.Control.EncoderDegreesPerStep,
                    item.Control.EncoderReversed))
                .ToArray();
            return new ControllerLayoutWorkspace(
                controller.Layout.GridColumns,
                controller.Layout.GridRows,
                controller.Layout.SnapToGrid,
                controls,
                controller.Layout.Zoom,
                controller.Layout.PhotoPaneWidth,
                controller.Layout.IsLayoutLocked);
        }
    }

    public RuntimeOperation UpdateControllerLayout(ControllerLayoutWorkspace workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workspace);
        lock (_gate)
        {
            if (_confirmed is null || FindEditableController(_confirmed.PersistentId) is not { } controller)
            {
                return RuntimeOperation.Failed("Confirm a controller before changing its layout workspace.");
            }

            var definitions = controller.Layout.Rows
                .SelectMany(row => row.Controls)
                .Where(control => control.ControlId is not null)
                .GroupBy(control => control.ControlId!.Value.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var requested = workspace.Controls
                .Select(control => control.ControlId)
                .ToArray();
            var visibleIds = definitions.Keys.Where(id => !IsTargus(_confirmed) || !TargusKeypadLayout.IsHiddenPresentationReport(id)).ToArray();
            if (requested.Length != visibleIds.Length ||
                requested.Distinct(StringComparer.Ordinal).Count() != requested.Length ||
                !requested.ToHashSet(StringComparer.Ordinal).SetEquals(visibleIds))
            {
                return RuntimeOperation.Failed(
                    "The controller controls changed while its layout was being edited. Refresh and try again.");
            }

            controller.Layout.GridColumns = Math.Clamp(workspace.GridColumns, 1, 40);
            controller.Layout.GridRows = Math.Clamp(workspace.GridRows, 1, 40);
            controller.Layout.SnapToGrid = workspace.SnapToGrid;
            controller.Layout.Zoom = Math.Clamp(workspace.Zoom, 0.25, 2.0);
            controller.Layout.PhotoPaneWidth = Math.Clamp(workspace.PhotoPaneWidth, 240, 1200);
            controller.Layout.IsLayoutLocked = workspace.IsLayoutLocked;
            foreach (var placement in workspace.Controls)
            {
                var definition = definitions[placement.ControlId];
                definition.X = double.IsFinite(placement.X ?? double.NaN)
                    ? Math.Max(0, placement.X!.Value)
                    : null;
                definition.Y = double.IsFinite(placement.Y ?? double.NaN)
                    ? Math.Max(0, placement.Y!.Value)
                    : null;
                definition.Width = placement.Width;
                definition.Height = placement.Height;
                definition.ColorKey = placement.ColorKey;
                definition.AnalogRawAtMinimum = placement.AnalogRawAtMinimum;
                definition.AnalogRawAtMaximum = placement.AnalogRawAtMaximum;
                definition.AnalogRawAtCenter = placement.AnalogRawAtCenter;
                definition.EncoderDegreesPerStep = placement.EncoderDegreesPerStep;
                definition.EncoderReversed = placement.EncoderReversed;
            }

            controller.Normalize();
            if (!RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot())))
            {
                return RuntimeOperation.Failed(
                    "The layout changed locally, but Tappy could not safely publish the updated profile. Restart before arming output.");
            }

            _engine.ConnectController(controller.Identity);
        }

        return RuntimeOperation.Ok("Controller layout updated. Save the profile to retain its positions and sizes.");
    }

    public RuntimeOperation SyncControllerLedColors(IReadOnlyDictionary<string, string> colors)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(colors);
        RuntimeInputDevice confirmed;
        lock (_gate)
        {
            if (_confirmed is null)
            {
                return RuntimeOperation.Failed("Confirm a controller before synchronizing its lights.");
            }

            confirmed = _confirmed;
        }

        if (confirmed.ProviderId == LogitechG13ProviderId)
        {
            if (_logitechG13LightingOutput is null)
            {
                return RuntimeOperation.Failed("Logitech G13 lighting output is unavailable in this Tappy session.");
            }

            var requestedColors = colors.Values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (requestedColors.Length > 1)
            {
                return RuntimeOperation.Failed(
                    "The G13 has one whole-device RGB zone. Choose one color for the complete controller before syncing.");
            }

            var colorKey = requestedColors.SingleOrDefault() ?? "Default";
            var (red, green, blue) = G13Rgb(colorKey);
            try
            {
                _logitechG13LightingOutput.SetBacklightRgb(confirmed.PersistentId, red, green, blue);
                return RuntimeOperation.Ok(
                    $"Synchronized the G13 whole-device backlight to {G13ColorLabel(colorKey)}. Its keys share this one RGB color.");
            }
            catch (Exception exception)
            {
                return RuntimeOperation.Failed($"The confirmed Logitech G13 rejected lighting synchronization: {exception.Message}");
            }
        }

        if (_controllerLedOutput is null)
        {
            return RuntimeOperation.Failed("Hardware LED output is unavailable in this Tappy session.");
        }

        var ledModelId = MidiControllerModelCatalog.Find(confirmed.ProviderId, confirmed.DisplayName)?.Id;
        if (ledModelId == MidiControllerModelCatalog.AkaiFireV1Id)
        {
            return SyncAkaiFirePadColors(confirmed, colors);
        }

        if (ledModelId != MidiControllerModelCatalog.AkaiApcMiniV1Id)
        {
            return RuntimeOperation.Failed(
                "Tappy has no verified lighting protocol for this controller. Its square colors remain visual only.");
        }

        var outputs = MatchMidiOutputPorts(WinMmMidiOutput.GetDevices(false), confirmed.DisplayName);
        if (outputs.Length != 1)
        {
            return RuntimeOperation.Failed(outputs.Length == 0
                ? "The APC MINI MIDI output port is not available. Square colors were kept in Tappy."
                : "More than one APC MINI output port shares this exact name, so Tappy refused to guess which unit to light.");
        }

        var messages = colors
            .Select(item => (Note: TryParseApcMiniLedNote(item.Key), Color: item.Value))
            .Where(item => item.Note is not null)
            .OrderBy(item => item.Note)
            .ToArray();
        try
        {
            foreach (var item in messages)
            {
                _controllerLedOutput.Send(
                    outputs[0].Name,
                    MidiMessageParser.Create(
                        MidiShortMessageKind.NoteOn,
                        channel: 1,
                        item.Note!.Value,
                        ApcMiniVelocity(item.Color)));
            }

            return RuntimeOperation.Ok(
                $"Synchronized {messages.Length} APC MINI lights. This model uses green, amber, red, or off; Tappy maps the visual palette to those verified colors.");
        }
        catch (Exception exception)
        {
            return RuntimeOperation.Failed($"The APC MINI rejected LED synchronization: {exception.Message}");
        }
    }

    /// <summary>
    /// Pads are the only addressable Fire control today; button/rectangle/
    /// bank-indicator CC lighting and the OLED remain unimplemented. See
    /// docs/AKAI_FIRE.md. The output port is matched by its exact WinMM name
    /// against this specific confirmed device, not just "any Fire-shaped
    /// output" — with two Fires connected, both resolve to the same model Id,
    /// so matching by model alone can no longer identify a single port. See
    /// D-024 in DECISIONS.md.
    /// </summary>
    private RuntimeOperation SyncAkaiFirePadColors(RuntimeInputDevice confirmed, IReadOnlyDictionary<string, string> colors)
    {
        var fireOutputs = MatchMidiOutputPorts(WinMmMidiOutput.GetDevices(false), confirmed.DisplayName);
        if (fireOutputs.Length != 1)
        {
            return RuntimeOperation.Failed(fireOutputs.Length == 0
                ? "The Fire MIDI output port is not available. Square colors were kept in Tappy."
                : "More than one Fire output port shares this exact name, so Tappy refused to guess which unit to light.");
        }

        var pads = colors
            .Select(item => (PadIndex: TryParseAkaiFirePadIndex(item.Key), Color: item.Value))
            .Where(item => item.PadIndex is not null)
            .OrderBy(item => item.PadIndex)
            .Select(item =>
            {
                var (red, green, blue) = FireRgb(item.Color);
                return (PadIndex: item.PadIndex!.Value, Red: red, Green: green, Blue: blue);
            })
            .ToArray();
        if (pads.Length == 0)
        {
            return RuntimeOperation.Ok("No addressable Fire pads had a color to synchronize.");
        }

        try
        {
            _controllerLedOutput!.SendSysEx(fireOutputs[0].Name, FirePadColorSysEx(pads));
            return RuntimeOperation.Ok($"Synchronized {pads.Length} Akai Fire pad lights.");
        }
        catch (Exception exception)
        {
            return RuntimeOperation.Failed($"The Fire rejected LED synchronization: {exception.Message}");
        }
    }

    /// <summary>
    /// WinMM input descriptors are presented as "MIDI: {port name}" (see
    /// WinMmMidiInputProvider.CreateDescriptors); WinMmMidiOutput.GetDevices
    /// reports the same port name without that prefix. Stripping it here lets
    /// a confirmed input device be matched to its own output port by exact
    /// name, rather than by model alone — matching by model alone can no
    /// longer identify a single port once two same-model units (e.g. two
    /// Fires) are connected simultaneously. See D-024 in DECISIONS.md.
    /// </summary>
    internal static string StripMidiDisplayPrefix(string displayName) =>
        displayName.StartsWith("MIDI: ", StringComparison.OrdinalIgnoreCase)
            ? displayName["MIDI: ".Length..]
            : displayName;

    internal static WinMmMidiOutput.Device[] MatchMidiOutputPorts(
        IEnumerable<WinMmMidiOutput.Device> outputs, string confirmedDisplayName)
    {
        var confirmedPortName = StripMidiDisplayPrefix(confirmedDisplayName);
        return outputs.Where(device => string.Equals(device.Name, confirmedPortName, StringComparison.Ordinal))
            .ToArray();
    }

    internal static readonly IReadOnlyList<string> AkaiFireColorKeys =
        ["Default", "Red", "Amber", "Orange", "Yellow", "Green", "Teal", "Blue", "Purple", "Pink", "Gray", "White"];

    internal static int? TryParseAkaiFirePadIndex(string controlId)
    {
        const string marker = ":note-";
        var markerIndex = controlId.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 ||
            !int.TryParse(controlId.AsSpan(markerIndex + marker.Length), out var note))
        {
            return null;
        }

        // Pads are notes 0x36-0x75 (54-117), row-major; the SysEx pad index below
        // is row*16+col, i.e. note-0x36, matching AkaiFireV1's pad layout exactly.
        return note is >= 0x36 and <= 0x75 ? note - 0x36 : null;
    }

    internal static (byte Red, byte Green, byte Blue) FireRgb(string? colorKey)
    {
        var trimmed = colorKey?.Trim();
        if (TryParseHexColor(trimmed, out var red, out var green, out var blue))
        {
            return (red, green, blue);
        }

        return trimmed?.ToLowerInvariant() switch
        {
            "red" => (255, 0, 0),
            "amber" => (255, 170, 0),
            "orange" => (255, 90, 0),
            "yellow" => (255, 255, 0),
            "green" => (0, 255, 0),
            "teal" => (0, 255, 170),
            "blue" => (0, 90, 255),
            "purple" => (150, 0, 255),
            "pink" => (255, 0, 150),
            "gray" => (120, 120, 120),
            "white" => (255, 255, 255),
            _ => (0, 0, 0),
        };
    }

    /// <summary>
    /// Parses a "#RRGGBB" freeform color, the format the Fire's full RGB picker
    /// writes into a control's saved ColorKey. Named starter-palette keys never
    /// match this shape and fall through to the switch above unchanged.
    /// </summary>
    internal static bool TryParseHexColor(string? value, out byte red, out byte green, out byte blue)
    {
        red = green = blue = 0;
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }

        return byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) &&
               byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) &&
               byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue);
    }

    /// <summary>
    /// F0 47 7F 43 65 &lt;lenHi&gt; &lt;lenLo&gt; [index red green blue]... F7 — one
    /// message for every changed pad. Manufacturer id 47 (Akai) and product id 43
    /// independently confirmed (MIDI Manufacturer ID registry; the Fire's own USB
    /// PID is 0043). Each 8-bit color channel is halved to fit MIDI's 7-bit limit,
    /// matching the cross-check project's own encoder. See docs/AKAI_FIRE.md.
    /// </summary>
    internal static byte[] FirePadColorSysEx(IReadOnlyList<(int PadIndex, byte Red, byte Green, byte Blue)> pads)
    {
        var payloadLength = pads.Count * 4;
        var packet = new byte[8 + payloadLength];
        packet[0] = 0xF0; packet[1] = 0x47; packet[2] = 0x7F; packet[3] = 0x43; packet[4] = 0x65;
        packet[5] = (byte)(payloadLength >> 7);
        packet[6] = (byte)(payloadLength & 0x7F);
        var offset = 7;
        foreach (var pad in pads)
        {
            packet[offset++] = (byte)pad.PadIndex;
            packet[offset++] = (byte)(pad.Red >> 1);
            packet[offset++] = (byte)(pad.Green >> 1);
            packet[offset++] = (byte)(pad.Blue >> 1);
        }

        packet[offset] = 0xF7;
        return packet;
    }

    /// <summary>
    /// Sends a fixed, minimal test pattern to a confirmed Fire's OLED. Thin
    /// wrapper over SendFireOledFrame — kept as its own method since it is
    /// the one physically-proven-safe entry point referenced throughout
    /// docs/AKAI_FIRE.md (D-027/D-028).
    /// </summary>
    public RuntimeOperation SendFireOledDiagnosticPattern() => SendFireOledFrame(FireOledDiagnosticPattern());

    /// <summary>
    /// Sends an arbitrary 128x64 monochrome bitmap to a confirmed Fire's
    /// OLED via the documented SysEx write (sub-ID 0E), physically proven
    /// safe with the diagnostic pattern (D-027) and confirmed on two real
    /// units at once (D-028). The Fire's OLED reverts to black on its own a
    /// few seconds after its last update — this method sends exactly one
    /// frame; holding content visible is the caller's responsibility (see
    /// FireOledContentPlayer). See docs/AKAI_FIRE.md.
    /// </summary>
    public RuntimeOperation SendFireOledFrame(bool[,] pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RuntimeInputDevice confirmed;
        lock (_gate)
        {
            if (_confirmed is null)
            {
                return RuntimeOperation.Failed("Confirm a controller before sending to its OLED display.");
            }

            confirmed = _confirmed;
        }

        if (MidiControllerModelCatalog.Find(confirmed.ProviderId, confirmed.DisplayName)?.Id !=
            MidiControllerModelCatalog.AkaiFireV1Id)
        {
            return RuntimeOperation.Failed("Only the Akai Fire has a Tappy-supported OLED display.");
        }

        if (_controllerLedOutput is null)
        {
            return RuntimeOperation.Failed("Hardware LED/OLED output is unavailable in this Tappy session.");
        }

        var fireOutputs = MatchMidiOutputPorts(WinMmMidiOutput.GetDevices(false), confirmed.DisplayName);
        if (fireOutputs.Length != 1)
        {
            return RuntimeOperation.Failed(fireOutputs.Length == 0
                ? "The Fire MIDI output port is not available. Nothing was sent to its OLED."
                : "More than one Fire output port shares this exact name, so Tappy refused to guess which unit's OLED to write.");
        }

        try
        {
            _controllerLedOutput.SendSysEx(fireOutputs[0].Name, FireOledFrameSysEx(pixels));
            return RuntimeOperation.Ok("Sent an OLED frame to the Fire's display.");
        }
        catch (Exception exception)
        {
            return RuntimeOperation.Failed($"The Fire rejected the OLED write: {exception.Message}");
        }
    }

    internal const int FireOledWidth = 128;
    internal const int FireOledHeight = 64;
    private const int FireOledPackedPayloadLength = 1171; // ceil(128*64/7)

    /// <summary>
    /// F0 47 7F 43 0E &lt;lenHi&gt; &lt;lenLo&gt; [00 07 00 7F][1,171 packed bytes] F7 —
    /// writes the Fire's full 128x64 monochrome OLED framebuffer in one message.
    /// Sub-ID 0E = write OLED (SEGGER part 3). The length bytes cover everything
    /// after them up to (not including) F7, matching FirePadColorSysEx's framing
    /// convention. See docs/AKAI_FIRE.md.
    /// </summary>
    internal static byte[] FireOledFrameSysEx(bool[,] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.GetLength(0) != FireOledWidth || pixels.GetLength(1) != FireOledHeight)
        {
            throw new ArgumentException($"Expected a {FireOledWidth}x{FireOledHeight} pixel buffer.", nameof(pixels));
        }

        var payload = new byte[FireOledPackedPayloadLength];
        for (var y = 0; y < FireOledHeight; y++)
        {
            for (var x = 0; x < FireOledWidth; x++)
            {
                if (!pixels[x, y]) { continue; }
                var (index, bit) = FireOledBitPosition(x, y);
                payload[index] |= (byte)(1 << bit);
            }
        }

        const int headerPayloadLength = 4 + FireOledPackedPayloadLength; // "00 07 00 7F" + packed bytes
        var packet = new byte[7 + headerPayloadLength + 1];
        packet[0] = 0xF0; packet[1] = 0x47; packet[2] = 0x7F; packet[3] = 0x43; packet[4] = 0x0E;
        packet[5] = (byte)(headerPayloadLength >> 7);
        packet[6] = (byte)(headerPayloadLength & 0x7F);
        packet[7] = 0x00; packet[8] = 0x07; packet[9] = 0x00; packet[10] = 0x7F;
        Array.Copy(payload, 0, packet, 11, payload.Length);
        packet[^1] = 0xF7;
        return packet;
    }

    /// <summary>
    /// The Fire's OLED bit ordering is not a linear framebuffer scan — SEGGER
    /// found "no obvious logic" after exhaustive testing. This closed-form
    /// tiling is reused verbatim from that reverse-engineering, independently
    /// cross-checked against another implementation of the same formula; it is
    /// treated as authoritative here, not re-derived. See docs/AKAI_FIRE.md.
    /// </summary>
    internal static (int Index, int Bit) FireOledBitPosition(int x, int y)
    {
        var column = x + FireOledWidth * (y / 8);
        var tileColumn = column % 7;
        var row = y % 8;
        var bit = 6 * tileColumn + row + (row <= tileColumn ? 13 : -1);
        return (column / 7 * 8 + bit / 7, bit % 7);
    }

    /// <summary>
    /// A minimal, visually unambiguous first OLED payload: a full one-pixel
    /// border (proves every edge maps to clean lines rather than scrambled
    /// noise) plus a solid 8x8 square in the top-left corner only (proves
    /// orientation — a swapped or mirrored axis would show it in the wrong
    /// corner). Deliberately simple: this isolates the packing formula from any
    /// text/font-rendering risk for the very first real write to this
    /// sub-command.
    /// </summary>
    internal static bool[,] FireOledDiagnosticPattern()
    {
        var pixels = new bool[FireOledWidth, FireOledHeight];
        for (var x = 0; x < FireOledWidth; x++)
        {
            pixels[x, 0] = true;
            pixels[x, FireOledHeight - 1] = true;
        }

        for (var y = 0; y < FireOledHeight; y++)
        {
            pixels[0, y] = true;
            pixels[FireOledWidth - 1, y] = true;
        }

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                pixels[x, y] = true;
            }
        }

        return pixels;
    }

    public ControllerLedColorCapability? GetControllerLedColorCapability()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var displayed = PresentedControllerLocked();
            if (displayed is not null &&
                MidiControllerModelCatalog.Find(displayed.ProviderId, displayed.DisplayName)?.Id ==
                MidiControllerModelCatalog.AkaiFireV1Id)
            {
                return new ControllerLedColorCapability(
                    MidiControllerModelCatalog.AkaiFireV1Id,
                    "Akai Fire pads — full RGB",
                    AkaiFireColorKeys,
                    FindEditableController(displayed.PersistentId)?.Layout.Rows
                        .SelectMany(row => row.Controls)
                        .Where(control => control.ControlId is { } id &&
                                          TryParseAkaiFirePadIndex(id.Value) is not null)
                        .Select(control => control.ControlId!.Value.Value)
                        .ToHashSet(StringComparer.Ordinal) ??
                    new HashSet<string>(StringComparer.Ordinal),
                    SupportsFreeformColor: true);
            }

            if (displayed is not null &&
                MidiControllerModelCatalog.Find(displayed.ProviderId, displayed.DisplayName)?.Id ==
                MidiControllerModelCatalog.AkaiApcMiniV1Id)
            {
                return new ControllerLedColorCapability(
                    MidiControllerModelCatalog.AkaiApcMiniV1Id,
                    "APC MINI v1 — off, green, red, and amber",
                    ["Default", "Green", "Red", "Amber"],
                    FindEditableController(displayed.PersistentId)?.Layout.Rows
                        .SelectMany(row => row.Controls)
                        .Where(control => control.ControlId is { } id &&
                                          TryParseApcMiniLedNote(id.Value) is not null)
                        .Select(control => control.ControlId!.Value.Value)
                        .ToHashSet(StringComparer.Ordinal) ??
                    new HashSet<string>(StringComparer.Ordinal));
            }

            if (displayed?.ProviderId == LogitechG13ProviderId)
            {
                return new ControllerLedColorCapability(
                    "logitech-g13-global-rgb",
                    "G13 lighting — one whole-device RGB zone; individual key colors are unavailable",
                    ["Default", "Red", "Amber", "Yellow", "Green", "Teal", "Blue", "Purple", "Pink", "White"],
                    new HashSet<string>(StringComparer.Ordinal),
                    ControllerLightingTopology.Global);
            }

            return null;
        }
    }

    internal static int? TryParseApcMiniLedNote(string controlId)
    {
        const string marker = ":note-";
        var markerIndex = controlId.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 ||
            !int.TryParse(controlId.AsSpan(markerIndex + marker.Length), out var note))
        {
            return null;
        }

        return note is >= 0 and <= 71 or >= 82 and <= 89 ? note : null;
    }

    internal static int ApcMiniVelocity(string? colorKey) =>
        colorKey?.Trim().ToLowerInvariant() switch
        {
            "green" or "teal" => 1,
            "red" or "pink" => 3,
            "amber" or "orange" or "yellow" or "white" => 5,
            _ => 0,
        };

    internal static (byte Red, byte Green, byte Blue) G13Rgb(string? colorKey) =>
        colorKey?.Trim().ToLowerInvariant() switch
        {
            "red" => (255, 0, 0),
            "amber" => (255, 160, 0),
            "yellow" => (255, 255, 0),
            "green" => (0, 255, 0),
            "teal" => (0, 255, 200),
            "blue" => (0, 90, 255),
            "purple" => (150, 0, 255),
            "pink" => (255, 0, 150),
            "white" => (255, 255, 255),
            _ => (0, 0, 0),
        };

    private static string G13ColorLabel(string? colorKey) =>
        string.IsNullOrWhiteSpace(colorKey) ||
        string.Equals(colorKey, "Default", StringComparison.OrdinalIgnoreCase)
            ? "off/default"
            : colorKey.Trim();

    public RuntimeOperation AssignControllerAction(string controlId, ControllerActionAssignment assignment)
    {
        lock (_gate)
        {
            var report = AssignmentReportLocked(controlId);
            return report is null ? RuntimeOperation.Failed("This hardware-mode/reference control has no reliable independent assignment signal.")
                : AssignControllerActionReport(report, assignment);
        }
    }

    private RuntimeOperation AssignControllerActionReport(string controlId, ControllerActionAssignment assignment)
    {
        if (controlId == TargusKeypadLayout.TripleZeroReference)
        { return RuntimeOperation.Failed("The 000 key's signal must be verified before it can have an independent assignment."); }
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(controlId))
        {
            return RuntimeOperation.Failed("Choose a physical control first.");
        }

        ArgumentNullException.ThrowIfNull(assignment);
        var press = assignment.PressSequence?.Clone() ?? new ControllerActionSequenceDefinition();
        var release = assignment.ReleaseSequence?.Clone() ?? new ControllerActionSequenceDefinition();
        press.Normalize();
        release.Normalize();
        release.Mode = ControllerActionSequenceMode.RunOnce;
        if (press.IsEmpty && release.IsEmpty)
        {
            return RuntimeOperation.Failed("Add at least one output action before assigning this control.");
        }

        var validation = ValidateSequence(press) ?? ValidateSequence(release) ??
                         ValidateMidiLifetime(press, release);
        if (validation is not null)
        {
            return RuntimeOperation.Failed(validation);
        }

        var bindingName = string.IsNullOrWhiteSpace(assignment.Name)
            ? (press.IsEmpty ? release.Name : press.Name)
            : assignment.Name.Trim();
        if (string.IsNullOrWhiteSpace(bindingName))
        {
            bindingName = press.IsEmpty ? "Release action" : "Controller action";
        }

        lock (_gate)
        {
            if (_outputSafetyNeedsAttention)
            {
                return RuntimeOperation.Failed(
                    "An earlier Tappy-owned output release was rejected. Rehearsal Mode remains on; restart Tappy before assigning or arming output.");
            }

            if (_confirmed is null)
            {
                return RuntimeOperation.Failed("Select, identify, release, and confirm a spare controller before assigning it.");
            }

            var controller = FindEditableController(_confirmed.PersistentId);
            if (controller is null)
            {
                return RuntimeOperation.Failed("The confirmed controller has no profile. Nothing was changed.");
            }

            var layer = controller.Layers.First(item => item.Id == controller.ActiveLayerId);
            var id = new ControlId(controlId);
            if (CombinationRequiresRunOnce(controller, id) && !press.IsEmpty && press.Mode != ControllerActionSequenceMode.RunOnce)
            { return RuntimeOperation.Failed("This button participates in a combination. Choose Run once when pressed; held/repeating combination macros are unavailable."); }
            if (_confirmed.ProviderId == LogitechG13ProviderId &&
                !LogitechG13InputProvider.SupportedControls.Any(item => item.ControlId == id) && !controller.Combinations.Any(c => c.ControlId == id))
            {
                return RuntimeOperation.Failed("That control does not belong to the confirmed Logitech G13 layout.");
            }

            var binding = layer.Bindings.FirstOrDefault(item => item.ControlId == id);
            if (binding is null)
            {
                binding = new ControlBindingDefinition { ControlId = id };
                layer.Bindings.Add(binding);
            }

            binding.Name = bindingName;
            binding.Enabled = true;
            binding.PressAction = new KeyboardActionDefinition();
            binding.ReleaseAction = new KeyboardActionDefinition();
            binding.PressSequence = press;
            binding.ReleaseSequence = release;
            EnsureObservedLayout(controller, id);
            if (!RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot())))
            {
                _isRehearsal = true;
                _ = _engine.SetRehearsalMode(true);
                return RuntimeOperation.Failed(
                    "The assignment was updated, but cleanup of prior output failed. Rehearsal Mode remains on; restart Tappy before arming output.");
            }

            _engine.ConnectController(controller.Identity);
        }

        return RuntimeOperation.Ok(
            $"Mapped this control to {bindingName}. Rehearsal Mode currently {(IsRehearsal ? "suppresses" : "allows")} output.");
    }

    public async Task<RuntimeOperation> SaveProfileAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TappyProfileSnapshot snapshot;
        lock (_gate)
        {
            snapshot = _editableProfile.CreateSnapshot();
        }

        await _profileStore.SaveAsync(DefaultProfileId, snapshot, cancellationToken).ConfigureAwait(false);
        return RuntimeOperation.Ok($"Profile saved atomically to {_profileStore.GetProfilePath(DefaultProfileId)}.");
    }

    public RuntimeOperation EmergencyStop(string reason)
    {
        RuntimeControlUpdate[] releasedUpdates;
        bool releaseSucceeded;
        lock (_gate)
        {
            _lifecycleResume = null;
            _rememberedReconnect = null;
            _isRememberedReconnect = false;
            // In-memory only, this session: does not touch the persisted profile
            // field, so a clean exit right after this still saves correctly.
            _startupRestorePersistentId = null;
            releasedUpdates = CaptureReleasedUpdatesLocked();
            releaseSucceeded = RecordCleanupResultLocked(_engine.EmergencyStop()) &&
                               !_outputSafetyNeedsAttention;
            _engine.Activation.Reset();
            _isRehearsal = true;
            _engine.SetRehearsalMode(true);
            ClearCaptureTargets();
            _candidate = null;
            _confirmed = null;
        }

        PublishReleasedUpdates(releasedUpdates);
        if (releaseSucceeded)
        {
            var message = $"Emergency stop: {reason}. Nothing is armed; all Tappy-owned output was released and source input remains pass-through.";
            RaiseState(
                identificationStatus: "Emergency stop disarmed the controller. Select, identify, release, and confirm it again.",
                mappingStatus: "Rehearsal Mode was restored and all Tappy-owned output was released.",
                status: message,
                activeControllerLabel: "No controller confirmed");
            return RuntimeOperation.Ok(message);
        }

        RaiseOutputSafetyFailureState(
            identificationStatus: "Emergency stop disarmed the controller, but Windows rejected an owned-output release. Restart Tappy before rearming.",
            activeControllerLabel: "No controller confirmed");
        return RuntimeOperation.Failed(OutputSafetyFailureStatus);
    }

    public async ValueTask DisposeAsync()
    {
        RuntimeControlUpdate[] releasedUpdates;
        _beforeDisposeGate?.Invoke();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Setting disposed and clearing activation under the same gate used by
            // input prechecks prevents an input that races disposal from arming a
            // fresh output after this cleanup completes.
            _disposed = true;
            _lifecycleResume = null;
            _rememberedReconnect = null;
            _isRememberedReconnect = false;
            releasedUpdates = CaptureReleasedUpdatesLocked();
            _confirmed = null;
            _candidate = null;
            _isRehearsal = true;
            RecordCleanupResultLocked(_engine.ResetForLifecycleTransition());
            _engine.Activation.Reset();
        }

        ClearCaptureTargets();
        PublishReleasedUpdates(releasedUpdates);
        _keyboardProvider.IdentificationInputReceived -= KeyboardProvider_OnIdentificationInputReceived;
        _keyboardProvider.InputReceived -= KeyboardProvider_OnInputReceived;
        _keyboardProvider.DeviceChanged -= KeyboardProvider_OnDeviceChanged;
        _keyboardProvider.LifecycleChanged -= Provider_OnLifecycleChanged;
        _keyboardProvider.Faulted -= Provider_OnFaulted;
        _actionOutput.Faulted -= ActionOutput_OnFaulted;
        if (_logitechG13Provider is not null)
        {
            _logitechG13Provider.IdentificationInputReceived -= LogitechG13Provider_OnIdentificationInputReceived;
            _logitechG13Provider.InputReceived -= LogitechG13Provider_OnInputReceived;
            _logitechG13Provider.DeviceChanged -= LogitechG13Provider_OnDeviceChanged;
            _logitechG13Provider.AvailabilityChanged -= LogitechG13Provider_OnAvailabilityChanged;
        }
        if (_midiProvider is not null)
        {
            _midiProvider.IdentificationInputReceived -= MidiProvider_OnIdentificationInputReceived;
            _midiProvider.InputReceived -= MidiProvider_OnInputReceived;
            _midiProvider.CaptureLost -= MidiProvider_OnCaptureLost;
            _midiProvider.Faulted -= Provider_OnFaulted;
        }

        if (_applicationLifecycleSource is not null)
        {
            _applicationLifecycleSource.LifecycleChanged -= Provider_OnLifecycleChanged;
        }
        if (_logitechG13Provider is not null)
        {
            await _logitechG13Provider.DisposeAsync().ConfigureAwait(false);
        }
        if (_midiProvider is not null)
        {
            await _midiProvider.DisposeAsync().ConfigureAwait(false);
        }

        await _keyboardProvider.DisposeAsync().ConfigureAwait(false);
        if (_actionOutput is IDisposable disposableActionOutput)
        {
            disposableActionOutput.Dispose();
        }
        _controllerLedOutput?.Dispose();
        if (_logitechG13LightingOutput is IDisposable disposableG13LightingOutput)
        {
            disposableG13LightingOutput.Dispose();
        }
    }

    private void KeyboardProvider_OnIdentificationInputReceived(object? sender, KeyboardInputReceivedEventArgs eventArgs)
    {
        lock (_gate)
        {
            if (IsTargus(_candidate) && _candidate!.PersistentId == eventArgs.Input.PersistentDeviceId)
            {
                if (TargusBankState.IsNumLock(eventArgs.Input.ControlId.Value)) { return; }
                _targusBank.Observe(eventArgs.Input.ControlId, eventArgs.Input.Signal.Kind);
            }
        }
        ProcessIdentificationInput(KeyboardProviderId, eventArgs.Input.Signal, eventArgs.Input.Key.DisplayName);
    }

    private void LogitechG13Provider_OnIdentificationInputReceived(object? sender, LogitechG13InputReceivedEventArgs eventArgs) =>
        ProcessIdentificationInput(LogitechG13ProviderId, eventArgs.Input.Signal, eventArgs.Input.DisplayName);

    private void MidiProvider_OnIdentificationInputReceived(object? sender, MidiInputReceivedEventArgs eventArgs) =>
        ProcessIdentificationInput(MidiProviderId, eventArgs.Input.Signal, eventArgs.Input.DisplayName,
            eventArgs.Input.CaptureGeneration);

    private void ProcessIdentificationInput(string providerId, ControlSignal signal, string displayName,
        long? midiCaptureGeneration = null)
    {
        MappingResult result;
        ControllerActivationState state;
        lock (_gate)
        {
            if (_disposed ||
                (midiCaptureGeneration is { } generation && _midiProvider?.IsCurrentCapture(generation) != true) ||
                _candidate?.ProviderId != providerId ||
                signal.ControllerSessionId != new ControllerSessionId(_candidate.SessionId))
            {
                return;
            }

            _beforeEngineProcess?.Invoke();
            result = _engine.Process(signal);
            state = _engine.Activation.State;
            if (_isRememberedReconnect)
            {
                RaiseState(identificationStatus: "Remembered controller restored. Release all controls to finish reconnecting.",
                    status: _lifecycleResume is { HasSlept: true }
                        ? "Waking: output waits for button release, then your previous mode resumes."
                        : "Reconnecting in Rehearsal Mode; output remains suppressed.");
                return;
            }
        }

        var status = state switch
        {
            ControllerActivationState.WaitingForNeutral => "Release every control on the selected device before identification begins.",
            ControllerActivationState.AwaitingIdentificationPress => "Now press one control on the selected device.",
            ControllerActivationState.AwaitingIdentificationRelease => $"Detected {displayName}. Release it to complete identification.",
            ControllerActivationState.AwaitingConfirmation => $"{displayName} was pressed and released. Click Confirm selected controller.",
            _ => result.Message
        };
        RaiseState(identificationStatus: status, status: "Assignments remain suspended during identification.");
    }

    private void KeyboardProvider_OnInputReceived(object? sender, KeyboardInputReceivedEventArgs eventArgs)
    {
        var input = eventArgs.Input;
        _diagnostics.Observe(input);
        ProcessInput(
            KeyboardProviderId,
            input.PersistentDeviceId,
            input.ControllerSessionId,
            input.ControlId,
            input.Key.DisplayName,
            input.Transition == KeyTransition.Press,
            input.IsRepeat,
            input.Signal);
    }

    private void LogitechG13Provider_OnInputReceived(object? sender, LogitechG13InputReceivedEventArgs eventArgs)
    {
        var input = eventArgs.Input;
        ProcessInput(
            LogitechG13ProviderId,
            input.PersistentDeviceId,
            input.ControllerSessionId,
            input.ControlId,
            input.DisplayName,
            input.Signal.Kind != ControlSignalKind.Release,
            input.Signal.Kind == ControlSignalKind.Repeat,
            input.Signal);
    }

    private void MidiProvider_OnInputReceived(object? sender, MidiInputReceivedEventArgs eventArgs)
    {
        var input = eventArgs.Input;
        ProcessInput(
            MidiProviderId,
            input.PersistentDeviceId,
            input.ControllerSessionId,
            input.ControlId,
            input.DisplayName,
            input.Signal.Kind != ControlSignalKind.Release,
            input.Signal.Kind == ControlSignalKind.Repeat,
            input.Signal,
            input.ControlKind is MidiInputControlKind.ControlIncrease or MidiInputControlKind.ControlDecrease
                ? input.Value
                : null,
            input.Signal.Kind == ControlSignalKind.Press
                ? input.ControlKind switch
                {
                    MidiInputControlKind.ControlIncrease => 1,
                    MidiInputControlKind.ControlDecrease => -1,
                    MidiInputControlKind.RelativeControlIncrease => input.Value,
                    MidiInputControlKind.RelativeControlDecrease => -input.Value,
                    _ => null,
                }
                : null,
            input.CaptureGeneration);
    }

    private void MidiProvider_OnCaptureLost(object? sender, MidiCaptureLostEventArgs eventArgs)
    {
        RuntimeControlUpdate[] releasedUpdates;
        bool cleanupSucceeded;
        lock (_gate)
        {
            var selected = _confirmed ?? _candidate;
            if (_disposed || selected?.ProviderId != MidiProviderId ||
                selected.SessionId != eventArgs.Descriptor.SessionId ||
                _midiProvider?.CurrentCaptureGeneration is not null)
            {
                return;
            }

            releasedUpdates = CaptureReleasedUpdatesLocked();
            _rememberedReconnect = _confirmed?.IsAmbiguous == false ? _confirmed :
                _isRememberedReconnect ? _rememberedReconnect : null;
            _isRememberedReconnect = false;
            _confirmed = null;
            _candidate = null;
            _isRehearsal = true;
            cleanupSucceeded = RecordCleanupResultLocked(_engine.SetRehearsalMode(true));
            cleanupSucceeded = RecordCleanupResultLocked(
                _engine.DisconnectController(new ControllerSessionId(selected.SessionId))) && cleanupSucceeded;
            _engine.Activation.Reset();
        }

        PublishReleasedUpdates(releasedUpdates);
        try
        {
            RefreshDevices();
        }
        catch (Exception exception)
        {
            Provider_OnFaulted(this, exception);
            return;
        }

        if (cleanupSucceeded)
        {
            RaiseState(identificationStatus: eventArgs.Reason,
                mappingStatus: "Rehearsal Mode was restored and Tappy-owned output was released.",
                status: "MIDI capture ended. A recognized reconnect restores the controller in Rehearsal Mode.",
                activeControllerLabel: "No controller confirmed");
        }
        else
        {
            RaiseOutputSafetyFailureState(activeControllerLabel: "No controller confirmed");
        }
    }

    private void ProcessInput(
        string providerId,
        string persistentDeviceId,
        ControllerSessionId controllerSessionId,
        ControlId controlId,
        string displayName,
        bool isPressed,
        bool isRepeat,
        ControlSignal signal,
        int? analogRawValue = null,
        double? analogDelta = null,
        long? midiCaptureGeneration = null)
    {
        MappingResult result;
        long eventCount;
        string action;
        string effectiveDisplayName;
        int simultaneous;
        RuntimeControlUpdate[]? bankUpdates = null;
        var presentationId = controlId;
        var canAssign = true;
        var hiddenPresentation = false;
        ControllerBankRequest? bankRequest = null;
        lock (_gate)
        {
            if (_disposed ||
                (midiCaptureGeneration is { } generation && _midiProvider?.IsCurrentCapture(generation) != true) ||
                _confirmed is null ||
                _confirmed.ProviderId != providerId || IsProtectedKeyboard(_confirmed) ||
                controllerSessionId != new ControllerSessionId(_confirmed.SessionId) ||
                persistentDeviceId != _confirmed.PersistentId)
            {
                return;
            }

            _beforeEngineProcess?.Invoke();
            // Num Lock is also emitted around ordinary keys on this keypad.
            // Preserve old bindings, but never execute ambiguous synchronization reports.
            if (IsTargus(_confirmed) && TargusBankState.IsNumLock(controlId.Value)) { return; }
            var bankChanged = IsTargus(_confirmed) && !signal.IsCleanup && _targusBank.Observe(controlId, signal.Kind);
            result = _engine.Process(signal);
            if (result.BankControl is { } command && GetBankEndpoint() is { } source)
            { bankRequest = new ControllerBankRequest(source, command); }
            eventCount = Interlocked.Increment(ref _aggregateEventCount);
            var controller = FindEditableController(persistentDeviceId);
            action = controller is null ? "Unassigned" : BindingLabel(controller, controlId);
            effectiveDisplayName = ResolveDisplayName(controller, controlId, displayName);
            if (controller is not null)
            {
                var observed = _observedControls.Add(controlId);
                var labelChanged = !_controlLabels.TryGetValue(controlId, out var oldLabel) ||
                                   !string.Equals(oldLabel, effectiveDisplayName, StringComparison.Ordinal);
                _controlLabels[controlId] = effectiveDisplayName;
                if ((observed || labelChanged) && !HasCodeDefinedLayout(controller))
                {
                    MergeObservedLayout(controller);
                }
            }

            simultaneous = _engine.InputStates.GetPressedControls(controllerSessionId).Count;
            if (IsTargus(_confirmed))
            {
                hiddenPresentation = TargusKeypadLayout.IsHiddenPresentationReport(controlId.Value);
                effectiveDisplayName = ResolveDisplayName(controller, presentationId, displayName);
                action = PresentationBindingLabelLocked(controller, presentationId);
                canAssign = _targusBank.AssignmentReport(presentationId.Value) is not null;
                if (bankChanged) { bankUpdates = CreateControlUpdatesLocked(controller, isSnapshot: true, preserveHeld: true); }
            }
            if (result.Disposition == MappingDisposition.OutputFailed)
            {
                _outputSafetyNeedsAttention = true;
                _isRehearsal = true;
                _ = _engine.SetRehearsalMode(true);
            }
        }

        // Routing may acquire another runtime's gate. Never invoke it while this gate is held.
        if (bankRequest is not null) { BankControlRequested?.Invoke(this, bankRequest); }
        if (result.TriggeredCombination is { } combinationId)
        {
            string comboLabel, comboAction;
            lock (_gate)
            {
                var profile = FindEditableController(persistentDeviceId);
                comboLabel = ResolveDisplayName(profile, combinationId, "Combination");
                comboAction = BindingLabel(profile, combinationId);
            }
            ControlChanged?.Invoke(this, new RuntimeControlUpdate(persistentDeviceId, combinationId.Value, comboLabel, true, false, comboAction, simultaneous, eventCount));
            ControlChanged?.Invoke(this, new RuntimeControlUpdate(persistentDeviceId, combinationId.Value, comboLabel, false, false, comboAction, simultaneous, eventCount));
        }
        if (bankUpdates is not null)
        {
            RaiseState();
            PublishReleasedUpdates(bankUpdates);
        }
        if (!hiddenPresentation) ControlChanged?.Invoke(this, new RuntimeControlUpdate(
            persistentDeviceId,
            presentationId.Value,
            effectiveDisplayName,
            isPressed,
            isRepeat,
            action,
            simultaneous,
            eventCount,
            AnalogRawValue: analogRawValue,
            AnalogDelta: analogDelta,
            CanAssign: canAssign));

        if (result.Disposition is MappingDisposition.OutputFailed or
            MappingDisposition.RateLimited or
            MappingDisposition.CycleRejected or
            MappingDisposition.DepthRejected or
            MappingDisposition.SourceNeedsAttention)
        {
            if (result.Disposition == MappingDisposition.OutputFailed)
            {
                RaiseOutputSafetyFailureState();
            }
            else
            {
                RaiseState(mappingStatus: result.Message, status: $"Needs attention: {result.Message}");
            }
        }
    }

    private void KeyboardProvider_OnDeviceChanged(object? sender, KeyboardDeviceChangedEventArgs eventArgs) =>
        ProcessDeviceChanged(
            KeyboardProviderId,
            eventArgs.Kind,
            eventArgs.Descriptor,
            eventArgs.WasCaptureTarget);

    private void LogitechG13Provider_OnDeviceChanged(object? sender, LogitechG13DeviceChangedEventArgs eventArgs) =>
        ProcessDeviceChanged(
            LogitechG13ProviderId,
            eventArgs.Kind,
            eventArgs.Descriptor,
            eventArgs.WasCaptureTarget);

    private void LogitechG13Provider_OnAvailabilityChanged(object? sender, EventArgs eventArgs)
    {
        lock (_gate)
        {
            UpdateOptionalCapabilityWarningLocked();
            if (!_initialized || _disposed)
            {
                return;
            }
        }

        RefreshDevices();
        if (IsOutputStateConfirmedSafe)
        {
            RaiseState(status: _optionalCapabilityWarning);
        }
        else
        {
            RaiseOutputSafetyFailureState();
        }
    }

    private void ProcessDeviceChanged(
        string providerId,
        RawInputDeviceChangeKind kind,
        SanitizedDeviceDescriptor? changedDescriptor,
        bool wasCaptureTarget)
    {
        var selectionWasRemoved = false;
        var outputReleaseSucceeded = true;
        var membershipChangeWasBenignStartupNoise = false;
        RuntimeControlUpdate[] releasedUpdates = [];
        var selectedMembershipChanged =
            kind == RawInputDeviceChangeKind.MembershipChanged && wasCaptureTarget;
        if (kind == RawInputDeviceChangeKind.Removal || selectedMembershipChanged)
        {
            ControllerSessionId? sessionToDisconnect = null;
            lock (_gate)
            {
                var removed = changedDescriptor is null
                    ? null
                    : new RuntimeInputDevice(providerId, changedDescriptor);
                selectionWasRemoved = wasCaptureTarget ||
                                      (removed is not null &&
                                       ((_confirmed?.ProviderId == providerId &&
                                         _confirmed.SessionHandle == removed.SessionHandle) ||
                                        (_candidate?.ProviderId == providerId &&
                                         _candidate.SessionHandle == removed.SessionHandle)));
                if (selectionWasRemoved)
                {
                    _targusBank.Reset();
                    releasedUpdates = CaptureReleasedUpdatesLocked();
                    if (!_suspended && !_sessionLocked) { _lifecycleResume = null; }
                    // A multi-interface device (e.g. Razer Tartarus) reports each of its
                    // own interfaces arriving separately even when it was never
                    // unplugged, which Windows/Tappy's own provider report as this same
                    // controller's "membership changed" repeatedly for up to roughly a
                    // second after Tappy starts. For an ALREADY-CONFIRMED, actively used
                    // device that is genuinely suspicious and must still require a fresh
                    // identification (unchanged below). But a candidate that was only
                    // ever silently auto-armed by startup reconnect -- never touched by
                    // a human -- has nothing to protect yet; treat this exactly like an
                    // ordinary disconnect so the retry already in progress can continue,
                    // instead of demanding a manual re-identification for what is almost
                    // always the device's own startup enumeration settling.
                    membershipChangeWasBenignStartupNoise = selectedMembershipChanged &&
                        _confirmed is null && _isRememberedReconnect && _candidate is { IsAmbiguous: false };
                    if (selectedMembershipChanged && !membershipChangeWasBenignStartupNoise)
                    {
                        _rememberedReconnect = null;
                    }
                    else if (_confirmed is { IsAmbiguous: false })
                    {
                        _rememberedReconnect = _confirmed;
                    }
                    else if (membershipChangeWasBenignStartupNoise)
                    {
                        _rememberedReconnect = _candidate;
                    }
                    _isRememberedReconnect = false;
                    var selected = _confirmed ?? _candidate ?? removed;
                    if (selected is not null)
                    {
                        sessionToDisconnect = new ControllerSessionId(selected.SessionId);
                    }

                    _confirmed = null;
                    _candidate = null;
                    _isRehearsal = true;
                    outputReleaseSucceeded = RecordCleanupResultLocked(
                        _engine.SetRehearsalMode(true));
                    _engine.Activation.Reset();
                }
            }

            if (changedDescriptor is { } descriptor)
            {
                var cleanup = _engine.DisconnectController(new ControllerSessionId(descriptor.SessionId));
                lock (_gate)
                {
                    outputReleaseSucceeded = RecordCleanupResultLocked(cleanup) && !_outputSafetyNeedsAttention;
                }
                _diagnostics.ObserveDisconnect(descriptor.PersistentId);
            }
            else if (sessionToDisconnect is { } selectedSession)
            {
                var cleanup = _engine.DisconnectController(selectedSession);
                lock (_gate)
                {
                    outputReleaseSucceeded = RecordCleanupResultLocked(cleanup) && !_outputSafetyNeedsAttention;
                }
            }

            if (selectionWasRemoved)
            {
                ClearCaptureTargets();
                PublishReleasedUpdates(releasedUpdates);
            }
        }

        try
        {
            RefreshDevices();
        }
        catch (Exception exception)
        {
            Provider_OnFaulted(this, exception);
        }

        if (selectionWasRemoved)
        {
            if (outputReleaseSucceeded)
            {
                var stillNeedsIdentification = selectedMembershipChanged && !membershipChangeWasBenignStartupNoise;
                var changeDescription = stillNeedsIdentification
                    ? "A selected controller interface changed. Its Tappy-owned outputs were released. Select and identify it again."
                    : "Controller disconnected and Tappy-owned outputs were released. The same recognized device will return automatically in Rehearsal Mode.";
                RaiseState(
                    identificationStatus: changeDescription,
                    mappingStatus: "Rehearsal Mode was restored; mappings are disarmed.",
                    status: stillNeedsIdentification
                        ? "Needs attention: selected controller membership changed; re-identification is required and fail-open pass-through remains."
                        : "Needs attention: selected controller disconnected; fail-open pass-through remains.",
                    activeControllerLabel: "No controller confirmed");
            }
            else
            {
                RaiseOutputSafetyFailureState(
                    identificationStatus: "The selected controller was removed and Windows rejected an owned-output release. Restart Tappy before rearming.",
                    activeControllerLabel: "No controller confirmed");
            }
        }
    }

    private void Provider_OnLifecycleChanged(object? sender, WindowsLifecycleSignalEventArgs eventArgs)
    {
        if (eventArgs.Signal is WindowsLifecycleSignal.Resuming or WindowsLifecycleSignal.SessionUnlocked)
        {
            lock (_gate)
            {
                if (_disposed) { return; }
                if (eventArgs.Signal == WindowsLifecycleSignal.Resuming) { _suspended = false; }
                else { _sessionLocked = false; }
                if (_suspended || _sessionLocked) { return; }

                if (_lifecycleResume is { HasSlept: true } paused && !_outputSafetyNeedsAttention)
                {
                    _rememberedReconnect = paused.Device;
                }
                else
                {
                    // A standalone Windows lock retains the existing manual-rearm
                    // policy. Only an observed sleep/wake cycle restores normal mode.
                    _lifecycleResume = null;
                }
            }

            PollRememberedReconnect();
            RaiseState();
            return;
        }

        if (eventArgs.Signal is WindowsLifecycleSignal.SessionLocked or
            WindowsLifecycleSignal.Suspending or
            WindowsLifecycleSignal.ShutdownRequested or
            WindowsLifecycleSignal.Shutdown)
        {
            RuntimeControlUpdate[] releasedUpdates;
            bool releaseSucceeded;
            lock (_gate)
            {
                if (_disposed) { return; }
                var shutdown = eventArgs.Signal is WindowsLifecycleSignal.ShutdownRequested or WindowsLifecycleSignal.Shutdown;
                _targusBank.Reset();
                if (shutdown)
                {
                    _lifecycleResume = null;
                }
                else
                {
                    if (_lifecycleResume is null && _confirmed is { IsAmbiguous: false } device)
                    {
                        _lifecycleResume = new LifecycleResumeSession(device, _isRehearsal, false);
                    }
                    if (eventArgs.Signal == WindowsLifecycleSignal.Suspending)
                    {
                        _suspended = true;
                        if (_lifecycleResume is { } retained)
                        {
                            _lifecycleResume = retained with { HasSlept = true };
                        }
                    }
                    else { _sessionLocked = true; }
                }
                _rememberedReconnect = null;
                _isRememberedReconnect = false;
                releasedUpdates = _lifecycleResume is { HasSlept: true } presentation
                    ? CreateControlUpdatesLocked(FindEditableController(presentation.Device.PersistentId), isSnapshot: true)
                    : CaptureReleasedUpdatesLocked();
                releaseSucceeded = RecordCleanupResultLocked(_engine.ResetForLifecycleTransition()) &&
                                   !_outputSafetyNeedsAttention;
                _engine.Activation.Reset();
                _isRehearsal = _lifecycleResume is { HasSlept: true } saved ? saved.Rehearsal : true;
                _engine.SetRehearsalMode(true);
                ClearCaptureTargets();
                _candidate = null;
                _confirmed = null;
            }

            PublishReleasedUpdates(releasedUpdates);
            if (releaseSucceeded)
            {
                if (_lifecycleResume is { HasSlept: true })
                {
                    RaiseState(
                        identificationStatus: "Controller retained while Windows sleeps. No setup will be needed on wake.",
                        mappingStatus: "Output paused; the previous mode will resume after wake/unlock and release of all controls.",
                        status: "Controller paused for Windows sleep. Assignments and layout are retained.");
                }
                else
                {
                    RaiseState(
                    identificationStatus: "Windows changed session state. Re-identify the controller before using mappings again.",
                    mappingStatus: "Rehearsal Mode was restored and all Tappy-owned output was released.",
                    status: $"Nothing is armed after Windows lifecycle event: {eventArgs.Signal}.",
                    activeControllerLabel: "No controller confirmed");
                }
            }
            else
            {
                RaiseOutputSafetyFailureState(
                    identificationStatus: "Windows changed session state and rejected an owned-output release. Restart Tappy before rearming.",
                    activeControllerLabel: "No controller confirmed");
            }
        }
    }

    private void Provider_OnFaulted(object? sender, Exception exception)
    {

        RuntimeControlUpdate[] releasedUpdates;
        bool releaseSucceeded;
        lock (_gate)
        {
            if (_disposed || (ReferenceEquals(sender, _midiProvider) &&
                              _midiProvider?.CurrentCaptureGeneration is not null))
            {
                // MIDI faults invalidate their capture before publication. A live
                // capture here belongs to a later selection, not the failed stream.
                return;
            }

            _lifecycleResume = null;
            _rememberedReconnect = null;
            _isRememberedReconnect = false;
            releasedUpdates = CaptureReleasedUpdatesLocked();
            releaseSucceeded = RecordCleanupResultLocked(_engine.ResetForLifecycleTransition()) &&
                               !_outputSafetyNeedsAttention;
            _engine.Activation.Reset();
            _isRehearsal = true;
            _engine.SetRehearsalMode(true);
            ClearCaptureTargets();
            _candidate = null;
            _confirmed = null;
        }

        PublishReleasedUpdates(releasedUpdates);
        if (releaseSucceeded)
        {
            RaiseState(
                identificationStatus: "The selected input provider is unavailable. Nothing is armed; re-identification is required after recovery.",
                mappingStatus: "The input backend failed; output is disabled, released, and Rehearsal Mode was restored.",
                status: $"Needs attention: {PrivacyRedactor.SanitizeDiagnosticText(exception.Message)}",
                activeControllerLabel: "No controller confirmed",
                sourceLabel: "Effective: Needs attention (fail-open)");
        }
        else
        {
            RaiseOutputSafetyFailureState(
                identificationStatus: "The input provider failed and Windows rejected an owned-output release. Restart Tappy before rearming.",
                activeControllerLabel: "No controller confirmed");
        }
    }

    private void ActionOutput_OnFaulted(object? sender, ControllerActionOutputFault fault)
    {
        RuntimeControlUpdate[] releasedUpdates;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _lifecycleResume = null;
            _rememberedReconnect = null;
            _isRememberedReconnect = false;
            releasedUpdates = CaptureReleasedUpdatesLocked();
            _ = _engine.EmergencyStop();
            _engine.Activation.Reset();
            _isRehearsal = true;
            _ = _engine.SetRehearsalMode(true);
            _outputSafetyNeedsAttention = true;
            ClearCaptureTargets();
            _candidate = null;
            _confirmed = null;
        }

        PublishReleasedUpdates(releasedUpdates);
        RaiseOutputSafetyFailureState(
            identificationStatus: "An assigned output failed. Nothing is armed; restart Tappy before rearming.",
            activeControllerLabel: "No controller confirmed",
            mappingStatus: $"Action output failed: {PrivacyRedactor.SanitizeDiagnosticText(fault.Message)}");
    }

    private RuntimeControlUpdate[] CaptureReleasedUpdatesLocked()
    {
        var confirmed = _confirmed;
        if (confirmed is null)
        {
            return [];
        }

        var eventCount = Interlocked.Read(ref _aggregateEventCount);
        var controller = FindEditableController(confirmed.PersistentId);
        return CreateControlUpdatesLocked(controller, confirmed.PersistentId, eventCount);
    }

    private RuntimeControlUpdate[] CreateControlUpdatesLocked(
        ControllerProfile? controller,
        string? persistentId = null,
        long? eventCount = null,
        bool isSnapshot = false,
        bool preserveHeld = false) =>
        GetControlsInPresentationOrder(controller).Select(control =>
            new RuntimeControlUpdate(
                persistentId ?? controller?.Identity.PersistentId?.Value ?? string.Empty,
                control.Value,
                _controlLabels.GetValueOrDefault(control, control.Value),
                preserveHeld && _confirmed is not null && _engine.InputStates.GetPressedControls(new ControllerSessionId(_confirmed.SessionId))
                    .Any(id => id == control),
                false,
                PresentationBindingLabelLocked(controller, control),
                0,
                eventCount ?? Interlocked.Read(ref _aggregateEventCount),
                isSnapshot,
                CanAssign: !IsTargus(PresentedControllerLocked()) || _targusBank.AssignmentReport(control.Value) is not null))
            .ToArray();

    private IReadOnlyList<ControlId> GetControlsInPresentationOrder(ControllerProfile? controller)
    {
        var ordered = controller?.Layout.Rows
            .SelectMany(row => row.Controls)
            .Where(control => control.ControlId is not null)
            .Select(control => control.ControlId!.Value)
            .Where(_observedControls.Contains)
            .Distinct()
            .ToList() ?? [];
        ordered.AddRange(_observedControls
            .Where(control => !ordered.Contains(control))
            .OrderBy(control => control.Value, StringComparer.Ordinal));
        return IsTargus(PresentedControllerLocked())
            ? ordered.Where(id => !TargusKeypadLayout.IsHiddenPresentationReport(id.Value)).ToArray()
            : ordered;
    }

    private void PublishReleasedUpdates(IEnumerable<RuntimeControlUpdate> updates)
    {
        foreach (var update in updates)
        {
            ControlChanged?.Invoke(this, update);
        }
    }

    private bool RecordCleanupResultLocked(OutputCleanupResult result)
    {
        if (!result.OutputReleaseSucceeded)
        {
            _outputSafetyNeedsAttention = true;
            _lifecycleResume = null;
            _isRehearsal = true;
        }

        return result.OutputReleaseSucceeded;
    }

    private void RaiseOutputSafetyFailureState(
        string? identificationStatus = null,
        string? activeControllerLabel = null,
        string? mappingStatus = null) =>
        RaiseState(
            identificationStatus: identificationStatus,
            mappingStatus: mappingStatus ?? OutputSafetyFailureMappingStatus,
            status: OutputSafetyFailureStatus,
            activeControllerLabel: activeControllerLabel,
            sourceLabel: "Effective: Needs attention (fail-open)");

    private void RaiseState(
        string? identificationStatus = null,
        string? mappingStatus = null,
        string? status = null,
        string? activeControllerLabel = null,
        string? activeLayerName = null,
        string? sourceLabel = null)
    {
        RuntimeInputDevice? confirmed;
        ControllerProfile? controller;
        bool identificationCaptureActive;
        bool rememberedReconnect;
        bool lifecyclePaused;
        bool waitingForReconnect;
        RuntimeControlUpdate[]? retainedControls;
        string? optionalCapabilityWarning;
        string? bankLabel;
        bool? numLockOn;
        int? tartarusKeymap;
        int? tartarusLedMask;
        lock (_gate)
        {
            rememberedReconnect = _isRememberedReconnect;
            lifecyclePaused = _confirmed is null && _lifecycleResume is { HasSlept: true };
            confirmed = PresentedControllerLocked();
            waitingForReconnect = confirmed is null && _candidate is null && _rememberedReconnect is not null &&
                _lifecycleResume is null && !_outputSafetyNeedsAttention;
            controller = confirmed is null ? null : FindEditableController(confirmed.PersistentId);
            retainedControls = lifecyclePaused ? CreateControlUpdatesLocked(controller, isSnapshot: true) : null;
            identificationCaptureActive = _candidate is not null;
            optionalCapabilityWarning = _optionalCapabilityWarning;
            bankLabel = IsTargus(confirmed) ? _targusBank.Label : null;
            numLockOn = IsTargus(confirmed) ? _targusBank.NumLockOn : null;
            tartarusKeymap = confirmed is not null && controller is not null &&
                RazerTartarusLayout.Matches(confirmed.ProviderId, confirmed.VendorId, confirmed.ProductId)
                ? controller.Layers.FindIndex(l => l.Id == controller.ActiveLayerId) + 1 : null;
            tartarusLedMask = CanSyncTartarusLocked() && _tartarusLedConfirmationGeneration == _confirmationGeneration
                ? _tartarusVerifiedLedMask : null;
        }

        var effectiveStatus = status ?? (lifecyclePaused ? "Controller paused for sleep/wake; output is suspended until Windows is awake, unlocked, and controls are released." :
            rememberedReconnect ? "Reconnecting in Rehearsal Mode; release all controls." :
            confirmed is null ? "Nothing is armed." : "Controller ready.");
        if (!string.IsNullOrWhiteSpace(optionalCapabilityWarning) &&
            !effectiveStatus.Contains(optionalCapabilityWarning, StringComparison.Ordinal))
        {
            effectiveStatus = $"{effectiveStatus} {optionalCapabilityWarning}";
        }

        StateChanged?.Invoke(this, new RuntimeState(
            confirmed is not null && !rememberedReconnect && !lifecyclePaused,
            !rememberedReconnect && _engine.Activation.State == ControllerActivationState.AwaitingConfirmation,
            identificationStatus ?? (lifecyclePaused ? "Remembered controller retained through sleep; waiting for safe wake-up." :
                rememberedReconnect ? "Remembered controller restored; waiting for neutral input." :
                confirmed is null ? "Choose and identify a spare controller." : "Controller confirmed."),
            activeControllerLabel ?? confirmed?.DisplayName ?? "No controller confirmed",
            activeLayerName ?? controller?.Layers.FirstOrDefault(item => item.Id == controller.ActiveLayerId)?.Name ?? "Layer 1",
            mappingStatus ?? (lifecyclePaused ? "Output paused; your previous operating mode will return after wake and release." :
                IsRehearsal ? "Rehearsal Mode suppresses output." : "Normal output is enabled for confirmed mappings."),
            effectiveStatus,
            sourceLabel ?? (controller is null ? "Effective: Pass-through" : SourceLabel(controller)),
            IsIdentificationCaptureActive: identificationCaptureActive,
            ActiveControllerProviderId: confirmed?.ProviderId,
            ActiveControllerVendorId: confirmed?.VendorId,
            ActiveControllerProductId: confirmed?.ProductId,
            IsRememberedReconnect: rememberedReconnect,
            IsLifecyclePaused: lifecyclePaused,
            RetainedControls: retainedControls,
            InputBankLabel: bankLabel,
            NumLockOn: numLockOn,
            IsWaitingForReconnect: waitingForReconnect,
            ActiveControllerPresentationModelId: confirmed?.PresentationModelId,
            TartarusKeymap: tartarusKeymap,
            TartarusVerifiedLedMask: tartarusLedMask));
    }

    private static bool IsTargus(RuntimeInputDevice? device) => device is not null &&
        TargusKeypadLayout.Matches(device.ProviderId, device.VendorId, device.ProductId);

    private string? AssignmentReportLocked(string physicalId) => IsTargus(_confirmed)
        ? _targusBank.AssignmentReport(physicalId) : physicalId;

    private string PresentationBindingLabelLocked(ControllerProfile? controller, ControlId physicalId)
    {
        if (!IsTargus(PresentedControllerLocked())) { return BindingLabel(controller, physicalId); }
        var report = _targusBank.AssignmentReport(physicalId.Value);
        return report is null ? _targusBank.UnavailableLabel(physicalId.Value)
            : BindingLabel(controller, new ControlId(report));
    }

    private RuntimeInputDevice? PresentedControllerLocked() =>
        _confirmed ?? (_isRememberedReconnect ? _candidate : null) ??
        (_lifecycleResume is { HasSlept: true } paused ? paused.Device : null);

    private sealed record LifecycleResumeSession(RuntimeInputDevice Device, bool Rehearsal, bool HasSlept);

    private ControllerProfile? FindEditableController(string persistentId) =>
        _editableProfile.Controllers.FirstOrDefault(controller =>
            controller.Identity.PersistentId is { } id && id.Value == persistentId);

    private static string BindingLabel(ControllerProfile? controller, ControlId controlId)
    {
        if (controller is null)
        {
            return "Unassigned";
        }

        var bankControl = controller.BankControls.FirstOrDefault(item => item.ControlId == controlId);
        var layer = controller.Layers.FirstOrDefault(item => item.Id == controller.ActiveLayerId);
        return bankControl?.Label ?? layer?.Bindings.FirstOrDefault(item => item.ControlId == controlId)?.Name ?? "Unassigned";
    }

    private static ControllerActionSequenceDefinition KeyboardActionToSequence(
        string name,
        KeyboardActionDefinition action) => new()
        {
            Name = name,
            Mode = action.Mode == KeyboardActionMode.HoldUntilRelease
                ? ControllerActionSequenceMode.WhileHeld
                : ControllerActionSequenceMode.RunOnce,
            Steps =
            [
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.KeyboardChord,
                    Keys = action.Keys is null ? [] : [.. action.Keys],
                },
            ],
        };

    private static ControllerLayoutDefinition MergeCodeDefinedLayout(
        ControllerLayoutDefinition current,
        ControllerLayoutDefinition canonical)
    {
        var result = canonical.Clone();
        // A saved layout whose Id doesn't match the canonical layout's Id predates
        // a change to that model's code-defined shape (e.g. the Akai Fire's
        // encoder/pad-grid separation), or was never one of these code-defined
        // shapes at all (a generic learned "generated-grid" layout, from before
        // this exact model name was recognized). Reusing its per-control ordering
        // in either case would fill each fresh canonical slot with whatever
        // control happened to occupy the same numeric position under the old
        // shape - producing a scrambled result, not a reordering. Position and
        // size reset to the fresh canonical layout instead; only per-control
        // customization that isn't tied to a specific slot (color, calibration)
        // still carries over, matched by control identity. See D-022 in
        // DECISIONS.md.
        var isCurrentShape = string.Equals(current.Id, canonical.Id, StringComparison.Ordinal);
        var hasSavedWorkspace = isCurrentShape &&
                                (!current.Id.Equals("generated-grid", StringComparison.OrdinalIgnoreCase) ||
                                 current.Rows.SelectMany(row => row.Controls)
                                     .Any(control => control.X is not null || control.Y is not null));
        if (hasSavedWorkspace)
        {
            result.GridColumns = current.GridColumns;
            result.GridRows = current.GridRows;
            result.SnapToGrid = current.SnapToGrid;
            result.IsLayoutLocked = current.IsLayoutLocked;
        }
        var canonicalControls = result.Rows
            .SelectMany(row => row.Controls)
            .Where(item => item.ControlId is not null)
            .Select(item => item.ControlId!.Value)
            .ToArray();
        var canonicalSet = canonicalControls.ToHashSet();
        var currentDefinitions = current.Rows
            .SelectMany(row => row.Controls)
            .Where(item => item.ControlId is not null && canonicalSet.Contains(item.ControlId.Value))
            .GroupBy(item => item.ControlId!.Value)
            .ToDictionary(group => group.Key, group => group.Last());
        foreach (var definition in result.Rows.SelectMany(row => row.Controls))
        {
            if (definition.ControlId is not { } id || !currentDefinitions.TryGetValue(id, out var previous))
            {
                continue;
            }

            if (isCurrentShape)
            {
                definition.X = previous.X ?? definition.X;
                definition.Y = previous.Y ?? definition.Y;
                definition.Width = previous.Width;
                definition.Height = previous.Height;
            }
            definition.ColorKey = previous.ColorKey;
            definition.AnalogRawAtMinimum = previous.AnalogRawAtMinimum;
            definition.AnalogRawAtMaximum = previous.AnalogRawAtMaximum;
            definition.AnalogRawAtCenter = previous.AnalogRawAtCenter;
            definition.EncoderDegreesPerStep = previous.EncoderDegreesPerStep;
            definition.EncoderReversed = previous.EncoderReversed;
        }

        if (isCurrentShape)
        {
            var existingOrder = current.Rows
                .SelectMany(row => row.Controls)
                .Where(item => item.ControlId is not null && canonicalSet.Contains(item.ControlId.Value))
                .Select(item => item.ControlId!.Value)
                .Distinct()
                .ToList();
            existingOrder.AddRange(canonicalControls.Where(control => !existingOrder.Contains(control)));
            ApplyPresentationOrder(result, existingOrder);
        }
        var combinations = current.Rows.SelectMany(r => r.Controls)
            .Where(c => c.ControlId is { } id && id.Value.StartsWith("combination:", StringComparison.Ordinal))
            .Select(c => c.Clone()).ToList();
        if (combinations.Count != 0) { result.Rows.Add(new LayoutRowDefinition { Id = "combinations", Controls = combinations }); }
        return result;
    }

    private static void ApplyPresentationOrder(
        ControllerLayoutDefinition layout,
        IReadOnlyList<ControlId> orderedControls)
    {
        var definitions = layout.Rows
            .SelectMany(row => row.Controls)
            .Where(item => item.ControlId is not null)
            .GroupBy(item => item.ControlId!.Value)
            .ToDictionary(group => group.Key, group => group.Last().Clone());
        var orderedIndex = 0;
        foreach (var row in layout.Rows)
        {
            for (var index = 0; index < row.Controls.Count; index++)
            {
                if (row.Controls[index].ControlId is null)
                {
                    continue;
                }

                row.Controls[index] = definitions[orderedControls[orderedIndex++]];
            }
        }
    }

    private void EnsureObservedLayout(ControllerProfile controller, ControlId controlId)
    {
        _observedControls.Add(controlId);
        if (HasCodeDefinedLayout(controller))
        {
            if (controller.Identity.ProviderId == LogitechG13ProviderId &&
                !controller.Layout.Rows.SelectMany(row => row.Controls)
                    .Any(item => item.ControlId == controlId))
            {
                controller.Layout = MergeCodeDefinedLayout(controller.Layout, CreateLogitechG13Layout());
            }

            return;
        }

        MergeObservedLayout(controller);
    }

    private static bool HasCodeDefinedLayout(ControllerProfile controller) =>
        string.Equals(controller.Layout.Id, "logitech-g13-code-layout-v1", StringComparison.Ordinal) ||
        string.Equals(controller.Layout.Id, MidiControllerModelCatalog.AkaiApcMiniV1LayoutId, StringComparison.Ordinal) ||
        string.Equals(controller.Layout.Id, MidiControllerModelCatalog.AkaiFireV1LayoutId, StringComparison.Ordinal);

    private static string ResolveDisplayName(
        ControllerProfile? controller,
        ControlId controlId,
        string fallback)
    {
        var label = controller?.Layout.Rows
            .SelectMany(row => row.Controls)
            .FirstOrDefault(item => item.ControlId == controlId)
            ?.Label;
        return string.IsNullOrWhiteSpace(label) ? fallback : label;
    }

    private void MergeObservedLayout(ControllerProfile controller)
    {
        // Learning a control or editing its assignment must never rebuild the user's workspace.
        var layout = controller.Layout;
        var existing = layout.Rows.SelectMany(row => row.Controls)
            .Where(control => control.ControlId.HasValue)
            .Select(control => control.ControlId!.Value).ToHashSet();
        var columns = Math.Max(1, layout.GridColumns);
        foreach (var controlId in _observedControls.OrderBy(item => item.Value, StringComparer.Ordinal))
        {
            if (!existing.Add(controlId))
            {
                continue;
            }

            var row = layout.Rows.LastOrDefault();
            if (row is null || row.Controls.Count >= columns)
            {
                var rowNumber = layout.Rows.Count + 1;
                while (layout.Rows.Any(item => item.Id == $"row-{rowNumber}"))
                {
                    rowNumber++;
                }

                row = new LayoutRowDefinition { Id = $"row-{rowNumber}" };
                layout.Rows.Add(row);
            }

            var learned = new LayoutControlDefinition
            {
                ControlId = controlId,
                Label = _controlLabels.GetValueOrDefault(controlId, controlId.Value)
            };
            if (TargusKeypadLayout.Matches(controller.Identity.ProviderId,
                controller.Identity.VendorId, controller.Identity.ProductId))
            { TargusKeypadLayout.PlaceNewReport(layout, learned); }
            else if (layout.Id == MicrosoftNumberPadLayout.LayoutId)
            { MicrosoftNumberPadLayout.PlaceNewReport(layout, learned); }
            else if (layout.Id == RazerTartarusLayout.LayoutId)
            { RazerTartarusLayout.PlaceNewReport(layout, learned); }
            row.Controls.Add(learned);
        }
    }

    private static void EnforceAvailableSourceModes(TappyProfile profile)
    {
        profile.Normalize();
        foreach (var controller in profile.Controllers)
        {
            ApplyAvailableSourceMode(controller);
        }
    }

    private static void ApplyAvailableSourceMode(ControllerProfile controller)
    {
        if (controller.SourceMode.Requested == RequestedSourceMode.PassThrough)
        {
            controller.SourceMode.Effective = EffectiveSourceMode.PassThrough;
            controller.SourceMode.Status = "Device-aware pass-through";
        }
        else
        {
            controller.SourceMode.Effective = EffectiveSourceMode.NeedsAttention;
            controller.SourceMode.Status = "Requested source backend is unavailable; fail-open pass-through";
        }
    }

    private static string SourceLabel(ControllerProfile controller) =>
        controller.Identity.ProviderId == MidiProviderId
            ? "Effective: MIDI input monitor"
            : controller.SourceMode.Effective switch
            {
                EffectiveSourceMode.PassThrough => "Effective: Pass-through",
                EffectiveSourceMode.GlobalBlock => "Effective: Global block",
                EffectiveSourceMode.Exclusive => "Effective: Exclusive",
                _ => "Effective: Needs attention (fail-open)"
            };

    private IReadOnlyList<RuntimeInputDevice> EnumerateRuntimeDevices()
    {
        var devices = _keyboardProvider.EnumerateKeyboards()
            .Select(descriptor => new RuntimeInputDevice(KeyboardProviderId, descriptor))
            .ToList();
        if (_logitechG13Provider is { IsAvailable: true })
        {
            devices.AddRange(_logitechG13Provider.EnumerateControllers()
                .Select(descriptor => new RuntimeInputDevice(LogitechG13ProviderId, descriptor)));
        }
        if (_midiProvider is not null)
        {
            devices.AddRange(_midiProvider.EnumerateControllers()
                .Select(descriptor => new RuntimeInputDevice(descriptor)));
        }

        return devices;
    }

    private void UpdateOptionalCapabilityWarningLocked()
    {
        _optionalCapabilityWarning = _logitechG13Provider is { IsAvailable: false }
            ? $"Needs attention: {_logitechG13Provider.AvailabilityStatus}"
            : null;
    }

    private void ClearCaptureTargets()
    {
        _keyboardProvider.ClearCaptureTarget();
        _logitechG13Provider?.ClearCaptureTarget();
        _midiProvider?.ClearCaptureTarget();
    }

    private bool SetCaptureTarget(RuntimeInputDevice device) =>
        device.ProviderId switch
        {
            KeyboardProviderId => _keyboardProvider.SetCaptureTarget(device.SessionHandle),
            LogitechG13ProviderId when _logitechG13Provider is not null =>
                _logitechG13Provider.SetCaptureTarget(device.SessionHandle),
            MidiProviderId when _midiProvider is not null && device.MidiDeviceId is { } midiDeviceId =>
                _midiProvider.SetCaptureTarget(midiDeviceId),
            _ => false,
        };

    private bool SetConfirmedPersistentId(RuntimeInputDevice device, string persistentId) =>
        device.ProviderId switch
        {
            KeyboardProviderId => _keyboardProvider.SetConfirmedPersistentId(persistentId),
            LogitechG13ProviderId when _logitechG13Provider is not null =>
                _logitechG13Provider.SetConfirmedPersistentId(persistentId),
            MidiProviderId when _midiProvider is not null =>
                _midiProvider.SetConfirmedPersistentId(persistentId),
            _ => false,
        };

    private bool IsCaptureTargetNeutral(RuntimeInputDevice device) =>
        device.ProviderId switch
        {
            KeyboardProviderId => _keyboardProvider.IsCaptureTargetNeutral,
            LogitechG13ProviderId when _logitechG13Provider is not null =>
                _logitechG13Provider.IsCaptureTargetNeutral,
            MidiProviderId when _midiProvider is not null =>
                _midiProvider.IsCaptureTargetNeutral,
            _ => false,
        };

    private static ControllerLayoutDefinition CreateLogitechG13Layout()
    {
        var definitions = LogitechG13InputProvider.SupportedControls
            .ToDictionary(item => item.Control);
        LayoutRowDefinition Row(string id, params LogitechG13Control[] controls) => new()
        {
            Id = id,
            Controls = controls.Select(control =>
            {
                var definition = definitions[control];
                return new LayoutControlDefinition
                {
                    ControlId = definition.ControlId,
                    Label = definition.DisplayName,
                    Kind = definition.ButtonBitIndex is null
                        ? LayoutControlKind.Axis
                        : control is >= LogitechG13Control.G1 and <= LogitechG13Control.G22
                            ? LayoutControlKind.Key
                            : LayoutControlKind.Button,
                    Cluster = control switch
                    {
                        >= LogitechG13Control.G1 and <= LogitechG13Control.G22 => "G keys",
                        >= LogitechG13Control.LcdNextPage and <= LogitechG13Control.LcdMenuRight => "LCD",
                        >= LogitechG13Control.M1 and <= LogitechG13Control.Mr => "Memory",
                        LogitechG13Control.Lights => "Lighting",
                        _ => "Joystick",
                    },
                };
            }).ToList(),
        };

        return new ControllerLayoutDefinition
        {
            Id = "logitech-g13-code-layout-v1",
            Name = "Logitech G13 code-rendered layout",
            Rows =
            [
                Row("g-row-1", LogitechG13Control.G1, LogitechG13Control.G2, LogitechG13Control.G3,
                    LogitechG13Control.G4, LogitechG13Control.G5, LogitechG13Control.G6, LogitechG13Control.G7),
                Row("g-row-2", LogitechG13Control.G8, LogitechG13Control.G9, LogitechG13Control.G10,
                    LogitechG13Control.G11, LogitechG13Control.G12, LogitechG13Control.G13, LogitechG13Control.G14),
                Row("g-row-3", LogitechG13Control.G15, LogitechG13Control.G16, LogitechG13Control.G17,
                    LogitechG13Control.G18, LogitechG13Control.G19),
                Row("g-row-4", LogitechG13Control.G20, LogitechG13Control.G21, LogitechG13Control.G22,
                    LogitechG13Control.M1, LogitechG13Control.M2, LogitechG13Control.M3, LogitechG13Control.Mr),
                Row("lcd-lighting", LogitechG13Control.LcdNextPage, LogitechG13Control.LcdMenuLeft,
                    LogitechG13Control.LcdMenu2, LogitechG13Control.LcdMenu3, LogitechG13Control.LcdMenuRight,
                    LogitechG13Control.Lights),
                Row("joystick", LogitechG13Control.JoystickLeftSide, LogitechG13Control.JoystickBottomSide,
                    LogitechG13Control.JoystickPress, LogitechG13Control.StickLeft, LogitechG13Control.StickRight,
                    LogitechG13Control.StickUp, LogitechG13Control.StickDown),
            ],
        };
    }

    private static ProductionProviders CreateProductionProviders()
    {
        var host = new RawInputMessageHost();
        return new ProductionProviders(
            new RawInputKeyboardProvider(new NativeRawInputDeviceEnumerator(), host),
            new LogitechG13InputProvider(new NativeLogitechG13DeviceEnumerator(), host),
            new WinMmMidiInputProvider());
    }

    private static ProductionOutputs CreateProductionOutputs()
    {
        var keyboard = new OwnedKeyboardOutput(new SendInputKeyboardOutput());
        return new ProductionOutputs(
            keyboard,
            new WindowsControllerActionOutput(keyboard),
            new WinMmMidiOutput(),
            new NativeLogitechG13LightingOutput());
    }

    private static ControllerIdentity ToIdentity(RuntimeInputDevice descriptor) => new(
        new ControllerSessionId(descriptor.SessionId),
        new ControllerPersistentId(descriptor.PersistentId),
        descriptor.IsAmbiguous
            ? ControllerIdentityConfidence.Ambiguous
            : ControllerIdentityConfidence.PortBound,
        descriptor.DisplayName,
        providerId: descriptor.ProviderId,
        descriptor.VendorId,
        descriptor.ProductId,
        descriptor.UsagePage,
        descriptor.Usage);

    private ControllerChoice ToChoice(RuntimeInputDevice descriptor) => new(
        descriptor.SessionId,
        descriptor.PersistentId,
        descriptor.DisplayName,
        (descriptor.IsAmbiguous
            ? ControllerIdentityConfidence.Ambiguous
            : ControllerIdentityConfidence.PortBound).ToString(),
        descriptor.ProviderId,
        descriptor.VendorId,
        descriptor.ProductId)
    { IsProtectedForTyping = IsProtectedKeyboard(descriptor) };

    private static bool IsHarmlessOutputKey(string value) =>
        value.Length is 3 or 4 &&
        value.StartsWith('F') &&
        int.TryParse(value.AsSpan(1), out var number) &&
        number is >= 13 and <= 24;

    private static KeyboardActionDefinition? CreateKeyboardAction(
        KeyboardActionMode mode,
        IReadOnlyList<string>? keyNames)
    {
        if (mode == KeyboardActionMode.None)
        {
            return new KeyboardActionDefinition();
        }

        if (keyNames is null || keyNames.Count is 0 or > 8)
        {
            return null;
        }

        var keys = keyNames
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => new KeyboardOutputKey(key))
            .Distinct()
            .ToList();
        if (keys.Count == 0 || keys.Any(key => !KeyboardOutputCapabilities.IsSupported(key)))
        {
            return null;
        }

        return new KeyboardActionDefinition { Mode = mode, Keys = keys };
    }

    private static string? ValidateSequence(ControllerActionSequenceDefinition sequence)
    {
        if (sequence.Steps.Count > 500)
        {
            return "An assignment cannot contain more than 500 action steps.";
        }

        if (sequence.Mode == ControllerActionSequenceMode.RepeatWhileHeld &&
            sequence.Steps.Any(step => step.Type is ControllerActionStepType.LaunchProgram or
                ControllerActionStepType.PowerShellCommand))
        {
            return "Program and PowerShell actions cannot repeat while held.";
        }

        foreach (var step in sequence.Steps)
        {
            switch (step.Type)
            {
                case ControllerActionStepType.KeyboardChord:
                case ControllerActionStepType.KeyDown:
                case ControllerActionStepType.KeyUp:
                    if (step.Keys.Count is 0 or > 8 ||
                        step.Keys.Any(key => !KeyboardOutputCapabilities.IsSupported(key)))
                    {
                        return "A keyboard step contains an unsupported key or exceeds the eight-key chord safety limit.";
                    }
                    break;
                case ControllerActionStepType.Text:
                    if (string.IsNullOrEmpty(step.Value) || step.Value.Length > 32_768)
                    {
                        return "A text step must contain between 1 and 32,768 characters.";
                    }
                    break;
                case ControllerActionStepType.Delay:
                    if (step.DurationMs is < 1 or > 600_000)
                    {
                        return "A delay must be between 1 millisecond and 10 minutes.";
                    }
                    break;
                case ControllerActionStepType.MouseButton:
                    var mouse = step.Value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                    if (mouse is not ("left" or "leftclick" or "leftdown" or "leftup" or
                        "right" or "rightclick" or "rightdown" or "rightup" or
                        "middle" or "middleclick" or "middledown" or "middleup" or
                        "x1" or "x1click" or "x1down" or "x1up" or
                        "x2" or "x2click" or "x2down" or "x2up"))
                    {
                        return "A mouse button step must use Left, Right, Middle, X1, or X2 with optional Click, Down, or Up.";
                    }
                    break;
                case ControllerActionStepType.LaunchProgram:
                    if (string.IsNullOrWhiteSpace(step.Value))
                    {
                        return "A program action requires a program, file, folder, or URL.";
                    }
                    break;
                case ControllerActionStepType.PowerShellCommand:
                    if (string.IsNullOrWhiteSpace(step.Value) ||
                        step.Target is not ("" or "Windows PowerShell 5.1" or "PowerShell 7" or
                            "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe"))
                    {
                        return "A PowerShell action requires a command and a supported PowerShell host.";
                    }
                    break;
                case ControllerActionStepType.Midi:
                    try
                    {
                        _ = MidiMessageParser.Parse(step.Value);
                    }
                    catch (ArgumentException exception)
                    {
                        return exception.Message;
                    }
                    break;
                case ControllerActionStepType.Osc:
                    if (step.Amount is < 1 or > 65_535)
                    {
                        return "An OSC destination port must be between 1 and 65,535.";
                    }
                    try
                    {
                        _ = OscPacketBuilder.Build(step.Value, step.Arguments);
                    }
                    catch (ArgumentException exception)
                    {
                        return exception.Message;
                    }
                    break;
            }
        }

        return null;
    }

    private static string? ValidateMidiLifetime(
        ControllerActionSequenceDefinition press,
        ControllerActionSequenceDefinition release)
    {
        var releaseEndingNotes = EndingMidiNotes(release);
        if (releaseEndingNotes.Count > 0)
        {
            return "A release sequence cannot leave a MIDI note on. Add the matching note-off step.";
        }

        var pressEndingNotes = EndingMidiNotes(press);
        if (pressEndingNotes.Count == 0 || press.Mode == ControllerActionSequenceMode.WhileHeld)
        {
            return null;
        }

        if (press.Mode == ControllerActionSequenceMode.RepeatWhileHeld)
        {
            return "A repeating sequence cannot leave a MIDI note on. Add a matching note-off within the sequence.";
        }

        pressEndingNotes.ExceptWith(MidiNoteOffs(release));
        return pressEndingNotes.Count == 0
            ? null
            : "A run-once MIDI note-on needs a matching release note-off, or use stop-and-clean-up-on-release behavior.";
    }

    private static HashSet<string> EndingMidiNotes(ControllerActionSequenceDefinition sequence)
    {
        var notes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in sequence.Steps.Where(step => step.Type == ControllerActionStepType.Midi))
        {
            var message = MidiMessageParser.Parse(step.Value);
            var key = MidiNoteKey(step.Target, message);
            if (message.IsNoteOn)
            {
                notes.Add(key);
            }
            else if (message.Kind is MidiShortMessageKind.NoteOff or MidiShortMessageKind.NoteOn)
            {
                notes.Remove(key);
            }
        }

        return notes;
    }

    private static HashSet<string> MidiNoteOffs(ControllerActionSequenceDefinition sequence) =>
        sequence.Steps
            .Where(step => step.Type == ControllerActionStepType.Midi)
            .Select(step => (Step: step, Message: MidiMessageParser.Parse(step.Value)))
            .Where(item => item.Message.Kind == MidiShortMessageKind.NoteOff ||
                           item.Message.Kind == MidiShortMessageKind.NoteOn && item.Message.Data2 == 0)
            .Select(item => MidiNoteKey(item.Step.Target, item.Message))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string MidiNoteKey(string device, MidiShortMessage message) =>
        $"{device.Trim()}|{message.Channel}|{message.Data1}";

    private static string FormatKeyboardBinding(
        KeyboardActionDefinition pressAction,
        KeyboardActionDefinition releaseAction)
    {
        var action = pressAction.Mode == KeyboardActionMode.None ? releaseAction : pressAction;
        var shortcut = string.Join(" + ", action.Keys.Select(key => key.Value));
        return pressAction.Mode switch
        {
            KeyboardActionMode.Tap => $"Tap {shortcut}",
            KeyboardActionMode.HoldUntilRelease => $"Hold {shortcut} until release",
            _ => $"On release: {shortcut}"
        };
    }

    private sealed record ProductionProviders(
        RawInputKeyboardProvider Keyboard,
        LogitechG13InputProvider LogitechG13,
        WinMmMidiInputProvider Midi);

    private sealed record ProductionOutputs(
        IKeyboardOutput Keyboard,
        WindowsControllerActionOutput Actions,
        WinMmMidiOutput ControllerLeds,
        NativeLogitechG13LightingOutput LogitechG13Lighting);

    private sealed record RuntimeInputDevice
    {
        internal RuntimeInputDevice(string providerId, SanitizedDeviceDescriptor descriptor)
        {
            ProviderId = providerId;
            SessionHandle = descriptor.SessionHandle;
            SessionId = descriptor.SessionId;
            PersistentId = descriptor.PersistentId;
            DisplayName = descriptor.DisplayName;
            VendorId = descriptor.VendorId;
            ProductId = descriptor.ProductId;
            PresentationModelId = descriptor.PresentationModelId;
            UsagePage = descriptor.UsagePage ?? (providerId == LogitechG13ProviderId
                ? LogitechG13Protocol.UsagePage
                : (ushort)0x0001);
            Usage = descriptor.Usage ?? (providerId == LogitechG13ProviderId
                ? LogitechG13Protocol.Usage
                : (ushort)0x0006);
            IsAmbiguous = providerId == LogitechG13ProviderId &&
                descriptor.Grouping != PhysicalDeviceGrouping.WindowsContainerId;
        }

        internal RuntimeInputDevice(MidiInputDeviceDescriptor descriptor)
        {
            ProviderId = MidiProviderId;
            MidiDeviceId = descriptor.DeviceId;
            SessionHandle = new nint(descriptor.DeviceId + 1);
            SessionId = descriptor.SessionId;
            PersistentId = descriptor.PersistentId;
            DisplayName = descriptor.DisplayName;
            VendorId = descriptor.ManufacturerId;
            ProductId = descriptor.ProductId;
            UsagePage = 0;
            Usage = 0;
            IsAmbiguous = descriptor.IsAmbiguous;
        }

        internal string ProviderId { get; }
        internal nint SessionHandle { get; }
        internal int? MidiDeviceId { get; }
        internal string SessionId { get; }
        internal string PersistentId { get; }
        internal string DisplayName { get; }
        internal string? PresentationModelId { get; }
        internal ushort? VendorId { get; }
        internal ushort? ProductId { get; }
        internal ushort UsagePage { get; }
        internal ushort Usage { get; }
        internal bool IsAmbiguous { get; }

        internal bool HasSameLiveIdentity(RuntimeInputDevice other) =>
            ProviderId == other.ProviderId && SessionId == other.SessionId;
    }
}
