using Tappy.Windows.Input;

namespace Tappy.Windows.Lifecycle;

/// <summary>
/// Explicit seam for WPF Application.Exit/SessionEnding and process shutdown paths.
/// Message-only windows do not receive broadcast shutdown messages, so the
/// composition root must forward those application lifecycle notifications here.
/// </summary>
public sealed class ApplicationLifecycleSignalSource : IWindowsLifecycleSignalSource
{
    public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;

    public void Report(WindowsLifecycleSignal signal) =>
        LifecycleChanged?.Invoke(this, new WindowsLifecycleSignalEventArgs(signal));
}
