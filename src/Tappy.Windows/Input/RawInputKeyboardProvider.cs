using System.Diagnostics;
using Tappy.Core.Abstractions;
using Tappy.Core.Input;
using Tappy.Windows.Lifecycle;
using Tappy.Windows.Output;

namespace Tappy.Windows.Input;

public sealed class KeyboardInputReceivedEventArgs(NormalizedKeyboardInput input) : EventArgs
{
    public NormalizedKeyboardInput Input { get; } = input;
}

public sealed class KeyboardDeviceChangedEventArgs(
    RawInputDeviceChangeKind kind,
    SanitizedDeviceDescriptor? descriptor,
    bool wasCaptureTarget) : EventArgs
{
    public RawInputDeviceChangeKind Kind { get; } = kind;

    public SanitizedDeviceDescriptor? Descriptor { get; } = descriptor;

    public bool WasCaptureTarget { get; } = wasCaptureTarget;
}

/// <summary>
/// Device-specific keyboard provider. Merely starting or enumerating the provider
/// never selects a keyboard. Both a session handle and its sanitized persistent ID
/// must be explicitly confirmed before any input event leaves this boundary.
/// </summary>
public sealed class RawInputKeyboardProvider : IInputDeviceProvider, IWindowsLifecycleSignalSource
{
    private readonly object _gate = new();
    private readonly IRawInputDeviceEnumerator _deviceEnumerator;
    private readonly IRawInputMessageHost _messageHost;
    private readonly Func<long> _timestampProvider;
    private readonly Dictionary<nint, SanitizedDeviceDescriptor> _logicalDescriptors = new();
    private readonly Dictionary<nint, SanitizedDeviceDescriptor> _descriptorByMember = new();
    private readonly Dictionary<PhysicalControlKey, HeldPhysicalControl> _heldControls = new();
    private nint? _captureTarget;
    private string? _confirmedPersistentId;
    private bool _disposed;

    public RawInputKeyboardProvider()
        : this(new NativeRawInputDeviceEnumerator(), new RawInputMessageHost())
    {
    }

    public RawInputKeyboardProvider(
        IRawInputDeviceEnumerator deviceEnumerator,
        IRawInputMessageHost messageHost,
        Func<long>? timestampProvider = null)
    {
        _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _messageHost = messageHost ?? throw new ArgumentNullException(nameof(messageHost));
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _messageHost.KeyboardPacketReceived += OnKeyboardPacketReceived;
        _messageHost.DeviceChanged += OnNativeDeviceChanged;
        _messageHost.LifecycleChanged += OnLifecycleChanged;
        _messageHost.Faulted += OnHostFaulted;
    }

    public event EventHandler<KeyboardInputReceivedEventArgs>? InputReceived;

    public event Action<ControlSignal>? SignalReceived;

    public event Action? DevicesChanged;

    /// <summary>
    /// Target-only input used by the select/identify/release confirmation UI. These
    /// events are never sent through the normal mapping event.
    /// </summary>
    public event EventHandler<KeyboardInputReceivedEventArgs>? IdentificationInputReceived;

    public event EventHandler<KeyboardDeviceChangedEventArgs>? DeviceChanged;

    public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;

    public event EventHandler<Exception>? Faulted;

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
                    .Select(descriptor => descriptor.ToCoreIdentity())
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

    public bool IsCaptureTargetNeutral
    {
        get
        {
            lock (_gate)
            {
                return _captureTarget is not { } target ||
                    !_logicalDescriptors.TryGetValue(target, out var descriptor) ||
                    !_heldControls.Values.Any(held =>
                        held.MemberHandles.Any(descriptor.ContainsSessionHandle));
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = EnumerateKeyboards();
        await _messageHost.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _messageHost.StopAsync(cancellationToken);

    ValueTask IInputDeviceProvider.StartAsync(CancellationToken cancellationToken) =>
        new(StartAsync(cancellationToken));

    ValueTask IInputDeviceProvider.StopAsync(CancellationToken cancellationToken) =>
        new(StopAsync(cancellationToken));

    public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var descriptors = _deviceEnumerator.EnumerateKeyboards();
        lock (_gate)
        {
            ReplaceDescriptorsLocked(descriptors);
        }

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
            _logicalDescriptors.TryGetValue(deviceHandle.Value, out descriptor);
        }

        descriptor ??= _deviceEnumerator.DescribeKeyboard(deviceHandle.Value);
        if (descriptor is null)
        {
            return false;
        }

        lock (_gate)
        {
            AddDescriptorLocked(descriptor);
            _captureTarget = descriptor.SessionHandle;
            _confirmedPersistentId = null;
            _heldControls.Clear();
        }

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
                _heldControls.Clear();
                return true;
            }

            if (_captureTarget is not { } target ||
                !_logicalDescriptors.TryGetValue(target, out var descriptor) ||
                !string.Equals(descriptor.PersistentId, persistentId, StringComparison.Ordinal) ||
                _heldControls.Values.Any(held =>
                    held.MemberHandles.Any(descriptor.ContainsSessionHandle)))
            {
                return false;
            }

            _confirmedPersistentId = persistentId;
            _heldControls.Clear();
            return true;
        }
    }

    public void ClearCaptureTarget()
    {
        lock (_gate)
        {
            _captureTarget = null;
            _confirmedPersistentId = null;
            _heldControls.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageHost.KeyboardPacketReceived -= OnKeyboardPacketReceived;
        _messageHost.DeviceChanged -= OnNativeDeviceChanged;
        _messageHost.LifecycleChanged -= OnLifecycleChanged;
        _messageHost.Faulted -= OnHostFaulted;
        await _messageHost.DisposeAsync().ConfigureAwait(false);
    }

    private void OnKeyboardPacketReceived(object? sender, RawKeyboardPacketEventArgs eventArgs)
    {
        var packet = eventArgs.Packet;
        if (InjectedInputMarker.IsSelfInjected(packet.ExtraInformation))
        {
            return;
        }

        NormalizedKeyboardInput? normalized = null;
        bool confirmed;
        lock (_gate)
        {
            if (!_descriptorByMember.TryGetValue(packet.DeviceHandle, out var descriptor) ||
                _captureTarget != descriptor.SessionHandle)
            {
                return;
            }

            var key = PhysicalControlKey.FromPacket(packet);
            if (!_heldControls.TryGetValue(key, out var held))
            {
                if (packet.IsBreak)
                {
                    // Ignore a release for which this selected physical controller
                    // has no observed make. It cannot safely affect mapped state.
                    return;
                }

                normalized = KeyboardPacketNormalizer.Normalize(
                    packet,
                    descriptor,
                    isRepeat: false,
                    _timestampProvider());
                held = new HeldPhysicalControl(packet.DeviceHandle, normalized);
                _heldControls.Add(key, held);
            }
            else if (packet.IsBreak)
            {
                if (!held.MemberHandles.Remove(packet.DeviceHandle))
                {
                    return;
                }

                if (held.MemberHandles.Count == 0)
                {
                    _heldControls.Remove(key);
                    normalized = KeyboardPacketNormalizer.Normalize(
                        packet,
                        descriptor,
                        isRepeat: false,
                        _timestampProvider());
                }
            }
            else if (held.MemberHandles.Add(packet.DeviceHandle))
            {
                // A second interface in the same authoritative Windows container
                // emitted the same make while the logical control is already down.
                // Track it for balanced release, but do not duplicate the press.
                return;
            }
            else if (held.PrimaryMemberHandle == packet.DeviceHandle)
            {
                normalized = KeyboardPacketNormalizer.Normalize(
                    packet,
                    descriptor,
                    isRepeat: true,
                    _timestampProvider());
            }
            else
            {
                // Mirrored repeat from a secondary collection. The primary member
                // is the deterministic repeat source for this held control.
                return;
            }

            confirmed = CaptureIsConfirmedLocked();
        }

        if (normalized is null)
        {
            return;
        }

        if (confirmed)
        {
            InputReceived?.Invoke(this, new KeyboardInputReceivedEventArgs(normalized));
            SignalReceived?.Invoke(normalized.Signal);
        }
        else
        {
            IdentificationInputReceived?.Invoke(this, new KeyboardInputReceivedEventArgs(normalized));
        }
    }

    private void OnNativeDeviceChanged(object? sender, NativeDeviceChangeEventArgs eventArgs)
    {
        SanitizedDeviceDescriptor? descriptor;
        bool wasCaptureTarget;
        RawInputDeviceChangeKind logicalChangeKind;
        List<(NormalizedKeyboardInput Input, bool Confirmed)> syntheticReleases = [];

        if (eventArgs.Kind == RawInputDeviceChangeKind.Arrival)
        {
            var enumerated = _deviceEnumerator.EnumerateKeyboards().ToList();
            descriptor = enumerated.FirstOrDefault(candidate =>
                candidate.ContainsSessionHandle(eventArgs.DeviceHandle));
            if (descriptor is null)
            {
                descriptor = _deviceEnumerator.DescribeKeyboard(eventArgs.DeviceHandle);
                if (descriptor is not null)
                {
                    enumerated.Add(descriptor);
                }
            }

            lock (_gate)
            {
                var previous = descriptor is null
                    ? null
                    : _logicalDescriptors.Values.FirstOrDefault(candidate =>
                        string.Equals(candidate.PersistentId, descriptor.PersistentId, StringComparison.Ordinal));
                ReplaceDescriptorsLocked(enumerated);
                descriptor = _descriptorByMember.GetValueOrDefault(eventArgs.DeviceHandle) ?? descriptor;
                wasCaptureTarget = descriptor is not null && _captureTarget == descriptor.SessionHandle;
                logicalChangeKind = previous is null
                    ? RawInputDeviceChangeKind.Arrival
                    : RawInputDeviceChangeKind.MembershipChanged;
            }
        }
        else
        {
            var enumerated = ExcludingMember(
                _deviceEnumerator.EnumerateKeyboards(),
                eventArgs.DeviceHandle);
            lock (_gate)
            {
                _descriptorByMember.TryGetValue(eventArgs.DeviceHandle, out var previous);
                var wasConfirmed = CaptureIsConfirmedLocked();
                wasCaptureTarget = previous is not null && _captureTarget == previous.SessionHandle;
                RemoveMemberFromHeldControlsLocked(
                    eventArgs.DeviceHandle,
                    wasConfirmed,
                    syntheticReleases);
                ReplaceDescriptorsLocked(enumerated);

                var remaining = previous is null
                    ? null
                    : _logicalDescriptors.Values.FirstOrDefault(candidate =>
                        string.Equals(candidate.PersistentId, previous.PersistentId, StringComparison.Ordinal));
                if (remaining is null)
                {
                    descriptor = previous;
                    logicalChangeKind = RawInputDeviceChangeKind.Removal;
                }
                else
                {
                    descriptor = remaining;
                    logicalChangeKind = RawInputDeviceChangeKind.MembershipChanged;
                }
            }
        }

        foreach (var release in syntheticReleases)
        {
            PublishInput(release.Input, release.Confirmed);
        }

        DeviceChanged?.Invoke(
            this,
            new KeyboardDeviceChangedEventArgs(logicalChangeKind, descriptor, wasCaptureTarget));
        DevicesChanged?.Invoke();
    }

    private void OnLifecycleChanged(object? sender, WindowsLifecycleSignalEventArgs eventArgs)
    {
        if (eventArgs.Signal is WindowsLifecycleSignal.SessionLocked or
            WindowsLifecycleSignal.Suspending or
            WindowsLifecycleSignal.ShutdownRequested or
            WindowsLifecycleSignal.Shutdown)
        {
            lock (_gate)
            {
                _heldControls.Clear();
            }
        }

        LifecycleChanged?.Invoke(this, eventArgs);
    }

    private void OnHostFaulted(object? sender, Exception exception) =>
        Faulted?.Invoke(this, exception);

    private bool CaptureIsConfirmedLocked() =>
        _captureTarget is { } target &&
        _confirmedPersistentId is not null &&
        _logicalDescriptors.TryGetValue(target, out var descriptor) &&
        string.Equals(descriptor.PersistentId, _confirmedPersistentId, StringComparison.Ordinal);

    private void ReplaceDescriptorsLocked(IEnumerable<SanitizedDeviceDescriptor> descriptors)
    {
        _logicalDescriptors.Clear();
        _descriptorByMember.Clear();
        foreach (var descriptor in descriptors)
        {
            AddDescriptorLocked(descriptor);
        }

        if (_captureTarget is { } target && !_logicalDescriptors.ContainsKey(target))
        {
            _captureTarget = null;
            _confirmedPersistentId = null;
            _heldControls.Clear();
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

    private void RemoveMemberFromHeldControlsLocked(
        nint removedHandle,
        bool confirmed,
        ICollection<(NormalizedKeyboardInput Input, bool Confirmed)> releases)
    {
        foreach (var pair in _heldControls.ToArray())
        {
            if (!pair.Value.MemberHandles.Remove(removedHandle))
            {
                continue;
            }

            if (pair.Value.MemberHandles.Count != 0)
            {
                if (pair.Value.PrimaryMemberHandle == removedHandle)
                {
                    pair.Value.PrimaryMemberHandle = pair.Value.MemberHandles
                        .OrderBy(handle => unchecked((nuint)handle))
                        .First();
                }

                continue;
            }

            _heldControls.Remove(pair.Key);
            var last = pair.Value.FirstInput;
            var releaseSignal = last.Signal with
            {
                Kind = ControlSignalKind.Release,
                Timestamp = _timestampProvider(),
            };
            releases.Add((last with
            {
                SessionDeviceHandle = removedHandle,
                Transition = KeyTransition.Release,
                IsRepeat = false,
                NativeMessage = 0,
                Signal = releaseSignal,
            }, confirmed));
        }
    }

    private void PublishInput(NormalizedKeyboardInput input, bool confirmed)
    {
        if (confirmed)
        {
            InputReceived?.Invoke(this, new KeyboardInputReceivedEventArgs(input));
            SignalReceived?.Invoke(input.Signal);
        }
        else
        {
            IdentificationInputReceived?.Invoke(this, new KeyboardInputReceivedEventArgs(input));
        }
    }

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

    private readonly record struct PhysicalControlKey(
        ushort MakeCode,
        bool E0,
        bool E1,
        ushort UsageWhenScanCodeIsZero)
    {
        internal static PhysicalControlKey FromPacket(RawKeyboardPacket packet) =>
            new(
                packet.MakeCode,
                packet.IsE0,
                packet.IsE1,
                packet.MakeCode == 0 ? packet.VirtualKey : (ushort)0);
    }

    private sealed class HeldPhysicalControl(
        nint primaryMemberHandle,
        NormalizedKeyboardInput firstInput)
    {
        internal nint PrimaryMemberHandle { get; set; } = primaryMemberHandle;

        internal HashSet<nint> MemberHandles { get; } = [primaryMemberHandle];

        internal NormalizedKeyboardInput FirstInput { get; } = firstInput;
    }
}
