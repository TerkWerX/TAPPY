using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Windows.Input;
using Tappy.Windows.Lifecycle;
using Tappy.Windows.Output;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    [Theory]
    [InlineData("winmm-midi:channel-1:note-0", 0)]
    [InlineData("winmm-midi:channel-1:note-71", 71)]
    [InlineData("winmm-midi:channel-1:note-82", 82)]
    [InlineData("winmm-midi:channel-1:note-89", 89)]
    [InlineData("winmm-midi:channel-1:note-72", null)]
    [InlineData("winmm-midi:channel-1:note-98", null)]
    [InlineData("winmm-midi:channel-1:cc-48:increase", null)]
    public void Apc_mini_led_sync_accepts_only_verified_lighted_notes(string controlId, int? expected) =>
        Assert.Equal(expected, DeviceAwareControllerRuntime.TryParseApcMiniLedNote(controlId));

    [Theory]
    [InlineData("Default", 0)]
    [InlineData("Green", 1)]
    [InlineData("Teal", 1)]
    [InlineData("Red", 3)]
    [InlineData("Pink", 3)]
    [InlineData("Orange", 5)]
    [InlineData("Amber", 5)]
    [InlineData("Yellow", 5)]
    [InlineData("White", 5)]
    [InlineData("Blue", 0)]
    [InlineData("Purple", 0)]
    public void Apc_mini_visual_colors_map_to_its_verified_bicolor_palette(string color, int expected) =>
        Assert.Equal(expected, DeviceAwareControllerRuntime.ApcMiniVelocity(color));

    [Theory]
    [InlineData("winmm-midi:channel-1:note-54", 0)] // Pad R1 C1
    [InlineData("winmm-midi:channel-1:note-117", 63)] // Pad R4 C16
    [InlineData("winmm-midi:channel-1:note-70", 16)] // Pad R2 C1
    [InlineData("winmm-midi:channel-1:note-53", null)] // Record / Loop Record (button, not a pad)
    [InlineData("winmm-midi:channel-1:note-118", null)] // one past the pad range
    [InlineData("winmm-midi:channel-1:cc-16:increase", null)] // encoder rotation, not a note
    [InlineData("winmm-midi:channel-1:note-16", null)] // encoder touch, not a pad
    public void Akai_Fire_pad_index_is_parsed_only_for_the_64_pad_notes(string controlId, int? expected) =>
        Assert.Equal(expected, DeviceAwareControllerRuntime.TryParseAkaiFirePadIndex(controlId));

    [Theory]
    [InlineData("Default", 0, 0, 0)]
    [InlineData("Red", 255, 0, 0)]
    [InlineData("Green", 0, 255, 0)]
    [InlineData("White", 255, 255, 255)]
    [InlineData(null, 0, 0, 0)]
    [InlineData("not-a-real-color", 0, 0, 0)]
    [InlineData("#FF6A00", 0xFF, 0x6A, 0x00)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData(" #112233 ", 0x11, 0x22, 0x33)]
    [InlineData("#gggggg", 0, 0, 0)] // malformed hex falls back to the named default, not a crash
    public void Akai_Fire_visual_colors_map_to_starter_palette_rgb(string? color, byte red, byte green, byte blue)
    {
        var (r, g, b) = DeviceAwareControllerRuntime.FireRgb(color);
        Assert.Equal((red, green, blue), (r, g, b));
    }

    [Theory]
    [InlineData("#FF6A00", true, (byte)0xFF, (byte)0x6A, (byte)0x00)]
    [InlineData("#000000", true, (byte)0, (byte)0, (byte)0)]
    [InlineData("#ffffff", true, (byte)255, (byte)255, (byte)255)]
    [InlineData("#12345", false, (byte)0, (byte)0, (byte)0)] // one digit short
    [InlineData("#gggggg", false, (byte)0, (byte)0, (byte)0)] // not hex digits
    [InlineData("112233", false, (byte)0, (byte)0, (byte)0)] // missing leading '#'
    [InlineData("Red", false, (byte)0, (byte)0, (byte)0)]
    [InlineData(null, false, (byte)0, (byte)0, (byte)0)]
    public void Hex_color_parsing_accepts_only_a_well_formed_rrggbb_value(
        string? value, bool expectedResult, byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        var result = DeviceAwareControllerRuntime.TryParseHexColor(value, out var red, out var green, out var blue);
        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            Assert.Equal((expectedRed, expectedGreen, expectedBlue), (red, green, blue));
        }
    }

    [Theory]
    [InlineData("MIDI: FL STUDIO FIRE", "FL STUDIO FIRE")]
    [InlineData("MIDI: 2 - FL STUDIO FIRE", "2 - FL STUDIO FIRE")]
    [InlineData("FL STUDIO FIRE", "FL STUDIO FIRE")] // no prefix to strip
    [InlineData("midi: apc mini", "apc mini")] // case-insensitive prefix match
    public void MIDI_display_prefix_is_stripped_to_recover_the_raw_output_port_name(
        string displayName, string expectedPortName) =>
        Assert.Equal(expectedPortName, DeviceAwareControllerRuntime.StripMidiDisplayPrefix(displayName));

    [Fact]
    public void A_confirmed_Fire_matches_only_its_own_output_port_when_two_Fires_are_connected()
    {
        // Reproduces a real regression found on TESTBED, 2026-09-20, right
        // after two real Fire units were both recognized (D-023): Sync
        // supported LEDs started refusing to light either unit, because the
        // output-port lookup matched by MODEL alone ("is this any Fire's
        // output port"), which both units' outputs now satisfy. See D-024 in
        // DECISIONS.md.
        var outputs = new[]
        {
            new WinMmMidiOutput.Device(0, "FL STUDIO FIRE"),
            new WinMmMidiOutput.Device(1, "2 - FL STUDIO FIRE"),
        };

        var firstUnit = DeviceAwareControllerRuntime.MatchMidiOutputPorts(outputs, "MIDI: FL STUDIO FIRE");
        var secondUnit = DeviceAwareControllerRuntime.MatchMidiOutputPorts(outputs, "MIDI: 2 - FL STUDIO FIRE");

        Assert.Equal("FL STUDIO FIRE", Assert.Single(firstUnit).Name);
        Assert.Equal("2 - FL STUDIO FIRE", Assert.Single(secondUnit).Name);
    }

    [Fact]
    public void Akai_Fire_pad_color_sysex_matches_the_documented_format_for_one_pad()
    {
        // F0 47 7F 43 65 <lenHi> <lenLo> [index R G B] F7; length = 4 for one pad.
        // Full-brightness blue (0,0,255) halves to 0x7F for the 7-bit channel.
        var packet = DeviceAwareControllerRuntime.FirePadColorSysEx([(0x23, 0x00, 0x00, 0xFF)]);

        Assert.Equal(
            new byte[] { 0xF0, 0x47, 0x7F, 0x43, 0x65, 0x00, 0x04, 0x23, 0x00, 0x00, 0x7F, 0xF7 },
            packet);
    }

    [Fact]
    public void Akai_Fire_pad_color_sysex_batches_every_pad_into_one_message()
    {
        var packet = DeviceAwareControllerRuntime.FirePadColorSysEx(
            [(0, 255, 0, 0), (1, 0, 255, 0), (63, 0, 0, 255)]);

        Assert.Equal(0xF0, packet[0]);
        Assert.Equal(0x47, packet[1]);
        Assert.Equal(0x7F, packet[2]);
        Assert.Equal(0x43, packet[3]);
        Assert.Equal(0x65, packet[4]);
        var payloadLength = (packet[5] << 7) | packet[6];
        Assert.Equal(12, payloadLength); // 3 pads x 4 bytes
        Assert.Equal(0xF7, packet[^1]);
        Assert.Equal(8 + payloadLength, packet.Length);
        // Third pad (index 63, full blue): channel halved from 255 to 127.
        Assert.Equal(63, packet[15]);
        Assert.Equal(0, packet[16]);
        Assert.Equal(0, packet[17]);
        Assert.Equal(0x7F, packet[18]);
    }

    [Fact]
    public async Task Akai_Fire_led_capability_advertises_freeform_rgb_support()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var keyboardProvider = new RawInputKeyboardProvider(
                new FakeEnumerator(),
                new FakeMessageHost(),
                keyboardIsNeutral: static () => true);
            using var midiBackend = new FakeMidiBackend(
                new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var midiProvider = new WinMmMidiInputProvider(midiBackend);
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboardProvider,
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: midiProvider);

            await runtime.InitializeAsync();
            var device = Assert.Single(runtime.Devices);
            Assert.True(runtime.BeginIdentification(device).Succeeded);
            midiBackend.Publish(PackMidi(0x90, 60, 127));
            midiBackend.Publish(PackMidi(0x80, 60, 0));
            Assert.True(runtime.ConfirmController().Succeeded);

            var capability = Assert.IsType<ControllerLedColorCapability>(runtime.GetControllerLedColorCapability());
            Assert.Equal(MidiControllerModelCatalog.AkaiFireV1Id, capability.ModelId);
            Assert.True(capability.SupportsFreeformColor);

            // A verified-palette controller must not silently gain freeform support.
            using var apcBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
            await using var apcRuntime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(
                    new FakeEnumerator(),
                    new FakeMessageHost(),
                    keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: new WinMmMidiInputProvider(apcBackend));
            await apcRuntime.InitializeAsync();
            var apcDevice = Assert.Single(apcRuntime.Devices);
            Assert.True(apcRuntime.BeginIdentification(apcDevice).Succeeded);
            apcBackend.Publish(PackMidi(0x90, 36, 127));
            apcBackend.Publish(PackMidi(0x80, 36, 0));
            Assert.True(apcRuntime.ConfirmController().Succeeded);
            var apcCapability = Assert.IsType<ControllerLedColorCapability>(apcRuntime.GetControllerLedColorCapability());
            Assert.False(apcCapability.SupportsFreeformColor);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_stale_saved_Fire_layout_is_reset_to_the_current_shape_not_scrambled_into_it()
    {
        // Reproduces a real regression found on TESTBED, 2026-09-20: a Fire
        // confirmed and saved before the encoder/pad-grid separation shipped
        // kept its old layout Id forever (that Id was never version-bumped), so
        // every later confirm re-used its stale per-control ordering to fill the
        // NEW row/column shape - visually interleaving encoders back into the
        // Mute/Solo and pad rows. See D-022 in DECISIONS.md.
        var root = NewTemporaryDirectory();
        try
        {
            var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var midiProvider = new WinMmMidiInputProvider(midiBackend);
            await using (var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(
                    new FakeEnumerator(),
                    new FakeMessageHost(),
                    keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: midiProvider))
            {
                await runtime.InitializeAsync();
                var device = Assert.Single(runtime.Devices);
                Assert.True(runtime.BeginIdentification(device).Succeeded);
                midiBackend.Publish(PackMidi(0x90, 60, 127));
                midiBackend.Publish(PackMidi(0x80, 60, 0));
                Assert.True(runtime.ConfirmController().Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }
            midiBackend.Dispose();

            // Corrupt the saved profile the way the real regression did: keep the
            // pre-separation layout Id, and place the Volume encoder's touch
            // control where the old shape put it - overlapping Mute/Solo 1 and
            // the first pad row, rather than its own row above everything.
            var store = new AtomicProfileStore(root);
            var profile = (await store.LoadAsync("default")).ToEditableProfile();
            var fire = Assert.Single(profile.Controllers,
                c => c.Identity.ProviderId == WinMmMidiInputProvider.ProviderId);
            fire.Layout.Id = "akai-fire-v1-code-layout-v1";
            fire.Layout.GridColumns = 16;
            fire.Layout.GridRows = 8;
            var volumeTouch = fire.Layout.Rows.SelectMany(r => r.Controls)
                .Single(c => c.ControlId!.Value.Value == "winmm-midi:channel-1:note-16");
            volumeTouch.X = 288;
            volumeTouch.Y = 232;
            volumeTouch.ColorKey = "Purple";
            await store.SaveAsync(profile.CreateSnapshot());

            var restoredMidiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            await using var restored = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(
                    new FakeEnumerator(),
                    new FakeMessageHost(),
                    keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: new WinMmMidiInputProvider(restoredMidiBackend));
            await restored.InitializeAsync();
            var restoredDevice = Assert.Single(restored.Devices);
            Assert.True(restored.BeginIdentification(restoredDevice).Succeeded);
            restoredMidiBackend.Publish(PackMidi(0x90, 60, 127));
            restoredMidiBackend.Publish(PackMidi(0x80, 60, 0));
            Assert.True(restored.ConfirmController().Succeeded);

            var workspace = Assert.IsType<ControllerLayoutWorkspace>(restored.GetControllerLayout());
            var restoredVolumeTouch = workspace.Controls.Single(c => c.ControlId == "winmm-midi:channel-1:note-16");
            // Volume's three controls (increase/touch/decrease) now share one
            // column — they render as one concentric ring cluster, not three
            // side-by-side tiles. See MidiControllerModelCatalog.CreateAkaiFireV1.
            Assert.Equal(2 * MainViewModel.LayoutCellWidth, restoredVolumeTouch.X);
            Assert.Equal(0 * MainViewModel.LayoutCellHeight, restoredVolumeTouch.Y);
            Assert.Equal("Purple", restoredVolumeTouch.ColorKey);

            // Every encoder control lands on row 0, never sharing a row with
            // Mute/Solo (row 2) or any pad.
            var encoders = new (int TouchNote, int RotationCc)[]
                { (0x10, 0x10), (0x11, 0x11), (0x12, 0x12), (0x13, 0x13), (0x19, 0x76) };
            var encoderControlIds = encoders
                .SelectMany(e => new[]
                {
                    $"winmm-midi:channel-1:note-{e.TouchNote}",
                    $"winmm-midi:channel-1:cc-{e.RotationCc}:increase",
                    $"winmm-midi:channel-1:cc-{e.RotationCc}:decrease",
                })
                .ToHashSet(StringComparer.Ordinal);
            Assert.All(workspace.Controls.Where(c => encoderControlIds.Contains(c.ControlId)),
                control => Assert.Equal(0, control.Y));

            restoredMidiBackend.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("MIDI: 2 - FL STUDIO FIRE")]
    [InlineData("2 - FL STUDIO FIRE")]
    [InlineData("MIDI: 3 - FL STUDIO FIRE")]
    public void A_second_simultaneously_connected_Fire_is_recognized_by_its_prefixed_name(string displayName)
    {
        // Confirmed against two real Fire units connected simultaneously on
        // TESTBED, 2026-09-20: Windows named the second one "2 - FL STUDIO
        // FIRE", a leading-number prefix, not Tappy's own "(device N)" suffix.
        Assert.Equal(
            MidiControllerModelCatalog.AkaiFireV1Id,
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, displayName)?.Id);
    }

    [Fact]
    public async Task Midi_pad_uses_the_same_full_assignment_pipeline_as_keyboard_controllers()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var keyboardHost = new FakeMessageHost();
            var keyboardProvider = new RawInputKeyboardProvider(
                new FakeEnumerator(),
                keyboardHost,
                keyboardIsNeutral: static () => true);
            using var midiBackend = new FakeMidiBackend(
                new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
            var midiProvider = new WinMmMidiInputProvider(midiBackend);
            var keyboardOutput = new RecordingOutput();
            var actionOutput = new RecordingActionOutput();
            var controlUpdates = new List<RuntimeControlUpdate>();
            await using var runtime = new DeviceAwareControllerRuntime(
                keyboardProvider,
                logitechG13Provider: null,
                keyboardOutput,
                new AtomicProfileStore(root),
                actionOutput: actionOutput,
                midiInputProvider: midiProvider);
            runtime.ControlChanged += (_, update) => controlUpdates.Add(update);

            await runtime.InitializeAsync();
            var device = Assert.Single(runtime.Devices);
            Assert.Equal(WinMmMidiInputProvider.ProviderId, device.ProviderId);
            Assert.True(runtime.BeginIdentification(device).Succeeded);
            midiBackend.Publish(PackMidi(0x90, 36, 127));
            midiBackend.Publish(PackMidi(0x80, 36, 0));
            Assert.True(runtime.ConfirmController().Succeeded);
            var ledCapability = Assert.IsType<ControllerLedColorCapability>(
                runtime.GetControllerLedColorCapability());
            Assert.Equal(["Default", "Green", "Red", "Amber"], ledCapability.SupportedColorKeys);
            var initialLayout = controlUpdates.Where(update => update.IsSnapshot).ToArray();
            Assert.Equal(99, initialLayout.Length);
            Assert.Contains(initialLayout, update =>
                update.ControlId == "winmm-midi:channel-1:note-57" &&
                update.DisplayLabel == "Pad R1 C2");
            Assert.Contains(initialLayout, update =>
                update.ControlId == "winmm-midi:channel-1:cc-56:increase" &&
                update.DisplayLabel == "Master fader ↑");

            var controlId = "winmm-midi:channel-1:note-36";
            var sequence = ControllerActionSequenceDefinition.Once(
                "MIDI pad full macro",
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.KeyboardChord,
                    Keys = [new KeyboardOutputKey("CTRL"), new KeyboardOutputKey("S")],
                },
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Text,
                    Value = "from APC MINI",
                },
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Midi,
                    Value = "cc:1:7:100",
                },
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Osc,
                    Value = "/tappy/test",
                    Arguments = "1",
                    Target = "127.0.0.1",
                    Amount = 9000,
                });
            Assert.True(runtime.AssignControllerAction(
                controlId,
                new ControllerActionAssignment(
                    sequence.Name,
                    sequence,
                    new ControllerActionSequenceDefinition())).Succeeded);

            var editableCopy = Assert.IsType<ControllerActionAssignment>(runtime.GetControllerAction(controlId));
            Assert.Equal(4, editableCopy.PressSequence.Steps.Count);
            Assert.Equal("from APC MINI", editableCopy.PressSequence.Steps[1].Value);
            editableCopy.PressSequence.Steps[1].Value = "changed only in editor copy";
            Assert.Equal(
                "from APC MINI",
                Assert.IsType<ControllerActionAssignment>(runtime.GetControllerAction(controlId))
                    .PressSequence.Steps[1].Value);

            var reordered = initialLayout.Select(update => update.ControlId).ToArray();
            (reordered[0], reordered[^1]) = (reordered[^1], reordered[0]);
            Assert.True(runtime.ReorderControls(reordered).Succeeded);
            var originalWorkspace = Assert.IsType<ControllerLayoutWorkspace>(runtime.GetControllerLayout());
            var customControls = originalWorkspace.Controls
                .Select((control, index) => index == 0
                    ? control with
                    {
                        X = 37,
                        Y = 49,
                        Width = 0.5,
                        Height = 0.75,
                        ColorKey = "Purple",
                        AnalogRawAtMinimum = 11,
                        AnalogRawAtMaximum = 119,
                        AnalogRawAtCenter = 63,
                        EncoderDegreesPerStep = 7.5,
                        EncoderReversed = true,
                    }
                    : control)
                .ToArray();
            Assert.True(runtime.UpdateControllerLayout(new ControllerLayoutWorkspace(
                12,
                10,
                SnapToGrid: false,
                customControls)).Succeeded);
            Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            var savedProfile = await new AtomicProfileStore(root).LoadAsync("default");
            var savedController = Assert.Single(savedProfile.Controllers);
            Assert.Equal(12, savedController.Layout.GridColumns);
            Assert.Equal(10, savedController.Layout.GridRows);
            Assert.False(savedController.Layout.SnapToGrid);
            var savedOrder = savedController.Layout.Rows
                .SelectMany(row => row.Controls)
                .Where(control => control.ControlId is not null)
                .Select(control => control.ControlId!.Value.Value)
                .ToArray();
            Assert.Equal(reordered, savedOrder);

            using (var restoredMidiBackend = new FakeMidiBackend(
                       new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3)))
            {
                var restoredMidiProvider = new WinMmMidiInputProvider(restoredMidiBackend);
                var restoredUpdates = new List<RuntimeControlUpdate>();
                await using var restoredRuntime = new DeviceAwareControllerRuntime(
                    new RawInputKeyboardProvider(
                        new FakeEnumerator(),
                        new FakeMessageHost(),
                        keyboardIsNeutral: static () => true),
                    logitechG13Provider: null,
                    new RecordingOutput(),
                    new AtomicProfileStore(root),
                    actionOutput: new RecordingActionOutput(),
                    midiInputProvider: restoredMidiProvider);
                restoredRuntime.ControlChanged += (_, update) => restoredUpdates.Add(update);
                await restoredRuntime.InitializeAsync();
                var restoredDevice = Assert.Single(restoredRuntime.Devices);
                Assert.True(restoredRuntime.BeginIdentification(restoredDevice).Succeeded);
                restoredMidiBackend.Publish(PackMidi(0x90, 36, 127));
                restoredMidiBackend.Publish(PackMidi(0x80, 36, 0));
                Assert.True(restoredRuntime.ConfirmController().Succeeded);
                Assert.Equal(
                    reordered,
                    restoredUpdates.Where(update => update.IsSnapshot).Select(update => update.ControlId));
                var restoredWorkspace = Assert.IsType<ControllerLayoutWorkspace>(
                    restoredRuntime.GetControllerLayout());
                Assert.Equal(12, restoredWorkspace.GridColumns);
                Assert.Equal(10, restoredWorkspace.GridRows);
                Assert.False(restoredWorkspace.SnapToGrid);
                var restoredPlacement = restoredWorkspace.Controls.Single(control =>
                    control.ControlId == customControls[0].ControlId);
                Assert.Equal(37, restoredPlacement.X);
                Assert.Equal(49, restoredPlacement.Y);
                Assert.Equal(0.5, restoredPlacement.Width);
                Assert.Equal(0.75, restoredPlacement.Height);
                Assert.Equal("Purple", restoredPlacement.ColorKey);
                Assert.Equal(11, restoredPlacement.AnalogRawAtMinimum);
                Assert.Equal(119, restoredPlacement.AnalogRawAtMaximum);
                Assert.Equal(63, restoredPlacement.AnalogRawAtCenter);
                Assert.Equal(7.5, restoredPlacement.EncoderDegreesPerStep);
                Assert.True(restoredPlacement.EncoderReversed);
            }

            midiBackend.Publish(PackMidi(0xB0, 48, 100));
            Assert.Contains(controlUpdates, update =>
                update.ControlId == "winmm-midi:channel-1:cc-48:increase" &&
                update.IsPressed &&
                update.AnalogRawValue == 100 &&
                update.AnalogDelta == 1);

            runtime.IsRehearsal = false;
            midiBackend.Publish(PackMidi(0x90, 36, 100));
            midiBackend.Publish(PackMidi(0x80, 36, 0));

            Assert.Contains(controlUpdates, update =>
                !update.IsSnapshot &&
                update.ControlId == controlId &&
                update.DisplayLabel == "Pad R4 C5");

            var request = Assert.Single(actionOutput.Started);
            Assert.Equal(4, request.Sequence.Steps.Count);
            Assert.Equal(
                [ControllerActionStepType.KeyboardChord, ControllerActionStepType.Text,
                    ControllerActionStepType.Midi, ControllerActionStepType.Osc],
                request.Sequence.Steps.Select(step => step.Type));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Identify_confirm_map_and_unplug_releases_held_output()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)42, @"\\?\HID#VID_1234&PID_5678#PORT_A");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            long timestamp = 0;
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                timestampProvider: () => ++timestamp,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            var controlUpdates = new List<RuntimeControlUpdate>();
            runtime.StateChanged += (_, state) => latestState = state;
            runtime.ControlChanged += (_, update) => controlUpdates.Add(update);

            await runtime.InitializeAsync();
            Assert.Single(runtime.Devices);
            Assert.True(runtime.BeginIdentification(runtime.Devices[0]).Succeeded);

            host.Emit(Packet((nint)42, 0x4F, 0x61, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)42, 0x4F, 0x61, RawKeyboardFlags.Break));
            Assert.True(latestState?.CanConfirm);
            Assert.True(runtime.ConfirmController().Succeeded);

            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)42, 0x4F, 0x61, RawKeyboardFlags.Make));

            Assert.Single(output.Down);
            Assert.Empty(output.Up);
            Assert.Equal("F24", output.Down[0].Keys.Single().Value);

            enumerator.Remove((nint)42);
            host.EmitDevice((nint)42, RawInputDeviceChangeKind.Removal);

            Assert.Single(output.Up);
            Assert.Equal("F24", output.Up[0].Keys.Single().Value);
            Assert.Contains(controlUpdates, update => update.ControlId == controlId && !update.IsPressed);
            Assert.Contains("disconnected", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.False(latestState?.IsConfirmed);
            Assert.True(runtime.IsRehearsal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Expanded_assignment_dispatches_a_balanced_three_key_chord()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)43, @"\\?\HID#VID_1234&PID_5678#CHORD");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, descriptor.SessionHandle, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            var assignment = KeyboardMappingAssignment.PressOnce(
                "Save as — Ctrl + Shift + S", ["CTRL", "SHIFT", "S"]);

            Assert.True(runtime.AssignKeyboardMapping(controlId, assignment).Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Make));
            host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Break));

            Assert.Equal(["CTRL", "SHIFT", "S"], output.Down.Single().Keys.Select(key => key.Value));
            Assert.Equal(["S", "SHIFT", "CTRL"], output.Up.Single().Keys.Select(key => key.Value));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Expanded_assignment_rejects_unknown_keys_before_profile_mutation()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)44, @"\\?\HID#VID_1234&PID_5678#UNKNOWN_KEY");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, new RecordingOutput(), new AtomicProfileStore(root));

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, descriptor.SessionHandle, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;

            var result = runtime.AssignKeyboardMapping(
                controlId,
                KeyboardMappingAssignment.PressOnce("Unknown", ["NOT-A-KEY"]));

            Assert.False(result.Succeeded);
            Assert.Contains("unsupported", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Multi_step_assignment_dispatches_through_action_output_and_cleans_up_on_release()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)45, @"\\?\HID#VID_1234&PID_5678#ACTIONS");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor), host, keyboardIsNeutral: static () => true);
            var actions = new RecordingActionOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: actions);

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, descriptor.SessionHandle, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            var sequence = new ControllerActionSequenceDefinition
            {
                Name = "MIDI and OSC",
                Mode = ControllerActionSequenceMode.WhileHeld,
                Steps =
                [
                    new ControllerActionStepDefinition
                    {
                        Type = ControllerActionStepType.Midi,
                        Value = "note:1:60:100"
                    },
                    new ControllerActionStepDefinition
                    {
                        Type = ControllerActionStepType.Osc,
                        Target = "127.0.0.1",
                        Amount = 8000,
                        Value = "/tappy/g13",
                        Arguments = "1"
                    }
                ]
            };

            var unsafeRunOnce = sequence.Clone();
            unsafeRunOnce.Mode = ControllerActionSequenceMode.RunOnce;
            var unsafeResult = runtime.AssignControllerAction(controlId,
                new ControllerActionAssignment(
                    "Unsafe MIDI note",
                    unsafeRunOnce,
                    new ControllerActionSequenceDefinition()));
            Assert.False(unsafeResult.Succeeded);
            Assert.Contains("note-off", unsafeResult.Message, StringComparison.OrdinalIgnoreCase);

            var assigned = runtime.AssignControllerAction(controlId,
                new ControllerActionAssignment("MIDI and OSC", sequence, new ControllerActionSequenceDefinition()));
            runtime.IsRehearsal = false;
            host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Make));
            host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Break));

            Assert.True(assigned.Succeeded, assigned.Message);
            var request = Assert.Single(actions.Started);
            Assert.Equal(2, request.Sequence.Steps.Count);
            Assert.Equal(request.OwnerId, Assert.Single(actions.ReleasedOwners));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rehearsal_runs_recognition_without_calling_output()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)51, @"\\?\HID#VID_1000&PID_2000#PORT_B");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            long timestamp = 10;
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                timestampProvider: () => ++timestamp,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeControlUpdate? latestControl = null;
            runtime.ControlChanged += (_, update) => latestControl = update;

            await runtime.InitializeAsync();
            runtime.BeginIdentification(runtime.Devices[0]);
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Break));
            runtime.ConfirmController();
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x50).Value;
            runtime.AssignMapping(controlId, "F23");

            Assert.True(runtime.IsRehearsal);
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Make));
            host.Emit(Packet((nint)51, 0x50, 0x62, RawKeyboardFlags.Break));

            Assert.Empty(output.Down);
            Assert.Empty(output.Up);
            Assert.NotNull(latestControl);
            Assert.Equal(controlId, latestControl.ControlId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Identification_requires_a_complete_press_release_and_neutral_state_before_confirmation()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)52, @"\\?\HID#VID_1000&PID_2000#NEUTRAL_GATE");
            var host = new FakeMessageHost();
            var output = new RecordingOutput();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            var controlUpdates = new List<RuntimeControlUpdate>();
            runtime.StateChanged += (_, state) => latestState = state;
            runtime.ControlChanged += (_, update) => controlUpdates.Add(update);

            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            host.Emit(Packet((nint)52, 0x4F, 0x61, RawKeyboardFlags.Make));

            Assert.False(latestState?.CanConfirm ?? true);
            Assert.False(runtime.ConfirmController().Succeeded);
            Assert.Empty(output.Down);
            Assert.Empty(controlUpdates);

            host.Emit(Packet((nint)52, 0x4F, 0x61, RawKeyboardFlags.Break));
            Assert.True(latestState?.CanConfirm);
            Assert.True(runtime.ConfirmController().Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Identification_refuses_to_arm_until_windows_reports_all_keyboard_controls_released()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)53, @"\\?\HID#VID_1000&PID_2000#PRE_ARM_NEUTRAL");
            var host = new FakeMessageHost();
            var keyboardIsNeutral = false;
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: () => keyboardIsNeutral);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, new RecordingOutput(), new AtomicProfileStore(root));

            await runtime.InitializeAsync();

            var rejected = runtime.BeginIdentification(runtime.Devices.Single());
            Assert.False(rejected.Succeeded);
            Assert.Contains("Release every keyboard", rejected.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(provider.CaptureTarget);

            keyboardIsNeutral = true;
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            Assert.Equal(descriptor.SessionHandle, provider.CaptureTarget);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Emergency_stop_releases_disarms_and_restores_rehearsal()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)61, @"\\?\HID#VID_1000&PID_2000#EMERGENCY");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            var updates = new List<RuntimeControlUpdate>();
            runtime.StateChanged += (_, state) => latestState = state;
            runtime.ControlChanged += (_, update) => updates.Add(update);

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)61, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)61, 0x4F, 0x61, RawKeyboardFlags.Make));
            Assert.Single(output.Down);

            runtime.EmergencyStop("test");

            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.False(latestState?.IsIdentificationCaptureActive ?? true);
            Assert.Contains(updates, update => update.ControlId == controlId && !update.IsPressed);

            host.Emit(Packet((nint)61, 0x4F, 0x61, RawKeyboardFlags.Break));
            host.Emit(Packet((nint)61, 0x4F, 0x61, RawKeyboardFlags.Make));
            Assert.Single(output.Down);
            Assert.Single(output.Up);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Session_lock_releases_held_output_and_requires_reidentification()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)62, @"\\?\HID#VID_1000&PID_2000#LOCK");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)62, 0x50, 0x62);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x50).Value;
            runtime.AssignMapping(controlId, "F23");
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)62, 0x50, 0x62, RawKeyboardFlags.Make));

            host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);

            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.Contains("Nothing is armed", latestState?.Status ?? string.Empty, StringComparison.Ordinal);
            host.Emit(Packet((nint)62, 0x50, 0x62, RawKeyboardFlags.Make));
            Assert.Single(output.Down);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Wpf_application_shutdown_relay_reaches_the_same_held_output_cleanup()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)621, @"\\?\HID#VID_1000&PID_2000#APP_SHUTDOWN");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            var applicationLifecycle = new ApplicationLifecycleSignalSource();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root), applicationLifecycle);
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)621, 0x50, 0x62);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x50).Value;
            runtime.AssignMapping(controlId, "F23");
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)621, 0x50, 0x62, RawKeyboardFlags.Make));

            applicationLifecycle.Report(WindowsLifecycleSignal.ShutdownRequested);

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Manual_refresh_recovers_safely_when_a_removal_notification_was_missed()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)63, @"\\?\HID#VID_1000&PID_2000#MISSED_REMOVAL");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)63, 0x51, 0x63);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x51).Value;
            runtime.AssignMapping(controlId, "F22");
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)63, 0x51, 0x63, RawKeyboardFlags.Make));
            enumerator.Remove((nint)63);

            runtime.RefreshDevices();

            Assert.Empty(runtime.Devices);
            Assert.Single(output.Up);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.Contains("disappeared", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Removing_an_identification_candidate_cancels_capture_without_arming()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)64, @"\\?\HID#VID_1000&PID_2000#CANDIDATE");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                keyboardIsNeutral: static () => true);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, new RecordingOutput(), new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices[0]).Succeeded);
            Assert.True(latestState?.IsIdentificationCaptureActive);
            enumerator.Remove((nint)64);
            host.EmitDevice((nint)64, RawInputDeviceChangeKind.Removal);

            Assert.Empty(runtime.Devices);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.False(latestState?.IsIdentificationCaptureActive ?? true);
            Assert.False(runtime.ConfirmController().Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_profile_reloads_identity_layout_layers_and_mapping_for_a_new_session_handle()
    {
        var root = NewTemporaryDirectory();
        const string path = @"\\?\HID#VID_1234&PID_5678#STABLE_PORT";
        try
        {
            var firstDescriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)71, path);
            var firstHost = new FakeMessageHost();
            var firstProvider = new RawInputKeyboardProvider(
                new FakeEnumerator(firstDescriptor),
                firstHost,
                keyboardIsNeutral: static () => true);
            await using (var firstRuntime = new DeviceAwareControllerRuntime(
                             firstProvider, new RecordingOutput(), new AtomicProfileStore(root)))
            {
                await firstRuntime.InitializeAsync();
                IdentifyAndConfirm(firstRuntime, firstHost, (nint)71, 0x52, 0x60);
                var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x52).Value;
                firstHost.Emit(Packet((nint)71, 0x52, 0x60, RawKeyboardFlags.Make));
                firstHost.Emit(Packet((nint)71, 0x52, 0x60, RawKeyboardFlags.Break));
                Assert.True(firstRuntime.AssignMapping(controlId, "F21").Succeeded);
                Assert.True((await firstRuntime.SaveProfileAsync()).Succeeded);
            }

            var saved = await new AtomicProfileStore(root).LoadAsync("default");
            var savedController = saved.FindController(firstDescriptor.PersistentId);
            var savedControlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x52);
            Assert.NotNull(savedController);
            Assert.Equal(3, savedController.Layers.Count);
            Assert.Contains(savedController.Layout.Rows.SelectMany(row => row.Controls),
                control => control.ControlId == savedControlId);
            Assert.Equal("F21", savedController.Layers[0].FindBinding(savedControlId)?.PressAction.Keys.Single().Value);

            var secondDescriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)72, path);
            var secondHost = new FakeMessageHost();
            var secondOutput = new RecordingOutput();
            var secondProvider = new RawInputKeyboardProvider(
                new FakeEnumerator(secondDescriptor),
                secondHost,
                keyboardIsNeutral: static () => true);
            await using var secondRuntime = new DeviceAwareControllerRuntime(
                secondProvider, secondOutput, new AtomicProfileStore(root));
            var restoredControls = new List<RuntimeControlUpdate>();
            secondRuntime.ControlChanged += (_, update) => restoredControls.Add(update);
            await secondRuntime.InitializeAsync();
            IdentifyAndConfirm(secondRuntime, secondHost, (nint)72, 0x52, 0x60);
            Assert.Contains(restoredControls, update =>
                update.ControlId == savedControlId.Value &&
                update.DisplayLabel.Contains("Numpad 0", StringComparison.Ordinal) &&
                update.AssignedAction == "Hold F21 until release" &&
                !update.IsPressed);
            secondRuntime.IsRehearsal = false;

            secondHost.Emit(Packet((nint)72, 0x52, 0x60, RawKeyboardFlags.Make));
            secondHost.Emit(Packet((nint)72, 0x52, 0x60, RawKeyboardFlags.Break));

            Assert.Single(secondOutput.Down);
            Assert.Single(secondOutput.Up);
            Assert.Equal("F21", secondOutput.Down[0].Keys.Single().Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Self_marked_input_is_invisible_to_mapping_and_ui()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)73, @"\\?\HID#VID_1000&PID_2000#SELF_MARKER");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            var updates = new List<RuntimeControlUpdate>();
            runtime.ControlChanged += (_, update) => updates.Add(update);

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)73, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            runtime.AssignMapping(controlId, "F24");
            runtime.IsRehearsal = false;
            updates.Clear();

            host.Emit(Packet(
                (nint)73, 0x4F, 0x61, RawKeyboardFlags.Make,
                Tappy.Windows.Output.InjectedInputMarker.Value));

            Assert.Empty(output.Down);
            Assert.Empty(output.Up);
            Assert.Empty(updates);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("emergency")]
    [InlineData("lifecycle")]
    [InlineData("unplug")]
    [InlineData("refresh")]
    [InlineData("identify")]
    public async Task Rejected_cleanup_release_is_reported_and_prevents_rearming(string cleanupPath)
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)81, @"\\?\HID#VID_1000&PID_2000#REJECTED_RELEASE");
            var enumerator = new FakeEnumerator(descriptor);
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                enumerator,
                host,
                keyboardIsNeutral: static () => true);
            var output = new RejectingReleaseOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));
            RuntimeState? latestState = null;
            runtime.StateChanged += (_, state) => latestState = state;

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, (nint)81, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet((nint)81, 0x4F, 0x61, RawKeyboardFlags.Make));
            output.RejectKeyUp = true;

            RuntimeOperation? emergencyResult = null;
            switch (cleanupPath)
            {
                case "emergency":
                    emergencyResult = runtime.EmergencyStop("test rejection");
                    break;
                case "lifecycle":
                    host.EmitLifecycle(WindowsLifecycleSignal.SessionLocked);
                    break;
                case "unplug":
                    enumerator.Remove((nint)81);
                    host.EmitDevice((nint)81, RawInputDeviceChangeKind.Removal);
                    break;
                case "refresh":
                    enumerator.Remove((nint)81);
                    runtime.RefreshDevices();
                    break;
                case "identify":
                    var identify = runtime.BeginIdentification(runtime.Devices.Single());
                    Assert.False(identify.Succeeded);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown cleanup path: {cleanupPath}");
            }

            // A rejected release remains owned, so the subsequent disarm can retry it.
            Assert.InRange(output.KeyUpAttempts, 1, 2);
            Assert.True(runtime.IsRehearsal);
            Assert.False(latestState?.IsConfirmed ?? true);
            Assert.Contains("cannot confirm", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restart Tappy", latestState?.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Effective: Needs attention (fail-open)", latestState?.EffectiveSourceLabel);
            runtime.IsRehearsal = false;
            Assert.True(runtime.IsRehearsal);
            if (emergencyResult is not null)
            {
                Assert.False(emergencyResult.Succeeded);
                Assert.Equal(latestState?.Status, emergencyResult.Message);
            }

            if (runtime.Devices.Count > 0)
            {
                var rearm = runtime.BeginIdentification(runtime.Devices[0]);
                Assert.False(rearm.Succeeded);
                Assert.Contains("Restart Tappy", rearm.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_records_rejected_cleanup_once_and_exposes_unsafe_output_state()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)82, @"\\?\HID#VID_1000&PID_2000#DISPOSE_REJECTION");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RejectingReleaseOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, descriptor.SessionHandle, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Make));
            output.RejectKeyUp = true;

            await runtime.DisposeAsync();
            await runtime.DisposeAsync();

            Assert.Equal(1, output.KeyUpAttempts);
            Assert.False(runtime.IsOutputStateConfirmedSafe);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_disarms_before_cleanup_can_reenter_input()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)83, @"\\?\HID#VID_1000&PID_2000#DISPOSE_REENTRY");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new ReentrantReleaseOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider, output, new AtomicProfileStore(root));

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, descriptor.SessionHandle, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Make));
            output.DuringKeyUp = () =>
            {
                host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Break));
                host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Make));
            };

            await runtime.DisposeAsync();

            Assert.Equal(1, output.KeyDownAttempts);
            Assert.Equal(1, output.KeyUpAttempts);
            Assert.True(runtime.IsOutputStateConfirmedSafe);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_waits_for_mapped_input_critical_section_then_releases_its_output()
    {
        var root = NewTemporaryDirectory();
        using var callbackInsideGate = new ManualResetEventSlim();
        using var allowCallback = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        var barrierArmed = 0;
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)84, @"\\?\HID#VID_1000&PID_2000#DISPOSE_BARRIER");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            var output = new RecordingOutput();
            await using var runtime = new DeviceAwareControllerRuntime(
                provider,
                output,
                new AtomicProfileStore(root),
                beforeEngineProcess: () =>
                {
                    if (Volatile.Read(ref barrierArmed) == 0)
                    {
                        return;
                    }

                    callbackInsideGate.Set();
                    allowCallback.Wait();
                },
                beforeDisposeGate: disposeStarted.Set);

            await runtime.InitializeAsync();
            IdentifyAndConfirm(runtime, host, descriptor.SessionHandle, 0x4F, 0x61);
            var controlId = Tappy.Core.Input.ControlId.FromRawInputKeyboard(0x4F).Value;
            Assert.True(runtime.AssignMapping(controlId, "F24").Succeeded);
            runtime.IsRehearsal = false;
            Volatile.Write(ref barrierArmed, 1);

            var inputTask = Task.Run(() =>
                host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Make)));
            Assert.True(callbackInsideGate.Wait(TimeSpan.FromSeconds(5)));
            var disposeTask = Task.Run(async () => await runtime.DisposeAsync());
            Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(disposeTask.IsCompleted);

            allowCallback.Set();
            await Task.WhenAll(inputTask, disposeTask);

            Assert.Single(output.Down);
            Assert.Single(output.Up);
            Assert.True(runtime.IsOutputStateConfirmedSafe);
        }
        finally
        {
            allowCallback.Set();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_waits_for_identification_input_critical_section_then_resets_activation()
    {
        var root = NewTemporaryDirectory();
        using var callbackInsideGate = new ManualResetEventSlim();
        using var allowCallback = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        var barrierArmed = 0;
        try
        {
            var descriptor = DeviceDescriptorSanitizer.CreateKeyboard(
                (nint)85, @"\\?\HID#VID_1000&PID_2000#IDENTIFICATION_DISPOSE_BARRIER");
            var host = new FakeMessageHost();
            var provider = new RawInputKeyboardProvider(
                new FakeEnumerator(descriptor),
                host,
                keyboardIsNeutral: static () => true);
            await using var runtime = new DeviceAwareControllerRuntime(
                provider,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                beforeEngineProcess: () =>
                {
                    if (Volatile.Read(ref barrierArmed) == 0)
                    {
                        return;
                    }

                    callbackInsideGate.Set();
                    allowCallback.Wait();
                },
                beforeDisposeGate: disposeStarted.Set);

            await runtime.InitializeAsync();
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            Volatile.Write(ref barrierArmed, 1);

            var inputTask = Task.Run(() =>
                host.Emit(Packet(descriptor.SessionHandle, 0x4F, 0x61, RawKeyboardFlags.Make)));
            Assert.True(callbackInsideGate.Wait(TimeSpan.FromSeconds(5)));
            var disposeTask = Task.Run(async () => await runtime.DisposeAsync());
            Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(disposeTask.IsCompleted);

            allowCallback.Set();
            await Task.WhenAll(inputTask, disposeTask);

            Assert.False(runtime.CanConfirmController);
            Assert.False(runtime.IsIdentificationCaptureActive);
            Assert.True(runtime.IsOutputStateConfirmedSafe);
        }
        finally
        {
            allowCallback.Set();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void IdentifyAndConfirm(
        DeviceAwareControllerRuntime runtime,
        FakeMessageHost host,
        nint handle,
        ushort scan,
        ushort virtualKey)
    {
        Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
        host.Emit(Packet(handle, scan, virtualKey, RawKeyboardFlags.Make));
        host.Emit(Packet(handle, scan, virtualKey, RawKeyboardFlags.Break));
        Assert.True(runtime.ConfirmController().Succeeded);
    }

    private static RawKeyboardPacket Packet(
        nint handle,
        ushort scan,
        ushort virtualKey,
        RawKeyboardFlags flags,
        uint extraInformation = 0) =>
        new(handle, scan, flags, 0, virtualKey,
            flags.HasFlag(RawKeyboardFlags.Break) ? 0x101u : 0x100u,
            extraInformation);

    private static uint PackMidi(byte status, byte data1, byte data2) =>
        status | ((uint)data1 << 8) | ((uint)data2 << 16);

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Tappy-AppTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeEnumerator(params SanitizedDeviceDescriptor[] devices) : IRawInputDeviceEnumerator
    {
        private readonly Dictionary<nint, SanitizedDeviceDescriptor> _devices =
            devices.ToDictionary(device => device.SessionHandle);

        public IReadOnlyList<SanitizedDeviceDescriptor> EnumerateKeyboards() => _devices.Values.ToArray();

        public SanitizedDeviceDescriptor? DescribeKeyboard(nint deviceHandle) =>
            _devices.GetValueOrDefault(deviceHandle);

        public void Remove(nint handle) => _devices.Remove(handle);
    }

    private sealed class FakeMessageHost : IRawInputMessageHost
    {
        public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;
        public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;
        public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;
        public event EventHandler<Exception>? Faulted;
        public bool IsRunning { get; private set; }
        public nint WindowHandle => (nint)99;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Emit(RawKeyboardPacket packet) =>
            KeyboardPacketReceived?.Invoke(this, new RawKeyboardPacketEventArgs(packet));

        public void EmitDevice(nint handle, RawInputDeviceChangeKind kind) =>
            DeviceChanged?.Invoke(this, new NativeDeviceChangeEventArgs(handle, kind));

        public void EmitLifecycle(WindowsLifecycleSignal signal) =>
            LifecycleChanged?.Invoke(this, new WindowsLifecycleSignalEventArgs(signal));

        public void EmitFault(Exception exception) => Faulted?.Invoke(this, exception);

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeMidiBackend(params WinMmMidiInputDevice[] devices) : IWinMmMidiInputBackend
    {
        private readonly IReadOnlyList<WinMmMidiInputDevice> _devices = devices;

        public event Action<WinMmMidiInputMessage>? ShortMessageReceived;
        public event EventHandler<Exception>? Faulted { add { } remove { } }
        public long SessionGeneration { get; private set; }

        public bool IsOpen { get; private set; }

        public IReadOnlyList<WinMmMidiInputDevice> EnumerateDevices() => _devices;

        public void Open(int deviceId)
        {
            Assert.Contains(_devices, device => device.DeviceId == deviceId);
            SessionGeneration++;
            IsOpen = true;
        }

        public void Start() => Assert.True(IsOpen);

        public void Stop()
        {
        }

        public void Close() => IsOpen = false;

        public void Publish(uint message)
        {
            Assert.True(IsOpen);
            ShortMessageReceived?.Invoke(new WinMmMidiInputMessage(SessionGeneration, message));
        }

        public void Dispose() => IsOpen = false;
    }

    private sealed class RecordingOutput : IKeyboardOutput
    {
        public List<KeyboardOutputRequest> Down { get; } = [];
        public List<KeyboardOutputRequest> Up { get; } = [];

        public void KeyDown(KeyboardOutputRequest request) => Down.Add(request);
        public void KeyUp(KeyboardOutputRequest request) => Up.Add(request);
    }

    private sealed class RejectingReleaseOutput : IKeyboardOutput
    {
        public bool RejectKeyUp { get; set; }

        public int KeyUpAttempts { get; private set; }

        public void KeyDown(KeyboardOutputRequest request)
        {
        }

        public void KeyUp(KeyboardOutputRequest request)
        {
            KeyUpAttempts++;
            if (RejectKeyUp)
            {
                throw new InvalidOperationException("Simulated Windows output rejection.");
            }
        }
    }

    private sealed class ReentrantReleaseOutput : IKeyboardOutput
    {
        private bool _invoked;

        public int KeyDownAttempts { get; private set; }

        public int KeyUpAttempts { get; private set; }

        public Action? DuringKeyUp { get; set; }

        public void KeyDown(KeyboardOutputRequest request) => KeyDownAttempts++;

        public void KeyUp(KeyboardOutputRequest request)
        {
            KeyUpAttempts++;
            if (!_invoked)
            {
                _invoked = true;
                DuringKeyUp?.Invoke();
            }
        }
    }

    private sealed class RecordingActionOutput : IControllerActionOutput
    {
        public List<ControllerActionOutputRequest> Started { get; } = [];
        public List<string> ReleasedOwners { get; } = [];

        public event EventHandler<ControllerActionOutputFault>? Faulted;

        public bool Start(ControllerActionOutputRequest request)
        {
            Started.Add(request);
            return true;
        }

        public bool ReleaseOwner(string ownerId)
        {
            ReleasedOwners.Add(ownerId);
            return true;
        }

        public bool ReleaseScope(string scopeId) => true;
        public bool ReleaseAll() => true;

        public void RaiseFault(ControllerActionOutputRequest request, Exception exception) =>
            Faulted?.Invoke(this, new ControllerActionOutputFault(
                request.OwnerId, request.ScopeId, exception.Message, exception));
    }
}
