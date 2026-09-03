using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tappy.App.Services;

public sealed class EmergencyHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x5450;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkF12 = 0x7B;
    private HwndSource? _source;
    private IntPtr _window;
    private Action? _callback;

    public bool IsRegistered { get; private set; }

    public bool Register(Window owner, Action callback, out string? error)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(callback);
        Dispose();
        error = null;
        _window = new WindowInteropHelper(owner).Handle;
        if (_window == IntPtr.Zero)
        {
            error = "The Tappy window handle is not ready.";
            return false;
        }

        _source = HwndSource.FromHwnd(_window);
        _source?.AddHook(WindowHook);
        if (!RegisterHotKey(_window, HotkeyId, ModControl | ModAlt | ModShift | ModNoRepeat, VkF12))
        {
            error = "Ctrl+Alt+Shift+F12 is reserved by another application. Mouse and tray emergency stop remain available.";
            Dispose();
            return false;
        }

        _callback = callback;
        IsRegistered = true;
        return true;
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _callback?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (IsRegistered && _window != IntPtr.Zero)
        {
            _ = UnregisterHotKey(_window, HotkeyId);
        }

        _source?.RemoveHook(WindowHook);
        _source = null;
        _window = IntPtr.Zero;
        _callback = null;
        IsRegistered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
