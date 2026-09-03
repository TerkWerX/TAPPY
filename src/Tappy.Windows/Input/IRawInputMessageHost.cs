namespace Tappy.Windows.Input;

public enum RawInputDeviceChangeKind
{
    Arrival,
    Removal,
    /// <summary>
    /// A Raw Input interface was added to or removed from a physical controller
    /// that remains present through another member of the same Windows container.
    /// </summary>
    MembershipChanged,
}

public enum WindowsLifecycleSignal
{
    SessionLocked,
    SessionUnlocked,
    Suspending,
    Resuming,
    ShutdownRequested,
    Shutdown,
}

public sealed class RawKeyboardPacketEventArgs(RawKeyboardPacket packet) : EventArgs
{
    public RawKeyboardPacket Packet { get; } = packet;
}

public sealed class NativeDeviceChangeEventArgs(
    nint deviceHandle,
    RawInputDeviceChangeKind kind) : EventArgs
{
    public nint DeviceHandle { get; } = deviceHandle;

    public RawInputDeviceChangeKind Kind { get; } = kind;
}

public sealed class WindowsLifecycleSignalEventArgs(
    WindowsLifecycleSignal signal) : EventArgs
{
    public WindowsLifecycleSignal Signal { get; } = signal;
}

public interface IRawInputMessageHost : IAsyncDisposable
{
    event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;

    event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;

    event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;

    event EventHandler<Exception>? Faulted;

    bool IsRunning { get; }

    nint WindowHandle { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
