using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Fact]
    public async Task Unifying_reconnect_does_not_adopt_an_identical_receiver_or_a_changed_port()
    {
        var root = NewRoot();
        try
        {
            var a = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_046D&PID_C52B#A");
            var b = DeviceDescriptorSanitizer.CreateKeyboard((nint)42, @"\\?\HID#VID_046D&PID_C52B#B");
            var moved = DeviceDescriptorSanitizer.CreateKeyboard((nint)43, @"\\?\HID#VID_046D&PID_C52B#NEW_PORT");
            var devices = new KeyboardEnumerator(a, b);
            var host = new TestHost();
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(devices, host,
                keyboardIsNeutral: () => true), output, new AtomicProfileStore(root));
            await runtime.InitializeAsync();
            runtime.SetTypingKeyboardProtection(b.PersistentId);
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single(d => d.PersistentId == a.PersistentId)).Succeeded);
            host.EmitKey(41, RawKeyboardFlags.Make);
            host.EmitKey(41, RawKeyboardFlags.Break);
            Assert.True(runtime.ConfirmController().Succeeded);
            Assert.True(runtime.AssignMapping(TargusKeypadLayout.Key(0x4F), "F24").Succeeded);
            await runtime.SaveProfileAsync();
            runtime.IsRehearsal = false;
            devices.Devices = [b, moved];
            host.EmitRemoval(41);
            runtime.PollRememberedReconnect();
            host.EmitKey(42, RawKeyboardFlags.Make);
            host.EmitKey(42, RawKeyboardFlags.Break);
            host.EmitKey(43, RawKeyboardFlags.Make);
            host.EmitKey(43, RawKeyboardFlags.Break);
            Assert.Equal(0, output.Down);
            Assert.Equal(a.PersistentId, runtime.ReservedDevice!.PersistentId);
            Assert.False(runtime.CanConfirmController);
            devices.Devices = [a, b];
            runtime.PollRememberedReconnect();
            runtime.PollRememberedReconnect();
            Assert.True(runtime.IsRehearsal);
            Assert.Equal(a.PersistentId, runtime.ReservedDevice!.PersistentId);
            Assert.NotNull(runtime.GetControllerAction(TargusKeypadLayout.Key(0x4F)));
            Assert.True(runtime.Devices.Single(d => d.PersistentId == b.PersistentId).IsProtectedForTyping);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Protection_save_failure_keeps_previous_protection_and_leaves_output_in_rehearsal()
    {
        var root = NewRoot();
        try
        {
            var a = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_046D&PID_C52B#A");
            var path = Path.Combine(root, "tabs.json");
            var protectionPath = path + ".typing-keyboard.json";
            new TypingKeyboardSettings(a.PersistentId, a.DisplayName).Save(protectionPath);
            await using var workspace = new ControllerWorkspace(_ => new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(a), new TestHost(), keyboardIsNeutral: () => true),
                new RecordingKeyboard(), new AtomicProfileStore(Path.Combine(root, "profile"))), action => action(), (_, _) => { }, path);
            await workspace.InitializeAsync();
            using var locked = File.Open(protectionPath, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.False(workspace.ClearTypingKeyboardProtection().Succeeded);
            Assert.True(workspace.Tabs[0].Runtime.Devices.Single().IsProtectedForTyping);
            Assert.True(workspace.Tabs[0].Runtime.IsRehearsal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Typing_protection_requires_physical_release_and_is_saved_across_tabs_and_restarts()
    {
        var root = NewRoot();
        try
        {
            var a = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_046D&PID_C52B#RECEIVER_A");
            var b = DeviceDescriptorSanitizer.CreateKeyboard((nint)42, @"\\?\HID#VID_046D&PID_C52B#RECEIVER_B");
            var devices = new KeyboardEnumerator(a, b);
            var host = new TestHost();
            var output = new RecordingKeyboard();
            var runtimes = new List<DeviceAwareControllerRuntime>();
            IControllerRuntime Create(int slot)
            {
                var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(devices,
                    new SharedInputHostLease(host), keyboardIsNeutral: () => true), output,
                    new AtomicProfileStore(Path.Combine(root, $"slot-{slot}")));
                runtimes.Add(runtime);
                return runtime;
            }
            var path = Path.Combine(root, "tabs.json");
            await using (var workspace = new ControllerWorkspace(Create, action => action(), (_, _) => { }, path))
            {
                await workspace.InitializeAsync();
                var vm = workspace.Tabs[0].ViewModel;
                vm.SelectedDevice = vm.Devices.Single(d => d.PersistentId == a.PersistentId);
                Assert.False(workspace.ProtectTypingKeyboard(vm).Succeeded);
                vm.BeginIdentification();
                host.EmitKey(42, RawKeyboardFlags.Make);
                host.EmitKey(42, RawKeyboardFlags.Break);
                Assert.False(workspace.ProtectTypingKeyboard(vm).Succeeded);
                host.EmitKey(41, RawKeyboardFlags.Make);
                Assert.False(workspace.ProtectTypingKeyboard(vm).Succeeded);
                host.EmitKey(41, RawKeyboardFlags.Break);
                vm.SelectedDevice = vm.Devices.Single(d => d.PersistentId == b.PersistentId);
                Assert.False(workspace.ProtectTypingKeyboard(vm).Succeeded);
                vm.SelectedDevice = vm.Devices.Single(d => d.PersistentId == a.PersistentId);
                Assert.True(workspace.ProtectTypingKeyboard(vm).Succeeded);
                Assert.Null(runtimes[0].ReservedDevice);
                Assert.True(vm.Devices.Single(d => d.PersistentId == a.PersistentId).IsProtectedForTyping);
                Assert.Contains("protected typing", vm.Devices.Single(d => d.PersistentId == a.PersistentId).DisplayName);
                var second = await workspace.AddAsync();
                Assert.False(second.Runtime.BeginIdentification(second.Runtime.Devices.Single(d => d.PersistentId == a.PersistentId)).Succeeded);
                Assert.True(second.Runtime.BeginIdentification(second.Runtime.Devices.Single(d => d.PersistentId == b.PersistentId)).Succeeded);
                host.EmitKey(42, RawKeyboardFlags.Make);
                host.EmitKey(42, RawKeyboardFlags.Break);
                Assert.True(second.Runtime.ConfirmController().Succeeded);
                Assert.Equal(0, output.Down);
            }
            await using var reopened = new ControllerWorkspace(Create, action => action(), (_, _) => { }, path);
            await reopened.InitializeAsync();
            Assert.All(reopened.Tabs, tab =>
            {
                Assert.False(tab.Runtime.BeginIdentification(tab.Runtime.Devices.Single(d => d.PersistentId == a.PersistentId)).Succeeded);
                Assert.Contains("protected", tab.ViewModel.TypingKeyboardStatus);
            });
            Assert.True(reopened.ClearTypingKeyboardProtection().Succeeded);
            Assert.All(reopened.Tabs, tab => Assert.True(tab.Runtime.IsRehearsal));
            Assert.True(reopened.Tabs[0].Runtime.BeginIdentification(reopened.Tabs[0].Runtime.Devices.Single(d => d.PersistentId == a.PersistentId)).Succeeded);
            Assert.Null(TypingKeyboardSettings.Load(path + ".typing-keyboard.json").PersistentId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Protecting_active_keyboard_releases_output_keeps_assignments_and_blocks_reconnect()
    {
        var root = NewRoot();
        try
        {
            var a = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_046D&PID_C52B#A");
            var host = new TestHost();
            var devices = new KeyboardEnumerator(a);
            var output = new RecordingKeyboard();
            await using var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(devices, host,
                keyboardIsNeutral: () => true), output, new AtomicProfileStore(root));
            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.EmitKey(41, RawKeyboardFlags.Make);
            host.EmitKey(41, RawKeyboardFlags.Break);
            Assert.True(runtime.ConfirmController().Succeeded);
            Assert.True(runtime.AssignMapping(TargusKeypadLayout.Key(0x4F), "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.EmitKey(41, RawKeyboardFlags.Make);
            Assert.Equal(1, output.Down);
            runtime.SetTypingKeyboardProtection(a.PersistentId);
            Assert.Equal(1, output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.Null(runtime.ReservedDevice);
            host.EmitKey(41, RawKeyboardFlags.Break);
            host.EmitKey(41, RawKeyboardFlags.Make);
            host.EmitKey(41, RawKeyboardFlags.Break);
            Assert.Equal(1, output.Down);
            host.EmitRemoval(41);
            runtime.PollRememberedReconnect();
            Assert.False(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            runtime.SetTypingKeyboardProtection(null);
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.EmitKey(41, RawKeyboardFlags.Make);
            host.EmitKey(41, RawKeyboardFlags.Break);
            Assert.True(runtime.ConfirmController().Succeeded);
            Assert.NotNull(runtime.GetControllerAction(TargusKeypadLayout.Key(0x4F)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":99}")]
    public async Task Unreadable_protection_settings_block_keyboard_controllers_until_explicit_clear(string json)
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "tabs.json");
            await File.WriteAllTextAsync(path + ".typing-keyboard.json", json);
            var device = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_046D&PID_C52B#A");
            await using var workspace = new ControllerWorkspace(_ => new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new KeyboardEnumerator(device), new TestHost(), keyboardIsNeutral: () => true),
                new RecordingKeyboard(), new AtomicProfileStore(Path.Combine(root, "profile"))), action => action(),
                (_, _) => { }, path);
            await workspace.InitializeAsync();
            var tab = workspace.Tabs[0];
            Assert.Contains("needs attention", tab.ViewModel.TypingKeyboardStatus);
            Assert.False(tab.Runtime.BeginIdentification(tab.Runtime.Devices.Single()).Succeeded);
            Assert.True(workspace.ClearTypingKeyboardProtection().Succeeded);
            Assert.True(tab.Runtime.BeginIdentification(tab.Runtime.Devices.Single()).Succeeded);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
