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
    private readonly IWinMmMidiInputBackend _backend;
    private readonly Func<long> _timestampProvider;
    private readonly Dictionary<int, MidiInputDeviceDescriptor> _descriptors = [];
    private readonly HashSet<ControlId> _heldNotes = [];
    private readonly Dictionary<(int Channel, int Control), int> _controlValues = [];
    private string _deviceSnapshot = string.Empty;
    private int? _captureTarget;
    private string? _confirmedPersistentId;
    private bool _started;
    private bool _disposed;

    public WinMmMidiInputProvider()
        : this(new WinMmMidiInputBackend())
    {
    }

    internal WinMmMidiInputProvider(
        IWinMmMidiInputBackend backend,
        Func<long>? timestampProvider = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _backend.ShortMessageReceived += OnShortMessageReceived;
    }

    public event EventHandler<MidiInputReceivedEventArgs>? InputReceived;

    public event EventHandler<MidiInputReceivedEventArgs>? IdentificationInputReceived;

    public event EventHandler<Exception>? Faulted;

    public event Action<ControlSignal>? SignalReceived;

    public event Action? DevicesChanged;

    public bool IsCaptureConfirmed
    {
        get
        {
            lock (_gate)
            {
                return _captureTarget is not null && _confirmedPersistentId is not null;
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

    public IReadOnlyList<MidiInputDeviceDescriptor> EnumerateControllers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var native = _backend.EnumerateDevices();
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
            var fingerprint = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes("Tappy.WinMM.MIDI.v1\0" + signature + "\0" + ordinal)));
            var displayName = duplicateCounts[signature] > 1
                ? $"MIDI: {device.Name} (device {ordinal + 1})"
                : $"MIDI: {device.Name}";
            descriptors.Add(new MidiInputDeviceDescriptor(
                device.DeviceId,
                $"midi-session-{device.DeviceId.ToString(CultureInfo.InvariantCulture)}",
                $"winmm-midi:sha256-{fingerprint}",
                displayName,
                device.ManufacturerId,
                device.ProductId,
                IsAmbiguous: true));
        }

        var devicesChanged = false;
        lock (_gate)
        {
            _descriptors.Clear();
            foreach (var descriptor in descriptors)
            {
                _descriptors[descriptor.DeviceId] = descriptor;
            }

            if (_captureTarget is { } target && !_descriptors.ContainsKey(target))
            {
                ClearCaptureLocked(closeBackend: true);
            }

            var snapshot = string.Join('|', descriptors.Select(item =>
                $"{item.DeviceId}:{item.PersistentId}"));
            devicesChanged = !string.Equals(snapshot, _deviceSnapshot, StringComparison.Ordinal);
            _deviceSnapshot = snapshot;
        }

        if (devicesChanged && _started)
        {
            DevicesChanged?.Invoke();
        }

        return descriptors;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _ = EnumerateControllers();
        _started = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearCaptureTarget();
        _started = false;
        return ValueTask.CompletedTask;
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
            if (!_started || !_descriptors.ContainsKey(deviceId.Value))
            {
                return false;
            }

            ClearCaptureLocked(closeBackend: true);
            try
            {
                _backend.Open(deviceId.Value);
                _backend.Start();
                _captureTarget = deviceId.Value;
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

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        ClearCaptureTarget();
        _disposed = true;
        _backend.ShortMessageReceived -= OnShortMessageReceived;
        _backend.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnShortMessageReceived(uint packedMessage)
    {
        var message = IncomingMidiShortMessage.Decode(packedMessage);
        List<(MidiInput Input, bool Confirmed)> pending = [];
        lock (_gate)
        {
            if (_captureTarget is not { } target ||
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
            if (item.Confirmed)
            {
                InputReceived?.Invoke(this, new MidiInputReceivedEventArgs(item.Input));
                SignalReceived?.Invoke(item.Input.Signal);
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
            signal);
    }

    private void ClearCaptureLocked(bool closeBackend)
    {
        _heldNotes.Clear();
        _controlValues.Clear();
        _captureTarget = null;
        _confirmedPersistentId = null;
        if (closeBackend)
        {
            _backend.Stop();
            _backend.Close();
        }
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
}
