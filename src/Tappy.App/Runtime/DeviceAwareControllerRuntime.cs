using System.Collections.ObjectModel;
using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Core.Profiles;
using Tappy.Windows.Diagnostics;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;
using Tappy.Windows.Output;
using Tappy.Windows.Profiles;

namespace Tappy.App.Runtime;

/// <summary>
/// Composes the keyboard and physical Logitech G13 Raw Input providers with the
/// platform-neutral mapping engine. Identification events have a separate
/// target-only path and can never execute a binding.
/// </summary>
public sealed class DeviceAwareControllerRuntime : IControllerRuntime
{
    private const string DefaultProfileId = "default";
    private const string KeyboardProviderId = "raw-input";
    private const string LogitechG13ProviderId = "raw-hid-g13";
    private const string OutputSafetyFailureStatus =
        "Needs attention: Windows rejected a Tappy output transition, so Tappy cannot confirm that every owned output is released. Mapping output is disarmed and source input remains fail-open pass-through; restart Tappy before rearming.";
    private const string OutputSafetyFailureMappingStatus =
        "Tappy could not confirm a safe output state. Rehearsal Mode remains on; verify the output state in a harmless target and restart Tappy before rearming.";
    private readonly object _gate = new();
    private readonly RawInputKeyboardProvider _keyboardProvider;
    private readonly LogitechG13InputProvider? _logitechG13Provider;
    private readonly IWindowsLifecycleSignalSource? _applicationLifecycleSource;
    private readonly AtomicProfileStore _profileStore;
    private readonly MappingEngine _engine;
    private readonly IControllerActionOutput _actionOutput;
    private readonly Action? _beforeEngineProcess;
    private readonly Action? _beforeDisposeGate;
    private readonly InputDiagnosticAggregate _diagnostics = new();
    private readonly List<RuntimeInputDevice> _devices = [];
    private readonly HashSet<ControlId> _observedControls = [];
    private readonly Dictionary<ControlId, string> _controlLabels = [];
    private TappyProfile _editableProfile = new();
    private RuntimeInputDevice? _candidate;
    private RuntimeInputDevice? _confirmed;
    private bool _isRehearsal = true;
    private bool _initialized;
    private bool _disposed;
    private bool _outputSafetyNeedsAttention;
    private string? _optionalCapabilityWarning;
    private long _aggregateEventCount;

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
            actionOutput: outputs.Actions)
    {
    }

    internal DeviceAwareControllerRuntime(
        RawInputKeyboardProvider provider,
        IKeyboardOutput keyboardOutput,
        AtomicProfileStore profileStore,
        IWindowsLifecycleSignalSource? applicationLifecycleSource = null,
        Action? beforeEngineProcess = null,
        Action? beforeDisposeGate = null,
        IControllerActionOutput? actionOutput = null)
        : this(
            provider,
            null,
            keyboardOutput,
            profileStore,
            applicationLifecycleSource,
            beforeEngineProcess,
            beforeDisposeGate,
            actionOutput)
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
        IControllerActionOutput? actionOutput = null)
    {
        _keyboardProvider = keyboardProvider ?? throw new ArgumentNullException(nameof(keyboardProvider));
        _logitechG13Provider = logitechG13Provider;
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _actionOutput = actionOutput ?? NullControllerActionOutput.Instance;
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
                if (!value && _outputSafetyNeedsAttention)
                {
                    _isRehearsal = true;
                    _ = _engine.SetRehearsalMode(true);
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
                RaiseState(mappingStatus: value
                    ? "Rehearsal Mode is on. Recognition continues; output is suppressed."
                    : "Normal output is enabled for the confirmed controller only.");
            }
        }
    }

    public bool CanConfirmController =>
        _engine.Activation.State == ControllerActivationState.AwaitingConfirmation;

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

        RefreshDevices();
        _initialized = true;
        RaiseState(status: loadStatus);
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
                _confirmed = null;
                _candidate = null;
            }
            else if (_candidate is not null &&
                     !devices.Any(item => item.HasSameLiveIdentity(_candidate)))
            {
                disconnectedSession = new ControllerSessionId(_candidate.SessionId);
                captureWasLost = true;
                _candidate = null;
            }

            if (captureWasLost)
            {
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
                    identificationStatus: "The selected controller is no longer present. Select and identify it again after reconnect.",
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

            releasedUpdates = CaptureReleasedUpdatesLocked();
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
                failure =
                    "Nothing was armed. Release every keyboard and controller control, then try again with the mouse; refresh the device list if everything is already neutral.";
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
            if (_outputSafetyNeedsAttention)
            {
                return RuntimeOperation.Failed(
                    "An earlier Tappy-owned output release was rejected. Restart Tappy before confirming a controller.");
            }

            if (_candidate is null ||
                _engine.Activation.State != ControllerActivationState.AwaitingConfirmation ||
                !IsCaptureTargetNeutral(_candidate))
            {
                return RuntimeOperation.Failed(
                    "Confirmation is unavailable until the selected device completes one press and release and all its controls are up.");
            }

            descriptor = _candidate;
            if (!SetConfirmedPersistentId(descriptor, descriptor.PersistentId))
            {
                return RuntimeOperation.Failed("The device changed or is not neutral. Identification remains unconfirmed.");
            }

            _engine.Activation.Confirm();
            var identity = ToIdentity(descriptor);
            controller = FindEditableController(descriptor.PersistentId) ??
                         ControllerProfile.Create(
                             identity,
                             descriptor.ProviderId == LogitechG13ProviderId
                                 ? LogitechG13InputProvider.SupportedControls.Select(item => item.ControlId)
                                 : null,
                             defaultLayerCount: 3);
            controller.Identity = identity;
            controller.DisplayName = descriptor.DisplayName;
            if (descriptor.ProviderId == LogitechG13ProviderId)
            {
                controller.Layout = CreateLogitechG13Layout();
            }
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
            _candidate = null;
            RecordCleanupResultLocked(_engine.SetProfile(_editableProfile.CreateSnapshot()));
            _engine.ConnectController(identity);
            restoredControls = CreateControlUpdatesLocked(controller, isSnapshot: true);
        }

        PublishReleasedUpdates(restoredControls);
        RaiseState(
            identificationStatus: "Controller confirmed for this session. Only its events can reach mappings.",
            status: controller.SourceMode.Effective == EffectiveSourceMode.PassThrough
                ? "Controller ready in Device-aware pass-through."
                : "Needs attention: the requested source backend is unavailable, so mappings remain disarmed and source input passes through.",
            activeControllerLabel: descriptor.DisplayName,
            activeLayerName: controller.Layers.First(layer => layer.Id == controller.ActiveLayerId).Name,
            sourceLabel: SourceLabel(controller));
        return RuntimeOperation.Ok("Controller confirmed. Original input remains pass-through.");
    }

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
            if (_confirmed.ProviderId == LogitechG13ProviderId &&
                !LogitechG13InputProvider.SupportedControls.Any(item => item.ControlId == id))
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

    public RuntimeOperation AssignControllerAction(string controlId, ControllerActionAssignment assignment)
    {
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
            if (_confirmed.ProviderId == LogitechG13ProviderId &&
                !LogitechG13InputProvider.SupportedControls.Any(item => item.ControlId == id))
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

        if (_applicationLifecycleSource is not null)
        {
            _applicationLifecycleSource.LifecycleChanged -= Provider_OnLifecycleChanged;
        }
        if (_logitechG13Provider is not null)
        {
            await _logitechG13Provider.DisposeAsync().ConfigureAwait(false);
        }

        await _keyboardProvider.DisposeAsync().ConfigureAwait(false);
        if (_actionOutput is IDisposable disposableActionOutput)
        {
            disposableActionOutput.Dispose();
        }
    }

    private void KeyboardProvider_OnIdentificationInputReceived(object? sender, KeyboardInputReceivedEventArgs eventArgs) =>
        ProcessIdentificationInput(KeyboardProviderId, eventArgs.Input.Signal, eventArgs.Input.Key.DisplayName);

    private void LogitechG13Provider_OnIdentificationInputReceived(object? sender, LogitechG13InputReceivedEventArgs eventArgs) =>
        ProcessIdentificationInput(LogitechG13ProviderId, eventArgs.Input.Signal, eventArgs.Input.DisplayName);

    private void ProcessIdentificationInput(string providerId, ControlSignal signal, string displayName)
    {
        MappingResult result;
        ControllerActivationState state;
        lock (_gate)
        {
            if (_disposed ||
                _candidate?.ProviderId != providerId ||
                signal.ControllerSessionId != new ControllerSessionId(_candidate.SessionId))
            {
                return;
            }

            _beforeEngineProcess?.Invoke();
            result = _engine.Process(signal);
            state = _engine.Activation.State;
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

    private void ProcessInput(
        string providerId,
        string persistentDeviceId,
        ControllerSessionId controllerSessionId,
        ControlId controlId,
        string displayName,
        bool isPressed,
        bool isRepeat,
        ControlSignal signal)
    {
        MappingResult result;
        long eventCount;
        string action;
        int simultaneous;
        lock (_gate)
        {
            if (_disposed ||
                _confirmed is null ||
                _confirmed.ProviderId != providerId ||
                persistentDeviceId != _confirmed.PersistentId)
            {
                return;
            }

            _beforeEngineProcess?.Invoke();
            result = _engine.Process(signal);
            eventCount = Interlocked.Increment(ref _aggregateEventCount);
            var controller = FindEditableController(persistentDeviceId);
            action = controller is null ? "Unassigned" : BindingLabel(controller, controlId);
            if (controller is not null)
            {
                var observed = _observedControls.Add(controlId);
                var labelChanged = !_controlLabels.TryGetValue(controlId, out var oldLabel) ||
                                   !string.Equals(oldLabel, displayName, StringComparison.Ordinal);
                _controlLabels[controlId] = displayName;
                if ((observed || labelChanged) && providerId != LogitechG13ProviderId)
                {
                    RebuildObservedLayout(controller);
                }
            }

            simultaneous = _engine.InputStates.GetPressedControls(controllerSessionId).Count;
            if (result.Disposition == MappingDisposition.OutputFailed)
            {
                _outputSafetyNeedsAttention = true;
                _isRehearsal = true;
                _ = _engine.SetRehearsalMode(true);
            }
        }

        ControlChanged?.Invoke(this, new RuntimeControlUpdate(
            persistentDeviceId,
            controlId.Value,
            displayName,
            isPressed,
            isRepeat,
            action,
            simultaneous,
            eventCount));

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
                    releasedUpdates = CaptureReleasedUpdatesLocked();
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
                var changeDescription = selectedMembershipChanged
                    ? "A selected controller interface changed. Its Tappy-owned outputs were released. Select and identify it again."
                    : "The selected controller was removed. Its Tappy-owned outputs were released. Select and identify it again after reconnect.";
                RaiseState(
                    identificationStatus: changeDescription,
                    mappingStatus: "Rehearsal Mode was restored; mappings are disarmed.",
                    status: selectedMembershipChanged
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
        if (eventArgs.Signal is WindowsLifecycleSignal.SessionLocked or
            WindowsLifecycleSignal.Suspending or
            WindowsLifecycleSignal.ShutdownRequested or
            WindowsLifecycleSignal.Shutdown)
        {
            RuntimeControlUpdate[] releasedUpdates;
            bool releaseSucceeded;
            lock (_gate)
            {
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
                    identificationStatus: "Windows changed session state. Re-identify the controller before using mappings again.",
                    mappingStatus: "Rehearsal Mode was restored and all Tappy-owned output was released.",
                    status: $"Nothing is armed after Windows lifecycle event: {eventArgs.Signal}.",
                    activeControllerLabel: "No controller confirmed");
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
                identificationStatus: "Raw Input is unavailable. Nothing is armed; re-identification is required after recovery.",
                mappingStatus: "The input backend failed; output is disabled, released, and Rehearsal Mode was restored.",
                status: $"Needs attention: {PrivacyRedactor.SanitizeDiagnosticText(exception.Message)}",
                activeControllerLabel: "No controller confirmed",
                sourceLabel: "Effective: Needs attention (fail-open)");
        }
        else
        {
            RaiseOutputSafetyFailureState(
                identificationStatus: "Raw Input failed and Windows rejected an owned-output release. Restart Tappy before rearming.",
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
        bool isSnapshot = false) =>
        GetControlsInPresentationOrder(controller).Select(control =>
            new RuntimeControlUpdate(
                persistentId ?? controller?.Identity.PersistentId?.Value ?? string.Empty,
                control.Value,
                _controlLabels.GetValueOrDefault(control, control.Value),
                false,
                false,
                BindingLabel(controller, control),
                0,
                eventCount ?? Interlocked.Read(ref _aggregateEventCount),
                isSnapshot))
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
        return ordered;
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
        string? optionalCapabilityWarning;
        lock (_gate)
        {
            confirmed = _confirmed;
            controller = confirmed is null ? null : FindEditableController(confirmed.PersistentId);
            identificationCaptureActive = _candidate is not null;
            optionalCapabilityWarning = _optionalCapabilityWarning;
        }

        var effectiveStatus = status ?? (confirmed is null ? "Nothing is armed." : "Controller ready.");
        if (!string.IsNullOrWhiteSpace(optionalCapabilityWarning) &&
            !effectiveStatus.Contains(optionalCapabilityWarning, StringComparison.Ordinal))
        {
            effectiveStatus = $"{effectiveStatus} {optionalCapabilityWarning}";
        }

        StateChanged?.Invoke(this, new RuntimeState(
            confirmed is not null,
            _engine.Activation.State == ControllerActivationState.AwaitingConfirmation,
            identificationStatus ?? (confirmed is null ? "Choose and identify a spare controller." : "Controller confirmed."),
            activeControllerLabel ?? confirmed?.DisplayName ?? "No controller confirmed",
            activeLayerName ?? controller?.Layers.FirstOrDefault(item => item.Id == controller.ActiveLayerId)?.Name ?? "Layer 1",
            mappingStatus ?? (IsRehearsal ? "Rehearsal Mode suppresses output." : "Normal output is enabled for confirmed mappings."),
            effectiveStatus,
            sourceLabel ?? (controller is null ? "Effective: Pass-through" : SourceLabel(controller)),
            IsIdentificationCaptureActive: identificationCaptureActive));
    }

    private ControllerProfile? FindEditableController(string persistentId) =>
        _editableProfile.Controllers.FirstOrDefault(controller =>
            controller.Identity.PersistentId is { } id && id.Value == persistentId);

    private static string BindingLabel(ControllerProfile? controller, ControlId controlId)
    {
        if (controller is null)
        {
            return "Unassigned";
        }

        var layer = controller.Layers.FirstOrDefault(item => item.Id == controller.ActiveLayerId);
        return layer?.Bindings.FirstOrDefault(item => item.ControlId == controlId)?.Name ?? "Unassigned";
    }

    private void EnsureObservedLayout(ControllerProfile controller, ControlId controlId)
    {
        _observedControls.Add(controlId);
        if (controller.Identity.ProviderId == LogitechG13ProviderId)
        {
            if (!controller.Layout.Rows.SelectMany(row => row.Controls)
                    .Any(item => item.ControlId == controlId))
            {
                controller.Layout = CreateLogitechG13Layout();
            }

            return;
        }

        RebuildObservedLayout(controller);
    }

    private void RebuildObservedLayout(ControllerProfile controller)
    {
        controller.Layout = ControllerLayoutDefinition.CreateGrid(
            _observedControls.OrderBy(item => item.Value, StringComparer.Ordinal));
        foreach (var layoutControl in controller.Layout.Rows.SelectMany(row => row.Controls))
        {
            if (layoutControl.ControlId is { } controlId &&
                _controlLabels.TryGetValue(controlId, out var label))
            {
                layoutControl.Label = label;
            }
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
        controller.SourceMode.Effective switch
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
    }

    private bool SetCaptureTarget(RuntimeInputDevice device) =>
        device.ProviderId switch
        {
            KeyboardProviderId => _keyboardProvider.SetCaptureTarget(device.SessionHandle),
            LogitechG13ProviderId when _logitechG13Provider is not null =>
                _logitechG13Provider.SetCaptureTarget(device.SessionHandle),
            _ => false,
        };

    private bool SetConfirmedPersistentId(RuntimeInputDevice device, string persistentId) =>
        device.ProviderId switch
        {
            KeyboardProviderId => _keyboardProvider.SetConfirmedPersistentId(persistentId),
            LogitechG13ProviderId when _logitechG13Provider is not null =>
                _logitechG13Provider.SetConfirmedPersistentId(persistentId),
            _ => false,
        };

    private bool IsCaptureTargetNeutral(RuntimeInputDevice device) =>
        device.ProviderId switch
        {
            KeyboardProviderId => _keyboardProvider.IsCaptureTargetNeutral,
            LogitechG13ProviderId when _logitechG13Provider is not null =>
                _logitechG13Provider.IsCaptureTargetNeutral,
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
            new LogitechG13InputProvider(new NativeLogitechG13DeviceEnumerator(), host));
    }

    private static ProductionOutputs CreateProductionOutputs()
    {
        var keyboard = new SendInputKeyboardOutput();
        return new ProductionOutputs(keyboard, new WindowsControllerActionOutput(keyboard));
    }

    private static ControllerIdentity ToIdentity(RuntimeInputDevice descriptor) => new(
        new ControllerSessionId(descriptor.SessionId),
        new ControllerPersistentId(descriptor.PersistentId),
        descriptor.ProviderId == LogitechG13ProviderId &&
        descriptor.Descriptor.Grouping != PhysicalDeviceGrouping.WindowsContainerId
            ? ControllerIdentityConfidence.Ambiguous
            : ControllerIdentityConfidence.PortBound,
        descriptor.DisplayName,
        providerId: descriptor.ProviderId,
        descriptor.VendorId,
        descriptor.ProductId,
        descriptor.UsagePage ?? (descriptor.ProviderId == LogitechG13ProviderId
            ? LogitechG13Protocol.UsagePage
            : (ushort)0x0001),
        descriptor.Usage ?? (descriptor.ProviderId == LogitechG13ProviderId
            ? LogitechG13Protocol.Usage
            : (ushort)0x0006));

    private static ControllerChoice ToChoice(RuntimeInputDevice descriptor) => new(
        descriptor.SessionId,
        descriptor.PersistentId,
        descriptor.DisplayName,
        (descriptor.ProviderId == LogitechG13ProviderId &&
         descriptor.Descriptor.Grouping != PhysicalDeviceGrouping.WindowsContainerId
            ? ControllerIdentityConfidence.Ambiguous
            : ControllerIdentityConfidence.PortBound).ToString(),
        descriptor.ProviderId);

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
        LogitechG13InputProvider LogitechG13);

    private sealed record ProductionOutputs(
        SendInputKeyboardOutput Keyboard,
        WindowsControllerActionOutput Actions);

    private sealed record RuntimeInputDevice(
        string ProviderId,
        SanitizedDeviceDescriptor Descriptor)
    {
        internal nint SessionHandle => Descriptor.SessionHandle;
        internal string SessionId => Descriptor.SessionId;
        internal string PersistentId => Descriptor.PersistentId;
        internal string DisplayName => Descriptor.DisplayName;
        internal ushort? VendorId => Descriptor.VendorId;
        internal ushort? ProductId => Descriptor.ProductId;
        internal ushort? UsagePage => Descriptor.UsagePage;
        internal ushort? Usage => Descriptor.Usage;

        internal bool HasSameLiveIdentity(RuntimeInputDevice other) =>
            ProviderId == other.ProviderId && SessionHandle == other.SessionHandle;
    }
}
