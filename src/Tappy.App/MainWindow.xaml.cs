using System.ComponentModel;
using MessageBox = System.Windows.MessageBox;
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

    private MainViewModel _viewModel;
    private readonly ControllerWorkspace? _workspace;
    private readonly WindowPlacementStore _placement;
    private readonly SessionMarker _sessionMarker;
    private readonly ThemeService _theme = new();
    private readonly EmergencyHotkeyService _hotkey = new();
    private readonly DispatcherTimer _reconnectTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _zoomPersistTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(66) };
    // The Fire's OLED reverts to black on its own a few seconds after its last
    // update (see docs/AKAI_FIRE.md, D-028), so even the slowest content mode
    // needs updates far more often than that. 150ms also has to carry the
    // scrolling wordmark mode smoothly, which needs faster updates than a
    // static status line would on its own.
    private readonly DispatcherTimer _oledContentTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private MainViewModel? _pendingZoomSaveTarget;
    private LedAnimationWindow? _animationWindow;
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
    private System.Windows.Point _panStart;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private bool _panActive;

    public MainWindow(
        MainViewModel viewModel,
        WindowPlacementStore placement,
        SessionMarker sessionMarker,
        ControllerWorkspace? workspace = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _sessionMarker = sessionMarker ?? throw new ArgumentNullException(nameof(sessionMarker));
        _workspace = workspace;
        InitializeComponent();
        DataContext = _viewModel;
        if (workspace is not null)
        {
            DeviceTabs.ItemsSource = workspace.VisibleTabs;
            DeviceTabs.SelectedIndex = 0;
            workspace.VisibleTabs.CollectionChanged += (_, _) => UpdateVisibleDeviceSelection();
        }
        else { DeviceTabBar.Visibility = Visibility.Collapsed; }
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        UpdateVisibleDeviceSelection();
        SourceInitialized += (_, _) =>
        {
            _placement.Restore(this);
            ApplyPresentationMode(_placement.CurrentMode, resize: false);
            ApplyCurrentViewModelPresentation();
        };
        Loaded += MainWindow_OnLoaded;
        _reconnectTimer.Tick += (_, _) =>
        {
            if (_workspace is null) { _viewModel.PollRememberedReconnect(); }
            else { _workspace.Poll(); }
        };
        // Debounced so a fast scroll-wheel zoom session doesn't save the profile on
        // every tick; still short enough to survive an abnormal termination shortly
        // after zooming, not just the normal Exit/shutdown save paths. Zoom is saved
        // per-tab (the confirmed controller's own layout), not window-level, so it
        // targets whichever MainViewModel was actually being zoomed, even if the
        // user switches tabs again before this timer fires.
        _zoomPersistTimer.Tick += async (_, _) =>
        {
            _zoomPersistTimer.Stop();
            var target = _pendingZoomSaveTarget;
            _pendingZoomSaveTarget = null;
            if (target is null) { return; }
            try { await target.SaveControllerLayoutAsync().ConfigureAwait(true); }
            catch { /* Zoom keeps working for this session even if the save failed. */ }
        };
        _animationTimer.Tick += (_, _) => _workspace?.AnimationPlayer.Tick(DateTimeOffset.UtcNow);
        _oledContentTimer.Tick += (_, _) => _workspace?.TickOledContent(DateTimeOffset.UtcNow);
    }

    private Runtime.RuntimeOperation StopAll(string reason) =>
        _workspace?.EmergencyStop(reason) ?? _viewModel.EmergencyStop(reason);

    // The debounced zoom save (see the constructor) only pushes the latest zoom
    // into the runtime's saved layout when its timer fires. On a normal exit that
    // timer is stopped first, so without this, a zoom change made just before
    // exiting would never reach the profile save that follows.
    private async Task FlushPendingZoomSaveAsync()
    {
        var target = _pendingZoomSaveTarget;
        _pendingZoomSaveTarget = null;
        if (target is null) { return; }
        try { await target.SaveControllerLayoutAsync().ConfigureAwait(true); }
        catch { /* The regular profile save that follows still covers everything else. */ }
    }

    private void FlushPendingZoomSave() => FlushPendingZoomSaveAsync().GetAwaiter().GetResult();

    private void ProtectTypingKeyboard_OnClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) { return; }
        var result = _workspace.ProtectTypingKeyboard(_viewModel);
        _viewModel.ReportStatus(result.Message);
    }

    private void ClearTypingKeyboard_OnClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is null || System.Windows.MessageBox.Show(this,
            "Allow this keyboard to be configured as a controller again? No assignments will be armed automatically. All tabs will remain in Rehearsal.",
            "Clear typing-keyboard protection", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) != MessageBoxResult.Yes) { return; }
        _viewModel.ReportStatus(_workspace.ClearTypingKeyboardProtection().Message);
    }

    public void FailSafeStop(string reason) => StopAll(reason);

    public void PrepareForSystemShutdown()
    {
        _reconnectTimer.Stop();
        _zoomPersistTimer.Stop();
        _animationTimer.Stop();
        _oledContentTimer.Stop();
        _workspace?.AnimationPlayer.Stop();
        FlushPendingZoomSave();
        var cleanup = StopAll("Windows session ending");
        try
        {
            var saved = _workspace is null
                ? _viewModel.SaveProfileForShutdownAsync().GetAwaiter().GetResult().Succeeded
                : _workspace.SaveAsync().GetAwaiter().GetResult();
            _placement.Save(this);
            _sessionMarker.TryComplete(
                saved && cleanup.Succeeded && (_workspace?.IsOutputStateConfirmedSafe ?? _viewModel.IsOutputStateConfirmedSafe));
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
            _reconnectTimer.Stop();
            _zoomPersistTimer.Stop();
            _animationTimer.Stop();
            _oledContentTimer.Stop();
            await FlushPendingZoomSaveAsync().ConfigureAwait(true);
            var cleanup = StopAll("application exit");
            try
            {
                if (_workspace is null) { await _viewModel.SaveProfileAsync().ConfigureAwait(true); }
                else { await _workspace.SaveAsync().ConfigureAwait(true); }
            }
            catch
            {
                // Output safety and shutdown do not depend on a successful save.
            }

            if (_workspace is null) { await _viewModel.DisposeAsync().ConfigureAwait(true); }
            else { await _workspace.DisposeAsync().ConfigureAwait(true); }
            _placement.Save(this);
            _sessionMarker.TryComplete(
                cleanup.Succeeded && (_workspace?.IsOutputStateConfirmedSafe ?? _viewModel.IsOutputStateConfirmedSafe));
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
            () => Dispatcher.Invoke(() => StopAll("notification-area command")),
            () => Dispatcher.BeginInvoke(async () => await ExitApplicationAsync(), DispatcherPriority.Send));

        if (!_hotkey.Register(this, () => StopAll("Ctrl+Alt+Shift+F12"), out var error))
        {
            _viewModel.ReportPersistentStatusWarning(
                error ?? "The emergency hotkey could not be registered. Mouse and tray recovery remain available.");
        }

        try
        {
            if (_workspace is null) { await _viewModel.InitializeAsync().ConfigureAwait(true); }
            else { await _workspace.InitializeAsync().ConfigureAwait(true); }
            _reconnectTimer.Start();
            _animationTimer.Start();
            _oledContentTimer.Start();
        }
        catch (Exception exception)
        {
            StopAll("input initialization failure");
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
        e.Handled = (_workspace?.IsIdentificationCaptureActive ?? false) || ShouldSuppressFocusedControlKeyInput(_viewModel);

    private void MainWindow_OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e) =>
        e.Handled = (_workspace?.IsIdentificationCaptureActive ?? false) || ShouldSuppressFocusedControlKeyInput(_viewModel);

    internal static bool ShouldSuppressFocusedControlKeyInput(MainViewModel viewModel) =>
        viewModel.IsIdentificationCaptureActive;

    private void DeviceTabBar_OnPreviewKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // WPF key events carry no trustworthy physical-device identity. A tab
        // click leaves focus here; native keypad Home/Left/Backspace must not
        // select another device or activate Add device. Plain Tab can leave the
        // strip for accessibility; all device selection remains pointer-driven.
        if (e.Key != Key.Tab || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        { e.Handled = true; }
    }

    private void DeviceTabBar_OnPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e) =>
        e.Handled = true;

    private void RefreshDevices_OnClick(object sender, RoutedEventArgs e) => _viewModel.RefreshDevices();
    private void BeginIdentification_OnClick(object sender, RoutedEventArgs e) => _viewModel.BeginIdentification();
    private void ConfirmController_OnClick(object sender, RoutedEventArgs e) => _viewModel.ConfirmController();
    private void OpenAssignmentEditor_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanAssignSelectedControl)
        {
            return;
        }

        var editContext = _viewModel.BeginSelectedAssignmentEdit();
        if (editContext is null)
        {
            return;
        }

        var sourceViewModel = _viewModel;
        var editor = new KeyboardAssignmentWindow(
            sourceViewModel.SelectedControlLabel + (sourceViewModel.GetSelectedBankControl() is null ? "" : " — Bank control overrides this saved assignment"),
            editContext.ExistingAssignment)
        {
            Owner = this,
            SaveAssignment = (keyboard, action) => action is not null
                ? sourceViewModel.AssignControllerAction(editContext.Target, action)
                : keyboard is not null
                    ? sourceViewModel.AssignKeyboardMapping(editContext.Target, keyboard)
                    : Runtime.RuntimeOperation.Failed("The assignment editor did not produce an assignment."),
        };
        if (_workspace is not null) { _workspace.BankControlsPaused = true; }
        try { editor.ShowDialog(); }
        finally { if (_workspace is not null) { _workspace.BankControlsPaused = false; } }
    }

    private async void CreateCombination_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = _viewModel;
        var members = vm.CheckedControlIds();
        if (vm.GetBankEndpoint() is not { } source) { return; }
        if (members.Length is < 2 or > 8 || members.Any(id => id.StartsWith("combination:", StringComparison.Ordinal)))
        { MessageBox.Show(this, "Check 2–8 physical button tiles first. Do not include another combination tile.", "Create combination"); return; }
        if (_workspace is not null) { _workspace.BankControlsPaused = true; }
        try
        {
            var names = string.Join(" + ", vm.Controls.Where(c => members.Contains(c.ControlId)).Select(c => c.Label));
            if (MessageBox.Show(this, names + "\n\nA new combination tile will appear beside the physical buttons. Select it to assign a macro or bank control.\n\nPress the buttons together, then release them all. The largest matching combination wins. Participating single buttons also run on release, not on press. Only run-once assignments are supported. Your hardware must report the buttons simultaneously.\n\nCreate this combination?", "Create combination", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { return; }
            var result = await vm.CreateCombinationAsync(source, members);
            if (!result.Succeeded) { MessageBox.Show(this, result.Message, "Combination needs attention"); }
        }
        finally { if (_workspace is not null) { _workspace.BankControlsPaused = false; } }
    }

    private async void RemoveCombination_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = _viewModel;
        if (!vm.SelectedIsCombination || vm.BeginSelectedAssignmentEdit() is not { } context) { return; }
        if (MessageBox.Show(this, "Remove this combination and its assignments in every bank? The physical buttons and their own assignments will be kept.", "Remove combination", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { return; }
        var result = await vm.RemoveCombinationAsync(context.Target);
        if (!result.Succeeded) { MessageBox.Show(this, result.Message, "Combination needs attention"); }
    }

    private void OpenBankControl_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = _viewModel;
        if (vm.BeginSelectedAssignmentEdit() is not { } context) { return; }
        var targets = _workspace?.GetBankTargets() ?? (vm.GetBankEndpoint() is { } self ? [self] : []);
        var editor = new BankControlWindow(vm.SelectedControlLabel, context.Target, vm.GetSelectedBankControl(), targets)
        {
            Owner = this,
            SaveBankControl = (target, binding) =>
            {
                var current = _workspace?.GetBankTargets() ?? (vm.GetBankEndpoint() is { } local ? [local] : []);
                if (binding is not null && (target is null || !current.Any(target.SameConnection)))
                { return Task.FromResult(Runtime.RuntimeOperation.Failed("The destination changed or disconnected. Reopen Bank control to choose it again.")); }
                return vm.SaveBankControlAsync(context.Target, binding);
            }
        };
        if (_workspace is not null) { _workspace.BankControlsPaused = true; }
        try { editor.ShowDialog(); }
        finally { if (_workspace is not null) { _workspace.BankControlsPaused = false; } }
    }

    private async void ControllerBank_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox { SelectedValue: string id, DataContext: MainViewModel vm } picker &&
            id != vm.SelectedBankId && vm.GetBankEndpoint() is { } endpoint)
        {
            await vm.ChangeBankAsync(endpoint, Tappy.Core.Models.BankControlOperation.Select, id).ConfigureAwait(true);
            // A rejected change must not leave the picker advertising a bank that
            // is not active. Preserve the binding when restoring the selection.
            if (ReferenceEquals(picker.DataContext, vm))
            { picker.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, vm.SelectedBankId); }
        }
    }

    private async void TartarusKeymap_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox { SelectedValue: int bank, DataContext: MainViewModel viewModel })
        { await viewModel.SelectTartarusKeymapAsync(bank).ConfigureAwait(true); }
    }

    private async void TartarusLeds_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel viewModel })
        { await viewModel.SyncTartarusIndicatorsAsync().ConfigureAwait(true); }
    }

    private async void TartarusFollowLeds_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel { TartarusFollowKeymaps: true } viewModel })
        { await viewModel.SyncTartarusIndicatorsAsync().ConfigureAwait(true); }
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

        if (_viewModel.IsLayoutLocked)
        {
            // Don't start a drag and don't capture the mouse — leaves the
            // normal Click event (ControlTile_OnClick) to select the tile,
            // same as every other button in the app. Locking stops
            // moving/resizing only, not selection or color actions.
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
        if (!_viewModel.IsLayoutLocked &&
            sender is System.Windows.Controls.Primitives.Thumb { DataContext: ControlTileViewModel tile })
        {
            _viewModel.SelectControl(tile);
        }
    }

    private void ResizeTile_OnDragDelta(
        object sender,
        System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (!_viewModel.IsLayoutLocked &&
            sender is System.Windows.Controls.Primitives.Thumb { DataContext: ControlTileViewModel tile })
        {
            _viewModel.ResizeControl(tile, e.HorizontalChange, e.VerticalChange);
        }
    }

    private async void ResizeTile_OnDragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        await _viewModel.SaveControllerLayoutAsync().ConfigureAwait(true);

    private async void ArrangeLayoutAsGrid_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ArrangeLayoutAsGridAsync().ConfigureAwait(true);

    private void SelectAllTiles_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.SelectAllControls();

    private void ClearTileSelection_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.ClearGroupSelection();

    // Prefills from the first checked (group-selected) tile, or the single
    // focused tile if nothing is checked — the same selection-resolution
    // MainViewModel.ResizeGroupSelectionAsync itself applies when Apply is
    // clicked, so the dialog's starting numbers match what Apply will
    // actually affect. Encoder cluster members are excluded from both the
    // prefill and the affected count — they can't be resized independently
    // (see ControlTileViewModel.IsEncoderClusterMember).
    private async void ResizeSelectedTiles_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = _viewModel;
        var groupSelected = vm.Controls.Where(tile => tile.IsGroupSelected && !tile.IsEncoderClusterMember).ToArray();
        var prefillSource = groupSelected.Length > 0
            ? groupSelected[0]
            : vm.Controls.FirstOrDefault(tile => tile.IsSelected && !tile.IsEncoderClusterMember);
        var affectedCount = groupSelected.Length > 0 ? groupSelected.Length : prefillSource is null ? 0 : 1;

        var dialog = new ResizeTilesWindow(
            prefillSource?.TileWidth ?? MainViewModel.DefaultTileWidth,
            prefillSource?.TileHeight ?? MainViewModel.DefaultTileHeight,
            affectedCount)
        { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await vm.ResizeGroupSelectionAsync(dialog.SelectedWidth, dialog.SelectedHeight).ConfigureAwait(true);
        }
    }

    private async void ApplyTileColor_OnClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedTileColorAsync().ConfigureAwait(true);

    private void SyncTileColorsToHardware_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel.SyncTileColorsToHardware();

    private void OpenFireColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        var vm = _viewModel;
        if (!vm.HasFreeformColorSupport) { return; }
        var (initialRed, initialGreen, initialBlue) =
            MainViewModel.TryParseHexColor(vm.SelectedTileColor.Key, out var red, out var green, out var blue)
                ? (red, green, blue)
                : ((byte)255, (byte)255, (byte)255);
        var picker = new FireColorPickerWindow(initialRed, initialGreen, initialBlue) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            vm.SelectCustomColor($"#{picker.SelectedRed:X2}{picker.SelectedGreen:X2}{picker.SelectedBlue:X2}");
        }
    }

    private void SendOledTestPattern_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasOledDisplay) { return; }
        if (MessageBox.Show(
                this,
                "This sends Tappy's first-ever write to the Fire's OLED display. The documented format is implemented and covered by tests, but has not been tried on real hardware before, and out-of-range SysEx values are reported to be able to destabilize the device. Continue?",
                "OLED test (experimental)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _viewModel.SendOledTestPattern();
    }

    private void SendOledTestPatternToAllFires_OnClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is null || !_viewModel.HasOledDisplay) { return; }
        if (MessageBox.Show(
                this,
                "Send the OLED test pattern to every confirmed Fire, one right after another? Each unit's screen reverts on its own a few seconds after its last update, so seeing them lit together only lasts briefly.",
                "OLED test — all Fires",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var results = _workspace.SendOledTestPatternToAllFires();
        var failed = results.Count(result => !result.Succeeded);
        _viewModel.ReportStatus(failed == 0
            ? $"Sent the OLED test pattern to {results.Count} confirmed Fire unit(s)."
            : $"Sent the OLED test pattern to {results.Count - failed} of {results.Count} confirmed Fire unit(s); {failed} failed.");
    }

    private void SetOledDisplayMode(FireOledDisplayMode mode, string statusLabel)
    {
        if (!_viewModel.HasOledDisplay) { return; }
        _viewModel.OledDisplayMode = mode;
        _viewModel.ReportStatus(mode == FireOledDisplayMode.Off
            ? "OLED display turned off. It goes dark on its own a few seconds later."
            : $"OLED display set to {statusLabel}.");
    }

    private void OledDisplayOff_OnClick(object sender, RoutedEventArgs e) =>
        SetOledDisplayMode(FireOledDisplayMode.Off, "off");

    private void OledDisplayLiveStatus_OnClick(object sender, RoutedEventArgs e) =>
        SetOledDisplayMode(FireOledDisplayMode.LiveStatus, "live status");

    private void OledDisplayTappyLogo_OnClick(object sender, RoutedEventArgs e) =>
        SetOledDisplayMode(FireOledDisplayMode.TappyLogo, "the Tappy logo");

    private void OledDisplayScrollingWordmark_OnClick(object sender, RoutedEventArgs e) =>
        SetOledDisplayMode(FireOledDisplayMode.ScrollingWordmark, "the scrolling TerkWerX wordmark");

    private void OpenLedAnimations_OnClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is null || !_viewModel.HasAnimatableLighting) { return; }
        var currentTab = _workspace.Tabs.FirstOrDefault(tab => tab.ViewModel == _viewModel);
        if (currentTab is null) { return; }
        if (_animationWindow is { IsLoaded: true })
        {
            _animationWindow.Activate();
            return;
        }

        _animationWindow = new LedAnimationWindow(_workspace, currentTab) { Owner = this };
        // Non-modal (Show, not ShowDialog) since playback is an ongoing, live
        // operation the user should be able to leave running while doing other
        // things — unlike every other editor in this app. Bank routing must
        // still never fire while it is open, so the pause brackets the window's
        // whole lifetime instead of one ShowDialog() call.
        _workspace.BankControlsPaused = true;
        _animationWindow.Closed += (_, _) =>
        {
            _workspace.BankControlsPaused = false;
            _animationWindow = null;
        };
        _animationWindow.Show();
    }

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

        // Locked means there is nothing left to move/select by dragging empty
        // space, so the same gesture pans the viewport instead — "click grab
        // to move around" once the layout is set, rather than marquee-select.
        if (_viewModel.IsLayoutLocked)
        {
            _panActive = true;
            _panStart = e.GetPosition(ControllerLayoutViewport);
            _panStartHorizontalOffset = ControllerLayoutViewport.HorizontalOffset;
            _panStartVerticalOffset = ControllerLayoutViewport.VerticalOffset;
            ControllerWorkspaceHost.CaptureMouse();
            e.Handled = true;
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
        if (_panActive)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var pointer = e.GetPosition(ControllerLayoutViewport);
            ControllerLayoutViewport.ScrollToHorizontalOffset(_panStartHorizontalOffset - (pointer.X - _panStart.X));
            ControllerLayoutViewport.ScrollToVerticalOffset(_panStartVerticalOffset - (pointer.Y - _panStart.Y));
            e.Handled = true;
            return;
        }

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
        if (_panActive)
        {
            _panActive = false;
            ControllerWorkspaceHost.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

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

    private const double MinControllerLayoutZoom = 0.25;
    private const double MaxControllerLayoutZoom = 2.0;

    private void ControllerLayoutViewport_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        HandleControllerLayoutZoom(e, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

    // Ctrl held is passed explicitly so tests can exercise both branches without
    // depending on live OS keyboard state. Anchors on the current viewport center
    // rather than the cursor position, which stays correct even before the window
    // has a PresentationSource (e.g. in headless tests).
    internal void HandleControllerLayoutZoom(MouseWheelEventArgs e, bool ctrlHeld)
    {
        if (!ctrlHeld)
        {
            return;
        }

        e.Handled = true;
        var oldZoom = ControllerWorkspaceScale.ScaleX;
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var newZoom = Math.Clamp(oldZoom * factor, MinControllerLayoutZoom, MaxControllerLayoutZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001)
        {
            return;
        }

        var viewport = ControllerLayoutViewport;
        var centerX = (viewport.HorizontalOffset + (viewport.ViewportWidth / 2)) / oldZoom;
        var centerY = (viewport.VerticalOffset + (viewport.ViewportHeight / 2)) / oldZoom;

        ControllerWorkspaceScale.ScaleX = newZoom;
        ControllerWorkspaceScale.ScaleY = newZoom;
        viewport.UpdateLayout();
        viewport.ScrollToHorizontalOffset((centerX * newZoom) - (viewport.ViewportWidth / 2));
        viewport.ScrollToVerticalOffset((centerY * newZoom) - (viewport.ViewportHeight / 2));

        // Zoom belongs to the confirmed controller being viewed, not the window, so
        // each device tab keeps its own independent zoom. Captured here (not read
        // fresh from _viewModel when the timer fires) so a tab switch within the
        // debounce window still saves to the tab that was actually zoomed.
        _viewModel.Zoom = newZoom;
        // Pushed into the runtime immediately (in-memory only, no disk write) — not
        // just at the end of the debounce below — because any runtime event in the
        // meantime (a control press, a bank change, anything that fires
        // StateChanged) resyncs this viewmodel from the runtime's current layout.
        // Without this, that resync would stomp Zoom back to its pre-scroll value
        // before the debounced save ever got a chance to persist it.
        _viewModel.SyncLayoutPresentationToRuntime();
        _pendingZoomSaveTarget = _viewModel;
        _zoomPersistTimer.Stop();
        _zoomPersistTimer.Start();
    }

    private void FitControllerLayoutToViewport_OnClick(object sender, RoutedEventArgs e) =>
        FitControllerLayoutToViewport();

    private void FillControllerLayoutToViewport_OnClick(object sender, RoutedEventArgs e) =>
        FillControllerLayoutToViewport();

    // Same scale/scroll/save mechanics as HandleControllerLayoutZoom above, just
    // computing the zoom that makes the whole layout visible instead of a
    // wheel-driven increment, then scrolling to the origin instead of keeping
    // the current viewport center. Measures against
    // MainViewModel.GetLayoutContentBounds() — the tight bounding box of the
    // actually-placed tiles — not LayoutSurfaceWidth/Height, which is a
    // grow-only high-water mark that can be far bigger than the current
    // arrangement (see GetLayoutContentBounds' own comment) and made this
    // button a near no-op once a tab's surface had ballooned to roughly
    // viewport size.
    internal void FitControllerLayoutToViewport() => ZoomControllerLayoutToContent(useMin: true);

    // Same as Fit, but zooms in until the content fully covers the viewport
    // on at least one axis (Math.Max instead of Math.Min) instead of
    // stopping once the whole layout is visible — standard "fit" vs. "fill,"
    // as in an image/video viewer. Still a single uniform ScaleX/ScaleY, so
    // nothing stretches; the other axis may need scrolling.
    internal void FillControllerLayoutToViewport() => ZoomControllerLayoutToContent(useMin: false);

    private void ZoomControllerLayoutToContent(bool useMin)
    {
        var viewport = ControllerLayoutViewport;
        var (contentWidth, contentHeight) = _viewModel.GetLayoutContentBounds();
        if (contentWidth <= 0 || contentHeight <= 0 ||
            viewport.ViewportWidth <= 0 || viewport.ViewportHeight <= 0)
        {
            return;
        }

        var widthRatio = viewport.ViewportWidth / contentWidth;
        var heightRatio = viewport.ViewportHeight / contentHeight;
        var targetZoom = useMin ? Math.Min(widthRatio, heightRatio) : Math.Max(widthRatio, heightRatio);
        var newZoom = Math.Clamp(targetZoom, MinControllerLayoutZoom, MaxControllerLayoutZoom);

        ControllerWorkspaceScale.ScaleX = newZoom;
        ControllerWorkspaceScale.ScaleY = newZoom;
        viewport.UpdateLayout();
        viewport.ScrollToHorizontalOffset(0);
        viewport.ScrollToVerticalOffset(0);

        _viewModel.Zoom = newZoom;
        _viewModel.SyncLayoutPresentationToRuntime();
        _pendingZoomSaveTarget = _viewModel;
        _zoomPersistTimer.Stop();
        _zoomPersistTimer.Start();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasControllerPhoto) ||
            e.PropertyName == nameof(MainViewModel.PhotoPaneWidth))
        {
            Dispatcher.BeginInvoke(UpdateControllerPhotoPane);
        }
        else if (e.PropertyName == nameof(MainViewModel.Zoom))
        {
            Dispatcher.BeginInvoke(() =>
            {
                ControllerWorkspaceScale.ScaleX = _viewModel.Zoom;
                ControllerWorkspaceScale.ScaleY = _viewModel.Zoom;
            });
        }
    }

    // Applies the currently displayed device tab's own saved zoom and photo-pane
    // width to the shared visual elements. Needed at startup and on every tab
    // switch, since ControllerWorkspaceScale/ControllerPhotoColumn are single
    // window-level visual elements shared across whichever tab is showing.
    private void ApplyCurrentViewModelPresentation()
    {
        ControllerWorkspaceScale.ScaleX = _viewModel.Zoom;
        ControllerWorkspaceScale.ScaleY = _viewModel.Zoom;
        UpdateControllerPhotoPane();
    }

    internal void UpdateControllerPhotoPane()
    {
        if (_viewModel.HasControllerPhoto)
        {
            ControllerPhotoColumn.MinWidth = 240;
            ControllerPhotoColumn.Width = new GridLength(_viewModel.PhotoPaneWidth);
        }
        else
        {
            ControllerPhotoColumn.MinWidth = 0;
            ControllerPhotoColumn.Width = new GridLength(0);
        }
    }

    private async void ControllerPhotoSplitter_OnDragCompleted(
        object sender,
        System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (!_viewModel.HasControllerPhoto || ControllerPhotoColumn.ActualWidth < 240)
        {
            return;
        }

        try
        {
            _viewModel.PhotoPaneWidth = ControllerPhotoColumn.ActualWidth;
            await _viewModel.SaveControllerLayoutAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _viewModel.ReportStatus($"The photo was resized for this session, but its width could not be saved: {exception.Message}");
        }
    }

    private void EmergencyStop_OnClick(object sender, RoutedEventArgs e) =>
        StopAll("mouse-accessible window command");

    private async void AddDeviceTab_OnClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is null) { return; }
        AddDeviceTabButton.IsEnabled = false;
        try { DeviceTabs.SelectedItem = await _workspace.AddAsync(); }
        catch (Exception exception) { _viewModel.ReportStatus($"Could not add device tab: {exception.Message}"); }
        finally { AddDeviceTabButton.IsEnabled = true; }
    }

    private void DeviceTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceTabs.SelectedItem is ControllerTab tab) { ShowTabViewModel(tab); }
    }

    private void ShowTabViewModel(ControllerTab tab)
    {
        if (ReferenceEquals(tab.ViewModel, _viewModel)) { return; }
        // Do not leave drag state pointing at a tile owned by a hidden device.
        _tileDragSource = null;
        _tileDragMoved = false;
        _marqueeActive = false;
        Mouse.Capture(null);
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel = tab.ViewModel;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        DataContext = _viewModel;
        Dispatcher.BeginInvoke(ApplyCurrentViewModelPresentation);
    }

    private void UpdateVisibleDeviceSelection()
    {
        if (_workspace is null) { return; }
        var empty = _workspace.VisibleTabs.Count == 0;
        EmptyWorkspacePanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        var idle = _workspace.Tabs.FirstOrDefault(tab => tab.Runtime.ReservedDevice is null);
        SetupPanel.IsEnabled = !empty || idle is not null;
        if (empty)
        {
            DeviceTabs.SelectedIndex = -1;
            if (idle is not null) { ShowTabViewModel(idle); }
        }
        else if (DeviceTabs.SelectedItem is not ControllerTab selected || !_workspace.VisibleTabs.Contains(selected))
        { DeviceTabs.SelectedItem = _workspace.VisibleTabs[0]; }
    }

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
