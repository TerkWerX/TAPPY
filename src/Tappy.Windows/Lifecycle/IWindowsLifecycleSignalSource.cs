using Tappy.Windows.Input;

namespace Tappy.Windows.Lifecycle;

public interface IWindowsLifecycleSignalSource
{
    event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;
}
