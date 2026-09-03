using Tappy.Core.Execution;
using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.Windows.Lifecycle;

public interface IWindowsMappingSafetyTarget
{
    void DisconnectController(ControllerSessionId sessionId);

    void ResetForLifecycleTransition();
}

public sealed class MappingEngineSafetyTarget(MappingEngine engine) : IWindowsMappingSafetyTarget
{
    private readonly MappingEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public void DisconnectController(ControllerSessionId sessionId) =>
        _ = _engine.DisconnectController(sessionId);

    public void ResetForLifecycleTransition() =>
        _ = _engine.ResetForLifecycleTransition();
}

/// <summary>
/// Connects native disconnect/lock/suspend/shutdown signals to Core's output-release
/// operations. Keeping this bridge explicit makes lifecycle cleanup testable without
/// a live Windows session transition.
/// </summary>
public sealed class WindowsMappingSafetyBridge : IDisposable
{
    private readonly RawInputKeyboardProvider _provider;
    private readonly IWindowsMappingSafetyTarget _target;
    private readonly IWindowsLifecycleSignalSource? _additionalLifecycleSource;
    private bool _disposed;

    public WindowsMappingSafetyBridge(
        RawInputKeyboardProvider provider,
        IWindowsMappingSafetyTarget target,
        IWindowsLifecycleSignalSource? additionalLifecycleSource = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _additionalLifecycleSource = ReferenceEquals(additionalLifecycleSource, provider)
            ? null
            : additionalLifecycleSource;
        _provider.DeviceChanged += OnDeviceChanged;
        _provider.LifecycleChanged += OnLifecycleChanged;
        if (_additionalLifecycleSource is not null)
        {
            _additionalLifecycleSource.LifecycleChanged += OnLifecycleChanged;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _provider.DeviceChanged -= OnDeviceChanged;
        _provider.LifecycleChanged -= OnLifecycleChanged;
        if (_additionalLifecycleSource is not null)
        {
            _additionalLifecycleSource.LifecycleChanged -= OnLifecycleChanged;
        }
    }

    private void OnDeviceChanged(object? sender, KeyboardDeviceChangedEventArgs eventArgs)
    {
        if (eventArgs.Kind == RawInputDeviceChangeKind.Removal && eventArgs.Descriptor is { } descriptor)
        {
            _target.DisconnectController(new ControllerSessionId(descriptor.SessionId));
        }
    }

    private void OnLifecycleChanged(object? sender, WindowsLifecycleSignalEventArgs eventArgs)
    {
        if (eventArgs.Signal is WindowsLifecycleSignal.SessionLocked or
            WindowsLifecycleSignal.Suspending or
            WindowsLifecycleSignal.ShutdownRequested or
            WindowsLifecycleSignal.Shutdown)
        {
            _target.ResetForLifecycleTransition();
        }
    }
}
