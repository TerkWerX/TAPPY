using Tappy.App.Services;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class MicrosoftNumberPadLayoutTests
{
    [Fact]
    public void All_seventeen_keys_match_photographed_order_and_proportions_without_overlap()
    {
        var layout = MicrosoftNumberPadLayout.Create();
        Assert.Equal(4, layout.GridColumns);
        Assert.Equal(5, layout.GridRows);
        Assert.Equal(new[] { "Num Lock", "/", "*", "−", "7", "8", "9", "+", "4", "5", "6", "1", "2", "3", "Enter", "0", "Decimal" },
            Keys(layout).Select(k => k.Label));
        Assert.Equal(17, Keys(layout).Select(k => k.ControlId).Distinct().Count());
        AssertDisjoint(layout);
        Assert.Equal(230, Keys(layout).Single(k => k.Label == "0").Width * 134, 6);
        Assert.Equal(186, Keys(layout).Single(k => k.Label == "Enter").Height * 106, 6);
        Assert.Equal(186, Keys(layout).Single(k => k.Label == "+").Height * 106, 6);
    }

    [Fact]
    public void Generic_press_order_is_repaired_and_unknown_reports_retained_separately()
    {
        var saved = MicrosoftNumberPadLayout.Create();
        saved.Id = "generated-grid";
        foreach (var key in Keys(saved)) { key.X = null; key.Y = null; key.Width = 1; key.ColorKey = "Green"; }
        saved.Rows.Reverse();
        saved.Rows.Add(new LayoutRowDefinition
        {
            Id = "extra",
            Controls = [new()
            { ControlId = new ControlId(MicrosoftNumberPadLayout.Key(0x47, true)), Label = "Home" }]
        });
        var result = MicrosoftNumberPadLayout.MergeSavedLayout(saved);
        Assert.Equal(18, Keys(result).Count());
        Assert.Equal("Num Lock", Keys(result).First().Label);
        Assert.All(Keys(result).Take(17), k => Assert.Equal("Green", k.ColorKey));
        Assert.Equal(520, Keys(result).Last().X);
        AssertDisjoint(result);
        Assert.All(Keys(saved), k => Assert.Null(k.X));
    }

    [Fact]
    public void Later_user_geometry_and_workspace_settings_survive_reconnect()
    {
        var saved = MicrosoftNumberPadLayout.Create();
        saved.GridColumns = 12; saved.GridRows = 8; saved.SnapToGrid = false;
        var seven = Keys(saved).Single(k => k.Label == "7");
        seven.X = 900; seven.Y = 410; seven.Width = 1.7; seven.Height = 1.2;
        var result = MicrosoftNumberPadLayout.MergeSavedLayout(saved);
        Assert.Equal((12, 8, false), (result.GridColumns, result.GridRows, result.SnapToGrid));
        foreach (var key in Keys(saved))
        {
            var after = Keys(result).Single(k => k.ControlId == key.ControlId);
            Assert.Equal((key.X, key.Y, key.Width, key.Height), (after.X, after.Y, after.Width, after.Height));
        }
    }

    [Fact]
    public void New_reports_avoid_custom_tiles_and_existing_extra_reports()
    {
        var layout = MicrosoftNumberPadLayout.Create();
        var moved = Keys(layout).First();
        moved.X = 520; moved.Y = 0; moved.Width = 2; moved.Height = 2;
        for (ushort scan = 0x60; scan < 0x74; scan++)
        {
            var key = new LayoutControlDefinition { ControlId = new ControlId(MicrosoftNumberPadLayout.Key(scan)), Label = "Extra" };
            MicrosoftNumberPadLayout.PlaceNewReport(layout, key);
            layout.Rows.Add(new LayoutRowDefinition { Id = $"extra-{scan}", Controls = [key] });
            AssertDisjoint(layout);
        }
    }

    [Fact]
    public void Photo_has_seventeen_disjoint_faces_and_requires_verified_model_not_a_friendly_name()
    {
        Assert.Null(ControllerPhotoCatalog.Find("raw-input", null, null, "Microsoft Bluetooth Number Pad"));
        Assert.Null(ControllerPhotoCatalog.Find("other", null, null,
            presentationModelId: KeyboardPresentationModels.MicrosoftBluetoothNumberPad));
        var photo = ControllerPhotoCatalog.Find("raw-input", null, null,
            presentationModelId: KeyboardPresentationModels.MicrosoftBluetoothNumberPad)!;
        var faces = photo.Hotspots.Values.DistinctBy(f => f.EffectiveVisualId).ToArray();
        Assert.Equal(17, faces.Length);
        foreach (var face in faces)
        {
            Assert.InRange(face.Left, 0, photo.Width - face.Width);
            Assert.InRange(face.Top, 0, photo.Height - face.Height);
            Assert.DoesNotContain(faces, other => other != face && face.Left < other.Left + other.Width &&
                other.Left < face.Left + face.Width && face.Top < other.Top + other.Height && other.Top < face.Top + face.Height);
        }
        Assert.Equal(photo.Hotspots[MicrosoftNumberPadLayout.Key(0x47)].EffectiveVisualId,
            photo.Hotspots[MicrosoftNumberPadLayout.Key(0x47, true)].EffectiveVisualId);
    }

    private static IEnumerable<LayoutControlDefinition> Keys(ControllerLayoutDefinition layout) => layout.Rows.SelectMany(r => r.Controls);
    private static void AssertDisjoint(ControllerLayoutDefinition layout)
    {
        foreach (var key in Keys(layout))
        {
            Assert.NotNull(key.X); Assert.NotNull(key.Y);
            Assert.DoesNotContain(Keys(layout), other => key != other && key.X < other.X + other.Width * 134 &&
                other.X < key.X + key.Width * 134 && key.Y < other.Y + other.Height * 106 && other.Y < key.Y + key.Height * 106);
        }
    }
}
