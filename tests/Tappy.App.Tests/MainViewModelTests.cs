using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;

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

        public Task<RuntimeOperation> SaveProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RuntimeOperation.Ok("Saved."));

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
