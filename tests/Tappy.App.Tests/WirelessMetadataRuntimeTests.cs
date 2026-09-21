using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Fact]
    public async Task Wireless_name_refresh_keeps_confirmation_held_output_assignments_and_typing_protection()
    {
        var root = NewRoot();
        try
        {
            var a = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_046D&PID_C52B#A");
            var b = DeviceDescriptorSanitizer.CreateKeyboard((nint)42, @"\\?\HID#BTHENUM_PAD#B");
            var devices = new MetadataEnumerator(a, b);
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
            runtime.IsRehearsal = false;
            host.EmitKey(41, RawKeyboardFlags.Make);
            Assert.Equal(1, output.Down);
            devices.Devices = [a with { DisplayName = "Logitech K400 Plus — Unifying (paired)" },
                b with { DisplayName = "Microsoft Bluetooth Number Pad — Bluetooth" }];
            devices.Changed = true;
            runtime.PollRememberedReconnect();
            Assert.False(runtime.IsRehearsal);
            Assert.Equal(a.PersistentId, runtime.ReservedDevice!.PersistentId);
            Assert.NotNull(runtime.GetControllerAction(TargusKeypadLayout.Key(0x4F)));
            Assert.Equal(0, output.Up);
            Assert.Contains("K400 Plus", runtime.Devices.Single(d => d.PersistentId == a.PersistentId).DisplayName);
            Assert.True(runtime.Devices.Single(d => d.PersistentId == b.PersistentId).IsProtectedForTyping);
            host.EmitKey(41, RawKeyboardFlags.Break);
            Assert.Equal(1, output.Up);
            Assert.False(runtime.BeginIdentification(runtime.Devices.Single(d => d.PersistentId == b.PersistentId)).Succeeded);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class MetadataEnumerator(params SanitizedDeviceDescriptor[] initial) : IRawInputDeviceEnumerator, IKeyboardMetadataUpdates
    {
        public IReadOnlyList<SanitizedDeviceDescriptor> Devices { get; set; } = initial;
        public bool Changed { get; set; }
        public bool IsMetadataPending => false;
        public bool ConsumeMetadataChange() { var result = Changed; Changed = false; return result; }
        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards() => Devices;
        public SanitizedDeviceDescriptor? DescribeKeyboard(nint handle) => Devices.FirstOrDefault(d => d.SessionHandle == handle);
    }
}
