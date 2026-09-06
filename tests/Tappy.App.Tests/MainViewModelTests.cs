using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Models;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class MainViewModelTests
{
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
            return Task.FromResult(RuntimeOperation.Ok("Saved."));
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
