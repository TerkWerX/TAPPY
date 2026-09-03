using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tappy.App.Services;
using Tappy.App.ViewModels;

namespace Tappy.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly WindowPlacementStore _placement;
    private readonly SessionMarker _sessionMarker;
    private readonly ThemeService _theme = new();
    private readonly EmergencyHotkeyService _hotkey = new();
    private TrayRecoveryService? _tray;
    private bool _allowClose;
    private int _exitStarted;

    public MainWindow(
        MainViewModel viewModel,
        WindowPlacementStore placement,
        SessionMarker sessionMarker)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _sessionMarker = sessionMarker ?? throw new ArgumentNullException(nameof(sessionMarker));
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += (_, _) =>
        {
            _placement.Restore(this);
            ApplyPresentationMode(_placement.CurrentMode, resize: false);
        };
        Loaded += MainWindow_OnLoaded;
    }

    public void FailSafeStop(string reason) => _viewModel.EmergencyStop(reason);

    public void PrepareForSystemShutdown()
    {
        _viewModel.EmergencyStop("Windows session ending");
        try
        {
            _viewModel.SaveProfileAsync().GetAwaiter().GetResult();
            _placement.Save(this);
            _sessionMarker.Complete();
        }
        catch
        {
            // Keep the recovery marker when shutdown persistence was incomplete.
        }

        _allowClose = true;
    }

    public async Task ExitApplicationAsync()
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        try
        {
            _viewModel.EmergencyStop("application exit");
            try
            {
                await _viewModel.SaveProfileAsync().ConfigureAwait(true);
            }
            catch
            {
                // Output safety and shutdown do not depend on a successful save.
            }

            await _viewModel.DisposeAsync().ConfigureAwait(true);
            _placement.Save(this);
            _sessionMarker.Complete();
        }
        finally
        {
            _hotkey.Dispose();
            _theme.Dispose();
            _tray?.Dispose();
            _tray = null;
            _allowClose = true;
            Close();
            System.Windows.Application.Current.Shutdown();
        }
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        _tray = new TrayRecoveryService(
            ShowFromTray,
            () => Dispatcher.Invoke(() => _viewModel.EmergencyStop("notification-area command")),
            () => Dispatcher.BeginInvoke(async () => await ExitApplicationAsync(), DispatcherPriority.Send));

        if (!_hotkey.Register(this, () => _viewModel.EmergencyStop("Ctrl+Alt+Shift+F12"), out var error))
        {
            _viewModel.ReportStatus(error ?? "The emergency hotkey could not be registered. Mouse and tray recovery remain available.");
        }

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _viewModel.EmergencyStop("input initialization failure");
            _viewModel.ReportStatus($"Needs attention: Raw Input could not start. {exception.Message}");
        }
    }

    private void ShowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        _tray?.ShowBalloon(
            "Tappy is still running",
            "Mappings remain active in Device-aware pass-through. Use the tray Emergency stop or Exit command at any time.");
    }

    private void MainWindow_OnStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) =>
        e.Handled = ShouldSuppressFocusedControlKeyInput(_viewModel);

    private void MainWindow_OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e) =>
        e.Handled = ShouldSuppressFocusedControlKeyInput(_viewModel);

    internal static bool ShouldSuppressFocusedControlKeyInput(MainViewModel viewModel) =>
        viewModel.IsIdentificationCaptureActive;

    private void RefreshDevices_OnClick(object sender, RoutedEventArgs e) => _viewModel.RefreshDevices();
    private void BeginIdentification_OnClick(object sender, RoutedEventArgs e) => _viewModel.BeginIdentification();
    private void ConfirmController_OnClick(object sender, RoutedEventArgs e) => _viewModel.ConfirmController();
    private void AssignMapping_OnClick(object sender, RoutedEventArgs e) => _viewModel.AssignMapping();

    private async void SaveProfile_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.SaveProfileAsync().ConfigureAwait(true);

    private void ControlTile_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ControlTileViewModel tile })
        {
            _viewModel.SelectControl(tile);
        }
    }

    private void EmergencyStop_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.EmergencyStop("mouse-accessible window command");

    private void ToTray_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _tray?.ShowBalloon("Tappy", "Tappy is running locally. The tray menu includes Emergency stop and Exit.");
    }

    private void Theme_OnClick(object sender, RoutedEventArgs e) => _theme.Toggle();

    private void About_OnClick(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private void FullView_OnClick(object sender, RoutedEventArgs e) =>
        ApplyPresentationMode(WindowPresentationMode.Full, resize: true);

    private void Compact_OnClick(object sender, RoutedEventArgs e) =>
        ApplyPresentationMode(WindowPresentationMode.Compact, resize: true);

    private void ControllerOnly_OnClick(object sender, RoutedEventArgs e) =>
        ApplyPresentationMode(WindowPresentationMode.ControllerOnly, resize: true);

    private void ApplyPresentationMode(WindowPresentationMode mode, bool resize)
    {
        var metrics = WindowPresentationPolicy.Get(mode);
        if (!resize)
        {
            MinWidth = metrics.MinimumWidth;
            MinHeight = metrics.MinimumHeight;
        }

        var full = mode == WindowPresentationMode.Full;
        SetupPanel.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        SetupColumn.Width = full ? new GridLength(330) : new GridLength(0);
        FullViewButton.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        if (resize)
        {
            var size = new System.Windows.Size(metrics.DefaultWidth, metrics.DefaultHeight);
            _placement.SwitchMode(this, mode, size);
        }
    }
}
