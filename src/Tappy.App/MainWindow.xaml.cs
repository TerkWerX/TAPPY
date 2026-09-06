using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Tappy.App.Services;
using Tappy.App.ViewModels;

namespace Tappy.App;

public partial class MainWindow : Window
{
    internal const string BackgroundNotificationMessage =
        "Tappy is still running in the notification area. Use Show Tappy, Emergency stop, or Exit Tappy at any time.";

    private readonly MainViewModel _viewModel;
    private readonly WindowPlacementStore _placement;
    private readonly SessionMarker _sessionMarker;
    private readonly ThemeService _theme = new();
    private readonly EmergencyHotkeyService _hotkey = new();
    private TrayRecoveryService? _tray;
    private bool _allowClose;
    private int _exitStarted;
    private System.Windows.Point _tileDragStart;
    private System.Windows.Point _tileDragOrigin;
    private ControlTileViewModel? _tileDragSource;
    private IReadOnlyDictionary<string, (double X, double Y)> _tileDragOrigins =
        new Dictionary<string, (double X, double Y)>();
    private bool _tileDragMoved;
    private System.Windows.Point _marqueeStart;
    private bool _marqueeActive;
    private IReadOnlySet<string> _marqueePreservedIds = new HashSet<string>(StringComparer.Ordinal);

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
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        SourceInitialized += (_, _) =>
        {
            _placement.Restore(this);
            ApplyPresentationMode(_placement.CurrentMode, resize: false);
            UpdateControllerPhotoPane();
        };
        Loaded += MainWindow_OnLoaded;
    }

    public void FailSafeStop(string reason) => _viewModel.EmergencyStop(reason);

    public void PrepareForSystemShutdown()
    {
        var cleanup = _viewModel.EmergencyStop("Windows session ending");
        try
        {
            _viewModel.SaveProfileAsync().GetAwaiter().GetResult();
            _placement.Save(this);
            _sessionMarker.TryComplete(
                cleanup.Succeeded && _viewModel.IsOutputStateConfirmedSafe);
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
            var cleanup = _viewModel.EmergencyStop("application exit");
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
            _sessionMarker.TryComplete(
                cleanup.Succeeded && _viewModel.IsOutputStateConfirmedSafe);
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
            _viewModel.ReportPersistentStatusWarning(
                error ?? "The emergency hotkey could not be registered. Mouse and tray recovery remain available.");
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
            BackgroundNotificationMessage);
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
    private void OpenAssignmentEditor_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanAssignSelectedControl)
        {
            return;
        }

        var editor = new KeyboardAssignmentWindow(
            _viewModel.SelectedControlLabel,
            _viewModel.GetSelectedControllerAction())
        {
            Owner = this,
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        if (editor.ActionResult is { } actionAssignment)
        {
            _viewModel.AssignControllerAction(actionAssignment);
        }
        else if (editor.Result is { } assignment)
        {
            _viewModel.AssignKeyboardMapping(assignment);
        }
    }

    private async void SaveProfile_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.SaveProfileAsync().ConfigureAwait(true);

    private async void CaptureAnalogMinimum_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureSelectedAnalogMinimumAsync().ConfigureAwait(true);

    private async void CaptureAnalogCenter_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureSelectedAnalogCenterAsync().ConfigureAwait(true);

    private async void CaptureAnalogMaximum_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CaptureSelectedAnalogMaximumAsync().ConfigureAwait(true);

    private async void ResetAnalogCalibration_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ResetSelectedAnalogCalibrationAsync().ConfigureAwait(true);

    private async void SlowEncoder_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.TuneSelectedEncoderAsync(0.5).ConfigureAwait(true);

    private async void FastEncoder_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.TuneSelectedEncoderAsync(2).ConfigureAwait(true);

    private async void ReverseEncoder_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ReverseSelectedEncoderAsync().ConfigureAwait(true);

    private void ResetEncoderAngle_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.ResetSelectedEncoderAngle();

    private void ControlTile_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ControlTileViewModel tile })
        {
            _viewModel.SelectControl(tile);
        }
    }

    private void ControlTile_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: ControlTileViewModel tile } button)
        {
            return;
        }

        _viewModel.SelectControl(tile);
        var movingTiles = _viewModel.PrepareGroupDrag(tile);
        _tileDragStart = e.GetPosition(ControllerLayoutCanvas);
        _tileDragOrigin = new System.Windows.Point(tile.X, tile.Y);
        _tileDragOrigins = movingTiles.ToDictionary(
            item => item.ControlId,
            item => (item.X, item.Y),
            StringComparer.Ordinal);
        _tileDragSource = tile;
        _tileDragMoved = false;
        button.CaptureMouse();
        e.Handled = true;
    }

    private void ControlTile_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _tileDragSource is null)
        {
            return;
        }

        var current = e.GetPosition(ControllerLayoutCanvas);
        if (Math.Abs(current.X - _tileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _tileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance &&
            !_tileDragMoved)
        {
            return;
        }

        _tileDragMoved = true;
        _viewModel.MoveControlGroup(
            _tileDragSource,
            _tileDragOrigin.X + current.X - _tileDragStart.X,
            _tileDragOrigin.Y + current.Y - _tileDragStart.Y,
            _tileDragOrigins);
        e.Handled = true;
    }

    private async void ControlTile_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_tileDragSource is null)
        {
            return;
        }

        var moved = _tileDragMoved;
        _tileDragSource = null;
        _tileDragOrigins = new Dictionary<string, (double X, double Y)>();
        _tileDragMoved = false;
        if (sender is UIElement element)
        {
            element.ReleaseMouseCapture();
        }

        e.Handled = true;
        if (moved)
        {
            await _viewModel.SaveControllerLayoutAsync().ConfigureAwait(true);
        }
    }

    private void ResizeTile_OnDragStarted(
        object sender,
        System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.Thumb { DataContext: ControlTileViewModel tile })
        {
            _viewModel.SelectControl(tile);
        }
    }

    private void ResizeTile_OnDragDelta(
        object sender,
        System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.Thumb { DataContext: ControlTileViewModel tile })
        {
            _viewModel.ResizeControl(tile, e.HorizontalChange, e.VerticalChange);
        }
    }

    private async void ResizeTile_OnDragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        await _viewModel.SaveControllerLayoutAsync().ConfigureAwait(true);

    private async void ApplyLayoutWorkspace_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyLayoutWorkspaceAsync(arrangeAsGrid: false).ConfigureAwait(true);

    private async void ArrangeLayoutAsGrid_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyLayoutWorkspaceAsync(arrangeAsGrid: true).ConfigureAwait(true);

    private void SelectAllTiles_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.SelectAllControls();

    private void ClearTileSelection_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.ClearGroupSelection();

    private async void ApplyTileColor_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedTileColorAsync().ConfigureAwait(true);

    private void SyncTileColorsToHardware_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.SyncTileColorsToHardware();

    private void TileGroupCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { DataContext: ControlTileViewModel tile })
        {
            _viewModel.SetGroupSelection(tile, true);
        }

        e.Handled = true;
    }

    private void TileGroupCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { DataContext: ControlTileViewModel tile })
        {
            _viewModel.SetGroupSelection(tile, false);
        }

        e.Handled = true;
    }

    private void ControllerWorkspace_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _marqueeActive = true;
        _marqueeStart = e.GetPosition(ControllerWorkspaceHost);
        _marqueePreservedIds = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? _viewModel.Controls.Where(tile => tile.IsGroupSelected)
                .Select(tile => tile.ControlId).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        if (_marqueePreservedIds.Count == 0)
        {
            _viewModel.ClearGroupSelection();
        }
        Canvas.SetLeft(SelectionMarquee, _marqueeStart.X);
        Canvas.SetTop(SelectionMarquee, _marqueeStart.Y);
        SelectionMarquee.Width = 0;
        SelectionMarquee.Height = 0;
        SelectionMarquee.Visibility = Visibility.Visible;
        ControllerWorkspaceHost.CaptureMouse();
        e.Handled = true;
    }

    private void ControllerWorkspace_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_marqueeActive || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(ControllerWorkspaceHost);
        var left = Math.Min(_marqueeStart.X, current.X);
        var top = Math.Min(_marqueeStart.Y, current.Y);
        var right = Math.Max(_marqueeStart.X, current.X);
        var bottom = Math.Max(_marqueeStart.Y, current.Y);
        Canvas.SetLeft(SelectionMarquee, left);
        Canvas.SetTop(SelectionMarquee, top);
        SelectionMarquee.Width = right - left;
        SelectionMarquee.Height = bottom - top;
        _viewModel.SelectControlsInArea(left, top, right, bottom, _marqueePreservedIds);
        e.Handled = true;
    }

    private void ControllerWorkspace_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_marqueeActive)
        {
            return;
        }

        _marqueeActive = false;
        SelectionMarquee.Visibility = Visibility.Collapsed;
        ControllerWorkspaceHost.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static bool IsInsideInteractiveElement(DependencyObject? source)
    {
        for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            if (current is FrameworkElement { Name: nameof(ControllerWorkspaceHost) })
            {
                break;
            }
        }

        return false;
    }

    private void ControllerLayoutViewport_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        _viewModel.EnsureLayoutSurface(
            Math.Max(0, e.NewSize.Width - 20),
            Math.Max(0, e.NewSize.Height - 20));

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasControllerPhoto))
        {
            Dispatcher.BeginInvoke(UpdateControllerPhotoPane);
        }
    }

    private void UpdateControllerPhotoPane()
    {
        if (_viewModel.HasControllerPhoto)
        {
            ControllerPhotoColumn.MinWidth = 240;
            ControllerPhotoColumn.Width = new GridLength(_placement.ControllerPhotoPaneWidth);
        }
        else
        {
            ControllerPhotoColumn.MinWidth = 0;
            ControllerPhotoColumn.Width = new GridLength(0);
        }
    }

    private void ControllerPhotoSplitter_OnDragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (!_viewModel.HasControllerPhoto || ControllerPhotoColumn.ActualWidth < 240)
        {
            return;
        }

        try
        {
            _placement.ControllerPhotoPaneWidth = ControllerPhotoColumn.ActualWidth;
            _placement.Save(this);
        }
        catch (Exception exception)
        {
            _viewModel.ReportStatus($"The photo was resized for this session, but its width could not be saved: {exception.Message}");
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
