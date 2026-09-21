using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Models;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class MidiControllerModelCatalogTests
{
    [Fact]
    public void APC_MINI_v1_catalog_has_complete_unique_layout_and_friendly_labels()
    {
        var model = Assert.IsType<MidiControllerModelDefinition>(
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, "MIDI: APC MINI"));

        Assert.Equal(MidiControllerModelCatalog.AkaiApcMiniV1Id, model.Id);
        Assert.Equal(99, model.Controls.Count);
        Assert.Equal(99, model.Controls.Select(item => item.ControlId).Distinct().Count());
        Assert.Equal(99, model.Layout.Rows.SelectMany(row => row.Controls).Count());
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-57" &&
            item.Label == "Pad R1 C2");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-98" &&
            item.Label == "Shift");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:cc-56:decrease" &&
            item.Label == "Master fader ↓");
    }

    [Theory]
    [InlineData("MIDI: APC MINI (device 2)")]
    [InlineData("midi: apc mini")]
    public void APC_MINI_duplicate_suffix_and_case_are_recognized(string displayName)
    {
        Assert.NotNull(MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, displayName));
    }

    [Theory]
    [InlineData("MIDI: APC mini mk2")]
    [InlineData("MIDI: APC KEY 25")]
    [InlineData("MIDI: Launchpad Mini")]
    public void Similar_or_unverified_models_are_not_misidentified(string displayName)
    {
        Assert.Null(MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, displayName));
    }

    [Fact]
    public void Akai_Fire_catalog_has_complete_unique_button_pad_and_encoder_layout()
    {
        var model = Assert.IsType<MidiControllerModelDefinition>(
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, "MIDI: FL STUDIO FIRE"));

        Assert.Equal(MidiControllerModelCatalog.AkaiFireV1Id, model.Id);
        // 20 buttons + 64 pads + 5 encoders x (1 touch + 2 rotation directions).
        Assert.Equal(99, model.Controls.Count);
        Assert.Equal(99, model.Controls.Select(item => item.ControlId).Distinct().Count());
        Assert.Equal(99, model.Layout.Rows.SelectMany(row => row.Controls).Count());
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-54" &&
            item.Label == "Pad R1 C1");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-117" &&
            item.Label == "Pad R4 C16");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-48" &&
            item.Label == "Shift");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-26" &&
            item.Label == "Modes / Bank");

        // Touch = ordinary Note On/Off; rotation = relative CC (see below).
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-16" && item.Label == "Volume touch");
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:cc-16:increase" && item.Kind == LayoutControlKind.Encoder);
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:cc-16:decrease" && item.Kind == LayoutControlKind.Encoder);
        Assert.Contains(model.Controls, item =>
            item.ControlId.Value == "winmm-midi:channel-1:note-25" && item.Label == "Select touch");
        Assert.Contains(model.Controls, item => item.ControlId.Value == "winmm-midi:channel-1:cc-118:increase");
        Assert.Contains(model.Controls, item => item.ControlId.Value == "winmm-midi:channel-1:cc-118:decrease");

        Assert.Equal([16, 17, 18, 19, 118], model.RelativeControlChangeNumbers.Order());
    }

    [Fact]
    public void Akai_Fire_default_layout_mirrors_physical_shape_without_overlap()
    {
        var model = Assert.IsType<MidiControllerModelDefinition>(
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, "MIDI: FL STUDIO FIRE"));
        var placed = model.Layout.Rows.SelectMany(row => row.Controls).ToArray();
        Assert.Equal(99, placed.Length);
        Assert.All(placed, control =>
        {
            Assert.NotNull(control.X);
            Assert.NotNull(control.Y);
        });

        static double CellX(int column) => column * MainViewModel.LayoutCellWidth;
        static double CellY(int row) => row * MainViewModel.LayoutCellHeight;
        static string NoteId(int note) => $"winmm-midi:channel-1:note-{note}";
        var byId = placed.ToDictionary(item => item.ControlId!.Value.Value, StringComparer.Ordinal);

        // Row 0 is the encoder strip, on its own, above everything else — not
        // mixed into the pad grid. Each encoder's increase/touch/decrease share
        // one column — the workspace renders them as one concentric ring
        // cluster in the same spot, not three tiles side by side (see
        // MainViewModel's encoder-cluster detection).
        Assert.Equal(CellX(2), byId["winmm-midi:channel-1:cc-16:increase"].X!.Value); // Volume ▲
        Assert.Equal(CellX(2), byId[NoteId(0x10)].X!.Value); // Volume touch
        Assert.Equal(CellY(0), byId[NoteId(0x10)].Y!.Value);
        Assert.Equal(CellX(2), byId["winmm-midi:channel-1:cc-16:decrease"].X!.Value); // Volume ▼
        Assert.Equal(CellX(6), byId["winmm-midi:channel-1:note-25"].X!.Value); // Select touch

        // Row 1: the top button strip, one full row below the encoders.
        Assert.Equal(CellX(0), byId[NoteId(0x1A)].X!.Value); // Modes / Bank
        Assert.Equal(CellY(1), byId[NoteId(0x1A)].Y!.Value);
        Assert.Equal(CellX(16), byId[NoteId(0x22)].X!.Value); // Grid Left, near the right edge

        // Mute/Solo strip sits at rows 2-5, column 0, with an empty column (1)
        // between it and the pad grid so it reads as a separate group.
        Assert.Equal(CellX(0), byId[NoteId(0x24)].X!.Value); // Mute/Solo 1
        Assert.Equal(CellY(2), byId[NoteId(0x24)].Y!.Value);
        // Column 1 is an empty gap only across the Mute/Solo + pad rows (2-5) —
        // the bottom button strip legitimately uses column 1 (Note/Snap).
        Assert.DoesNotContain(placed, control =>
            control.X == CellX(1) && control.Y >= CellY(2) && control.Y <= CellY(5));

        // The 64-pad grid occupies columns 2-17, rows 2-5, in physical reading order.
        Assert.Equal(CellX(2), byId[NoteId(0x36)].X!.Value); // Pad R1 C1
        Assert.Equal(CellY(2), byId[NoteId(0x36)].Y!.Value);
        Assert.Equal(CellX(17), byId[NoteId(0x45)].X!.Value); // Pad R1 C16
        Assert.Equal(CellX(17), byId[NoteId(0x75)].X!.Value); // Pad R4 C16
        Assert.Equal(CellY(5), byId[NoteId(0x75)].Y!.Value);

        // Bottom strip: mode buttons at the left, transport buttons at the right.
        Assert.Equal(CellX(0), byId[NoteId(0x2C)].X!.Value); // Step
        Assert.Equal(CellY(6), byId[NoteId(0x2C)].Y!.Value);
        Assert.Equal(CellX(16), byId[NoteId(0x35)].X!.Value); // Record

        // Each encoder's increase/touch/decrease are meant to coincide exactly
        // (one concentric cluster, not three tiles) — the overlap check below
        // must skip pairs that are cluster-mates rather than flag them.
        int[] encoderTouchNotes = [0x10, 0x11, 0x12, 0x13, 0x19];
        int[] encoderRotationCcs = [0x10, 0x11, 0x12, 0x13, 0x76];
        var clusterIndexByControlId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < encoderTouchNotes.Length; index++)
        {
            clusterIndexByControlId[NoteId(encoderTouchNotes[index])] = index;
            clusterIndexByControlId[$"winmm-midi:channel-1:cc-{encoderRotationCcs[index]}:increase"] = index;
            clusterIndexByControlId[$"winmm-midi:channel-1:cc-{encoderRotationCcs[index]}:decrease"] = index;
        }

        static bool AreClusterMates(
            LayoutControlDefinition a, LayoutControlDefinition b, Dictionary<string, int> clusterIndexByControlId) =>
            clusterIndexByControlId.TryGetValue(a.ControlId!.Value.Value, out var clusterA) &&
            clusterIndexByControlId.TryGetValue(b.ControlId!.Value.Value, out var clusterB) &&
            clusterA == clusterB;

        const double tileWidth = 134, tileHeight = 106; // MainViewModel.DefaultTileWidth/Height
        foreach (var control in placed)
        {
            foreach (var other in placed.Where(candidate =>
                candidate != control && !AreClusterMates(control, candidate, clusterIndexByControlId)))
            {
                Assert.False(
                    control.X!.Value < other.X!.Value + tileWidth && other.X!.Value < control.X!.Value + tileWidth &&
                    control.Y!.Value < other.Y!.Value + tileHeight && other.Y!.Value < control.Y!.Value + tileHeight,
                    $"Overlapping Fire default tiles: {control.ControlId}, {other.ControlId}");
            }
        }
    }

    [Theory]
    [InlineData("MIDI: FL STUDIO FIRE (device 2)")]
    [InlineData("midi: fl studio fire")]
    public void Akai_Fire_duplicate_suffix_and_case_are_recognized(string displayName)
    {
        Assert.NotNull(MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, displayName));
    }

    [Theory]
    [InlineData("MIDI: Akai Fire")]
    [InlineData("MIDI: Fire")]
    [InlineData("MIDI: APC MINI")]
    public void Akai_Fire_similar_names_are_not_misidentified(string displayName)
    {
        // Only the confirmed exact WinMM name "FL STUDIO FIRE" (see
        // docs/AKAI_FIRE.md) matches; plausible-looking variants must not.
        Assert.NotEqual(
            MidiControllerModelCatalog.AkaiFireV1Id,
            MidiControllerModelCatalog.Find(WinMmMidiInputProvider.ProviderId, displayName)?.Id);
    }
}
