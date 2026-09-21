using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.App.Services;

/// <summary>Original RZ07-0103 only, using the factory mapping in Razer's master guide.</summary>
internal static class RazerTartarusLayout
{
    internal const string LayoutId = "razer-tartarus-original-layout-v1";
    internal static bool Matches(string? provider, ushort? vendor, ushort? product) =>
        provider == "raw-input" && vendor == 0x1532 && product == 0x0201;
    internal static string Key(ushort scan, bool extended = false) => ControlId.FromRawInputKeyboard(scan, extended, false).Value;

    internal static ControllerLayoutDefinition Create()
    {
        string[] labels = ["Tab", "Q", "W", "E", "R", "Caps Lock", "A", "S", "D", "F", "Left Shift", "Z", "X", "C", "V"];
        ushort[] scans = [0x0F, 0x10, 0x11, 0x12, 0x13, 0x3A, 0x1E, 0x1F, 0x20, 0x21, 0x2A, 0x2C, 0x2D, 0x2E, 0x2F];
        var result = new ControllerLayoutDefinition { Id = LayoutId, Name = "Razer Tartarus (original)", GridColumns = 8, GridRows = 5 };
        for (var row = 0; row < 3; row++)
        {
            result.Rows.Add(new LayoutRowDefinition
            {
                Id = $"tartarus-keys-{row}",
                Controls = Enumerable.Range(row * 5, 5)
                .Select(i => Tile($"{i + 1:00} · {labels[i]}", Key(scans[i]), (i % 5) * 114, row * 94)).ToList()
            });
        }
        // The thumb module is a separate cluster, not another row of alphabet keys.
        // Eight-way diagonals are simultaneous arrow reports, not four invented keys.
        result.Rows.Add(new LayoutRowDefinition
        {
            Id = "tartarus-thumb",
            Controls =
        [
            Tile("Thumb · Alt", Key(0x38), 663, 0, 150),
            Tile("↑ Up", Key(0x48, true), 690, 104, 96, 80),
            Tile("← Left", Key(0x4B, true), 586, 192, 96, 80),
            Tile("→ Right", Key(0x4D, true), 794, 192, 96, 80),
            Tile("↓ Down", Key(0x50, true), 690, 280, 96, 80),
            Tile("Thumb · Space", Key(0x39), 663, 378, 150, 72)
        ]
        });
        return result;
    }

    internal static ControllerLayoutDefinition MergeSavedLayout(ControllerLayoutDefinition saved)
    {
        var result = Create();
        var currentVersion = saved.Id == LayoutId;
        var previous = saved.Rows.SelectMany(r => r.Controls).Where(k => k.ControlId is not null)
            .GroupBy(k => k.ControlId!.Value).ToDictionary(g => g.Key, g => g.Last());
        foreach (var row in result.Rows)
        {
            for (var i = 0; i < row.Controls.Count; i++)
            {
                var key = row.Controls[i];
                if (!previous.Remove(key.ControlId!.Value, out var old)) { continue; }
                var restored = old.Clone();
                restored.Label = key.Label;
                if (!currentVersion)
                { restored.X = key.X; restored.Y = key.Y; restored.Width = key.Width; restored.Height = key.Height; }
                else { restored.X ??= key.X; restored.Y ??= key.Y; }
                row.Controls[i] = restored;
            }
        }
        foreach (var old in previous.Values)
        {
            var extra = old.Clone();
            if (!currentVersion || extra.X is null || extra.Y is null) { PlaceNewReport(result, extra); }
            result.Rows.Add(new LayoutRowDefinition { Id = $"tartarus-extra-{result.Rows.Count}", Controls = [extra] });
        }
        result.SnapToGrid = saved.SnapToGrid;
        result.Orientation = saved.Orientation;
        if (currentVersion) { result.GridColumns = saved.GridColumns; result.GridRows = saved.GridRows; }
        return result;
    }

    internal static void PlaceNewReport(ControllerLayoutDefinition layout, LayoutControlDefinition key)
    {
        for (var i = 0; ; i++)
        {
            key.X = 850 + (i % 4) * 144d; key.Y = (i / 4) * 116d;
            if (layout.Rows.SelectMany(r => r.Controls).All(k =>
                key.X >= k.X + Math.Clamp(k.Width * 134, 48, 600) + 8 ||
                k.X >= key.X + Math.Clamp(key.Width * 134, 48, 600) + 8 ||
                key.Y >= k.Y + Math.Clamp(k.Height * 106, 48, 400) + 8 ||
                k.Y >= key.Y + Math.Clamp(key.Height * 106, 48, 400) + 8)) { return; }
        }
    }

    // Tappy's explicit bank legend, not a claim to read Synapse's active profile.
    internal static int IndicatorMask(int bank) => bank switch
    { 1 => 1, 2 => 2, 3 => 4, 4 => 3, 5 => 5, 6 => 6, 7 => 7, 8 => 0, _ => throw new ArgumentOutOfRangeException(nameof(bank)) };

    private static LayoutControlDefinition Tile(string label, string id, double x, double y, double width = 104, double height = 84) =>
        new()
        {
            ControlId = new ControlId(id),
            Label = label,
            Kind = LayoutControlKind.Key,
            X = x,
            Y = y,
            Width = width / 134,
            Height = height / 106
        };
}
