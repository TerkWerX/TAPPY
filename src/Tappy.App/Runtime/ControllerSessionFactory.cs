using Tappy.Core.Output;
using Tappy.Windows;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;
using Tappy.Windows.Output;
using Tappy.Windows.Profiles;

namespace Tappy.App.Runtime;

/// <summary>One native registration and output arbiter for all independent device tabs.</summary>
internal sealed class ControllerSessionFactory : IAsyncDisposable
{
    private readonly RawInputMessageHost _host = new();
    private readonly OwnedKeyboardOutput _keyboard = new(new SendInputKeyboardOutput());
    private readonly WindowsControllerActionOutput _actions;
    private readonly IWindowsLifecycleSignalSource _lifecycle;

    internal ControllerSessionFactory(IWindowsLifecycleSignalSource lifecycle)
    {
        _lifecycle = lifecycle;
        _actions = new WindowsControllerActionOutput(_keyboard);
    }

    internal IControllerRuntime Create(int slot)
    {
        // Slot one keeps the existing profile in place. Additional slots have their
        // own atomic profile store, so saving a hidden tab cannot overwrite another.
        var root = slot == 1 ? ProductIdentity.LocalDataRoot :
            Path.Combine(ProductIdentity.LocalDataRoot, "device-tabs", $"device-{slot}");
        var lease = new SharedInputHostLease(_host);
        return new DeviceAwareControllerRuntime(
            new RawInputKeyboardProvider(new NativeRawInputDeviceEnumerator(), lease),
            new LogitechG13InputProvider(new NativeLogitechG13DeviceEnumerator(), lease),
            _keyboard, new AtomicProfileStore(root), _lifecycle,
            actionOutput: new SessionActionOutput(_actions),
            midiInputProvider: new WinMmMidiInputProvider(),
            controllerLedOutput: new WinMmMidiOutput(),
            logitechG13LightingOutput: new NativeLogitechG13LightingOutput(),
            tartarusIndicatorOutput: new NativeRazerTartarusIndicatorOutput());
    }

    public async ValueTask DisposeAsync()
    {
        _actions.Dispose();
        await _host.DisposeAsync().ConfigureAwait(false);
    }
}

// Providers unsubscribe their own handlers on disposal. Only the factory owns the
// shared native window; stopping one provider must not stop other device tabs.
internal sealed class SharedInputHostLease(IRawHidInputMessageHost host) : IRawHidInputMessageHost
{
    public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived
    { add => host.KeyboardPacketReceived += value; remove => host.KeyboardPacketReceived -= value; }
    public event EventHandler<RawHidInputPacketEventArgs>? HidPacketReceived
    { add => host.HidPacketReceived += value; remove => host.HidPacketReceived -= value; }
    public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged
    { add => host.DeviceChanged += value; remove => host.DeviceChanged -= value; }
    public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged
    { add => host.LifecycleChanged += value; remove => host.LifecycleChanged -= value; }
    public event EventHandler<Exception>? Faulted
    { add => host.Faulted += value; remove => host.Faulted -= value; }
    public bool IsRunning => host.IsRunning;
    public nint WindowHandle => host.WindowHandle;
    public Task StartAsync(CancellationToken cancellationToken = default) => host.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Scope cleanup to this tab while sharing MIDI/key ownership across tabs.</summary>
internal sealed class SessionActionOutput(IControllerActionOutput output) : IControllerActionOutput
{
    private readonly string _prefix = $"tab:{Guid.NewGuid():N}:";
    private readonly object _gate = new();
    private readonly HashSet<string> _scopes = new(StringComparer.Ordinal);
    // A shared output fault is deliberately broadcast: all tabs must stop rather
    // than pretending another device can safely continue after a failed release.
    public event EventHandler<ControllerActionOutputFault>? Faulted
    { add => output.Faulted += value; remove => output.Faulted -= value; }
    public bool Start(ControllerActionOutputRequest request)
    {
        lock (_gate)
        {
            _scopes.Add(request.ScopeId);
            return output.Start(request with
            {
                OwnerId = _prefix + request.OwnerId,
                ScopeId = _prefix + request.ScopeId,
                PreviousOwnerId = request.PreviousOwnerId is null ? null : _prefix + request.PreviousOwnerId
            });
        }
    }
    public bool ReleaseOwner(string ownerId) => output.ReleaseOwner(_prefix + ownerId);
    public bool ReleaseScope(string scopeId) => output.ReleaseScope(_prefix + scopeId);
    public bool ReleaseAll()
    {
        lock (_gate)
        {
            var succeeded = true;
            foreach (var scope in _scopes.ToArray())
            {
                if (output.ReleaseScope(_prefix + scope)) { _scopes.Remove(scope); }
                else { succeeded = false; }
            }
            return succeeded;
        }
    }
}
