using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.App.Services;

internal static class MicrosoftNumberPadLayout
{
    internal const string LayoutId = "microsoft-bluetooth-number-pad-layout-v1";
    internal static string Key(ushort scan, bool extended = false) => ControlId.FromRawInputKeyboard(scan, extended, false).Value;

    internal static ControllerLayoutDefinition Create() => new()
    {
        Id = LayoutId,
        Name = "Microsoft Bluetooth Number Pad",
        GridColumns = 4,
        GridRows = 5,
        Rows =
        [
            Row(0, Tile("Num Lock", Key(0x45), 0, 0), Tile("/", Key(0x35, true), 1, 0),
                Tile("*", Key(0x37), 2, 0), Tile("−", Key(0x4A), 3, 0)),
            Row(1, Tile("7", Key(0x47), 0, 1), Tile("8", Key(0x48), 1, 1),
                Tile("9", Key(0x49), 2, 1), Tile("+", Key(0x4E), 3, 1, height: 186)),
            Row(2, Tile("4", Key(0x4B), 0, 2), Tile("5", Key(0x4C), 1, 2), Tile("6", Key(0x4D), 2, 2)),
            Row(3, Tile("1", Key(0x4F), 0, 3), Tile("2", Key(0x50), 1, 3),
                Tile("3", Key(0x51), 2, 3), Tile("Enter", Key(0x1C, true), 3, 3, height: 186)),
            Row(4, Tile("0", Key(0x52), 0, 4, width: 230), Tile("Decimal", Key(0x53), 2, 4))
        ]
    };

    internal static ControllerLayoutDefinition MergeSavedLayout(ControllerLayoutDefinition saved)
    {
        var result = Create();
        var legacy = saved.Id != LayoutId;
        var previous = saved.Rows.SelectMany(row => row.Controls).Where(key => key.ControlId is not null)
            .GroupBy(key => key.ControlId!.Value).ToDictionary(g => g.Key, g => g.Last());
        foreach (var key in result.Rows.SelectMany(row => row.Controls))
        {
            if (!previous.Remove(key.ControlId!.Value, out var old)) { continue; }
            key.ColorKey = old.ColorKey;
            key.AnalogRawAtMinimum = old.AnalogRawAtMinimum;
            key.AnalogRawAtMaximum = old.AnalogRawAtMaximum;
            key.AnalogRawAtCenter = old.AnalogRawAtCenter;
            key.EncoderDegreesPerStep = old.EncoderDegreesPerStep;
            key.EncoderReversed = old.EncoderReversed;
            if (!legacy)
            {
                key.X = old.X ?? key.X; key.Y = old.Y ?? key.Y;
                key.Width = old.Width; key.Height = old.Height;
            }
        }
        foreach (var old in previous.Values)
        {
            var extra = old.Clone();
            if (legacy || extra.X is null || extra.Y is null) { PlaceNewReport(result, extra); }
            result.Rows.Add(Row(result.Rows.Count, extra));
        }
        result.SnapToGrid = saved.SnapToGrid;
        if (!legacy) { result.GridColumns = saved.GridColumns; result.GridRows = saved.GridRows; }
        result.Orientation = saved.Orientation;
        return result;
    }

    internal static void PlaceNewReport(ControllerLayoutDefinition layout, LayoutControlDefinition key)
    {
        // Unknown/alternate reports retain their own assignments, separately to the
        // right of the photographed keypad. Never overlap a tall + or Enter key.
        for (var index = 0; ; index++)
        {
            key.X = 520 + (index % 4) * 144d;
            key.Y = (index / 4) * 116d;
            if (layout.Rows.SelectMany(row => row.Controls).All(other => !Intersects(key, other))) { return; }
        }
    }

    private static bool Intersects(LayoutControlDefinition a, LayoutControlDefinition b) =>
        a.X < b.X + Math.Clamp(b.Width * 134, 48, 600) + 8 && b.X < a.X + Math.Clamp(a.Width * 134, 48, 600) + 8 &&
        a.Y < b.Y + Math.Clamp(b.Height * 106, 48, 400) + 8 && b.Y < a.Y + Math.Clamp(a.Height * 106, 48, 400) + 8;
    private static LayoutRowDefinition Row(int row, params LayoutControlDefinition[] keys) => new() { Id = $"ms-pad-row-{row}", Controls = [.. keys] };
    private static LayoutControlDefinition Tile(string label, string id, int col, int row, double width = 110, double height = 88) =>
        new()
        {
            ControlId = new ControlId(id),
            Label = label,
            Kind = LayoutControlKind.Key,
            X = col * 120,
            Y = row * 98,
            Width = width / 134,
            Height = height / 106,
            Cluster = "Numeric keypad"
        };
}
