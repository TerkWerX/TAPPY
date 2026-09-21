using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Windows.Input;
using Tappy.Windows.Output;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Production_device_tabs_wire_the_Tartarus_indicator_output(int slot)
    {
        // Exercise the same composition root as App, without initializing native
        // providers, touching profiles, or sending hardware commands.
        await using var factory = new ControllerSessionFactory(new TartarusTestLifecycle());
        await using var runtime = factory.Create(slot);
        var field = typeof(DeviceAwareControllerRuntime).GetField("_tartarusIndicatorOutput",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.IsType<NativeRazerTartarusIndicatorOutput>(field.GetValue(runtime));
    }

    [Fact]
    public async Task Tartarus_follow_checkbox_click_stays_checked_and_follows_the_picker()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = NewRoot();
            try
            {
                var device = DeviceDescriptorSanitizer.CreateKeyboard(41, @"\\?\HID#VID_1532&PID_0201&MI_00#TEST#GUID");
                var host = new TestHost();
                var leds = new TestTartarusLeds
                { Block = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously) };
                var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(new KeyboardEnumerator(device), host,
                    keyboardIsNeutral: () => true), new RecordingKeyboard(), new AtomicProfileStore(root), tartarusIndicatorOutput: leds);
                await using var vm = new MainViewModel(runtime,
                    a => System.Windows.Application.Current.Dispatcher.Invoke(a), (_, _) => { });
                await vm.InitializeAsync();
                Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
                host.EmitKey(41, RawKeyboardFlags.Make); host.EmitKey(41, RawKeyboardFlags.Break);
                Assert.True(runtime.ConfirmController().Succeeded);
                var window = new MainWindow(vm, new WindowPlacementStore(root), new SessionMarker(root));
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1920, 1080)); content.Arrange(new Rect(0, 0, 1920, 1080)); content.UpdateLayout();
                var checkbox = TartarusVisualChildren<CheckBox>(content).Single(c => Equals(c.Content, "Follow bank changes"));
                var picker = TartarusVisualChildren<ComboBox>(content).Single(c => ReferenceEquals(c.ItemsSource, vm.TartarusKeymaps));
                Assert.True(checkbox.IsEnabled);
                // Invoke the real ToggleButton click path: toggles IsChecked,
                // updates the binding, and calls MainWindow's routed handler.
                void Click() => typeof(System.Windows.Controls.Primitives.ToggleButton)
                    .GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(checkbox, null);
                Click();
                Assert.True(checkbox.IsChecked);
                Assert.True(vm.TartarusFollowKeymaps);
                Assert.False(checkbox.IsEnabled); // Briefly busy until the hardware reply.
                Assert.Single(leds.Calls);
                leds.Block.SetResult(1);
                for (var i = 0; i < 200 && !vm.CanChangeTartarusKeymap; i++) { await Task.Delay(5); }
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(checkbox.IsEnabled);
                Assert.True(checkbox.IsChecked);
                Assert.Contains("confirmed by device readback", vm.TartarusIndicatorStatus);
                leds.Block = null;
                picker.SelectedValue = 3;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(3, vm.SelectedTartarusKeymap);
                Assert.Equal(4, leds.Calls.Last().Mask);
                Assert.True(checkbox.IsChecked);
                Click();
                Assert.False(checkbox.IsChecked);
                var calls = leds.Calls.Count;
                picker.SelectedValue = 2;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(calls, leds.Calls.Count);
                leds.Fail = true;
                Click();
                Assert.False(checkbox.IsChecked);
                Assert.Contains("not confirmed synchronized", vm.TartarusIndicatorStatus);
            }
            finally { Directory.Delete(root, true); }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static IEnumerable<T> TartarusVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) { yield return typed; }
            foreach (var descendant in TartarusVisualChildren<T>(child)) { yield return descendant; }
        }
    }

    private sealed class TartarusTestLifecycle : Tappy.Windows.Lifecycle.IWindowsLifecycleSignalSource
    {
        public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged { add { } remove { } }
    }

    [Fact]
    public async Task Tartarus_sync_is_opt_in_verified_and_following_stops_on_failure()
    {
        var root = NewRoot();
        try
        {
            var device = DeviceDescriptorSanitizer.CreateKeyboard(41, @"\\?\HID#VID_1532&PID_0201&MI_00#TEST#GUID");
            var host = new TestHost();
            var leds = new TestTartarusLeds();
            var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(new KeyboardEnumerator(device), host,
                keyboardIsNeutral: () => true), new RecordingKeyboard(), new AtomicProfileStore(root), tartarusIndicatorOutput: leds);
            await using var vm = new MainViewModel(runtime, a => a(), (_, _) => { });
            await vm.InitializeAsync();
            Assert.False((await runtime.SyncTartarusIndicatorsAsync()).Succeeded);
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.EmitKey(41, RawKeyboardFlags.Make); host.EmitKey(41, RawKeyboardFlags.Break);
            Assert.True(runtime.ConfirmController().Succeeded);
            Assert.Empty(leds.Calls);
            Assert.Contains("preview", vm.TartarusIndicatorStatus);
            await vm.SelectTartarusKeymapAsync(2);
            Assert.Empty(leds.Calls);
            await vm.SyncTartarusIndicatorsAsync();
            Assert.Equal((device.PersistentId, 2), Assert.Single(leds.Calls));
            Assert.Contains("confirmed by device readback", vm.TartarusIndicatorStatus);
            vm.TartarusFollowKeymaps = true;
            await vm.SelectTartarusKeymapAsync(3);
            Assert.Equal((device.PersistentId, 4), leds.Calls.Last());
            Assert.True(vm.TartarusFollowKeymaps);
            leds.Fail = true;
            await vm.SelectTartarusKeymapAsync(4);
            Assert.False(vm.TartarusFollowKeymaps);
            Assert.Contains("not confirmed synchronized", vm.TartarusIndicatorStatus);
            var count = leds.Calls.Count;
            await vm.SelectTartarusKeymapAsync(5);
            Assert.Equal(count, leds.Calls.Count);
            Assert.Contains("preview", vm.TartarusIndicatorStatus);
            runtime.EmergencyStop("test");
            Assert.False((await runtime.SyncTartarusIndicatorsAsync()).Succeeded);
            Assert.False(vm.HasTartarusKeymaps);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_led_result_cannot_mark_new_bank_or_confirmation_synchronized(bool reconfirm)
    {
        var root = NewRoot();
        try
        {
            var host = new TestHost();
            var device = DeviceDescriptorSanitizer.CreateKeyboard(41, @"\\?\HID#VID_1532&PID_0201&MI_00#TEST#GUID");
            var leds = new TestTartarusLeds { Block = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(new KeyboardEnumerator(device), host,
                keyboardIsNeutral: () => true), new RecordingKeyboard(), new AtomicProfileStore(root), tartarusIndicatorOutput: leds);
            RuntimeState? latest = null;
            runtime.StateChanged += (_, state) => latest = state;
            await runtime.InitializeAsync();
            void Confirm()
            {
                Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
                host.EmitKey(41, RawKeyboardFlags.Make); host.EmitKey(41, RawKeyboardFlags.Break);
                Assert.True(runtime.ConfirmController().Succeeded);
            }
            Confirm();
            var syncing = runtime.SyncTartarusIndicatorsAsync();
            Assert.False((await runtime.SyncTartarusIndicatorsAsync()).Succeeded);
            if (reconfirm) { runtime.EmergencyStop("reconfirm"); Confirm(); }
            else { Assert.True(runtime.SelectTartarusKeymap(2).Succeeded); }
            Assert.False(leds.IsCurrent!());
            leds.Block.SetResult(1);
            Assert.False((await syncing).Succeeded);
            Assert.Null(latest!.TartarusVerifiedLedMask);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class TestTartarusLeds : IRazerTartarusIndicatorOutput
    {
        internal bool Fail { get; set; }
        internal TaskCompletionSource<int>? Block { get; set; }
        internal Func<bool>? IsCurrent { get; private set; }
        internal List<(string PersistentId, int Mask)> Calls { get; } = [];
        public Task<int> SynchronizeAsync(string persistentId, int mask, Func<bool> isCurrent)
        {
            Assert.True(isCurrent());
            IsCurrent = isCurrent;
            Calls.Add((persistentId, mask));
            return Fail ? Task.FromException<int>(new IOException("test response failure")) : Block?.Task ?? Task.FromResult(mask);
        }
    }
}
