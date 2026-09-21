using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Output;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Fact]
    public async Task Targus_navigation_mode_and_plain_enter_illuminate_the_existing_photo_face()
    {
        var root = NewRoot();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_05A4&PID_9862#PAD_A");
            var host = new TestHost();
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(descriptor), host, keyboardIsNeutral: () => true),
                output, new AtomicProfileStore(root));
            await using var vm = new MainViewModel(runtime, action => action(), (_, _) => { });
            await vm.InitializeAsync();
            vm.SelectedDevice = vm.Devices.Single();
            vm.BeginIdentification();
            host.EmitKey(41, RawKeyboardFlags.Make);
            host.EmitKey(41, RawKeyboardFlags.Break);
            vm.ConfirmController();
            var reports = new (ushort Scan, ushort VirtualKey, bool Extended)[]
            {
                (0x47, 0x24, true), (0x48, 0x26, true), (0x49, 0x21, true),
                (0x4B, 0x25, true), (0x4D, 0x27, true), (0x4F, 0x23, true),
                (0x50, 0x28, true), (0x51, 0x22, true), (0x52, 0x2D, true),
                (0x53, 0x2E, true), (0x1C, 0x0D, false),
                (0x47, 0x67, false), (0x48, 0x68, false)
            };
            foreach (var report in reports)
            {
                foreach (var control in vm.Controls) { control.IsIlluminated = false; }
                var flags = report.Extended ? RawKeyboardFlags.E0 : RawKeyboardFlags.Make;
                var id = TargusKeypadLayout.Key(report.Scan, report.Extended);
                host.EmitKey(41, flags, report.Scan, report.VirtualKey);
                var face = ControllerPhotoCatalog.Find("raw-input", 0x05A4, 0x9862)!.Hotspots[id];
                var visual = vm.ControllerPhotoHotspots.Single(item => item.VisualId == face.EffectiveVisualId);
                Assert.True(visual.IsIlluminated, id);
                var tile = vm.Controls.Single(item => item.ControlId == id);
                vm.SelectControl(tile);
                Assert.True(visual.IsSelected, id);
                Assert.Equal(19, vm.ControllerPhotoHotspots.Count);
                foreach (var first in vm.Controls)
                {
                    Assert.DoesNotContain(vm.Controls, second => second != first &&
                        first.X < second.X + second.TileWidth && second.X < first.X + first.TileWidth &&
                        first.Y < second.Y + second.TileHeight && second.Y < first.Y + first.TileHeight);
                }
                host.EmitKey(41, flags | RawKeyboardFlags.Break, report.Scan, report.VirtualKey);
                Assert.False(tile.IsPressed);
            }
            Assert.Equal(0, output.Down);
            Assert.Equal(0, output.Up);
            // The owner's pad emits base Enter with either physical mode.
            // It must select the original tall box, never a mode-dependent alias.
            var sharedEnter = vm.Controls.Single(c => c.ControlId == TargusKeypadLayout.Key(0x1C));
            Assert.DoesNotContain(vm.Controls, c => c.ControlId == TargusKeypadLayout.Key(0x1C, true));
            foreach (var mode in new[] { RawKeyboardFlags.Make, RawKeyboardFlags.E0 })
            {
                host.EmitKey(41, mode, 0x47, 0x67);
                host.EmitKey(41, mode | RawKeyboardFlags.Break, 0x47, 0x67);
                host.EmitKey(41, RawKeyboardFlags.Make, 0x1C, 0x0D);
                Assert.True(sharedEnter.IsPressed);
                Assert.True(sharedEnter.IsSelected);
                Assert.DoesNotContain(vm.Controls, c => c.ControlId == TargusKeypadLayout.Key(0x1C, true));
                Assert.Equal("Enter (shared)", sharedEnter.Label);
                Assert.Equal(300, sharedEnter.X);
                Assert.Equal(186, sharedEnter.TileHeight, 6);
                host.EmitKey(41, RawKeyboardFlags.Break, 0x1C, 0x0D);
                Assert.False(sharedEnter.IsPressed);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Two_identical_keypads_have_complete_independent_tabs_and_shared_key_ownership()
    {
        var root = NewRoot();
        try
        {
            var a = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_05A4&PID_9862#PAD_A")
                with
            { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
            var b = DeviceDescriptorSanitizer.CreateKeyboard((nint)42, @"\\?\HID#VID_05A4&PID_9862#PAD_B")
                with
            { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
            var devices = new KeyboardEnumerator(a, b);
            var host = new TestHost();
            var physical = new RecordingKeyboard();
            var output = new OwnedKeyboardOutput(physical);
            var runtimes = new List<DeviceAwareControllerRuntime>();
            await using var workspace = new ControllerWorkspace(slot =>
            {
                var runtime = new DeviceAwareControllerRuntime(
                    new RawInputKeyboardProvider(devices, new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                    output, new AtomicProfileStore(Path.Combine(root, $"device-{slot}")));
                runtimes.Add(runtime);
                return runtime;
            }, action => action(), (_, _) => { }, Path.Combine(root, "tabs.json"));
            await workspace.InitializeAsync();
            var first = workspace.Tabs[0].ViewModel;
            void Confirm(MainViewModel vm, nint handle)
            {
                vm.SelectedDevice = vm.Devices.Single(choice => choice.SessionId == (handle == 41 ? a.SessionId : b.SessionId));
                vm.BeginIdentification();
                host.EmitKey(handle, RawKeyboardFlags.Make);
                host.EmitKey(handle, RawKeyboardFlags.Break);
                vm.ConfirmController();
            }
            Confirm(first, 41);
            Assert.Equal(29, first.Controls.Count);
            Assert.True(first.HasControllerPhoto);
            Assert.Equal("targus-pauk10u", first.ControllerPhotoId);
            var id = TargusKeypadLayout.Key(0x4F);
            Assert.True(runtimes[0].AssignMapping(id, "F24").Succeeded);
            var custom = runtimes[0].GetControllerLayout()!;
            Assert.True(runtimes[0].UpdateControllerLayout(custom with
            { Controls = custom.Controls.Select(c => c.ControlId == id ? c with { X = 333, Y = 222 } : c).ToArray() }).Succeeded);

            var second = (await workspace.AddAsync()).ViewModel;
            second.SelectedDevice = second.Devices.Single(choice => choice.SessionId == a.SessionId);
            second.BeginIdentification();
            Assert.False(second.IsIdentificationCaptureActive);
            Assert.Contains("another tab", second.IdentificationStatus);
            second.SelectedDevice = second.Devices.Single(choice => choice.SessionId == b.SessionId);
            second.BeginIdentification();
            first.IsRehearsal = false;
            Assert.True(first.IsRehearsal);
            Assert.Contains("identification", first.MappingStatus);
            Confirm(second, 42);
            Assert.Equal(29, second.Controls.Count);
            Assert.True(runtimes[1].AssignMapping(id, "F24").Succeeded);
            first.IsRehearsal = false;
            second.IsRehearsal = false;
            host.EmitKey(41, RawKeyboardFlags.Make);
            Assert.True(first.Controls.Single(c => c.ControlId == id).IsPressed);
            Assert.False(second.Controls.Single(c => c.ControlId == id).IsPressed);
            host.EmitKey(42, RawKeyboardFlags.Make);
            Assert.Equal(1, physical.Down); // One physical F24 despite two owners.
            host.EmitKey(41, RawKeyboardFlags.Break);
            Assert.Equal(0, physical.Up);
            Assert.True(second.Controls.Single(c => c.ControlId == id).IsPressed);
            devices.Devices = [a];
            host.EmitRemoval(42);
            Assert.Equal(1, physical.Up);
            Assert.Empty(second.Controls);
            Assert.Single(workspace.VisibleTabs);
            Assert.Same(first, workspace.VisibleTabs[0].ViewModel);
            Assert.Equal(29, first.Controls.Count);
            Assert.False(first.IsRehearsal);
            Assert.True(second.IsRehearsal);
            devices.Devices = [a, b];
            workspace.Poll();
            Assert.Equal(29, second.Controls.Count);
            Assert.True(second.IsRehearsal);
            Assert.Equal(333, runtimes[0].GetControllerLayout()!.Controls.Single(c => c.ControlId == id).X);

            devices.Devices = [];
            host.EmitRemoval(41);
            host.EmitRemoval(42);
            Assert.Empty(workspace.VisibleTabs);
            Assert.Equal(2, workspace.Tabs.Count); // Retained, never disposed or reused.
            Assert.True(first.IsWaitingForReconnect);
            Assert.True(second.IsWaitingForReconnect);
            devices.Devices = [b];
            workspace.Poll();
            Assert.Single(workspace.VisibleTabs);
            Assert.Same(second, workspace.VisibleTabs[0].ViewModel);
            Assert.NotNull(runtimes[1].GetControllerAction(id));
            devices.Devices = [a, b];
            workspace.Poll();
            Assert.Equal(new[] { 1, 2 }, workspace.VisibleTabs.Select(tab => tab.Slot));
            Assert.Same(first, workspace.VisibleTabs[0].ViewModel);
            Assert.Same(second, workspace.VisibleTabs[1].ViewModel);
            Assert.Equal(333, runtimes[0].GetControllerLayout()!.Controls.Single(c => c.ControlId == id).X);
            Assert.True(first.IsRehearsal);
            Assert.True(second.IsRehearsal);

            Assert.True(await workspace.SaveAsync());
            var savedA = await new AtomicProfileStore(Path.Combine(root, "device-1")).LoadAsync();
            var savedB = await new AtomicProfileStore(Path.Combine(root, "device-2")).LoadAsync();
            Assert.Equal(a.PersistentId, Assert.Single(savedA.Controllers).Identity.PersistentId!.Value.Value);
            Assert.Equal(b.PersistentId, Assert.Single(savedB.Controllers).Identity.PersistentId!.Value.Value);
            Assert.Equal("2", File.ReadAllText(Path.Combine(root, "tabs.json")));
            Assert.True(workspace.EmergencyStop("test all tabs").Succeeded);
            workspace.Poll();
            Assert.All(workspace.Tabs, tab =>
            {
                Assert.Empty(tab.ViewModel.Controls);
                Assert.True(tab.ViewModel.IsRehearsal);
                Assert.Null(tab.Runtime.ReservedDevice);
            });
            Assert.Equal(1, physical.Down);
            Assert.Equal(1, physical.Up);
            await workspace.DisposeAsync();
            await using var reopened = new ControllerWorkspace(slot => new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(devices, new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                output, new AtomicProfileStore(Path.Combine(root, $"device-{slot}"))),
                action => action(), (_, _) => { }, Path.Combine(root, "tabs.json"));
            await reopened.InitializeAsync();
            Assert.Equal(2, reopened.Tabs.Count);
            // Both keypads never disconnected from the PC across this simulated Tappy
            // restart, and both slots had a confirmed device saved: both silently
            // reconnect (no re-identification gesture) as soon as each tab's runtime
            // enumerates its remembered device, exactly like a live mid-session
            // reconnect. One poll completes the neutral-state handshake into Active.
            Assert.Equal(2, reopened.VisibleTabs.Count);
            Assert.Contains("Remembered", reopened.Tabs[0].ViewModel.IdentificationStatus);
            // ReservedDevice already reflects the armed-but-not-yet-confirmed candidate
            // (it falls back through _confirmed ?? _candidate ?? _rememberedReconnect);
            // each slot claims its own saved device, never the other slot's.
            Assert.Equal(a.PersistentId, reopened.Tabs[0].Runtime.ReservedDevice?.PersistentId);
            Assert.Equal(b.PersistentId, reopened.Tabs[1].Runtime.ReservedDevice?.PersistentId);
            reopened.Poll();
            Assert.Equal(333, reopened.Tabs[0].Runtime.GetControllerLayout()!.Controls.Single(c => c.ControlId == id).X);
            Assert.Equal(0, reopened.Tabs[1].Runtime.GetControllerLayout()!.Controls.Single(c => c.ControlId == id).X);
            Assert.All(reopened.Tabs, tab => Assert.NotNull(tab.Runtime.GetControllerAction(id)));
            Assert.All(reopened.Tabs, tab => Assert.True(tab.ViewModel.IsRehearsal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Each_device_tab_keeps_its_own_independent_zoom_and_photo_pane_width()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = NewRoot();
            var application = System.Windows.Application.Current;
            var oldThemes = application.Resources.MergedDictionaries.ToArray();
            try
            {
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Tappy;component/Themes/Dark.xaml") });

                var a = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_05A4&PID_9862#ZOOM_A")
                    with
                { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
                var b = DeviceDescriptorSanitizer.CreateKeyboard((nint)42, @"\\?\HID#VID_05A4&PID_9862#ZOOM_B")
                    with
                { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
                var devices = new KeyboardEnumerator(a, b);
                var host = new TestHost();
                var output = new RecordingKeyboard();
                await using var workspace = new ControllerWorkspace(slot => new DeviceAwareControllerRuntime(
                    new RawInputKeyboardProvider(devices, new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                    output, new AtomicProfileStore(Path.Combine(root, $"device-{slot}"))),
                    action => action(), (_, _) => { }, Path.Combine(root, "tabs.json"));
                await workspace.InitializeAsync();
                var first = workspace.Tabs[0].ViewModel;
                void Confirm(MainViewModel vm, nint handle)
                {
                    vm.SelectedDevice = vm.Devices.Single(choice => choice.SessionId == (handle == 41 ? a.SessionId : b.SessionId));
                    vm.BeginIdentification();
                    host.EmitKey(handle, RawKeyboardFlags.Make);
                    host.EmitKey(handle, RawKeyboardFlags.Break);
                    vm.ConfirmController();
                }

                // MainWindow is constructed (and subscribes to the first tab's
                // PropertyChanged) before anything is confirmed, exactly like the
                // real app: MainWindow's constructor runs, then Loaded triggers
                // InitializeAsync/confirmation. Confirming before construction (as
                // an earlier version of this test did) would miss the
                // HasControllerPhoto/layout PropertyChanged notifications entirely.
                var window = new MainWindow(first, new WindowPlacementStore(root), new SessionMarker(root), workspace);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1500, 900));
                content.Arrange(new Rect(0, 0, 1500, 900));
                content.UpdateLayout();
                var scale = (ScaleTransform)window.FindName("ControllerWorkspaceScale");
                var photoColumn = (ColumnDefinition)window.FindName("ControllerPhotoColumn");
                var tabs = (ListBox)window.FindName("DeviceTabs");
                // ShowTabViewModel/the PropertyChanged handler defer applying
                // zoom/pane-width via Dispatcher.BeginInvoke; force it to run now.
                void Pump() => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                Confirm(first, 41);
                Pump();
                var second = (await workspace.AddAsync()).ViewModel;
                Confirm(second, 42);
                Pump();
                tabs.SelectedItem = workspace.Tabs[0];
                Pump();

                Assert.True(first.HasControllerPhoto);
                Assert.Equal(420, photoColumn.Width.Value);

                static MouseWheelEventArgs Wheel(int delta) => new(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent };

                Assert.Equal(1.0, scale.ScaleX);
                window.HandleControllerLayoutZoom(Wheel(120), ctrlHeld: true);
                Assert.True(scale.ScaleX > 1.0);
                var firstZoom = scale.ScaleX;
                await first.SaveControllerLayoutAsync();

                tabs.SelectedItem = workspace.Tabs[1];
                Pump();
                // Tab 2 was never zoomed; switching to it must not inherit tab 1's
                // zoom (the bug: one shared ScaleTransform was applied window-wide).
                Assert.Equal(1.0, scale.ScaleX);

                window.HandleControllerLayoutZoom(Wheel(-120), ctrlHeld: true);
                var secondZoom = scale.ScaleX;
                Assert.True(secondZoom < 1.0);
                second.PhotoPaneWidth = 900;
                await second.SaveControllerLayoutAsync();

                tabs.SelectedItem = workspace.Tabs[0];
                Pump();
                Assert.Equal(firstZoom, scale.ScaleX);
                Assert.Equal(420, first.PhotoPaneWidth);
                Assert.Equal(420, photoColumn.Width.Value);

                tabs.SelectedItem = workspace.Tabs[1];
                Pump();
                Assert.Equal(secondZoom, scale.ScaleX);
                Assert.Equal(900, second.PhotoPaneWidth);
                Assert.Equal(900, photoColumn.Width.Value);

                // Each tab's own runtime-saved layout carries its own values,
                // independent of the other tab's — this is what survives a restart.
                Assert.Equal(firstZoom, workspace.Tabs[0].Runtime.GetControllerLayout()!.Zoom);
                Assert.Equal(secondZoom, workspace.Tabs[1].Runtime.GetControllerLayout()!.Zoom);
                Assert.Equal(420, workspace.Tabs[0].Runtime.GetControllerLayout()!.PhotoPaneWidth);
                Assert.Equal(900, workspace.Tabs[1].Runtime.GetControllerLayout()!.PhotoPaneWidth);
            }
            finally
            {
                application.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in oldThemes) { application.Resources.MergedDictionaries.Add(dictionary); }
                Directory.Delete(root, recursive: true);
            }
        });
    }

    [Fact]
    public async Task Zoom_survives_an_unrelated_control_event_before_the_debounced_save_runs()
    {
        var root = NewRoot();
        try
        {
            var descriptor = G13() with { Grouping = PhysicalDeviceGrouping.WindowsContainerId };
            var host = new TestHost();
            var devices = new G13Enumerator(descriptor);
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(), host, keyboardIsNeutral: () => true),
                new LogitechG13InputProvider(devices, host), output, new AtomicProfileStore(root));
            await using var vm = new MainViewModel(runtime, action => action(), (_, _) => { });
            await vm.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.EmitHid(1);
            host.EmitHid(0);
            Assert.True(runtime.ConfirmController().Succeeded);
            Assert.Equal(1.0, vm.Zoom);

            // Mirrors MainWindow.HandleControllerLayoutZoom: set Zoom, then
            // immediately push it into the runtime (in-memory only) rather than
            // waiting for the debounced disk save.
            vm.Zoom = 1.8;
            vm.SyncLayoutPresentationToRuntime();

            // An unrelated state change (toggling Rehearsal Mode — an ordinary
            // control press/release does not fire StateChanged, only
            // ControlChanged) fires RaiseState, which resyncs this viewmodel's
            // layout from the runtime's current state (Runtime_OnStateChanged ->
            // SynchronizeControllerLayout). Before the fix, Zoom was only pushed
            // into the runtime when the debounce timer fired, so this resync
            // would revert it back to 1.0 first.
            Assert.True(vm.IsRehearsal);
            vm.IsRehearsal = false;
            Assert.Equal(1.8, vm.Zoom);

            await vm.SaveControllerLayoutAsync();
            Assert.Equal(1.8, runtime.GetControllerLayout()!.Zoom);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Targus_reference_key_cannot_be_assigned_and_unknown_models_remain_generic()
    {
        var root = NewRoot();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_05A4&PID_9862#PAD_A");
            var host = new TestHost();
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(descriptor), host, keyboardIsNeutral: () => true),
                new RecordingKeyboard(), new AtomicProfileStore(root));
            await using var vm = new MainViewModel(runtime, action => action(), (_, _) => { });
            await vm.InitializeAsync();
            vm.SelectedDevice = vm.Devices.Single();
            vm.BeginIdentification();
            host.EmitKey(41, RawKeyboardFlags.Make);
            host.EmitKey(41, RawKeyboardFlags.Break);
            vm.ConfirmController();
            vm.SelectControl(vm.Controls.Single(tile => tile.ControlId == TargusKeypadLayout.TripleZeroReference));
            Assert.False(vm.CanAssignSelectedControl);
            Assert.Null(runtime.BeginAssignmentEdit(TargusKeypadLayout.TripleZeroReference));
            Assert.False(runtime.AssignMapping(TargusKeypadLayout.TripleZeroReference, "F24").Succeeded);
            Assert.Null(ControllerPhotoCatalog.Find("raw-input", 0x05A4, 0x9999));
            var photo = ControllerPhotoCatalog.Find("raw-input", 0x05A4, 0x9862)!;
            Assert.All(photo.Hotspots.Values, spot =>
            {
                Assert.InRange(spot.Left, 0, photo.Width - spot.Width);
                Assert.InRange(spot.Top, 0, photo.Height - spot.Height);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Action_cleanup_and_follow_up_ownership_are_scoped_to_their_device_tab()
    {
        var sink = new TabActionSink();
        var first = new SessionActionOutput(sink);
        var second = new SessionActionOutput(sink);
        var request = new ControllerActionOutputRequest("owner", "scope", null!, 0,
            new Tappy.Core.Input.ExecutionAncestry("test"), PreviousOwnerId: "previous");
        Assert.True(first.Start(request));
        Assert.True(second.Start(request));
        Assert.Equal(2, sink.Requests.Count);
        Assert.NotEqual(sink.Requests[0].OwnerId, sink.Requests[1].OwnerId);
        Assert.NotEqual(sink.Requests[0].ScopeId, sink.Requests[1].ScopeId);
        Assert.EndsWith(":previous", sink.Requests[0].PreviousOwnerId);
        Assert.True(first.ReleaseAll());
        Assert.Equal([sink.Requests[0].ScopeId], sink.ReleasedScopes);
        Assert.True(second.ReleaseOwner("owner"));
        Assert.Equal(sink.Requests[1].OwnerId, sink.ReleasedOwner);
        sink.RejectRelease = true;
        Assert.False(second.ReleaseAll());
        sink.RejectRelease = false;
        Assert.True(second.ReleaseAll());
        Assert.Equal(3, sink.ReleasedScopes.Count); // Failed scope retained for retry.
        Assert.Equal(0, sink.ReleaseAllCalls);
    }

    private sealed class TabActionSink : IControllerActionOutput
    {
        public List<ControllerActionOutputRequest> Requests { get; } = [];
        public List<string> ReleasedScopes { get; } = [];
        public string? ReleasedOwner { get; private set; }
        public bool RejectRelease { get; set; }
        public int ReleaseAllCalls { get; private set; }
        public event EventHandler<ControllerActionOutputFault>? Faulted { add { } remove { } }
        public bool Start(ControllerActionOutputRequest request) { Requests.Add(request); return true; }
        public bool ReleaseOwner(string ownerId) { ReleasedOwner = ownerId; return true; }
        public bool ReleaseScope(string scopeId) { ReleasedScopes.Add(scopeId); return !RejectRelease; }
        public bool ReleaseAll() { ReleaseAllCalls++; return true; }
    }
}
