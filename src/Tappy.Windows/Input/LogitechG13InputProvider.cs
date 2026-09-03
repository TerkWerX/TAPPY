using System.Diagnostics;
using Tappy.Core.Abstractions;
using Tappy.Core.Input;
using Tappy.Windows.Lifecycle;

namespace Tappy.Windows.Input;

/// <summary>
/// Explicit-selection Raw Input provider for the physical Logitech G13 vendor HID
/// collection (046D:C21C, FF00:0000). It never treats the C232 emulated keyboard as
/// the G13 and never publishes input before persistent identity confirmation.
/// </summary>
public sealed class LogitechG13InputProvider : IInputDeviceProvider, IWindowsLifecycleSignalSource
{
    private readonly object _gate = new();
    private readonly ILogitechG13DeviceEnumerator _deviceEnumerator;
    private readonly IRawHidInputMessageHost _messageHost;
    private readonly Func<long> _timestampProvider;
    private readonly LogitechG13ReportDecoder _decoder = new();
    private readonly Dictionary<nint, SanitizedDeviceDescriptor> _logicalDescriptors = new();
    private readonly Dictionary<nint, SanitizedDeviceDescriptor> _descriptorByMember = new();
    private readonly Dictionary<LogitechG13Control, LogitechG13Input> _heldControls = new();
    private nint? _captureTarget;
    private string? _confirmedPersistentId;
    private bool _isAvailable = true;
    private string _availabilityStatus = "Logitech G13 Raw Input is available.";
    private bool _disposed;

    public LogitechG13InputProvider()
        : this(new NativeLogitechG13DeviceEnumerator(), new RawInputMessageHost())
    {
    }

    public LogitechG13InputProvider(
        ILogitechG13DeviceEnumerator deviceEnumerator,
        IRawHidInputMessageHost messageHost,
        Func<long>? timestampProvider = null)
    {
        _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _messageHost = messageHost ?? throw new ArgumentNullException(nameof(messageHost));
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _messageHost.HidPacketReceived += OnHidPacketReceived;
        _messageHost.DeviceChanged += OnNativeDeviceChanged;
        _messageHost.LifecycleChanged += OnLifecycleChanged;
        _messageHost.Faulted += OnHostFaulted;
    }

    public event EventHandler<LogitechG13InputReceivedEventArgs>? InputReceived;

    public event EventHandler<LogitechG13InputReceivedEventArgs>? IdentificationInputReceived;

    public event EventHandler<LogitechG13AnalogChangedEventArgs>? AnalogStateChanged;

    public event EventHandler<LogitechG13DeviceChangedEventArgs>? DeviceChanged;

    public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;

    public event EventHandler<Exception>? Faulted;

    public event EventHandler? AvailabilityChanged;

    public event Action<ControlSignal>? SignalReceived;

    public event Action? DevicesChanged;

    public static IReadOnlyList<LogitechG13ControlDefinition> SupportedControls =>
        LogitechG13ReportDecoder.Controls;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _isAvailable;
            }
        }
    }

    public string AvailabilityStatus
    {
        get
        {
            lock (_gate)
            {
                return _availabilityStatus;
            }
        }
    }

    public bool IsCaptureConfirmed
    {
        get
        {
            lock (_gate)
            {
                return CaptureIsConfirmedLocked();
            }
        }
    }

    public IReadOnlyList<ControllerIdentity> ConnectedControllers
    {
        get
        {
            lock (_gate)
            {
                return _logicalDescriptors.Values
                    .Select(ToCoreIdentity)
                    .OrderBy(identity => identity.SessionId.Value, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public nint? CaptureTarget
    {
        get
        {
            lock (_gate)
            {
                return _captureTarget;
            }
        }
    }

    /// <summary>
    /// False after selection until at least one well-formed neutral report from the
    /// selected physical C21C collection has been observed.
    /// </summary>
    public bool IsCaptureTargetNeutral
    {
        get
        {
            lock (_gate)
            {
                return _captureTarget is null ||
                    (_decoder.HasObservedFrame && _heldControls.Count == 0);
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = EnumerateControllers();
        await _messageHost.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ClearCaptureTarget();
        await _messageHost.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    ValueTask IInputDeviceProvider.StartAsync(CancellationToken cancellationToken) =>
        new(StartAsync(cancellationToken));

    ValueTask IInputDeviceProvider.StopAsync(CancellationToken cancellationToken) =>
        new(StopAsync(cancellationToken));

    public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateControllers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (!_isAvailable)
            {
                return [];
            }
        }

        var descriptors = _deviceEnumerator.EnumerateControllers()
            .Where(IsExpectedDescriptor)
            .ToArray();
        List<PendingInput> releases = [];
        lock (_gate)
        {
            var targetRemains = _captureTarget is not { } target ||
                descriptors.Any(descriptor => descriptor.SessionHandle == target);
            if (!targetRemains)
            {
                releases.AddRange(ReleaseAllLocked());
                _captureTarget = null;
                _confirmedPersistentId = null;
            }

            ReplaceDescriptorsLocked(descriptors);
        }

        Publish(releases);
        return descriptors.ToArray();
    }

    public bool SetCaptureTarget(nint? deviceHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (deviceHandle is null)
        {
            ClearCaptureTarget();
            return true;
        }

        SanitizedDeviceDescriptor? descriptor;
        lock (_gate)
        {
            if (!_isAvailable)
            {
                return false;
            }

            descriptor = _logicalDescriptors.GetValueOrDefault(deviceHandle.Value) ??
                _descriptorByMember.GetValueOrDefault(deviceHandle.Value);
        }

        descriptor ??= _deviceEnumerator.DescribeController(deviceHandle.Value);
        if (descriptor is null || !IsExpectedDescriptor(descriptor))
        {
            return false;
        }

        List<PendingInput> releases;
        lock (_gate)
        {
            if (!_isAvailable)
            {
                return false;
            }

            releases = ReleaseAllLocked();
            AddDescriptorLocked(descriptor);
            _captureTarget = descriptor.SessionHandle;
            _confirmedPersistentId = null;
            _decoder.Reset();
        }

        Publish(releases);
        return true;
    }

    public bool SetConfirmedPersistentId(string? persistentId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (persistentId is null)
            {
                _confirmedPersistentId = null;
                return true;
            }

            if (_captureTarget is not { } target ||
                !_logicalDescriptors.TryGetValue(target, out var descriptor) ||
                !string.Equals(descriptor.PersistentId, persistentId, StringComparison.Ordinal) ||
                !_decoder.HasObservedFrame ||
                _heldControls.Count != 0)
            {
                return false;
            }

            _confirmedPersistentId = persistentId;
            return true;
        }
    }

    public void ClearCaptureTarget()
    {
        List<PendingInput> releases;
        lock (_gate)
        {
            releases = ReleaseAllLocked();
            _captureTarget = null;
            _confirmedPersistentId = null;
        }

        Publish(releases);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        ClearCaptureTarget();
        _disposed = true;
        _messageHost.HidPacketReceived -= OnHidPacketReceived;
        _messageHost.DeviceChanged -= OnNativeDeviceChanged;
        _messageHost.LifecycleChanged -= OnLifecycleChanged;
        _messageHost.Faulted -= OnHostFaulted;
        await _messageHost.DisposeAsync().ConfigureAwait(false);
    }

    private void OnHidPacketReceived(object? sender, RawHidInputPacketEventArgs eventArgs)
    {
        List<PendingInput> inputs = [];
        LogitechG13AnalogChangedEventArgs? analogChange = null;
        lock (_gate)
        {
            var packet = eventArgs.Packet;
            if (!_isAvailable ||
                !_descriptorByMember.TryGetValue(packet.DeviceHandle, out var descriptor) ||
                _captureTarget != descriptor.SessionHandle ||
                !IsExpectedDescriptor(descriptor))
            {
                return;
            }

            var transitions = _decoder.Process(packet, out var analogChanged);
            var confirmed = CaptureIsConfirmedLocked();
            foreach (var transition in transitions)
            {
                if (transition.Kind == ControlSignalKind.Release &&
                    !_heldControls.ContainsKey(transition.Definition.Control))
                {
                    continue;
                }

                var input = CreateInput(packet.DeviceHandle, descriptor, transition);
                if (transition.Kind == ControlSignalKind.Press)
                {
                    _heldControls[transition.Definition.Control] = input;
                }
                else
                {
                    _heldControls.Remove(transition.Definition.Control);
                }

                inputs.Add(new PendingInput(input, confirmed));
            }

            if (analogChanged && confirmed)
            {
                analogChange = new LogitechG13AnalogChangedEventArgs(
                    new ControllerSessionId(descriptor.SessionId),
                    _decoder.AnalogState);
            }
        }

        Publish(inputs);
        if (analogChange is not null)
        {
            AnalogStateChanged?.Invoke(this, analogChange);
        }
    }

    private void OnNativeDeviceChanged(object? sender, NativeDeviceChangeEventArgs eventArgs)
    {
        lock (_gate)
        {
            if (!_isAvailable)
            {
                return;
            }
        }

        SanitizedDeviceDescriptor? descriptor;
        bool wasCaptureTarget;
        RawInputDeviceChangeKind logicalChangeKind;
        List<PendingInput> releases = [];

        if (eventArgs.Kind == RawInputDeviceChangeKind.Arrival)
        {
            var enumerated = _deviceEnumerator.EnumerateControllers()
                .Where(IsExpectedDescriptor)
                .ToList();
            descriptor = enumerated.FirstOrDefault(candidate =>
                candidate.ContainsSessionHandle(eventArgs.DeviceHandle));
            if (descriptor is null)
            {
                descriptor = _deviceEnumerator.DescribeController(eventArgs.DeviceHandle);
                if (descriptor is null || !IsExpectedDescriptor(descriptor))
                {
                    return;
                }

                enumerated.Add(descriptor);
            }

            lock (_gate)
            {
                var previous = _logicalDescriptors.Values.FirstOrDefault(candidate =>
                    string.Equals(candidate.PersistentId, descriptor.PersistentId, StringComparison.Ordinal));
                ReplaceDescriptorsLocked(enumerated);
                descriptor = _descriptorByMember.GetValueOrDefault(eventArgs.DeviceHandle) ?? descriptor;
                wasCaptureTarget = _captureTarget == descriptor.SessionHandle;
                logicalChangeKind = previous is null
                    ? RawInputDeviceChangeKind.Arrival
                    : RawInputDeviceChangeKind.MembershipChanged;
            }
        }
        else
        {
            var enumerated = ExcludingMember(
                _deviceEnumerator.EnumerateControllers().Where(IsExpectedDescriptor).ToArray(),
                eventArgs.DeviceHandle);
            lock (_gate)
            {
                _descriptorByMember.TryGetValue(eventArgs.DeviceHandle, out var previous);
                if (previous is null)
                {
                    return;
                }

                wasCaptureTarget = _captureTarget == previous.SessionHandle;
                if (wasCaptureTarget)
                {
                    releases.AddRange(ReleaseAllLocked());
                    _confirmedPersistentId = null;
                }

                ReplaceDescriptorsLocked(enumerated);
                var remaining = _logicalDescriptors.Values.FirstOrDefault(candidate =>
                    string.Equals(candidate.PersistentId, previous.PersistentId, StringComparison.Ordinal));
                if (remaining is null)
                {
                    descriptor = previous;
                    logicalChangeKind = RawInputDeviceChangeKind.Removal;
                    if (wasCaptureTarget)
                    {
                        _captureTarget = null;
                    }
                }
                else
                {
                    descriptor = remaining;
                    logicalChangeKind = RawInputDeviceChangeKind.MembershipChanged;
                }
            }
        }

        Publish(releases);
        DeviceChanged?.Invoke(
            this,
            new LogitechG13DeviceChangedEventArgs(logicalChangeKind, descriptor, wasCaptureTarget));
        DevicesChanged?.Invoke();
    }

    private void OnLifecycleChanged(object? sender, WindowsLifecycleSignalEventArgs eventArgs)
    {
        List<PendingInput> releases = [];
        if (eventArgs.Signal is WindowsLifecycleSignal.SessionLocked or
            WindowsLifecycleSignal.Suspending or
            WindowsLifecycleSignal.ShutdownRequested or
            WindowsLifecycleSignal.Shutdown)
        {
            lock (_gate)
            {
                releases.AddRange(ReleaseAllLocked());
                _confirmedPersistentId = null;
            }
        }

        Publish(releases);
        LifecycleChanged?.Invoke(this, eventArgs);
    }

    private void OnHostFaulted(object? sender, Exception exception)
    {
        if (exception is RawInputCapabilityException
            {
                Capability: RawInputCapability.LogitechG13,
            })
        {
            HandleCapabilityFailure();
            return;
        }

        List<PendingInput> releases;
        lock (_gate)
        {
            releases = ReleaseAllLocked();
            _captureTarget = null;
            _confirmedPersistentId = null;
        }

        Publish(releases);
        Faulted?.Invoke(this, exception);
    }

    private void HandleCapabilityFailure()
    {
        List<PendingInput> releases;
        lock (_gate)
        {
            if (!_isAvailable)
            {
                return;
            }

            releases = ReleaseAllLocked();
            _captureTarget = null;
            _confirmedPersistentId = null;
            _logicalDescriptors.Clear();
            _descriptorByMember.Clear();
            _isAvailable = false;
            _availabilityStatus =
                "Logitech G13 Raw Input is unavailable; keyboard controllers remain available.";
        }

        Publish(releases);
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private LogitechG13Input CreateInput(
        nint memberHandle,
        SanitizedDeviceDescriptor descriptor,
        LogitechG13DecodedTransition transition)
    {
        var sessionId = new ControllerSessionId(descriptor.SessionId);
        var signal = ControlSignal.Physical(
            sessionId,
            transition.Definition.ControlId,
            transition.Kind,
            _timestampProvider());
        return new LogitechG13Input(
            memberHandle,
            sessionId,
            descriptor.PersistentId,
            transition.Definition.ControlId,
            transition.Definition.Control,
            transition.Definition.DisplayName,
            transition.Definition.ButtonBitIndex,
            _decoder.AnalogState,
            signal);
    }

    private List<PendingInput> ReleaseAllLocked()
    {
        var confirmed = CaptureIsConfirmedLocked();
        var releases = _heldControls.Values
            .OrderBy(input => input.ControlId.Value, StringComparer.Ordinal)
            .Select(input => new PendingInput(
                input with
                {
                    AnalogState = _decoder.AnalogState,
                    Signal = input.Signal with
                    {
                        Kind = ControlSignalKind.Release,
                        Timestamp = _timestampProvider(),
                    },
                },
                confirmed))
            .ToList();
        _heldControls.Clear();
        _decoder.Reset();
        return releases;
    }

    private void Publish(IEnumerable<PendingInput> inputs)
    {
        foreach (var pending in inputs)
        {
            if (pending.Confirmed)
            {
                InputReceived?.Invoke(this, new LogitechG13InputReceivedEventArgs(pending.Input));
                SignalReceived?.Invoke(pending.Input.Signal);
            }
            else
            {
                IdentificationInputReceived?.Invoke(
                    this,
                    new LogitechG13InputReceivedEventArgs(pending.Input));
            }
        }
    }

    private bool CaptureIsConfirmedLocked() =>
        _captureTarget is { } target &&
        _confirmedPersistentId is not null &&
        _logicalDescriptors.TryGetValue(target, out var descriptor) &&
        string.Equals(descriptor.PersistentId, _confirmedPersistentId, StringComparison.Ordinal);

    private void ReplaceDescriptorsLocked(IEnumerable<SanitizedDeviceDescriptor> descriptors)
    {
        _logicalDescriptors.Clear();
        _descriptorByMember.Clear();
        foreach (var descriptor in descriptors.Where(IsExpectedDescriptor))
        {
            AddDescriptorLocked(descriptor);
        }
    }

    private void AddDescriptorLocked(SanitizedDeviceDescriptor descriptor)
    {
        _logicalDescriptors[descriptor.SessionHandle] = descriptor;
        foreach (var memberHandle in descriptor.MemberSessionHandles)
        {
            _descriptorByMember[memberHandle] = descriptor;
        }
    }

    private static bool IsExpectedDescriptor(SanitizedDeviceDescriptor descriptor) =>
        descriptor.Kind == RawInputDeviceKind.Hid &&
        descriptor.VendorId == LogitechG13Protocol.VendorId &&
        descriptor.ProductId == LogitechG13Protocol.ProductId &&
        descriptor.UsagePage == LogitechG13Protocol.UsagePage &&
        descriptor.Usage == LogitechG13Protocol.Usage;

    private static ControllerIdentity ToCoreIdentity(SanitizedDeviceDescriptor descriptor) =>
        new(
            new ControllerSessionId(descriptor.SessionId),
            new ControllerPersistentId(descriptor.PersistentId),
            descriptor.Grouping == PhysicalDeviceGrouping.WindowsContainerId
                ? ControllerIdentityConfidence.PortBound
                : ControllerIdentityConfidence.Ambiguous,
            descriptor.DisplayName,
            providerId: "raw-hid-g13",
            vendorId: descriptor.VendorId,
            productId: descriptor.ProductId,
            usagePage: LogitechG13Protocol.UsagePage,
            usage: LogitechG13Protocol.Usage);

    private static IReadOnlyList<SanitizedDeviceDescriptor> ExcludingMember(
        IReadOnlyList<SanitizedDeviceDescriptor> descriptors,
        nint removedHandle)
    {
        var result = new List<SanitizedDeviceDescriptor>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            if (!descriptor.ContainsSessionHandle(removedHandle))
            {
                result.Add(descriptor);
                continue;
            }

            var remaining = descriptor.MemberSessionHandles
                .Where(handle => handle != removedHandle)
                .ToArray();
            if (remaining.Length != 0)
            {
                result.Add(descriptor with { MemberSessionHandles = remaining });
            }
        }

        return result;
    }

    private sealed record PendingInput(LogitechG13Input Input, bool Confirmed);
}
