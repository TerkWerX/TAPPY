using System.Text.RegularExpressions;
using Tappy.App.ViewModels;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Windows.Input;

namespace Tappy.App.Services;

public sealed record MidiControllerControlDefinition(
    ControlId ControlId,
    string Label,
    LayoutControlKind Kind,
    string Cluster,
    double? X = null,
    double? Y = null);

public sealed record MidiControllerModelDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<MidiControllerControlDefinition> Controls,
    ControllerLayoutDefinition Layout,
    IReadOnlyList<int>? RelativeControlChangeNumbers = null)
{
    /// <summary>CC numbers (any channel) that must be decoded as relative/signed
    /// values rather than absolute-diffed. See WinMmMidiInputProvider.SetRelativeControlChanges.</summary>
    public IReadOnlyList<int> RelativeControlChangeNumbers { get; init; } = RelativeControlChangeNumbers ?? [];
}

/// <summary>
/// Code-defined layouts are enabled only for exact, reviewed MIDI model names.
/// Unknown MIDI devices retain Tappy's learn-as-you-press layout.
/// </summary>
public static partial class MidiControllerModelCatalog
{
    public const string AkaiApcMiniV1Id = "akai-apc-mini-v1";
    public const string AkaiApcMiniV1LayoutId = "akai-apc-mini-v1-code-layout-v1";
    public const string AkaiFireV1Id = "akai-fire-v1";
    // Bumped from v1 to v2 when the encoders moved to their own row, separate
    // from the pad grid (see D-022 in DECISIONS.md) — a saved layout still
    // tagged v1 predates that shape and must not have its per-control ordering
    // reused, or the fresh v2 row/column structure gets filled with whatever
    // control happened to occupy the same numeric slot under the old shape.
    public const string AkaiFireV1LayoutId = "akai-fire-v1-code-layout-v2";

    private static readonly MidiControllerModelDefinition AkaiApcMiniV1 = CreateAkaiApcMiniV1();
    private static readonly MidiControllerModelDefinition AkaiFireV1 = CreateAkaiFireV1();

    public static MidiControllerModelDefinition? Find(string? providerId, string? displayName)
    {
        if (!string.Equals(providerId, WinMmMidiInputProvider.ProviderId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var modelName = displayName.Trim();
        if (modelName.StartsWith("MIDI:", StringComparison.OrdinalIgnoreCase))
        {
            modelName = modelName[5..].Trim();
        }

        modelName = DuplicateDeviceSuffix().Replace(modelName, string.Empty).Trim();
        // A second simultaneously-connected Fire uses a leading "N - " prefix
        // instead of Tappy's own "(device N)" suffix convention — confirmed by
        // the owner connecting two real Fire units on TESTBED, 2026-09-20, which
        // enumerated as "2 - FL STUDIO FIRE" (see docs/AKAI_FIRE.md).
        modelName = DuplicateDevicePrefix().Replace(modelName, string.Empty).Trim();
        if (string.Equals(modelName, "APC MINI", StringComparison.OrdinalIgnoreCase))
        {
            return AkaiApcMiniV1;
        }

        // Confirmed by direct WinMM enumeration against a real connected Fire on
        // TESTBED, 2026-09-19 (USB 09E8:0043) — see docs/AKAI_FIRE.md.
        if (string.Equals(modelName, "FL STUDIO FIRE", StringComparison.OrdinalIgnoreCase))
        {
            return AkaiFireV1;
        }

        return null;
    }

    private static MidiControllerModelDefinition CreateAkaiApcMiniV1()
    {
        var rows = new List<LayoutRowDefinition>();
        var controls = new List<MidiControllerControlDefinition>();

        for (var row = 0; row < 8; row++)
        {
            var noteStart = 56 - (row * 8);
            rows.Add(Row(
                $"pad-row-{row + 1}",
                Enumerable.Range(0, 8).Select(column =>
                    Control(
                        Note(noteStart + column),
                        $"Pad R{row + 1} C{column + 1}",
                        LayoutControlKind.Key,
                        "8×8 pad matrix"))));
        }

        rows.Add(Row(
            "clip-stop-row",
            Enumerable.Range(0, 8).Select(index =>
                Control(Note(64 + index), $"Clip stop {index + 1}", LayoutControlKind.Button, "Clip stop"))));

        var sceneLabels = new[]
        {
            "Scene: Clip stop", "Scene: Solo", "Scene: Record arm", "Scene: Mute",
            "Scene: Select", "Scene 6", "Scene 7", "Scene: Stop all clips",
        };
        var sceneControls = Enumerable.Range(0, 8)
            .Select(index => Control(Note(82 + index), sceneLabels[index], LayoutControlKind.Button, "Scene launch"))
            .Append(Control(Note(98), "Shift", LayoutControlKind.Button, "Shift"));
        rows.Add(Row("scene-and-shift", sceneControls));

        rows.Add(Row(
            "fader-increase",
            Enumerable.Range(0, 9).Select(index =>
                Control(
                    ControlChange(48 + index, increase: true),
                    index == 8 ? "Master fader ↑" : $"Fader {index + 1} ↑",
                    LayoutControlKind.Axis,
                    "Faders"))));
        rows.Add(Row(
            "fader-decrease",
            Enumerable.Range(0, 9).Select(index =>
                Control(
                    ControlChange(48 + index, increase: false),
                    index == 8 ? "Master fader ↓" : $"Fader {index + 1} ↓",
                    LayoutControlKind.Axis,
                    "Faders"))));

        var layout = new ControllerLayoutDefinition
        {
            Id = AkaiApcMiniV1LayoutId,
            Name = "Akai Professional APC MINI v1 code-rendered layout",
            Rows = rows,
        };
        layout.Normalize();
        return new MidiControllerModelDefinition(
            AkaiApcMiniV1Id,
            "Akai Professional APC MINI v1",
            controls,
            layout);

        LayoutRowDefinition Row(string id, IEnumerable<MidiControllerControlDefinition> rowControls)
        {
            var items = rowControls.ToArray();
            controls.AddRange(items);
            return new LayoutRowDefinition
            {
                Id = id,
                Controls = items.Select(item => new LayoutControlDefinition
                {
                    ControlId = item.ControlId,
                    Label = item.Label,
                    Kind = item.Kind,
                    Cluster = item.Cluster,
                }).ToList(),
            };
        }

        static MidiControllerControlDefinition Control(
            ControlId id,
            string label,
            LayoutControlKind kind,
            string cluster) => new(id, label, kind, cluster);
    }

    /// <summary>
    /// Buttons, pads and the five touch-capacitive encoders. See docs/AKAI_FIRE.md.
    /// Encoder rotation is relative/signed, not absolute — WinMmMidiInputProvider
    /// must be told which CC numbers on the confirmed device decode that way; see
    /// RelativeControlChangeNumbers below and its use in DeviceAwareControllerRuntime.
    ///
    /// Every control gets an explicit initial (column, row) position. Column 0 is
    /// the Mute/Solo strip left of the pads, column 1 is a deliberately empty gap
    /// (so Mute/Solo reads as a separate group, not more pad-grid squares), and
    /// columns 2–17 are the 16 pad columns. Row 0 is the encoder strip
    /// (increase/touch/decrease triplets, touch in the middle — not the encoders'
    /// actual physical row, deliberately placed on its own row with the whole top
    /// button strip as a buffer before the pads, so they read as a distinct
    /// control group rather than 15 more squares in the 64-pad grid). Row 1 is the
    /// top button strip, rows 2–5 are the four pad rows, row 6 is the bottom
    /// button strip. Without explicit positions the
    /// generic layout engine packs each code-defined row tightly from column 0 in
    /// declaration order, which looks nothing like the physical layout. Button/pad
    /// positions are measured from the same processed photo as the hotspots in
    /// ControllerPhotoCatalog; the encoder strip has no photo hotspot yet (visual
    /// rotation feedback needs one — a follow-up) so its exact column only needs to
    /// be reasonable, not photo-accurate. All positions are still only a starting
    /// arrangement — the user can freely drag/resize afterward.
    /// </summary>
    private static MidiControllerModelDefinition CreateAkaiFireV1()
    {
        var rows = new List<LayoutRowDefinition>();
        var controls = new List<MidiControllerControlDefinition>();

        var encoders = new (string Label, int TouchNote, int RotationCc)[]
        {
            ("Volume", 0x10, 0x10),
            ("Pan", 0x11, 0x11),
            ("Filter", 0x12, 0x12),
            ("Resonance", 0x13, 0x13),
            ("Select", 0x19, 0x76),
        };
        for (var index = 0; index < encoders.Length; index++)
        {
            var (label, touchNote, rotationCc) = encoders[index];
            // All three of one encoder's controls share a single column: the
            // workspace renders them as one concentric ring cluster in the
            // same spot, not three tiles side by side (see MainViewModel's
            // encoder-cluster detection), so a cluster only needs one slot.
            var column = 2 + index;
            rows.Add(Row(
                $"encoder-{index + 1}",
                [
                    Control(ControlChange(rotationCc, increase: true), $"{label} ▲",
                        LayoutControlKind.Encoder, "Encoders", column, 0),
                    Control(Note(touchNote), $"{label} touch", LayoutControlKind.Button, "Encoders", column, 0),
                    Control(ControlChange(rotationCc, increase: false), $"{label} ▼",
                        LayoutControlKind.Encoder, "Encoders", column, 0),
                ]));
        }

        rows.Add(Row(
            "modes-and-nav",
            [
                Control(Note(0x1A), "Modes / Bank", LayoutControlKind.Button, "Modes and navigation", 0, 1),
                Control(Note(0x1F), "Pattern ▼", LayoutControlKind.Button, "Modes and navigation", 10, 1),
                Control(Note(0x20), "Pattern ▲", LayoutControlKind.Button, "Modes and navigation", 11, 1),
                Control(Note(0x21), "Browser", LayoutControlKind.Button, "Modes and navigation", 13, 1),
                Control(Note(0x22), "Grid Left", LayoutControlKind.Button, "Modes and navigation", 16, 1),
                Control(Note(0x23), "Grid Right", LayoutControlKind.Button, "Modes and navigation", 17, 1),
            ]));

        for (var row = 0; row < 4; row++)
        {
            var noteStart = 0x36 + (row * 16);
            rows.Add(Row(
                $"pad-row-{row + 1}",
                Enumerable.Range(0, 16).Select(column =>
                    Control(
                        Note(noteStart + column),
                        $"Pad R{row + 1} C{column + 1}",
                        LayoutControlKind.Key,
                        "4×16 pad matrix",
                        column + 2,
                        row + 2))));
        }

        rows.Add(Row(
            "mute-solo",
            Enumerable.Range(0, 4).Select(index =>
                Control(Note(0x24 + index), $"Mute/Solo {index + 1}", LayoutControlKind.Button, "Mute/Solo",
                    0, index + 2))));

        rows.Add(Row(
            "mode-buttons",
            [
                Control(Note(0x2C), "Step / Accent", LayoutControlKind.Button, "Mode buttons", 0, 6),
                Control(Note(0x2D), "Note / Snap", LayoutControlKind.Button, "Mode buttons", 1, 6),
                Control(Note(0x2E), "Drum / Tap", LayoutControlKind.Button, "Mode buttons", 2, 6),
                Control(Note(0x2F), "Perform / Overview", LayoutControlKind.Button, "Mode buttons", 3, 6),
                Control(Note(0x30), "Shift", LayoutControlKind.Button, "Mode buttons", 4, 6),
                Control(Note(0x31), "Alt", LayoutControlKind.Button, "Mode buttons", 5, 6),
            ]));

        rows.Add(Row(
            "transport",
            [
                Control(Note(0x32), "Pattern Song / Metronome", LayoutControlKind.Button, "Transport", 13, 6),
                Control(Note(0x33), "Play / Wait", LayoutControlKind.Button, "Transport", 14, 6),
                Control(Note(0x34), "Stop / Countdown", LayoutControlKind.Button, "Transport", 15, 6),
                Control(Note(0x35), "Record / Loop Record", LayoutControlKind.Button, "Transport", 16, 6),
            ]));

        var layout = new ControllerLayoutDefinition
        {
            Id = AkaiFireV1LayoutId,
            Name = "Akai Professional Fire code-rendered layout",
            Rows = rows,
        };
        layout.Normalize();
        return new MidiControllerModelDefinition(
            AkaiFireV1Id,
            "Akai Professional Fire",
            controls,
            layout,
            RelativeControlChangeNumbers: encoders.Select(item => item.RotationCc).ToArray());

        LayoutRowDefinition Row(string id, IEnumerable<MidiControllerControlDefinition> rowControls)
        {
            var items = rowControls.ToArray();
            controls.AddRange(items);
            return new LayoutRowDefinition
            {
                Id = id,
                Controls = items.Select(item => new LayoutControlDefinition
                {
                    ControlId = item.ControlId,
                    Label = item.Label,
                    Kind = item.Kind,
                    Cluster = item.Cluster,
                    X = item.X,
                    Y = item.Y,
                }).ToList(),
            };
        }

        static MidiControllerControlDefinition Control(
            ControlId id,
            string label,
            LayoutControlKind kind,
            string cluster,
            int gridColumn,
            int gridRow) => new(id, label, kind, cluster,
                gridColumn * MainViewModel.LayoutCellWidth,
                gridRow * MainViewModel.LayoutCellHeight);
    }

    private static ControlId Note(int note) =>
        ControlId.Create(WinMmMidiInputProvider.ProviderId, $"channel-1:note-{note}");

    private static ControlId ControlChange(int control, bool increase) =>
        ControlId.Create(
            WinMmMidiInputProvider.ProviderId,
            $"channel-1:cc-{control}:{(increase ? "increase" : "decrease")}");

    [GeneratedRegex(@"\s+\(device\s+\d+\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateDeviceSuffix();

    [GeneratedRegex(@"^\d+\s*-\s*", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateDevicePrefix();
}
