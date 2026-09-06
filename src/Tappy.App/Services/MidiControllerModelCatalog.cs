using System.Text.RegularExpressions;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Windows.Input;

namespace Tappy.App.Services;

public sealed record MidiControllerControlDefinition(
    ControlId ControlId,
    string Label,
    LayoutControlKind Kind,
    string Cluster);

public sealed record MidiControllerModelDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<MidiControllerControlDefinition> Controls,
    ControllerLayoutDefinition Layout);

/// <summary>
/// Code-defined layouts are enabled only for exact, reviewed MIDI model names.
/// Unknown MIDI devices retain Tappy's learn-as-you-press layout.
/// </summary>
public static partial class MidiControllerModelCatalog
{
    public const string AkaiApcMiniV1Id = "akai-apc-mini-v1";
    public const string AkaiApcMiniV1LayoutId = "akai-apc-mini-v1-code-layout-v1";

    private static readonly MidiControllerModelDefinition AkaiApcMiniV1 = CreateAkaiApcMiniV1();

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
        return string.Equals(modelName, "APC MINI", StringComparison.OrdinalIgnoreCase)
            ? AkaiApcMiniV1
            : null;
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

    private static ControlId Note(int note) =>
        ControlId.Create(WinMmMidiInputProvider.ProviderId, $"channel-1:note-{note}");

    private static ControlId ControlChange(int control, bool increase) =>
        ControlId.Create(
            WinMmMidiInputProvider.ProviderId,
            $"channel-1:cc-{control}:{(increase ? "increase" : "decrease")}");

    [GeneratedRegex(@"\s+\(device\s+\d+\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateDeviceSuffix();
}
