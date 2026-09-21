using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.App.Services;

internal static class TargusKeypadLayout
{
    internal const string LayoutId = "targus-pauk10u-layout-v4";
    internal const string TripleZeroReference = "reference:targus-pauk10u-triple-zero";
    internal static bool Matches(string? provider, ushort? vendor, ushort? product) =>
        provider == "raw-input" && vendor == 0x05A4 && product == 0x9862;
    internal static string Key(ushort scan, bool extended = false) =>
        ControlId.FromRawInputKeyboard(scan, extended, false).Value;

    // Retain old report bindings/layout data without showing a redundant Enter.
    // The owner's physical Enter emits the base report in both Num Lock modes.
    internal static bool IsHiddenPresentationReport(string id) =>
        id == Key(0x45) || id == Key(0x1C, true);

    // 000 remains a reference until its physical signal is verified. Do not
    // invent a scan code or use a timing heuristic to distinguish it from 0.
    internal static ControllerLayoutDefinition CreatePhysical() => new()
    {
        Id = LayoutId,
        Name = "Targus PAUK10U",
        GridColumns = 4,
        GridRows = 5,
        Rows =
        [
            Row(0, Tile("Num Lock", Key(0x45, true), 0, 0), Tile("/", Key(0x35, true), 1, 0),
                Tile("*", Key(0x37), 2, 0), Tile("Backspace", Key(0x0E), 3, 0)),
            Row(1, Tile("7", Key(0x47), 0, 1), Tile("8", Key(0x48), 1, 1),
                Tile("9", Key(0x49), 2, 1), Tile("−", Key(0x4A), 3, 1)),
            Row(2, Tile("4", Key(0x4B), 0, 2), Tile("5", Key(0x4C), 1, 2),
                Tile("6", Key(0x4D), 2, 2), Tile("+", Key(0x4E), 3, 2)),
            Row(3, Tile("1", Key(0x4F), 0, 3), Tile("2", Key(0x50), 1, 3),
                Tile("3", Key(0x51), 2, 3), Tile("Enter (keypad)", Key(0x1C, true), 3, 3, 2)),
            Row(4, Tile("0", Key(0x52), 0, 4),
                Tile("000", TripleZeroReference, 1, 4), Tile("Decimal", Key(0x53), 2, 4))
        ]
    };

    internal static ControllerLayoutDefinition Create()
    {
        var layout = CreatePhysical();
        layout.GridColumns = 8;
        // TESTBED emits base Enter in both Num Lock modes. Keep that exact
        // report on the main tall key; retain E0 separately without merging macros.
        var enter = layout.Rows.SelectMany(row => row.Controls).Single(key => key.ControlId?.Value == Key(0x1C, true));
        enter.ControlId = new ControlId(Key(0x1C));
        enter.Label = "Enter (shared)";
        layout.Rows.AddRange([
            Row(5, Tile("Home", Key(0x47, true), 4, 1), Tile("Up", Key(0x48, true), 5, 1), Tile("Page Up", Key(0x49, true), 6, 1)),
            Row(6, Tile("Left", Key(0x4B, true), 4, 2), Tile("Right", Key(0x4D, true), 6, 2)),
            Row(7, Tile("End", Key(0x4F, true), 4, 3), Tile("Down", Key(0x50, true), 5, 3), Tile("Page Down", Key(0x51, true), 6, 3)),
            Row(8, Tile("Insert", Key(0x52, true), 4, 4), Tile("Delete", Key(0x53, true), 6, 4), Tile("Enter (alternate report)", Key(0x1C, true), 7, 3, 2))
        ]);
        foreach (var key in layout.Rows.SelectMany(row => row.Controls))
        {
            var column = key.X!.Value / 120;
            key.X = column * 100 + (column >= 4 ? 20 : 0);
            key.Width = 92d / 134;
            key.Cluster = column >= 4 ? "Navigation reports" : "Numeric keypad";
        }
        return layout;
    }

    internal static ControllerLayoutDefinition MergeSavedLayout(ControllerLayoutDefinition saved)
    {
        var result = Create();
        var legacy = saved.Id != LayoutId;
        var previous = saved.Rows.SelectMany(row => row.Controls)
            .Where(key => key.ControlId is not null).GroupBy(key => key.ControlId!.Value)
            .ToDictionary(group => group.Key, group => group.Last());
        var primary = result.Rows.SelectMany(row => row.Controls).ToArray();
        foreach (var key in primary)
        {
            if (!previous.Remove(key.ControlId!.Value, out var old)) { continue; }
            key.ColorKey = old.ColorKey;
            // One-time repair requested by the owner: older layouts retained
            // jumbled report positions below the keypad. Reset known geometry,
            // not IDs, colors or bindings. Future v4 custom layouts stay untouched.
            if (!legacy && old.X is not null && old.Y is not null)
            {
                key.X = old.X;
                key.Y = old.Y;
                key.Width = old.Width;
                key.Height = old.Height;
            }
        }
        foreach (var old in previous.Values.OrderBy(key => key.ControlId!.Value.Value, StringComparer.Ordinal))
        {
            var extra = old.Clone();
            if (extra.X is null || extra.Y is null ||
                (legacy && result.Rows.SelectMany(row => row.Controls).Any(key => Intersects(extra, key))))
            { PlaceInExtraGrid(extra, result.Rows.SelectMany(row => row.Controls)); }
            result.Rows.Add(new LayoutRowDefinition { Id = $"extra-{result.Rows.Count}", Controls = [extra] });
        }
        result.SnapToGrid = saved.SnapToGrid;
        if (!legacy)
        {
            result.GridColumns = saved.GridColumns;
            result.GridRows = saved.GridRows;
        }
        return result;
    }

    internal static void PlaceNewReport(ControllerLayoutDefinition layout, LayoutControlDefinition key) =>
        PlaceInExtraGrid(key, layout.Rows.SelectMany(row => row.Controls));

    private static void PlaceInExtraGrid(LayoutControlDefinition key, IEnumerable<LayoutControlDefinition> controls)
    {
        var occupied = controls.Where(other => !ReferenceEquals(other, key)).ToArray();
        // Separate four-column section below the five-row physical keypad.
        // Test actual rectangles, including a resized Enter or moved custom tile.
        for (var index = 0; ; index++)
        {
            key.X = (index % 4) * 144d;
            key.Y = 580 + (index / 4) * 116d;
            if (occupied.All(other => !Intersects(key, other, 8))) { return; }
        }
    }

    private static bool Intersects(LayoutControlDefinition first, LayoutControlDefinition second, double gap = 0) =>
        first.X is { } x && first.Y is { } y && second.X is { } otherX && second.Y is { } otherY &&
        x < otherX + Math.Clamp(second.Width * 134, 48, 600) + gap &&
        otherX < x + Math.Clamp(first.Width * 134, 48, 600) + gap &&
        y < otherY + Math.Clamp(second.Height * 106, 48, 400) + gap &&
        otherY < y + Math.Clamp(first.Height * 106, 48, 400) + gap;

    private static LayoutRowDefinition Row(int row, params LayoutControlDefinition[] controls) =>
        new() { Id = $"targus-row-{row}", Controls = [.. controls] };
    private static LayoutControlDefinition Tile(string label, string id, int column, int row, double height = 1) =>
        new()
        {
            ControlId = new ControlId(id),
            Label = label,
            Kind = LayoutControlKind.Key,
            X = column * 120,
            Y = row * 98,
            Width = 110d / 134,
            Height = (height > 1 ? 186d : 88d) / 106,
            Cluster = "Numeric keypad"
        };
}
