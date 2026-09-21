using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Tappy.App.Services;

public enum WindowPresentationMode
{
    Full,
    Compact,
    ControllerOnly
}

internal readonly record struct WindowPresentationMetrics(
    double MinimumWidth,
    double MinimumHeight,
    double DefaultWidth,
    double DefaultHeight);

internal static class WindowPresentationPolicy
{
    public static WindowPresentationMetrics Get(WindowPresentationMode mode) => mode switch
    {
        WindowPresentationMode.Compact => new(640, 460, 780, 590),
        WindowPresentationMode.ControllerOnly => new(500, 360, 560, 430),
        _ => new(760, 540, 1120, 760)
    };
}

public sealed class WindowPlacementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private WindowPlacementDocument _document = new();

    public WindowPlacementStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "window-state.json");
        Load();
    }

    public WindowPresentationMode CurrentMode { get; private set; } = WindowPresentationMode.Full;

    public void Restore(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        CurrentMode = Enum.TryParse<WindowPresentationMode>(_document.LastMode, out var mode)
            ? mode
            : WindowPresentationMode.Full;
        var metrics = WindowPresentationPolicy.Get(CurrentMode);
        window.MinWidth = metrics.MinimumWidth;
        window.MinHeight = metrics.MinimumHeight;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(item =>
                         string.Equals(item.DeviceName, _document.Monitor, StringComparison.OrdinalIgnoreCase))
                     ?? Forms.Screen.PrimaryScreen
                     ?? Forms.Screen.AllScreens.First();
        var dpi = VisualTreeHelper.GetDpi(window);
        var work = ToDip(screen.WorkingArea, dpi);
        var size = GetSize(CurrentMode);
        window.Width = Math.Clamp(size.Width, Math.Min(window.MinWidth, work.Width), work.Width);
        window.Height = Math.Clamp(size.Height, Math.Min(window.MinHeight, work.Height), work.Height);
        window.Left = Math.Clamp(_document.Left, work.Left, Math.Max(work.Left, work.Right - window.Width));
        window.Top = Math.Clamp(_document.Top, work.Top, Math.Max(work.Top, work.Bottom - window.Height));
        if (_document.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public void SwitchMode(Window window, WindowPresentationMode mode, System.Windows.Size defaultSize)
    {
        ArgumentNullException.ThrowIfNull(window);
        Capture(window, CurrentMode);
        CurrentMode = mode;
        var metrics = WindowPresentationPolicy.Get(mode);
        window.MinWidth = metrics.MinimumWidth;
        window.MinHeight = metrics.MinimumHeight;
        var screen = Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle);
        var dpi = VisualTreeHelper.GetDpi(window);
        var work = ToDip(screen.WorkingArea, dpi);
        var size = _document.Sizes.TryGetValue(mode.ToString(), out var saved)
            ? saved
            : new WindowSize(defaultSize.Width, defaultSize.Height);

        if (window.WindowState == WindowState.Maximized)
        {
            window.WindowState = WindowState.Normal;
        }

        var left = double.IsFinite(window.Left) ? window.Left : work.Left;
        var top = double.IsFinite(window.Top) ? window.Top : work.Top;
        window.Width = Math.Clamp(size.Width, Math.Min(window.MinWidth, work.Width), work.Width);
        window.Height = Math.Clamp(size.Height, Math.Min(window.MinHeight, work.Height), work.Height);
        window.Left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - window.Width));
        window.Top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - window.Height));
        _document.LastMode = mode.ToString();
    }

    public void Save(Window window)
    {
        Capture(window, CurrentMode);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_document, JsonOptions));
        File.Move(temporary, _path, true);
    }

    private void Capture(Window window, WindowPresentationMode mode)
    {
        var bounds = window.RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _document.Left = bounds.Left;
            _document.Top = bounds.Top;
            _document.Sizes[mode.ToString()] = new WindowSize(bounds.Width, bounds.Height);
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            _document.Monitor = Forms.Screen.FromHandle(handle).DeviceName;
        }

        _document.Maximized = window.WindowState == WindowState.Maximized;
        _document.LastMode = mode.ToString();
    }

    private WindowSize GetSize(WindowPresentationMode mode) =>
        _document.Sizes.TryGetValue(mode.ToString(), out var size)
            ? size
            : DefaultSize(mode);

    private static WindowSize DefaultSize(WindowPresentationMode mode)
    {
        var metrics = WindowPresentationPolicy.Get(mode);
        return new WindowSize(metrics.DefaultWidth, metrics.DefaultHeight);
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            _document = JsonSerializer.Deserialize<WindowPlacementDocument>(File.ReadAllText(_path)) ?? new();
        }
        catch
        {
            _document = new WindowPlacementDocument();
        }
    }

    private static Rect ToDip(System.Drawing.Rectangle rectangle, DpiScale dpi) => new(
        rectangle.Left / dpi.DpiScaleX,
        rectangle.Top / dpi.DpiScaleY,
        rectangle.Width / dpi.DpiScaleX,
        rectangle.Height / dpi.DpiScaleY);

    private sealed class WindowPlacementDocument
    {
        public double Left { get; set; } = 80;
        public double Top { get; set; } = 80;
        public bool Maximized { get; set; }
        public string Monitor { get; set; } = string.Empty;
        public string LastMode { get; set; } = WindowPresentationMode.Full.ToString();
        public Dictionary<string, WindowSize> Sizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record WindowSize(double Width, double Height);
}
