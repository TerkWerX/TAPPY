using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Models;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task Controller_layout_zoom_only_activates_with_ctrl_and_stays_within_bounds()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "tappy-zoom-test-" + Guid.NewGuid().ToString("N"));
            var application = System.Windows.Application.Current;
            var originalThemes = application.Resources.MergedDictionaries.ToArray();
            try
            {
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Tappy;component/Themes/Dark.xaml") });

                var runtime = new FakeRuntime();
                await using var vm = new MainViewModel(runtime, action => action(), (_, _) => { });
                await vm.InitializeAsync();
                var window = new MainWindow(vm, new WindowPlacementStore(root), new SessionMarker(root));
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1500, 900));
                content.Arrange(new Rect(0, 0, 1500, 900));
                content.UpdateLayout();

                var scale = (ScaleTransform)window.FindName("ControllerWorkspaceScale");
                Assert.Equal(1, scale.ScaleX);
                Assert.Equal(1, scale.ScaleY);

                static MouseWheelEventArgs Wheel(int delta) => new(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent };

                var withoutCtrl = Wheel(120);
                window.HandleControllerLayoutZoom(withoutCtrl, ctrlHeld: false);
                Assert.False(withoutCtrl.Handled);
                Assert.Equal(1, scale.ScaleX);

                var zoomIn = Wheel(120);
                window.HandleControllerLayoutZoom(zoomIn, ctrlHeld: true);
                Assert.True(zoomIn.Handled);
                Assert.True(scale.ScaleX > 1);
                Assert.Equal(scale.ScaleX, scale.ScaleY);

                for (var i = 0; i < 30; i++)
                {
                    window.HandleControllerLayoutZoom(Wheel(120), ctrlHeld: true);
                }
                Assert.Equal(2.0, scale.ScaleX);
                Assert.Equal(2.0, scale.ScaleY);

                for (var i = 0; i < 60; i++)
                {
                    window.HandleControllerLayoutZoom(Wheel(-120), ctrlHeld: true);
                }
                Assert.Equal(0.25, scale.ScaleX);
                Assert.Equal(0.25, scale.ScaleY);
            }
            finally
            {
                application.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in originalThemes)
                {
                    application.Resources.MergedDictionaries.Add(dictionary);
                }
            }
        });
    }

    [Fact]
    public async Task Controller_workspace_zoom_and_photo_pane_width_are_per_controller_not_per_window()
    {
        var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
        var runtime = new FakeRuntime
        {
            ExistingLayout = new ControllerLayoutWorkspace(8, 6, false,
                [new ControllerControlPlacement(g1.ControlId.Value, 0, 0, 360, 240, 1.5, 0.75)],
                Zoom: 1.6, PhotoPaneWidth: 640, IsLayoutLocked: true)
        };
        await using var vm = new MainViewModel(runtime, action => action(), (_, _) => { });
        await vm.InitializeAsync();

        // MainViewModel is one-per-device-tab; nothing here is window-level, so the
        // property itself (not a shared window-scoped store) is the source of truth,
        // clamped to the same bounds MainWindow enforces during live zooming/resizing.
        Assert.Equal(1.0, vm.Zoom);
        Assert.Equal(420, vm.PhotoPaneWidth);
        Assert.False(vm.IsLayoutLocked);
        vm.Zoom = 50;
        Assert.Equal(2.0, vm.Zoom);
        vm.Zoom = -3;
        Assert.Equal(0.25, vm.Zoom);
        vm.PhotoPaneWidth = 5000;
        Assert.Equal(1200, vm.PhotoPaneWidth);
        vm.PhotoPaneWidth = 1;
        Assert.Equal(240, vm.PhotoPaneWidth);

        // Confirming a controller loads its own saved zoom/pane width/lock from
        // the runtime, exactly like LayoutColumns/LayoutRows already do.
        var confirmed = new RuntimeState(true, false, "Controller confirmed.", "Logitech G13",
            "Layer 1", "Rehearsal Mode suppresses output.", "Controller ready.",
            ActiveControllerProviderId: "raw-hid-g13",
            ActiveControllerVendorId: LogitechG13Protocol.VendorId,
            ActiveControllerProductId: LogitechG13Protocol.ProductId);
        runtime.EmitState(confirmed);
        Assert.Equal(1.6, vm.Zoom);
        Assert.Equal(640, vm.PhotoPaneWidth);
        Assert.True(vm.IsLayoutLocked);

        vm.Zoom = 0.5;
        vm.PhotoPaneWidth = 500;
        vm.IsLayoutLocked = false;
        await vm.SaveControllerLayoutAsync();
        Assert.Equal(0.5, runtime.UpdatedLayout!.Zoom);
        Assert.Equal(500, runtime.UpdatedLayout!.PhotoPaneWidth);
        Assert.False(runtime.UpdatedLayout!.IsLayoutLocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disarming_removes_tiles_and_photo_and_confirmation_restores_saved_presentation(bool deferredUi)
    {
        var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
        var layout = new ControllerLayoutWorkspace(8, 6, false,
            [new ControllerControlPlacement(g1.ControlId.Value, 0, 0, 360, 240, 1.5, 0.75)]);
        var runtime = new FakeRuntime { ExistingLayout = layout };
        var ui = new Queue<Action>();
        var delayed = new Queue<Action>();
        await using var viewModel = new MainViewModel(runtime,
            action => { if (deferredUi) { ui.Enqueue(action); } else { action(); } },
            (_, action) => delayed.Enqueue(action));
        void DrainUi()
        {
            while (ui.TryDequeue(out var action)) { action(); }
        }
        var confirmed = new RuntimeState(true, false, "Controller confirmed.", "Logitech G13",
            "Layer 1", "Rehearsal Mode suppresses output.", "Controller ready.",
            ActiveControllerProviderId: "raw-hid-g13",
            ActiveControllerVendorId: LogitechG13Protocol.VendorId,
            ActiveControllerProductId: LogitechG13Protocol.ProductId);
        await viewModel.InitializeAsync();
        runtime.EmitState(confirmed);
        runtime.EmitControl(new RuntimeControlUpdate("g13-persistent", g1.ControlId.Value,
            g1.DisplayName, true, false, "Hold F24 until release", 1, 1));
        DrainUi();
        var original = Assert.Single(viewModel.Controls);
        viewModel.SelectAllControls();
        Assert.True(viewModel.HasControllerPhoto);
        Assert.True(viewModel.CanAssignSelectedControl);
        runtime.EmitControl(new RuntimeControlUpdate("g13-persistent", g1.ControlId.Value,
            g1.DisplayName, false, false, "Hold F24 until release", 0, 2));
        runtime.EmitState(new RuntimeState(false, false, "Controller removed.", "No controller confirmed",
            "Layer 1", "Rehearsal Mode restored.", "Controller disconnected."));
        DrainUi();

        Assert.Empty(viewModel.Controls);
        Assert.Empty(viewModel.ControllerPhotoHotspots);
        Assert.False(viewModel.HasControllerPhoto);
        Assert.Equal("None", viewModel.SelectedControlLabel);
        Assert.Equal("None", viewModel.PressedSummary);
        Assert.Equal(0, viewModel.GroupSelectedCount);
        Assert.False(viewModel.CanAssignSelectedControl);
        while (delayed.TryDequeue(out var action)) { action(); }
        Assert.Empty(viewModel.Controls);

        // The real runtime publishes saved tile snapshots before its confirmed state.
        runtime.EmitControl(new RuntimeControlUpdate("g13-persistent", g1.ControlId.Value,
            g1.DisplayName, false, false, "Hold F24 until release", 0, 0, IsSnapshot: true));
        runtime.EmitState(confirmed);
        DrainUi();
        var restored = Assert.Single(viewModel.Controls);
        Assert.NotSame(original, restored);
        Assert.Equal("Hold F24 until release", restored.Action);
        Assert.Equal(original.X, restored.X);
        Assert.Equal(original.Y, restored.Y);
        Assert.Equal(original.TileWidth, restored.TileWidth);
        Assert.Equal(original.TileHeight, restored.TileHeight);
        Assert.False(restored.IsGroupSelected);
        Assert.True(viewModel.HasControllerPhoto);
        Assert.Single(viewModel.ControllerPhotoHotspots);
        Assert.Same(layout, runtime.ExistingLayout);
        Assert.Null(runtime.UpdatedLayout);
        Assert.Equal(0, runtime.SaveCalls);
    }

    [Fact]
    public async Task Emergency_stop_immediately_removes_tiles_and_assignment_selection()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        runtime.EmitControl(new RuntimeControlUpdate("g13-persistent", "g13:g1", "G1",
            true, false, "Hold F24 until release", 1, 1));
        viewModel.SelectAllControls();

        Assert.True(viewModel.EmergencyStop("test").Succeeded);

        Assert.Empty(viewModel.Controls);
        Assert.Equal("None", viewModel.SelectedControlLabel);
        Assert.False(viewModel.CanAssignSelectedControl);
        Assert.Equal(0, viewModel.GroupSelectedCount);
        Assert.Equal(0, runtime.SaveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assignment_editor_saves_to_its_captured_control_after_another_physical_press(bool keyboardOnly)
    {
        var runtime = new FakeRuntime { ExistingAction = new ControllerActionAssignment("G1 macro", new(), new()) };
        await using var viewModel = new MainViewModel(runtime, action => action());
        runtime.EmitControl(new RuntimeControlUpdate("persistent-1", "g13:g1", "G1", true, false, "G1 original", 1, 1));
        var context = Assert.IsType<ControllerAssignmentEditContext>(viewModel.BeginSelectedAssignmentEdit());
        runtime.EmitControl(new RuntimeControlUpdate("persistent-1", "g13:g2", "G2", true, false, "G2 original", 2, 2));

        var result = keyboardOnly
            ? viewModel.AssignKeyboardMapping(context.Target, KeyboardMappingAssignment.PressOnce("Edited G1", ["F24"]))
            : viewModel.AssignControllerAction(context.Target, new ControllerActionAssignment("Edited G1", new(), new()));

        Assert.True(result.Succeeded);
        Assert.Equal("g13:g1", runtime.LastAssignmentTarget?.ControlId);
        Assert.Equal("G2", viewModel.SelectedControlLabel);
        Assert.Equal("Edited G1", viewModel.Controls.Single(tile => tile.ControlId == "g13:g1").Action);
        Assert.Equal("G2 original", viewModel.Controls.Single(tile => tile.ControlId == "g13:g2").Action);
    }

    [Fact]
    public async Task Rejected_assignment_target_does_not_update_any_tile()
    {
        var runtime = new FakeRuntime { RejectAssignmentTarget = true };
        await using var viewModel = new MainViewModel(runtime, action => action());
        runtime.EmitControl(new RuntimeControlUpdate("persistent-1", "g13:g1", "G1", true, false, "Original", 1, 1));
        var context = Assert.IsType<ControllerAssignmentEditContext>(viewModel.BeginSelectedAssignmentEdit());

        var result = viewModel.AssignControllerAction(context.Target, new ControllerActionAssignment("Edited", new(), new()));

        Assert.False(result.Succeeded);
        Assert.Equal("Original", Assert.Single(viewModel.Controls).Action);
        Assert.Equal(result.Message, viewModel.MappingStatus);
    }

    [Fact]
    public async Task Shutdown_persistence_completes_while_the_calling_ui_context_is_blocked()
    {
        var diskWrite = new TaskCompletionSource<RuntimeOperation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime { PendingSave = diskWrite.Task };
        await using var viewModel = new MainViewModel(runtime, action => action());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<RuntimeOperation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new NonPumpingSynchronizationContext();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var save = viewModel.SaveProfileForShutdownAsync();
                entered.SetResult();
                // Match Windows' synchronous session-ending callback: this thread cannot pump posted UI work.
#pragma warning disable xUnit1031
                finished.SetResult(save.GetAwaiter().GetResult());
#pragma warning restore xUnit1031
            }
            catch (Exception exception)
            {
                entered.TrySetException(exception);
                finished.TrySetException(exception);
            }
        })
        { IsBackground = true };
        thread.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        diskWrite.SetResult(RuntimeOperation.Ok("Saved"));

        Assert.True((await finished.Task.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        Assert.Equal(0, context.PostedCount);
        Assert.Equal("No mapping selected.", viewModel.MappingStatus);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        private int _postedCount;
        public int PostedCount => Volatile.Read(ref _postedCount);
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref _postedCount);
    }

    [Fact]
    public async Task Enumeration_never_auto_selects_a_controller()
    {
        var runtime = new FakeRuntime
        {
            Available =
            [
                new ControllerChoice("session-1", "persistent-1", "Spare numpad", "PortBound"),
                new ControllerChoice("session-2", "persistent-2", "Primary keyboard", "PortBound")
            ]
        };
        await using var viewModel = new MainViewModel(runtime, action => action());

        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.Devices.Count);
        Assert.Null(viewModel.SelectedDevice);
        Assert.Contains("None is selected automatically", viewModel.Status, StringComparison.Ordinal);
        Assert.True(runtime.IsRehearsal);
    }

    [Fact]
    public async Task Identification_requires_the_user_to_choose_a_specific_device()
    {
        var runtime = new FakeRuntime
        {
            Available = [new ControllerChoice("session-1", "persistent-1", "Spare numpad", "PortBound")]
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        viewModel.BeginIdentification();
        Assert.Equal(0, runtime.BeginCalls);
        Assert.Contains("Choose a specific device", viewModel.IdentificationStatus, StringComparison.Ordinal);

        viewModel.SelectedDevice = viewModel.Devices[0];
        viewModel.BeginIdentification();
        Assert.Equal(1, runtime.BeginCalls);
        Assert.Same(viewModel.Devices[0], runtime.LastCandidate);
    }

    [Fact]
    public async Task Rehearsal_toggle_is_forwarded_to_the_runtime()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        viewModel.IsRehearsal = false;

        Assert.False(runtime.IsRehearsal);
        Assert.Contains("Normal output", viewModel.MappingStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rehearsal_refusal_immediately_restores_effective_mode_and_never_claims_normal_output()
    {
        var runtime = new FakeRuntime { RefuseNormalMode = true };
        var queuedUi = new Queue<Action>();
        await using var viewModel = new MainViewModel(runtime, action => queuedUi.Enqueue(action));
        await viewModel.InitializeAsync();

        viewModel.IsRehearsal = false;

        Assert.True(runtime.IsRehearsal);
        Assert.True(viewModel.IsRehearsal);
        Assert.Contains("refused", viewModel.MappingStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Normal output is armed", viewModel.MappingStatus, StringComparison.Ordinal);

        Assert.Single(queuedUi);
        queuedUi.Dequeue()();
        Assert.Contains("could not confirm a safe output state", viewModel.MappingStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Synchronous_rehearsal_refusal_preserves_runtime_failure_detail()
    {
        var runtime = new FakeRuntime { RefuseNormalMode = true };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        viewModel.IsRehearsal = false;

        Assert.True(viewModel.IsRehearsal);
        Assert.Contains("could not confirm a safe output state", viewModel.MappingStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Normal output is armed", viewModel.MappingStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Persistent_startup_warning_survives_initialization_and_queued_runtime_state()
    {
        const string warning =
            "The emergency hotkey could not be registered. Mouse and tray recovery remain available.";
        var runtime = new FakeRuntime
        {
            Available = [new ControllerChoice("session-1", "persistent-1", "Spare numpad", "PortBound")],
            InitializeState = new RuntimeState(
                false,
                false,
                "Choose and identify a controller.",
                "No controller confirmed",
                "Layer 1",
                "Rehearsal Mode suppresses output.",
                "Initialization completed.")
        };
        var queuedUi = new Queue<Action>();
        await using var viewModel = new MainViewModel(runtime, action => queuedUi.Enqueue(action));
        viewModel.ReportPersistentStatusWarning(warning);

        await viewModel.InitializeAsync();

        Assert.Contains(warning, viewModel.Status, StringComparison.Ordinal);
        Assert.Single(queuedUi);
        queuedUi.Dequeue()();
        Assert.Contains("Initialization completed", viewModel.Status, StringComparison.Ordinal);
        Assert.Contains(warning, viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Physical_press_selects_and_illuminates_without_losing_simultaneous_state()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", true, false,
            "Unassigned", 1, 1));
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-b", "Numpad 2", true, false,
            "Send F24", 2, 2));

        Assert.Equal(2, viewModel.Controls.Count);
        Assert.All(viewModel.Controls, control => Assert.True(control.IsPressed));
        Assert.Equal("Numpad 2", viewModel.SelectedControlLabel);
        Assert.Contains("Numpad 1", viewModel.PressedSummary, StringComparison.Ordinal);
        Assert.Contains("Numpad 2", viewModel.PressedSummary, StringComparison.Ordinal);
        Assert.Contains("simultaneous: 2", viewModel.EventSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task G13_grid_selection_and_physical_input_share_the_same_photo_hotspot_state()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitState(new RuntimeState(
            true,
            false,
            "Controller confirmed.",
            "Logitech G13",
            "Layer 1",
            "Rehearsal Mode suppresses output.",
            "Controller ready.",
            ActiveControllerProviderId: "raw-hid-g13",
            ActiveControllerVendorId: LogitechG13Protocol.VendorId,
            ActiveControllerProductId: LogitechG13Protocol.ProductId));
        var g1 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G1);
        var g2 = LogitechG13InputProvider.SupportedControls.Single(item => item.Control == LogitechG13Control.G2);
        runtime.EmitControl(new RuntimeControlUpdate(
            "g13-persistent", g1.ControlId.Value, g1.DisplayName, false, false,
            "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "g13-persistent", g2.ControlId.Value, g2.DisplayName, false, false,
            "Unassigned", 0, 0, IsSnapshot: true));

        Assert.True(viewModel.HasControllerPhoto);
        Assert.Equal(2, viewModel.ControllerPhotoHotspots.Count);
        var g1Tile = viewModel.Controls.Single(item => item.ControlId == g1.ControlId.Value);
        var g1Hotspot = viewModel.ControllerPhotoHotspots.Single(item => item.Tile == g1Tile);

        viewModel.SelectControl(g1Tile);

        Assert.True(g1Hotspot.Tile.IsSelected);

        runtime.EmitControl(new RuntimeControlUpdate(
            "g13-persistent", g2.ControlId.Value, g2.DisplayName, true, false,
            "Unassigned", 1, 1));

        var g2Hotspot = viewModel.ControllerPhotoHotspots.Single(item => item.Tile.ControlId == g2.ControlId.Value);
        Assert.False(g1Hotspot.Tile.IsSelected);
        Assert.True(g2Hotspot.Tile.IsSelected);
        Assert.True(g2Hotspot.Tile.IsIlluminated);

        runtime.EmitControl(new RuntimeControlUpdate(
            "g13-persistent", g1.ControlId.Value, g1.DisplayName, true, false,
            "Unassigned", 2, 2));

        Assert.True(g1Hotspot.Tile.IsSelected);
        Assert.True(g1Hotspot.Tile.IsIlluminated);
        Assert.True(g2Hotspot.Tile.IsIlluminated);
    }

    [Fact]
    public async Task Fire_tiles_get_a_circular_corner_radius_for_round_controls_and_stay_rectangular_for_pads()
    {
        const string volumeIncrease = "winmm-midi:channel-1:cc-16:increase";
        const string muteSolo1 = "winmm-midi:channel-1:note-36";
        const string pad1 = "winmm-midi:channel-1:note-54";
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: FL STUDIO FIRE", "Layer 1", "Ready", "Ready",
            ActiveControllerProviderId: WinMmMidiInputProvider.ProviderId));
        runtime.EmitControl(new RuntimeControlUpdate(
            "fire", volumeIncrease, "Volume ▲", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "fire", muteSolo1, "Mute/Solo 1", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "fire", pad1, "Pad R1 C1", false, false, "Unassigned", 0, 0, IsSnapshot: true));

        // Reuses the exact photo hotspot Shape already measured for every Fire
        // control this session (encoders: D-033, Mute/Solo: D-035) rather than
        // a separate shape table — a tile's roundness comes straight from
        // MainViewModel.EnsurePhotoHotspot.
        var volumeTile = viewModel.Controls.Single(item => item.ControlId == volumeIncrease);
        var muteSoloTile = viewModel.Controls.Single(item => item.ControlId == muteSolo1);
        var padTile = viewModel.Controls.Single(item => item.ControlId == pad1);

        Assert.Equal(999, volumeTile.CornerRadius);
        Assert.Equal(999, muteSoloTile.CornerRadius);
        Assert.Equal(2, padTile.CornerRadius);
    }

    [Fact]
    public async Task Grid_style_controllers_get_photo_shaped_tile_sizes_matching_their_real_proportions()
    {
        const string volumeIncrease = "winmm-midi:channel-1:cc-16:increase";
        var fireRuntime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiFireV1Id, "Akai Fire pads — full RGB",
                ["Default"], new HashSet<string>(StringComparer.Ordinal), SupportsFreeformColor: true),
        };
        await using var fireViewModel = new MainViewModel(fireRuntime, action => action());
        await fireViewModel.InitializeAsync();
        fireRuntime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: FL STUDIO FIRE", "Layer 1", "Ready", "Ready",
            ActiveControllerProviderId: WinMmMidiInputProvider.ProviderId));
        fireRuntime.EmitControl(new RuntimeControlUpdate(
            "fire", volumeIncrease, "Volume ▲", false, false, "Unassigned", 0, 0, IsSnapshot: true));

        var volumeTile = fireViewModel.Controls.Single(item => item.ControlId == volumeIncrease);
        // Volume encoder hotspot is a 52-radius circle (104px). Unlike other
        // grid-style controls, an encoder's cluster renders at
        // EncoderClusterTileScale (1.0, the raw measured diameter) rather
        // than PhotoShapeTileScale — it's already three logical controls
        // sharing one compact ring, so it isn't scaled up further.
        Assert.Equal(104, volumeTile.TileWidth);
        Assert.Equal(104, volumeTile.TileHeight);

        const string pad1 = "winmm-midi:channel-1:note-56";
        var apcRuntime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiApcMiniV1Id, "APC MINI v1 — off, green, red, and amber",
                ["Default", "Green", "Red", "Amber"], new HashSet<string>(StringComparer.Ordinal)),
        };
        await using var apcViewModel = new MainViewModel(apcRuntime, action => action());
        await apcViewModel.InitializeAsync();
        apcRuntime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: APC MINI", "Layer 1", "Ready", "Ready",
            ActiveControllerProviderId: WinMmMidiInputProvider.ProviderId));
        apcRuntime.EmitControl(new RuntimeControlUpdate(
            "apc", pad1, "Pad R1 C1", false, false, "Unassigned", 0, 0, IsSnapshot: true));

        var padTile = apcViewModel.Controls.Single(item => item.ControlId == pad1);
        // Measured pad hotspot 104x50 (wide/landscape) scaled by 1.5 = 156x75.
        Assert.Equal(156, padTile.TileWidth);
        Assert.Equal(75, padTile.TileHeight);
    }

    [Fact]
    public async Task Encoder_cluster_tiles_share_one_bounding_box_get_the_right_role_and_drag_together()
    {
        const string increase = "winmm-midi:channel-1:cc-16:increase";
        const string touch = "winmm-midi:channel-1:note-16";
        const string decrease = "winmm-midi:channel-1:cc-16:decrease";
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiFireV1Id, "Akai Fire pads — full RGB",
                ["Default"], new HashSet<string>(StringComparer.Ordinal), SupportsFreeformColor: true),
            // A brand-new (never-before-confirmed) Fire gets this shape from
            // MidiControllerModelCatalog's own code-default layout, where all
            // three of Volume's controls already share NaturalColumn 2 — no
            // saved X/Y, so ApplyControlPlacement derives it from that shared
            // column (see MidiControllerModelCatalog.CreateAkaiFireV1).
            ExistingLayout = new ControllerLayoutWorkspace(
                18, 7, SnapToGrid: false,
                [
                    new ControllerControlPlacement(increase, 0, 2, null, null, 1, 1),
                    new ControllerControlPlacement(touch, 0, 2, null, null, 1, 1),
                    new ControllerControlPlacement(decrease, 0, 2, null, null, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: FL STUDIO FIRE", "Layer 1", "Ready", "Ready",
            ActiveControllerProviderId: WinMmMidiInputProvider.ProviderId));
        runtime.EmitControl(new RuntimeControlUpdate(
            "fire", increase, "Volume ▲", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "fire", touch, "Volume touch", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "fire", decrease, "Volume ▼", false, false, "Unassigned", 0, 0, IsSnapshot: true));

        var increaseTile = viewModel.Controls.Single(item => item.ControlId == increase);
        var touchTile = viewModel.Controls.Single(item => item.ControlId == touch);
        var decreaseTile = viewModel.Controls.Single(item => item.ControlId == decrease);

        Assert.Equal(EncoderRingRole.IncreaseHalf, increaseTile.RingRole);
        Assert.Equal(EncoderRingRole.Center, touchTile.RingRole);
        Assert.Equal(EncoderRingRole.DecreaseHalf, decreaseTile.RingRole);
        Assert.NotNull(increaseTile.ClusterId);
        Assert.Equal(increaseTile.ClusterId, touchTile.ClusterId);
        Assert.Equal(increaseTile.ClusterId, decreaseTile.ClusterId);

        // The three controls render as one concentric cluster in the same
        // spot, not three side-by-side tiles — they must share an identical
        // bounding box or the ring illusion falls apart.
        Assert.Equal(increaseTile.X, touchTile.X);
        Assert.Equal(increaseTile.X, decreaseTile.X);
        Assert.Equal(increaseTile.Y, touchTile.Y);
        Assert.Equal(increaseTile.Y, decreaseTile.Y);
        Assert.Equal(increaseTile.TileWidth, touchTile.TileWidth);
        Assert.Equal(increaseTile.TileWidth, decreaseTile.TileWidth);
        Assert.Equal(increaseTile.TileHeight, touchTile.TileHeight);
        Assert.Equal(increaseTile.TileHeight, decreaseTile.TileHeight);

        // Dragging any one cluster member (here, decrease — none of the
        // three are checkbox group-selected) must move its two siblings
        // along with it, or the cluster would visually fall apart.
        var moving = viewModel.PrepareGroupDrag(decreaseTile);
        Assert.Equal(3, moving.Count);
        Assert.Contains(moving, tile => tile.ControlId == increase);
        Assert.Contains(moving, tile => tile.ControlId == touch);
        Assert.Contains(moving, tile => tile.ControlId == decrease);
        Assert.False(increaseTile.IsGroupSelected);
        Assert.False(touchTile.IsGroupSelected);
        Assert.False(decreaseTile.IsGroupSelected);

        var origins = moving.ToDictionary(tile => tile.ControlId, tile => (tile.X, tile.Y));
        var originalX = decreaseTile.X;
        var originalY = decreaseTile.Y;
        viewModel.MoveControlGroup(decreaseTile, originalX + 40, originalY + 25, origins);

        Assert.Equal(originalX + 40, decreaseTile.X);
        Assert.Equal(originalY + 25, decreaseTile.Y);
        Assert.Equal(decreaseTile.X, increaseTile.X);
        Assert.Equal(decreaseTile.Y, increaseTile.Y);
        Assert.Equal(decreaseTile.X, touchTile.X);
        Assert.Equal(decreaseTile.Y, touchTile.Y);
    }

    [Fact]
    public async Task Layout_content_bounds_reflect_current_tile_positions_not_a_stale_grown_surface()
    {
        var runtime = new FakeRuntime
        {
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [
                    new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1),
                    new ControllerControlPlacement("b", 0, 1, 60, 40, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        foreach (var id in new[] { "a", "b" })
        {
            runtime.EmitControl(new RuntimeControlUpdate(
                "controller", id, id.ToUpperInvariant(), false, false, "Unassigned", 0, 0, IsSnapshot: true));
        }
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "Test controller", "Layer 1", "Ready", "Ready"));

        // Simulate a since-undone larger arrangement: EnsureLayoutSurface only
        // ever grows (see its own doc comment), so a much bigger historical
        // extent stays remembered even after the tiles below are back in a
        // small, compact area.
        viewModel.EnsureLayoutSurface(2000, 1500);
        Assert.Equal(2000, viewModel.LayoutSurfaceWidth);
        Assert.Equal(1500, viewModel.LayoutSurfaceHeight);

        // Tile "a" is at X:20 Width:134 (default) -> right edge 154; "b" is at
        // X:60 -> right edge 194. The tightest content box is the larger of
        // the two edges, plus the same +10 margin EnsureLayoutSurface's own
        // callers already use — nowhere near the stale 2000x1500 surface.
        var (contentWidth, contentHeight) = viewModel.GetLayoutContentBounds();
        Assert.True(contentWidth < 300, $"Expected a tight content width, got {contentWidth}");
        Assert.True(contentHeight < 300, $"Expected a tight content height, got {contentHeight}");
    }

    [Fact]
    public async Task ResizeGroupSelectionAsync_resizes_only_selected_squares_clamped_and_saves()
    {
        var runtime = new FakeRuntime
        {
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [
                    new ControllerControlPlacement("a", 0, 0, 0, 0, 1, 1),
                    new ControllerControlPlacement("b", 0, 1, 150, 0, 1, 1),
                    new ControllerControlPlacement("c", 0, 2, 300, 0, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        foreach (var id in new[] { "a", "b", "c" })
        {
            runtime.EmitControl(new RuntimeControlUpdate(
                "controller", id, id.ToUpperInvariant(), false, false, "Unassigned", 0, 0, IsSnapshot: true));
        }
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "Test controller", "Layer 1", "Ready", "Ready"));

        var tileA = viewModel.Controls.Single(item => item.ControlId == "a");
        var tileB = viewModel.Controls.Single(item => item.ControlId == "b");
        var tileC = viewModel.Controls.Single(item => item.ControlId == "c");
        viewModel.SetGroupSelection(tileA, true);
        viewModel.SetGroupSelection(tileB, true);

        // Requested height (900) exceeds the shared clamp
        // (MinimumTileSize..400) already used by the per-tile resize Thumb
        // and ApplyControlPlacement — it must come out clamped, not raw.
        await viewModel.ResizeGroupSelectionAsync(220, 900);

        Assert.Equal(220, tileA.TileWidth);
        Assert.Equal(400, tileA.TileHeight);
        Assert.Equal(220, tileB.TileWidth);
        Assert.Equal(400, tileB.TileHeight);
        Assert.NotEqual(220, tileC.TileWidth);
        Assert.NotEqual(400, tileC.TileHeight);
        Assert.Equal(1, runtime.SaveCalls);
    }

    [Fact]
    public async Task ResizeGroupSelectionAsync_excludes_an_encoder_cluster_tile_even_as_the_sole_selection()
    {
        const string touch = "winmm-midi:channel-1:note-16";
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiFireV1Id, "Akai Fire pads — full RGB",
                ["Default"], new HashSet<string>(StringComparer.Ordinal), SupportsFreeformColor: true),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: FL STUDIO FIRE", "Layer 1", "Ready", "Ready",
            ActiveControllerProviderId: WinMmMidiInputProvider.ProviderId));
        runtime.EmitControl(new RuntimeControlUpdate(
            "fire", touch, "Volume touch", false, false, "Unassigned", 0, 0, IsSnapshot: true));

        var touchTile = viewModel.Controls.Single(item => item.ControlId == touch);
        viewModel.SelectControl(touchTile);
        Assert.True(touchTile.IsEncoderClusterMember);
        var originalWidth = touchTile.TileWidth;
        var originalHeight = touchTile.TileHeight;

        await viewModel.ResizeGroupSelectionAsync(300, 300);

        Assert.Equal(originalWidth, touchTile.TileWidth);
        Assert.Equal(originalHeight, touchTile.TileHeight);
        Assert.Equal(0, runtime.SaveCalls);
    }

    [Fact]
    public async Task Apc_fader_directions_share_one_moving_cap_and_calibration_is_saved()
    {
        const string increase = "winmm-midi:channel-1:cc-48:increase";
        const string decrease = "winmm-midi:channel-1:cc-48:decrease";
        var runtime = new FakeRuntime
        {
            ExistingLayout = new ControllerLayoutWorkspace(
                2, 1, SnapToGrid: true,
                [
                    new ControllerControlPlacement(increase, 0, 0, 0, 0, 1, 1),
                    new ControllerControlPlacement(decrease, 0, 1, 144, 0, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: APC MINI", "Layer 1", "Ready", "Ready",
            ActiveControllerProviderId: WinMmMidiInputProvider.ProviderId));
        runtime.EmitControl(new RuntimeControlUpdate(
            "apc", increase, "Fader 1 ↑", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "apc", decrease, "Fader 1 ↓", false, false, "Unassigned", 0, 0, IsSnapshot: true));

        var hotspot = Assert.Single(viewModel.ControllerPhotoHotspots);
        Assert.True(hotspot.IsLinear);

        runtime.EmitControl(new RuntimeControlUpdate(
            "apc", increase, "Fader 1 ↑", true, false, "Unassigned", 1, 1,
            AnalogRawValue: 0, AnalogDelta: 1));
        var bottom = hotspot.IndicatorTop;
        runtime.EmitControl(new RuntimeControlUpdate(
            "apc", increase, "Fader 1 ↑", true, false, "Unassigned", 1, 2,
            AnalogRawValue: 127, AnalogDelta: 1));
        var top = hotspot.IndicatorTop;

        Assert.True(top < bottom);
        Assert.Equal(8, top);
        Assert.Equal(202, bottom);

        var tile = viewModel.Controls.Single(item => item.ControlId == increase);
        viewModel.SelectControl(tile);
        runtime.EmitControl(new RuntimeControlUpdate(
            "apc", increase, "Fader 1 ↑", true, false, "Unassigned", 1, 3,
            AnalogRawValue: 11, AnalogDelta: 1));
        await viewModel.CaptureSelectedAnalogMinimumAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "apc", increase, "Fader 1 ↑", true, false, "Unassigned", 1, 4,
            AnalogRawValue: 63, AnalogDelta: 1));
        await viewModel.CaptureSelectedAnalogCenterAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "apc", increase, "Fader 1 ↑", true, false, "Unassigned", 1, 5,
            AnalogRawValue: 119, AnalogDelta: 1));
        await viewModel.CaptureSelectedAnalogMaximumAsync();

        var saved = Assert.IsType<ControllerLayoutWorkspace>(runtime.UpdatedLayout);
        Assert.All(saved.Controls, placement =>
        {
            Assert.Equal(11, placement.AnalogRawAtMinimum);
            Assert.Equal(63, placement.AnalogRawAtCenter);
            Assert.Equal(119, placement.AnalogRawAtMaximum);
        });
    }

    [Fact]
    public async Task Assigned_square_opens_existing_action_and_drag_swap_is_saved()
    {
        var existing = new ControllerActionAssignment(
            "A long macro",
            ControllerActionSequenceDefinition.Once(
                "A long macro",
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Text,
                    Value = "keep every character",
                }),
            new ControllerActionSequenceDefinition());
        var runtime = new FakeRuntime { ExistingAction = existing };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "a", "A", false, false, "A long macro", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "b", "B", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "c", "C", false, false, "Unassigned", 0, 0, IsSnapshot: true));

        var source = viewModel.Controls[0];
        var target = viewModel.Controls[2];
        viewModel.SelectControl(source);

        Assert.True(viewModel.SelectedControlHasAssignment);
        Assert.Equal("Edit assignment…", viewModel.AssignmentEditorButtonLabel);
        Assert.Same(existing, viewModel.GetSelectedControllerAction());

        await viewModel.ReorderControlsAsync(source, target);

        Assert.Equal(["c", "b", "a"], viewModel.Controls.Select(tile => tile.ControlId));
        Assert.Equal(["c", "b", "a"], runtime.LastReorderedControls);
        Assert.Equal(1, runtime.SaveCalls);
        Assert.Contains("saved", viewModel.MappingStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Freeform_positions_sizes_and_workspace_settings_are_saved()
    {
        var runtime = new FakeRuntime
        {
            ExistingLayout = new ControllerLayoutWorkspace(
                8,
                6,
                SnapToGrid: false,
                [
                    new ControllerControlPlacement("a", 0, 0, 25, 35, 1, 1),
                    new ControllerControlPlacement("b", 0, 1, null, null, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "a", "A", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "b", "B", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "Test controller", "Layer 1", "Ready", "Ready"));

        var tile = viewModel.Controls[0];
        Assert.Equal(25, tile.X);
        Assert.Equal(35, tile.Y);
        viewModel.MoveControl(tile, 1403, 917);
        viewModel.ResizeControl(tile, -44, -38);
        viewModel.LayoutColumns = 11;
        viewModel.LayoutRows = 9;

        await viewModel.SaveControllerLayoutAsync();

        var saved = Assert.IsType<ControllerLayoutWorkspace>(runtime.UpdatedLayout);
        Assert.Equal(11, saved.GridColumns);
        Assert.Equal(9, saved.GridRows);
        var placement = saved.Controls.Single(control => control.ControlId == "a");
        Assert.Equal(1403, placement.X);
        Assert.Equal(917, placement.Y);
        Assert.True(viewModel.LayoutSurfaceWidth > 1403);
        Assert.True(viewModel.LayoutSurfaceHeight > 917);
        Assert.True(placement.Width < 1);
        Assert.True(placement.Height < 1);
        Assert.Equal(1, runtime.SaveCalls);
    }

    [Fact]
    public async Task Selected_squares_move_as_a_group_and_marquee_selection_is_exact()
    {
        var runtime = new FakeRuntime
        {
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [
                    new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1),
                    new ControllerControlPlacement("b", 0, 1, 180, 30, 1, 1),
                    new ControllerControlPlacement("c", 1, 0, 20, 180, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        foreach (var id in new[] { "a", "b", "c" })
        {
            runtime.EmitControl(new RuntimeControlUpdate(
                "controller", id, id.ToUpperInvariant(), false, false, "Unassigned", 0, 0, IsSnapshot: true));
        }
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "Test controller", "Layer 1", "Ready", "Ready"));

        viewModel.SelectControlsInArea(0, 0, 400, 150);
        Assert.Equal(2, viewModel.GroupSelectedCount);
        Assert.True(viewModel.Controls[0].IsGroupSelected);
        Assert.True(viewModel.Controls[1].IsGroupSelected);
        Assert.False(viewModel.Controls[2].IsGroupSelected);

        var anchor = viewModel.Controls[0];
        var moving = viewModel.PrepareGroupDrag(anchor);
        var origins = moving.ToDictionary(tile => tile.ControlId, tile => (tile.X, tile.Y));
        viewModel.MoveControlGroup(anchor, 100, 110, origins);

        Assert.Equal(100, viewModel.Controls[0].X);
        Assert.Equal(110, viewModel.Controls[0].Y);
        Assert.Equal(260, viewModel.Controls[1].X);
        Assert.Equal(110, viewModel.Controls[1].Y);
        Assert.Equal(20, viewModel.Controls[2].X);
        Assert.Equal(180, viewModel.Controls[2].Y);

        viewModel.SelectControlsInArea(0, 230, 170, 320);
        Assert.Equal(["c"], viewModel.Controls.Where(tile => tile.IsGroupSelected).Select(tile => tile.ControlId));
    }

    [Fact]
    public async Task Square_colors_apply_only_to_addressable_controls_and_persist_in_the_layout()
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                "test-rgb", "Test RGB", ["Default", "Red", "Purple"],
                new HashSet<string>(["a"], StringComparer.Ordinal)),
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [
                    new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1),
                    new ControllerControlPlacement("b", 0, 1, 180, 30, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        foreach (var id in new[] { "a", "b" })
        {
            runtime.EmitControl(new RuntimeControlUpdate(
                "controller", id, id.ToUpperInvariant(), false, false, "Unassigned", 0, 0, IsSnapshot: true));
        }
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "Test controller", "Layer 1", "Ready", "Ready"));
        viewModel.SelectAllControls();
        viewModel.SelectedTileColor = viewModel.TileColorChoices.Single(color => color.Key == "Purple");

        await viewModel.ApplySelectedTileColorAsync();

        Assert.Equal("Purple", viewModel.Controls.Single(tile => tile.ControlId == "a").ColorKey);
        Assert.Equal("Default", viewModel.Controls.Single(tile => tile.ControlId == "b").ColorKey);
        var saved = Assert.IsType<ControllerLayoutWorkspace>(runtime.UpdatedLayout);
        Assert.Equal("Purple", saved.Controls.Single(placement => placement.ControlId == "a").ColorKey);
        Assert.Equal("Default", saved.Controls.Single(placement => placement.ControlId == "b").ColorKey);
        Assert.Contains("Kept 1 non-addressable control unchanged", viewModel.MappingStatus);
        Assert.Equal(1, runtime.SaveCalls);
    }

    [Fact]
    public async Task Coloring_an_explicitly_checked_non_addressable_group_does_not_fall_back_to_an_unrelated_focused_tile()
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                "test-rgb", "Test RGB", ["Default", "Red", "Purple"],
                new HashSet<string>(["a"], StringComparer.Ordinal)), // only "a" is addressable; "b" never is
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [
                    new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1),
                    new ControllerControlPlacement("b", 0, 1, 180, 30, 1, 1),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        foreach (var id in new[] { "a", "b" })
        {
            runtime.EmitControl(new RuntimeControlUpdate(
                "controller", id, id.ToUpperInvariant(), false, false, "Unassigned", 0, 0, IsSnapshot: true));
        }
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "Test controller", "Layer 1", "Ready", "Ready"));
        var tileA = viewModel.Controls.Single(tile => tile.ControlId == "a");
        var tileB = viewModel.Controls.Single(tile => tile.ControlId == "b");
        // "a" (addressable) was individually clicked earlier and still holds focus,
        // exactly like a tile left selected from prior testing. The user now
        // explicitly checks only "b" (not addressable) as a group and tries to
        // color that group — "a" must not be silently recolored instead.
        viewModel.SelectControl(tileA);
        viewModel.SetGroupSelection(tileB, true);
        viewModel.SelectedTileColor = viewModel.TileColorChoices.Single(color => color.Key == "Purple");

        await viewModel.ApplySelectedTileColorAsync();

        Assert.Equal("Default", tileA.ColorKey);
        Assert.Equal("Default", tileB.ColorKey);
        Assert.Contains("None of the 1 selected squares are color-addressable", viewModel.MappingStatus);
        Assert.Equal(0, runtime.SaveCalls);
    }

    [Fact]
    public async Task Color_picker_contains_only_the_confirmed_models_verified_palette()
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiApcMiniV1Id,
                "APC MINI v1 — off, green, red, and amber",
                ["Default", "Green", "Red", "Amber"],
                new HashSet<string>(["a"], StringComparer.Ordinal)),
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1, "Blue")]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "a", "A", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: APC MINI", "Layer 1", "Ready", "Ready"));

        Assert.True(viewModel.HasSupportedTileColors);
        Assert.Equal(["Default", "Green", "Red", "Amber"],
            viewModel.TileColorChoices.Select(color => color.Key));
        Assert.DoesNotContain(viewModel.TileColorChoices, color => color.Key == "Blue");
        Assert.Equal("Default", Assert.Single(viewModel.Controls).ColorKey);
    }

    [Fact]
    public async Task Global_lighting_device_offers_one_palette_and_applies_one_color_to_the_whole_device()
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                "logitech-g13-global-rgb",
                "G13 lighting — one whole-device RGB zone; individual key colors are unavailable",
                ["Default", "Red", "Green", "Blue"],
                new HashSet<string>(StringComparer.Ordinal),
                ControllerLightingTopology.Global),
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [
                    new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1, "Blue"),
                    new ControllerControlPlacement("b", 0, 1, 180, 30, 1, 1, "Blue"),
                ]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "a", "A", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "b", "B", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "Logitech G13", "Layer 1", "Ready", "Ready"));

        Assert.True(viewModel.HasSupportedTileColors);
        Assert.True(viewModel.UsesGlobalTileColor);
        Assert.Equal(["Default", "Red", "Green", "Blue"],
            viewModel.TileColorChoices.Select(color => color.Key));
        Assert.Contains("whole-device RGB zone", viewModel.TileColorAvailabilityLabel);
        Assert.Equal("Preview whole device", viewModel.TileColorApplyButtonLabel);
        Assert.Equal("Sync G13 lighting", viewModel.TileColorSyncButtonLabel);
        Assert.Contains("only to the confirmed Logitech G13", viewModel.TileColorSyncToolTip);
        Assert.All(viewModel.Controls, tile => Assert.Equal("Blue", tile.ColorKey));

        viewModel.SelectedTileColor = viewModel.TileColorChoices.Single(color => color.Key == "Red");
        await viewModel.ApplySelectedTileColorAsync();
        Assert.All(viewModel.Controls, tile => Assert.Equal("Red", tile.ColorKey));
        Assert.All(Assert.IsType<ControllerLayoutWorkspace>(runtime.UpdatedLayout).Controls,
            placement => Assert.Equal("Red", placement.ColorKey));

        viewModel.SyncTileColorsToHardware();
        Assert.Equal("Red", Assert.Single(runtime.LastLedColors).Value);
        Assert.Contains("whole G13", viewModel.MappingStatus);
    }

    [Fact]
    public async Task Freeform_color_capability_offers_a_custom_color_that_applies_and_syncs()
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiFireV1Id,
                "Akai Fire pads — full RGB",
                DeviceAwareControllerRuntime.AkaiFireColorKeys,
                new HashSet<string>(["a"], StringComparer.Ordinal),
                SupportsFreeformColor: true),
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1, "Default")]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "a", "A", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: Akai Fire", "Layer 1", "Ready", "Ready"));

        Assert.True(viewModel.HasFreeformColorSupport);
        viewModel.SelectCustomColor("#FF6A00");
        Assert.Equal("#FF6A00", viewModel.SelectedTileColor.Key);

        var tile = Assert.Single(viewModel.Controls);
        tile.IsGroupSelected = true;
        await viewModel.ApplySelectedTileColorAsync();
        Assert.Equal("#FF6A00", tile.ColorKey);
        Assert.Equal("#FF6A00",
            Assert.IsType<ControllerLayoutWorkspace>(runtime.UpdatedLayout).Controls.Single(c => c.ControlId == "a").ColorKey);

        viewModel.SyncTileColorsToHardware();
        Assert.Equal("#FF6A00", runtime.LastLedColors["a"]);
    }

    [Fact]
    public async Task A_picked_but_unapplied_custom_color_survives_an_unrelated_runtime_state_event()
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiFireV1Id,
                "Akai Fire pads — full RGB",
                DeviceAwareControllerRuntime.AkaiFireColorKeys,
                new HashSet<string>(["a"], StringComparer.Ordinal),
                SupportsFreeformColor: true),
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1, "Default")]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "a", "A", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: Akai Fire", "Layer 1", "Ready", "Ready"));

        viewModel.SelectCustomColor("#22AAFF");
        Assert.Equal("#22AAFF", viewModel.SelectedTileColor.Key);

        // An unrelated status update reaching Runtime_OnStateChanged (e.g. a bank
        // refresh or mode change unrelated to color) must not silently discard an
        // armed-but-not-yet-applied custom color; see D-019 for the same "stomping"
        // bug class in the per-tab zoom feature.
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: Akai Fire", "Layer 1", "Some unrelated status", "Ready"));

        Assert.Equal("#22AAFF", viewModel.SelectedTileColor.Key);
    }

    [Fact]
    public async Task Non_freeform_controller_never_offers_a_custom_color_button()
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                MidiControllerModelCatalog.AkaiApcMiniV1Id,
                "APC MINI v1 — off, green, red, and amber",
                ["Default", "Green", "Red", "Amber"],
                new HashSet<string>(["a"], StringComparer.Ordinal)),
            ExistingLayout = new ControllerLayoutWorkspace(
                8, 6, SnapToGrid: false,
                [new ControllerControlPlacement("a", 0, 0, 20, 30, 1, 1, "Default")]),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "controller", "a", "A", false, false, "Unassigned", 0, 0, IsSnapshot: true));
        runtime.EmitState(new RuntimeState(
            true, false, "Confirmed", "MIDI: APC MINI", "Layer 1", "Ready", "Ready"));

        Assert.False(viewModel.HasFreeformColorSupport);
        viewModel.SelectCustomColor("#FF6A00");
        Assert.NotEqual("#FF6A00", viewModel.SelectedTileColor.Key);
    }

    [Theory]
    [InlineData("akai-fire-v1", true)]
    [InlineData("akai-apc-mini-v1", true)]
    [InlineData("logitech-g13-global-rgb", false)]
    [InlineData("razer-tartarus-original", false)]
    public async Task HasAnimatableLighting_reflects_whether_a_grid_adapter_is_registered(string modelId, bool expected)
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                modelId, "Test capability", ["Default"], new HashSet<string>(StringComparer.Ordinal)),
            ExistingLayout = new ControllerLayoutWorkspace(8, 6, SnapToGrid: false, []),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitState(new RuntimeState(true, false, "Confirmed", "Test device", "Layer 1", "Ready", "Ready"));

        Assert.Equal(expected, viewModel.HasAnimatableLighting);
    }

    [Fact]
    public async Task HasAnimatableLighting_is_false_when_unconfirmed()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasAnimatableLighting);
    }

    [Theory]
    [InlineData("akai-fire-v1", true)]
    [InlineData("akai-apc-mini-v1", false)]
    [InlineData("logitech-g13-global-rgb", false)]
    [InlineData("razer-tartarus-original", false)]
    public async Task HasOledDisplay_is_true_only_for_a_confirmed_fire(string modelId, bool expected)
    {
        var runtime = new FakeRuntime
        {
            LedCapability = new ControllerLedColorCapability(
                modelId, "Test capability", ["Default"], new HashSet<string>(StringComparer.Ordinal)),
            ExistingLayout = new ControllerLayoutWorkspace(8, 6, SnapToGrid: false, []),
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitState(new RuntimeState(true, false, "Confirmed", "Test device", "Layer 1", "Ready", "Ready"));

        Assert.Equal(expected, viewModel.HasOledDisplay);
    }

    [Fact]
    public async Task HasOledDisplay_is_false_when_unconfirmed()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasOledDisplay);
    }

    [Fact]
    public async Task Deferred_dispatch_preserves_fast_tap_selection_and_a_visible_pulse()
    {
        var runtime = new FakeRuntime();
        var ui = new Queue<Action>();
        var delayed = new Queue<Action>();
        await using var viewModel = new MainViewModel(
            runtime,
            action => ui.Enqueue(action),
            (_, action) => delayed.Enqueue(action));
        await viewModel.InitializeAsync();

        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", true, false,
            "Unassigned", 1, 1));
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", false, false,
            "Unassigned", 0, 2));

        Assert.Single(ui);
        Assert.Empty(viewModel.Controls);
        ui.Dequeue()();

        var tile = Assert.Single(viewModel.Controls);
        Assert.False(tile.IsPressed);
        Assert.True(tile.IsIlluminated);
        Assert.Equal("Numpad 1", viewModel.SelectedControlLabel);
        Assert.Equal("None", viewModel.PressedSummary);
        Assert.Contains("last: release", viewModel.EventSummary, StringComparison.Ordinal);
        Assert.Single(delayed);

        delayed.Dequeue()();
        Assert.False(tile.IsIlluminated);
    }

    [Fact]
    public async Task Deferred_dispatch_preserves_fifo_order_and_truthful_simultaneous_state()
    {
        var runtime = new FakeRuntime();
        var ui = new Queue<Action>();
        var delayed = new Queue<Action>();
        await using var viewModel = new MainViewModel(
            runtime,
            action => ui.Enqueue(action),
            (_, action) => delayed.Enqueue(action));
        await viewModel.InitializeAsync();

        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", true, false,
            "Unassigned", 1, 1));
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-b", "Numpad 2", true, false,
            "Unassigned", 2, 2));
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", false, false,
            "Unassigned", 1, 3));

        ui.Dequeue()();

        Assert.Equal(["Numpad 1", "Numpad 2"], viewModel.Controls.Select(control => control.Label));
        Assert.False(viewModel.Controls[0].IsPressed);
        Assert.True(viewModel.Controls[0].IsIlluminated);
        Assert.True(viewModel.Controls[1].IsPressed);
        Assert.True(viewModel.Controls[1].IsIlluminated);
        Assert.Equal("Numpad 2", viewModel.SelectedControlLabel);
        Assert.Equal("Numpad 2", viewModel.PressedSummary);
        Assert.Contains("simultaneous: 1", viewModel.EventSummary, StringComparison.Ordinal);
        Assert.Contains("last: release", viewModel.EventSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Visual_buffer_compaction_preserves_ordered_edges_and_final_states()
    {
        var buffer = new VisualUpdateBuffer(capacity: 3);
        buffer.Enqueue(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", true, false,
            "Unassigned", 1, 1));
        buffer.Enqueue(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-b", "Numpad 2", true, false,
            "Unassigned", 2, 2));
        buffer.Enqueue(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", false, false,
            "Unassigned", 1, 3));
        buffer.Enqueue(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-b", "Numpad 2", false, false,
            "Unassigned", 0, 4));

        var batch = buffer.Drain();

        Assert.True(batch.WasCompacted);
        Assert.Equal([1, 2, 3, 4], batch.Updates.Select(update => update.AggregateEventCount));
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public async Task Repeat_preserves_selection_and_releases_update_each_simultaneous_visual()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", true, false,
            "Unassigned", 1, 1));
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-b", "Numpad 2", true, false,
            "Unassigned", 2, 2));

        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", true, true,
            "Unassigned", 2, 3));

        Assert.Equal("Numpad 2", viewModel.SelectedControlLabel);
        Assert.Contains("last: repeat", viewModel.EventSummary, StringComparison.Ordinal);
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", false, false,
            "Unassigned", 1, 4));
        Assert.False(viewModel.Controls.Single(control => control.Label == "Numpad 1").IsPressed);
        Assert.True(viewModel.Controls.Single(control => control.Label == "Numpad 2").IsPressed);
        Assert.Equal("Numpad 2", viewModel.PressedSummary);
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-b", "Numpad 2", false, false,
            "Unassigned", 0, 5));
        Assert.Equal("None", viewModel.PressedSummary);
        Assert.Contains("last: release", viewModel.EventSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assignment_requires_a_selected_control_then_uses_safe_output_range()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        Assert.Equal("F13", viewModel.HarmlessOutputKeys[0]);
        Assert.Equal("F24", viewModel.HarmlessOutputKeys[^1]);

        viewModel.AssignMapping();
        Assert.Equal(0, runtime.AssignCalls);

        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "raw-input:key-a", "Numpad 1", true, false,
            "Unassigned", 1, 1));
        viewModel.SelectedOutputKey = "F20";
        viewModel.AssignMapping();

        Assert.Equal(1, runtime.AssignCalls);
        Assert.Equal(("raw-input:key-a", "F20"), runtime.LastAssignment);
        Assert.Equal("Hold F20 until release", viewModel.Controls[0].Action);
    }

    [Fact]
    public async Task Expanded_keyboard_assignment_is_forwarded_with_its_chord_and_tile_summary()
    {
        var runtime = new FakeRuntime();
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-1", "g13:g1", "G1", true, false,
            "Unassigned", 1, 1));
        var assignment = KeyboardMappingAssignment.PressOnce(
            "Save as — Ctrl + Shift + S", ["CTRL", "SHIFT", "S"]);

        viewModel.AssignKeyboardMapping(assignment);

        Assert.Equal(1, runtime.KeyboardAssignCalls);
        Assert.Equal("g13:g1", runtime.LastKeyboardAssignment.Control);
        Assert.Same(assignment, runtime.LastKeyboardAssignment.Assignment);
        Assert.Equal(assignment.Name, viewModel.Controls[0].Action);
    }

    [Fact]
    public async Task Identification_clears_stale_selection_and_suppresses_focused_wpf_keys()
    {
        var runtime = new FakeRuntime
        {
            Available = [new ControllerChoice("session-1", "persistent-1", "Spare numpad", "PortBound")]
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();
        runtime.EmitControl(new RuntimeControlUpdate(
            "persistent-old", "raw-input:key-a", "Old key", true, false,
            "Send F24", 1, 1));
        Assert.Equal("Old key", viewModel.SelectedControlLabel);

        viewModel.SelectedDevice = viewModel.Devices[0];
        viewModel.BeginIdentification();

        Assert.True(viewModel.IsIdentificationCaptureActive);
        Assert.True(MainWindow.ShouldSuppressFocusedControlKeyInput(viewModel));
        Assert.Empty(viewModel.Controls);
        Assert.Equal("None", viewModel.SelectedControlLabel);

        viewModel.ConfirmController();
        Assert.False(viewModel.IsIdentificationCaptureActive);
        Assert.False(MainWindow.ShouldSuppressFocusedControlKeyInput(viewModel));
    }

    [Fact]
    public async Task Failed_reidentification_immediately_reflects_the_runtime_capture_state()
    {
        var runtime = new FakeRuntime
        {
            Available = [new ControllerChoice("session-1", "persistent-1", "Spare numpad", "PortBound")]
        };
        var queuedUi = new Queue<Action>();
        await using var viewModel = new MainViewModel(runtime, action => queuedUi.Enqueue(action));
        await viewModel.InitializeAsync();
        viewModel.SelectedDevice = viewModel.Devices[0];
        viewModel.BeginIdentification();
        Assert.True(viewModel.IsIdentificationCaptureActive);

        runtime.BeginResult = RuntimeOperation.Failed("The device is no longer present.");
        viewModel.BeginIdentification();

        Assert.False(runtime.IsIdentificationCaptureActive);
        Assert.False(viewModel.IsIdentificationCaptureActive);
        Assert.False(viewModel.CanConfirmController);
        Assert.Contains("no longer present", viewModel.IdentificationStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Emergency_stop_preserves_a_runtime_output_safety_failure_message()
    {
        var runtime = new FakeRuntime
        {
            EmergencyResult = RuntimeOperation.Failed("Needs attention: output release was rejected.")
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        var result = viewModel.EmergencyStop("test");

        Assert.False(result.Succeeded);
        Assert.False(viewModel.IsOutputStateConfirmedSafe);
        Assert.Equal(runtime.EmergencyResult.Message, viewModel.Status);
        Assert.True(viewModel.IsRehearsal);
        Assert.Equal("None", viewModel.PressedSummary);
    }

    [Fact]
    public async Task Refresh_finishes_with_the_enumeration_result_instead_of_a_stale_busy_message()
    {
        var runtime = new FakeRuntime
        {
            Available = [new ControllerChoice("session-1", "persistent-1", "Spare numpad", "PortBound")]
        };
        await using var viewModel = new MainViewModel(runtime, action => action());
        await viewModel.InitializeAsync();

        viewModel.RefreshDevices();

        Assert.Contains("1 controller device", viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("Refreshing", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Pressed_tile_automation_name_reports_live_pressed_and_released_state()
    {
        var tile = new ControlTileViewModel { ControlId = "raw-input:key-a", Label = "Numpad 1" };

        tile.IsPressed = true;
        Assert.Contains("Pressed", tile.AutomationName, StringComparison.Ordinal);

        tile.IsPressed = false;
        Assert.Contains("Released", tile.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void Presentation_modes_have_distinct_usable_minimums_and_controller_only_is_subcompact()
    {
        var full = WindowPresentationPolicy.Get(WindowPresentationMode.Full);
        var compact = WindowPresentationPolicy.Get(WindowPresentationMode.Compact);
        var controllerOnly = WindowPresentationPolicy.Get(WindowPresentationMode.ControllerOnly);

        Assert.True(compact.MinimumWidth < full.MinimumWidth);
        Assert.True(controllerOnly.MinimumWidth < compact.MinimumWidth);
        Assert.True(controllerOnly.MinimumHeight < full.MinimumHeight);
        Assert.True(controllerOnly.DefaultWidth >= controllerOnly.MinimumWidth);
        Assert.True(controllerOnly.DefaultHeight >= controllerOnly.MinimumHeight);
    }

    [Fact]
    public void Theme_selection_prioritizes_an_already_active_high_contrast_setting()
    {
        Assert.Equal("Themes/HighContrast.xaml", ThemeService.SelectResourcePath(light: false, highContrast: true));
        Assert.Equal("Themes/HighContrast.xaml", ThemeService.SelectResourcePath(light: true, highContrast: true));
        Assert.Equal("Themes/Dark.xaml", ThemeService.SelectResourcePath(light: false, highContrast: false));
        Assert.Equal("Themes/Light.xaml", ThemeService.SelectResourcePath(light: true, highContrast: false));
    }

    [Fact]
    public void Background_notification_is_truthful_for_armed_rehearsal_and_unconfirmed_states()
    {
        Assert.Contains("notification area", MainWindow.BackgroundNotificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Show Tappy", MainWindow.BackgroundNotificationMessage, StringComparison.Ordinal);
        Assert.Contains("Emergency stop", MainWindow.BackgroundNotificationMessage, StringComparison.Ordinal);
        Assert.Contains("Exit Tappy", MainWindow.BackgroundNotificationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Mappings remain active", MainWindow.BackgroundNotificationMessage, StringComparison.Ordinal);
    }

    private sealed class FakeRuntime : IControllerRuntime
    {
        private bool _isRehearsal;

        public event EventHandler? DevicesChanged;
        public event EventHandler<RuntimeControlUpdate>? ControlChanged;
        public event EventHandler<RuntimeState>? StateChanged;

        public IReadOnlyList<ControllerChoice> Available { get; init; } = [];
        public IReadOnlyList<ControllerChoice> Devices => Available;
        public bool IsRehearsal
        {
            get => _isRehearsal;
            set
            {
                if (!value && RefuseNormalMode)
                {
                    _isRehearsal = true;
                    StateChanged?.Invoke(this, new RuntimeState(
                        false,
                        false,
                        "Choose and identify a controller.",
                        "No controller confirmed",
                        "Layer 1",
                        "Tappy could not confirm a safe output state. Rehearsal Mode remains on.",
                        "Needs attention: restart Tappy before rearming.",
                        "Effective: Needs attention (fail-open)"));
                    return;
                }

                _isRehearsal = value;
            }
        }

        public bool CanConfirmController { get; private set; }
        public bool IsIdentificationCaptureActive { get; private set; }
        public bool IsOutputStateConfirmedSafe { get; private set; } = true;
        public bool RefuseNormalMode { get; init; }
        public RuntimeOperation BeginResult { get; set; } = RuntimeOperation.Ok("Press and release one control.");
        public RuntimeState? InitializeState { get; init; }
        public int BeginCalls { get; private set; }
        public ControllerChoice? LastCandidate { get; private set; }
        public int AssignCalls { get; private set; }
        public (string Control, string Output) LastAssignment { get; private set; }
        public int KeyboardAssignCalls { get; private set; }
        public (string Control, KeyboardMappingAssignment? Assignment) LastKeyboardAssignment { get; private set; }
        public ControllerActionAssignment? ExistingAction { get; init; }
        public ControllerAssignmentTarget? LastAssignmentTarget { get; private set; }
        public bool RejectAssignmentTarget { get; init; }
        public Task<RuntimeOperation>? PendingSave { get; init; }
        public ControllerLayoutWorkspace? ExistingLayout { get; init; }
        public ControllerLedColorCapability? LedCapability { get; init; }
        public ControllerLayoutWorkspace? UpdatedLayout { get; private set; }
        public IReadOnlyDictionary<string, string> LastLedColors { get; private set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public IReadOnlyList<string> LastReorderedControls { get; private set; } = [];
        public int SaveCalls { get; private set; }
        public RuntimeOperation EmergencyResult { get; init; } = RuntimeOperation.Ok("Emergency stop completed.");

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (InitializeState is not null)
            {
                StateChanged?.Invoke(this, InitializeState);
            }

            return Task.CompletedTask;
        }

        public void RefreshDevices() => DevicesChanged?.Invoke(this, EventArgs.Empty);

        public RuntimeOperation BeginIdentification(ControllerChoice device)
        {
            BeginCalls++;
            LastCandidate = device;
            IsIdentificationCaptureActive = BeginResult.Succeeded;
            CanConfirmController = false;
            return BeginResult;
        }

        public RuntimeOperation ConfirmController()
        {
            IsIdentificationCaptureActive = false;
            CanConfirmController = false;
            return RuntimeOperation.Ok("Controller confirmed.");
        }

        public RuntimeOperation AssignMapping(string controlId, string outputKey)
        {
            AssignCalls++;
            LastAssignment = (controlId, outputKey);
            return RuntimeOperation.Ok($"Mapped to {outputKey}.");
        }

        public RuntimeOperation AssignKeyboardMapping(string controlId, KeyboardMappingAssignment assignment)
        {
            KeyboardAssignCalls++;
            LastKeyboardAssignment = (controlId, assignment);
            return RuntimeOperation.Ok($"Mapped to {assignment.Name}.");
        }

        public ControllerActionAssignment? GetControllerAction(string controlId) => ExistingAction;

        public ControllerAssignmentEditContext? BeginAssignmentEdit(string controlId) => new(
            new ControllerAssignmentTarget("session-1", "persistent-1", "raw-hid-g13", "layer-1", controlId, 1),
            ExistingAction);

        public RuntimeOperation AssignKeyboardMapping(ControllerAssignmentTarget target, KeyboardMappingAssignment assignment)
        {
            LastAssignmentTarget = target;
            return RejectAssignmentTarget ? RuntimeOperation.Failed("Controller changed; the draft was not applied.") : RuntimeOperation.Ok("Saved");
        }

        public RuntimeOperation AssignControllerAction(ControllerAssignmentTarget target, ControllerActionAssignment assignment)
        {
            LastAssignmentTarget = target;
            return RejectAssignmentTarget ? RuntimeOperation.Failed("Controller changed; the draft was not applied.") : RuntimeOperation.Ok("Saved");
        }

        public RuntimeOperation ReorderControls(IReadOnlyList<string> orderedControlIds)
        {
            LastReorderedControls = orderedControlIds.ToArray();
            return RuntimeOperation.Ok("Reordered.");
        }

        public ControllerLayoutWorkspace? GetControllerLayout() => ExistingLayout;

        public ControllerLedColorCapability? GetControllerLedColorCapability() => LedCapability;

        public RuntimeOperation SyncControllerLedColors(IReadOnlyDictionary<string, string> colors)
        {
            LastLedColors = new Dictionary<string, string>(colors, StringComparer.Ordinal);
            return RuntimeOperation.Ok("Synchronized the whole G13.");
        }

        public RuntimeOperation UpdateControllerLayout(ControllerLayoutWorkspace workspace)
        {
            UpdatedLayout = workspace;
            return RuntimeOperation.Ok("Layout updated.");
        }

        public Task<RuntimeOperation> SaveProfileAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return PendingSave ?? Task.FromResult(RuntimeOperation.Ok("Saved."));
        }

        public RuntimeOperation EmergencyStop(string reason)
        {
            IsIdentificationCaptureActive = false;
            CanConfirmController = false;
            IsOutputStateConfirmedSafe &= EmergencyResult.Succeeded;
            return EmergencyResult;
        }

        public void EmitControl(RuntimeControlUpdate update) => ControlChanged?.Invoke(this, update);

        public void EmitState(RuntimeState state) => StateChanged?.Invoke(this, state);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
