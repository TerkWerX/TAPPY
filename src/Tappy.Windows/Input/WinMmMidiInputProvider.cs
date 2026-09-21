using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tappy.Core.Abstractions;
using Tappy.Core.Input;

namespace Tappy.Windows.Input;

/// <summary>
/// Explicit-selection WinMM MIDI provider. It publishes nothing to mappings until
/// a device has completed Tappy's identify-release-confirm gate.
/// </summary>
public sealed class WinMmMidiInputProvider : IInputDeviceProvider
{
    public const string ProviderId = "winmm-midi";
    private readonly object _gate = new();
    private readonly object _inventoryGate = new();
    private readonly IWinMmMidiInputBackend _backend;
    private readonly IDeviceContainerIdResolver _containerIdResolver;
    private readonly Func<int, string?> _deviceInterfacePathResolver;
    private readonly Func<long> _timestampProvider;
    private readonly TimeSpan _monitorInterval;
    private readonly Dictionary<int, MidiInputDeviceDescriptor> _descriptors = [];
    private readonly Dictionary<int, (WinMmMidiInputDevice Device, Guid? ContainerId)> _containerIdCache = [];
    private readonly HashSet<ControlId> _heldNotes = [];
    private readonly Dictionary<(int Channel, int Control), int> _controlValues = [];
    private readonly HashSet<int> _relativeControlNumbers = [];
    private string _deviceSnapshot = string.Empty;
    private int? _captureTarget;
    private string? _confirmedPersistentId;
    private long _nativeGeneration;
    private long _captureGeneration;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _monitorFaulted;
    private bool _started;
    private volatile bool _disposed;

    public WinMmMidiInputProvider()
        : this(new WinMmMidiInputBackend())
    {
    }

    internal WinMmMidiInputProvider(
        IWinMmMidiInputBackend backend,
        Func<long>? timestampProvider = null,
        TimeSpan? monitorInterval = null,
        IDeviceContainerIdResolver? containerIdResolver = null,
        Func<int, string?>? deviceInterfacePathResolver = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _containerIdResolver = containerIdResolver ?? new WindowsDeviceContainerIdResolver();
        _deviceInterfacePathResolver = deviceInterfacePathResolver ?? WinMmDeviceInterfaceQuery.TryGetMidiInDeviceInterfacePath;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _monitorInterval = monitorInterval ?? TimeSpan.FromSeconds(1);
        if (_monitorInterval <= TimeSpan.Zero && _monitorInterval != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorInterval));
        }
        _backend.ShortMessageReceived += OnShortMessageReceived;
        _backend.Faulted += OnBackendFaulted;
    }

    public event EventHandler<MidiInputReceivedEventArgs>? InputReceived;

    public event EventHandler<MidiInputReceivedEventArgs>? IdentificationInputReceived;

    public event EventHandler<Exception>? Faulted;

    public event EventHandler<MidiCaptureLostEventArgs>? CaptureLost;

    public event Action<ControlSignal>? SignalReceived;

    public event Action? DevicesChanged;

    public bool IsCaptureConfirmed
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _captureTarget is not null && _confirmedPersistentId is not null;
            }
        }
    }

    public bool IsCaptureTargetNeutral
    {
        get
        {
            lock (_gate)
            {
                return _heldNotes.Count == 0;
            }
        }
    }

    public IReadOnlyList<ControllerIdentity> ConnectedControllers =>
        EnumerateControllers().Select(ToCoreIdentity).ToArray();

    public bool IsCurrentCapture(long generation)
    {
        lock (_gate)
        {
            return !_disposed && _captureTarget is not null && generation == _captureGeneration;
        }
    }

    public long? CurrentCaptureGeneration
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _captureTarget is not null ? _captureGeneration : null;
            }
        }
    }

    public IReadOnlyList<MidiInputDeviceDescriptor> EnumerateControllers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IReadOnlyList<MidiInputDeviceDescriptor> descriptors;
        MidiCaptureLostEventArgs? captureLost = null;
        bool devicesChanged;
        bool notify;
        lock (_inventoryGate)
        {
            descriptors = CreateDescriptors(_backend.EnumerateDevices());
            var snapshot = string.Join('|', descriptors.Select(item =>
                $"{item.DeviceId}:{item.PersistentId}"));
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                devicesChanged = !string.Equals(snapshot, _deviceSnapshot, StringComparison.Ordinal);
                // WinMM IDs and duplicate ordinals can be reused after ANY MIDI
                // topology change. A matching integer cannot keep capture armed.
                if (devicesChanged && _captureTarget is { } target &&
                    _descriptors.TryGetValue(target, out var previous))
                {
                    captureLost = new MidiCaptureLostEventArgs(previous,
                        "The MIDI device inventory changed. Identify the controller again before using mappings.");
                    ClearCaptureLocked(closeBackend: true);
                }

                _descriptors.Clear();
                foreach (var descriptor in descriptors)
                {
                    _descriptors[descriptor.DeviceId] = descriptor;
                }
                _deviceSnapshot = snapshot;
                notify = _started;
            }
        }

        if (captureLost is not null)
        {
            CaptureLost?.Invoke(this, captureLost);
        }
        if (devicesChanged && notify)
        {
            DevicesChanged?.Invoke();
        }
        return descriptors;
    }

    private IReadOnlyList<MidiInputDeviceDescriptor> CreateDescriptors(IReadOnlyList<WinMmMidiInputDevice> native)
    {
        var duplicateCounts = native
            .GroupBy(DeviceSignature)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var descriptors = new List<MidiInputDeviceDescriptor>(native.Count);
        foreach (var device in native.OrderBy(item => item.DeviceId))
        {
            var signature = DeviceSignature(device);
            ordinals.TryGetValue(signature, out var ordinal);
            ordinals[signature] = ordinal + 1;
            var fallbackFingerprint = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes("Tappy.WinMM.MIDI.v1\0" + signature + "\0" + ordinal)));
            var displayName = duplicateCounts[signature] > 1
                ? $"MIDI: {device.Name} (device {ordinal + 1})"
                : $"MIDI: {device.Name}";

            // A resolved Windows ContainerId ties this device's identity to its
            // actual USB hardware node (the same property Raw Input keyboards
            // already use), independent of WinMM's own enumeration order -
            // unlike the ordinal-based fallback below, which two identical
            // units could swap between across a restart. When resolution fails
            // for any reason (older Windows, no SetupAPI-visible interface,
            // etc.) this falls back to today's signature+ordinal identity
            // unchanged, and stays Ambiguous exactly as before. See D-026 in
            // DECISIONS.md.
            var containerId = ResolveContainerIdCached(device);
            var persistentId = containerId is { } resolvedContainerId
                ? $"winmm-midi:vid-{device.ManufacturerId:X4}:pid-{device.ProductId:X4}:container-sha256-{ContainerFingerprint(resolvedContainerId)}"
                : $"winmm-midi:sha256-{fallbackFingerprint}";

            descriptors.Add(new MidiInputDeviceDescriptor(
                device.DeviceId,
                $"midi-session-{device.DeviceId.ToString(CultureInfo.InvariantCulture)}",
                persistentId,
                displayName,
                device.ManufacturerId,
                device.ProductId,
                IsAmbiguous: containerId is null));
        }

        return descriptors;
    }

    // EnumerateControllers (and therefore CreateDescriptors) runs on every poll
    // tick across every device tab - resolving a device's ContainerId is a real
    // Windows device enumeration (SetupDiGetClassDevs/SetupDiEnumDeviceInterfaces
    // over every device of that interface class), not a cheap lookup, and doing
    // it unconditionally on every tick made every tab with a MIDI device
    // effectively unusable. Resolve once per distinct device (keyed by its full
    // WinMM signature, not just the reusable numeric DeviceId) and reuse the
    // result until that signature changes.
    private Guid? ResolveContainerIdCached(WinMmMidiInputDevice device)
    {
        if (_containerIdCache.TryGetValue(device.DeviceId, out var cached) &&
            cached.Device.ManufacturerId == device.ManufacturerId &&
            cached.Device.ProductId == device.ProductId &&
            cached.Device.DriverVersion == device.DriverVersion &&
            string.Equals(cached.Device.Name, device.Name, StringComparison.Ordinal))
        {
            return cached.ContainerId;
        }

        var resolved = TryResolveContainerId(device.DeviceId);
        _containerIdCache[device.DeviceId] = (device, resolved);
        return resolved;
    }

    private Guid? TryResolveContainerId(int deviceId)
    {
        try
        {
            // Bounded, in case a real driver ever fails to honor "the system
            // intercepts this message" for these device messages the way
            // Microsoft's own documentation describes - a slow or blocked
            // native call must not be able to freeze a device tab indefinitely.
            // The lookup itself cannot be safely cancelled mid-call, so on
            // timeout this simply stops waiting and falls back to Ambiguous;
            // resolution is not retried again until this device's own signature
            // changes (ResolveContainerIdCached).
            var lookup = Task.Run(() =>
            {
                var path = _deviceInterfacePathResolver(deviceId);
                return path is null ? (Guid?)null : _containerIdResolver.Resolve(path);
            });
            return lookup.Wait(TimeSpan.FromSeconds(2)) ? lookup.Result : null;
        }
        catch (Exception)
        {
            // A native-lookup failure must fail closed (stay Ambiguous), never
            // escalate past device enumeration.
            return null;
        }
    }

    private static string ContainerFingerprint(Guid containerId) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("Tappy.WinMM.Midi.Container.v1\0" + containerId.ToString("D").ToUpperInvariant())));

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _ = EnumerateControllers();
        lock (_gate)
        {
            if (_started)
            {
                return ValueTask.CompletedTask;
            }
            _started = true;
            _monitorFaulted = false;
            if (_monitorInterval != Timeout.InfiniteTimeSpan)
            {
                var cancellation = new CancellationTokenSource();
                _monitorCancellation = cancellation;
                _monitorTask = Task.Run(() => MonitorDevicesAsync(cancellation.Token));
            }
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? monitor;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _started = false;
            cancellation = _monitorCancellation;
            monitor = _monitorTask;
            _monitorCancellation = null;
            _monitorTask = null;
            ClearCaptureLocked(closeBackend: true);
        }
        cancellation?.Cancel();
        if (monitor is not null)
        {
            await monitor.ConfigureAwait(false);
        }
        cancellation?.Dispose();
    }

    public bool SetCaptureTarget(int? deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (deviceId is null)
        {
            ClearCaptureTarget();
            return true;
        }

        Exception? failure = null;
        lock (_gate)
        {
            if (!_started || _monitorFaulted || !_descriptors.ContainsKey(deviceId.Value))
            {
                return false;
            }

            ClearCaptureLocked(closeBackend: true);
            try
            {
                _backend.Open(deviceId.Value);
                _nativeGeneration = _backend.SessionGeneration;
                _captureTarget = deviceId.Value;
                _backend.Start();
                return true;
            }
            catch (Exception exception)
            {
                ClearCaptureLocked(closeBackend: true);
                failure = exception;
            }
        }

        Faulted?.Invoke(this, failure!);
        return false;
    }

    public bool SetConfirmedPersistentId(string? persistentId)
    {
        lock (_gate)
        {
            if (persistentId is null)
            {
                _confirmedPersistentId = null;
                return true;
            }

            if (_captureTarget is not { } target ||
                !_descriptors.TryGetValue(target, out var descriptor) ||
                !string.Equals(descriptor.PersistentId, persistentId, StringComparison.Ordinal) ||
                _heldNotes.Count != 0)
            {
                return false;
            }

            _confirmedPersistentId = persistentId;
            return true;
        }
    }

    public void ClearCaptureTarget()
    {
        lock (_gate)
        {
            ClearCaptureLocked(closeBackend: true);
        }
    }

    /// <summary>
    /// Declares which Control Change numbers (any channel) on the current capture
    /// carry a relative/signed value per message (e.g. a touch-capacitive endless
    /// encoder) rather than an absolute position to diff against the previous
    /// reading. Replaces any previous set; pass an empty set to return to the
    /// default absolute-diff decoding for every CC number. Model-specific — the
    /// App-layer catalog decides which numbers apply for a confirmed device.
    /// </summary>
    public void SetRelativeControlChanges(IEnumerable<int> controllerNumbers)
    {
        ArgumentNullException.ThrowIfNull(controllerNumbers);
        lock (_gate)
        {
            _relativeControlNumbers.Clear();
            _relativeControlNumbers.UnionWith(controllerNumbers);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backend.ShortMessageReceived -= OnShortMessageReceived;
        _backend.Faulted -= OnBackendFaulted;
        await StopAsync().ConfigureAwait(false);
        _backend.Dispose();
    }

    private void OnShortMessageReceived(WinMmMidiInputMessage packet)
    {
        var message = IncomingMidiShortMessage.Decode(packet.PackedMessage);
        List<(MidiInput Input, bool Confirmed)> pending = [];
        lock (_gate)
        {
            if (_disposed || packet.SessionGeneration != _nativeGeneration ||
                _captureTarget is not { } target ||
                !_descriptors.TryGetValue(target, out var descriptor))
            {
                return;
            }

            var confirmed = _confirmedPersistentId is not null;
            switch (message.Kind)
            {
                case IncomingMidiMessageKind.NoteOn:
                    {
                        var controlId = NoteControlId(message.Channel, message.Data1);
                        var kind = _heldNotes.Add(controlId)
                            ? ControlSignalKind.Press
                            : ControlSignalKind.Repeat;
                        pending.Add((CreateInput(descriptor, controlId, MidiInputControlKind.Note,
                            message.Channel, message.Data1, message.Data2,
                            $"Ch {message.Channel} Note {message.Data1}", kind), confirmed));
                        break;
                    }
                case IncomingMidiMessageKind.NoteOff:
                    {
                        var controlId = NoteControlId(message.Channel, message.Data1);
                        if (_heldNotes.Remove(controlId))
                        {
                            pending.Add((CreateInput(descriptor, controlId, MidiInputControlKind.Note,
                                message.Channel, message.Data1, message.Data2,
                                $"Ch {message.Channel} Note {message.Data1}", ControlSignalKind.Release), confirmed));
                        }
                        break;
                    }
                case IncomingMidiMessageKind.ControlChange:
                    {
                        if (_relativeControlNumbers.Contains(message.Data1))
                        {
                            var relativeDelta = DecodeRelativeControlChange(message.Data2);
                            if (relativeDelta == 0)
                            {
                                break;
                            }

                            var relativeIncrease = relativeDelta > 0;
                            var magnitude = Math.Abs(relativeDelta);
                            var relativeKind = relativeIncrease
                                ? MidiInputControlKind.RelativeControlIncrease
                                : MidiInputControlKind.RelativeControlDecrease;
                            var relativeDirection = relativeIncrease ? "increase" : "decrease";
                            var relativeControlId = ControlChangeId(message.Channel, message.Data1, relativeIncrease);
                            var relativeLabel = $"Ch {message.Channel} CC {message.Data1} {relativeDirection}";
                            pending.Add((CreateInput(descriptor, relativeControlId, relativeKind,
                                message.Channel, message.Data1, magnitude, relativeLabel, ControlSignalKind.Press), confirmed));
                            pending.Add((CreateInput(descriptor, relativeControlId, relativeKind,
                                message.Channel, message.Data1, magnitude, relativeLabel, ControlSignalKind.Release), confirmed));
                            break;
                        }

                        var key = (message.Channel, message.Data1);
                        _controlValues.TryGetValue(key, out var previous);
                        if (!_controlValues.ContainsKey(key) && message.Data2 == 0)
                        {
                            _controlValues[key] = 0;
                            break;
                        }

                        if (message.Data2 == previous)
                        {
                            break;
                        }

                        var increase = message.Data2 > previous;
                        _controlValues[key] = message.Data2;
                        var controlKind = increase
                            ? MidiInputControlKind.ControlIncrease
                            : MidiInputControlKind.ControlDecrease;
                        var direction = increase ? "increase" : "decrease";
                        var controlId = ControlChangeId(message.Channel, message.Data1, increase);
                        var label = $"Ch {message.Channel} CC {message.Data1} {direction}";
                        pending.Add((CreateInput(descriptor, controlId, controlKind,
                            message.Channel, message.Data1, message.Data2, label, ControlSignalKind.Press), confirmed));
                        pending.Add((CreateInput(descriptor, controlId, controlKind,
                            message.Channel, message.Data1, message.Data2, label, ControlSignalKind.Release), confirmed));
                        break;
                    }
                case IncomingMidiMessageKind.ProgramChange:
                    {
                        var controlId = ProgramChangeId(message.Channel, message.Data1);
                        var label = $"Ch {message.Channel} Program {message.Data1}";
                        pending.Add((CreateInput(descriptor, controlId, MidiInputControlKind.ProgramChange,
                            message.Channel, message.Data1, 0, label, ControlSignalKind.Press), confirmed));
                        pending.Add((CreateInput(descriptor, controlId, MidiInputControlKind.ProgramChange,
                            message.Channel, message.Data1, 0, label, ControlSignalKind.Release), confirmed));
                        break;
                    }
            }
        }

        foreach (var item in pending)
        {
            if (!IsCurrentCapture(item.Input.CaptureGeneration))
            {
                break;
            }
            if (item.Confirmed)
            {
                InputReceived?.Invoke(this, new MidiInputReceivedEventArgs(item.Input));
                if (IsCurrentCapture(item.Input.CaptureGeneration))
                {
                    SignalReceived?.Invoke(item.Input.Signal);
                }
            }
            else
            {
                IdentificationInputReceived?.Invoke(this, new MidiInputReceivedEventArgs(item.Input));
            }
        }
    }

    private MidiInput CreateInput(
        MidiInputDeviceDescriptor descriptor,
        ControlId controlId,
        MidiInputControlKind controlKind,
        int channel,
        int number,
        int value,
        string displayName,
        ControlSignalKind signalKind)
    {
        var sessionId = new ControllerSessionId(descriptor.SessionId);
        var signal = ControlSignal.Physical(sessionId, controlId, signalKind, _timestampProvider());
        return new MidiInput(
            descriptor.DeviceId,
            sessionId,
            descriptor.PersistentId,
            controlId,
            controlKind,
            channel,
            number,
            value,
            displayName,
            signal)
        { CaptureGeneration = _captureGeneration };
    }

    private void ClearCaptureLocked(bool closeBackend)
    {
        _heldNotes.Clear();
        _controlValues.Clear();
        _relativeControlNumbers.Clear();
        _captureTarget = null;
        _confirmedPersistentId = null;
        _captureGeneration++;
        if (closeBackend)
        {
            _backend.Stop();
            _backend.Close();
        }
    }

    private async Task MonitorDevicesAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_monitorInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = EnumerateControllers();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _monitorFaulted = true;
            }
            OnBackendFaulted(this, exception);
        }
    }

    private void OnBackendFaulted(object? sender, Exception exception)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            if (exception is WinMmMidiInputSessionException sessionFailure &&
                (_captureTarget is null || sessionFailure.SessionGeneration != _nativeGeneration))
            {
                return;
            }
            ClearCaptureLocked(closeBackend: true);
        }
        // Faulted is the runtime's cleanup authority for this path. Sending
        // CaptureLost as well would run cleanup twice for one backend failure.
        Faulted?.Invoke(this, exception);
    }

    private static string DeviceSignature(WinMmMidiInputDevice device) => string.Create(
        CultureInfo.InvariantCulture,
        $"{device.ManufacturerId:X4}:{device.ProductId:X4}:{device.DriverVersion:X8}:{device.Name.Trim().ToUpperInvariant()}");

    private static ControllerIdentity ToCoreIdentity(MidiInputDeviceDescriptor descriptor) => new(
        new ControllerSessionId(descriptor.SessionId),
        new ControllerPersistentId(descriptor.PersistentId),
        descriptor.IsAmbiguous ? ControllerIdentityConfidence.Ambiguous : ControllerIdentityConfidence.PortBound,
        descriptor.DisplayName,
        ProviderId,
        descriptor.ManufacturerId,
        descriptor.ProductId,
        usagePage: 0,
        usage: 0);

    private static ControlId NoteControlId(int channel, int note) =>
        ControlId.Create(ProviderId, $"channel-{channel}:note-{note}");

    private static ControlId ControlChangeId(int channel, int control, bool increase) =>
        ControlId.Create(ProviderId, $"channel-{channel}:cc-{control}:{(increase ? "increase" : "decrease")}");

    private static ControlId ProgramChangeId(int channel, int program) =>
        ControlId.Create(ProviderId, $"channel-{channel}:program-{program}");

    // 1-63 = clockwise steps of that size; 65-127 = counterclockwise (value-128);
    // 0 and 64 are the encoder's neutral/no-movement values. Documented for the
    // Akai Fire in docs/AKAI_FIRE.md; not specific to that model.
    private static int DecodeRelativeControlChange(int value) => value switch
    {
        >= 1 and <= 63 => value,
        >= 65 and <= 127 => value - 128,
        _ => 0,
    };
}
