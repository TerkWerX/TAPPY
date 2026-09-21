using Tappy.App.Runtime;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generic_layout_survives_assignment_discovery_and_reload(bool useSequence)
    {
        var root = NewTemporaryDirectory();
        try
        {
            var host = new FakeMessageHost();
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)91, @"\\?\HID#VID_1000&PID_2000#LAYOUT_REGRESSION");
            var provider = new RawInputKeyboardProvider(new FakeEnumerator(descriptor), host,
                keyboardIsNeutral: static () => true);
            var store = new AtomicProfileStore(root);
            await using var runtime = new DeviceAwareControllerRuntime(provider, new RecordingOutput(), store);
            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)91, 0x4F, 0x61);
            host.Emit(Packet((nint)91, 0x4F, 0x61, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)91, 0x4F, 0x61, RawKeyboardFlags.Break));
            var initial = Assert.IsType<ControllerLayoutWorkspace>(runtime.GetControllerLayout());
            Assert.Equal(6, initial.GridColumns);
            var customized = Assert.Single(initial.Controls) with
            {
                X = 600,
                Y = 400,
                Width = 1.5,
                Height = 0.75,
                ColorKey = "Green",
                AnalogRawAtMinimum = 8,
                AnalogRawAtMaximum = 115,
                AnalogRawAtCenter = 61,
                EncoderDegreesPerStep = 7.5,
                EncoderReversed = true
            };
            Assert.True(runtime.UpdateControllerLayout(new ControllerLayoutWorkspace(
                12, 10, false, [customized])).Succeeded);

            var assigned = useSequence
                ? runtime.AssignControllerAction(customized.ControlId, TextAssignment("preserved"))
                : runtime.AssignKeyboardMapping(customized.ControlId,
                    KeyboardMappingAssignment.PressOnce("Copy", ["CTRL", "C"]));
            Assert.True(assigned.Succeeded, assigned.Message);
            AssertWorkspacePreserved(runtime.GetControllerLayout()!, customized);

            host.Emit(Packet((nint)91, 0x50, 0x62, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)91, 0x50, 0x62, RawKeyboardFlags.Break));
            var learned = runtime.GetControllerLayout()!;
            Assert.Equal(2, learned.Controls.Count);
            AssertWorkspacePreserved(learned, customized);
            Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            var persisted = await store.LoadAsync(AtomicProfileStore.DefaultProfileId);
            var layout = Assert.Single(persisted.Controllers).Layout;
            Assert.Equal(12, layout.GridColumns);
            Assert.Equal(10, layout.GridRows);
            Assert.False(layout.SnapToGrid);
            var tile = layout.Rows.SelectMany(row => row.Controls)
                .Single(control => control.ControlId?.Value == customized.ControlId);
            Assert.Equal(customized.X, tile.X);
            Assert.Equal(customized.ColorKey, tile.ColorKey);
            Assert.Equal(customized.AnalogRawAtCenter, tile.AnalogRawAtCenter);

            IdentifyAndConfirm(runtime, host, (nint)91, 0x4F, 0x61);
            AssertWorkspacePreserved(runtime.GetControllerLayout()!, customized);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Captured_editor_target_survives_another_key_press_but_not_reconfirmation()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var host = new FakeMessageHost();
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)92, @"\\?\HID#VID_1000&PID_2000#EDITOR_REGRESSION");
            var provider = new RawInputKeyboardProvider(new FakeEnumerator(descriptor), host,
                keyboardIsNeutral: static () => true);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, new RecordingOutput(), new AtomicProfileStore(root));
            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)92, 0x4F, 0x61);
            var firstId = ControlId.FromRawInputKeyboard(0x4F).Value;
            var secondId = ControlId.FromRawInputKeyboard(0x50).Value;
            Assert.True(runtime.AssignControllerAction(firstId, TextAssignment("original text")).Succeeded);
            var edit = Assert.IsType<ControllerAssignmentEditContext>(runtime.BeginAssignmentEdit(firstId));

            host.Emit(Packet((nint)92, 0x50, 0x62, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)92, 0x50, 0x62, RawKeyboardFlags.Break));
            Assert.True(runtime.AssignControllerAction(edit.Target, TextAssignment("edited text")).Succeeded);
            Assert.Equal("edited text", runtime.GetControllerAction(firstId)!.PressSequence.Steps[0].Value);
            Assert.Null(runtime.GetControllerAction(secondId));
            Assert.Equal("original text", edit.ExistingAssignment!.PressSequence.Steps[0].Value);

            Assert.False(runtime.AssignControllerAction(edit.Target with { LayerId = "layer-2" },
                TextAssignment("wrong layer")).Succeeded);
            Assert.False(runtime.AssignKeyboardMapping(edit.Target with { ControllerPersistentId = "another" },
                KeyboardMappingAssignment.PressOnce("Wrong", ["F13"])).Succeeded);
            Assert.Null(runtime.BeginAssignmentEdit("raw-input:not-a-known-control"));

            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            Assert.False(runtime.AssignControllerAction(edit.Target, TextAssignment("disconnected")).Succeeded);
            host.Emit(Packet((nint)92, 0x4F, 0x61, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)92, 0x4F, 0x61, RawKeyboardFlags.Break));
            Assert.True(runtime.ConfirmController().Succeeded);
            Assert.False(runtime.AssignControllerAction(edit.Target, TextAssignment("stale")).Succeeded);
            Assert.Equal("edited text", runtime.GetControllerAction(firstId)!.PressSequence.Steps[0].Value);
            var fresh = runtime.BeginAssignmentEdit(firstId)!;
            Assert.True(runtime.AssignKeyboardMapping(fresh.Target,
                KeyboardMappingAssignment.PressOnce("Fresh", ["F24"])).Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ControllerActionAssignment TextAssignment(string text) => new(text,
        ControllerActionSequenceDefinition.Once(text,
            new ControllerActionStepDefinition { Type = ControllerActionStepType.Text, Value = text }), new());

    private static void AssertWorkspacePreserved(ControllerLayoutWorkspace layout, ControllerControlPlacement expected)
    {
        Assert.Equal(12, layout.GridColumns);
        Assert.Equal(10, layout.GridRows);
        Assert.False(layout.SnapToGrid);
        Assert.Equal(expected, layout.Controls.Single(control => control.ControlId == expected.ControlId));
    }
}
