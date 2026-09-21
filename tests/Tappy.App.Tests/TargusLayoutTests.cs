using Tappy.App.Services;
using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.App.Tests;

public sealed class TargusLayoutTests
{
    [Fact]
    public void Legacy_mixed_sizes_and_unpositioned_reports_are_repaired_without_losing_keys()
    {
        var old = LegacyLayout();
        var ids = Keys(old).Select(key => key.ControlId).ToHashSet();
        var repaired = TargusKeypadLayout.MergeSavedLayout(old);
        Assert.Equal(TargusKeypadLayout.LayoutId, repaired.Id);
        Assert.True(ids.IsSubsetOf(Keys(repaired).Select(key => key.ControlId).ToHashSet()));
        AssertDisjoint(repaired);
        var seven = Keys(repaired).Single(key => key.ControlId!.Value.Value == TargusKeypadLayout.Key(0x47));
        Assert.Equal(92, seven.Width * 134, 6);
        Assert.Equal(88, seven.Height * 106, 6);
        Assert.Equal(0, seven.X);
        Assert.Equal(98, seven.Y);
        var enter = Keys(repaired).Single(key => key.ControlId!.Value.Value == TargusKeypadLayout.Key(0x1C));
        Assert.Equal(186, enter.Height * 106, 6);
        Assert.All(Keys(repaired).Where(key => !Keys(TargusKeypadLayout.Create()).Any(primary => primary.ControlId == key.ControlId)),
            key => Assert.True(key.Y >= 580));
        // Migration must not mutate the loaded profile until confirmation applies it.
        Assert.Equal(1, Keys(old).Single(key => key.ControlId == seven.ControlId).Width);
    }

    [Fact]
    public void V3_scattered_navigation_is_reset_to_physical_order_on_the_right_without_losing_colors()
    {
        var old = TargusKeypadLayout.Create();
        old.Id = "targus-pauk10u-layout-v3";
        var navigationScans = new ushort[] { 0x47, 0x48, 0x49, 0x4B, 0x4D, 0x4F, 0x50, 0x51, 0x52, 0x53 };
        foreach (var key in Keys(old).Where(c => navigationScans.Any(scan => c.ControlId?.Value == TargusKeypadLayout.Key(scan, true))))
        { key.X = 144; key.Y = 1000; key.ColorKey = "Green"; }
        var repaired = TargusKeypadLayout.MergeSavedLayout(old);
        AssertDisjoint(repaired);
        foreach (var scan in navigationScans)
        {
            var numeric = Keys(repaired).Single(c => c.ControlId?.Value == TargusKeypadLayout.Key(scan));
            var navigation = Keys(repaired).Single(c => c.ControlId?.Value == TargusKeypadLayout.Key(scan, true));
            Assert.Equal(numeric.X + 420, navigation.X);
            Assert.Equal(numeric.Y, navigation.Y);
            Assert.Equal("Green", navigation.ColorKey);
        }
        var enter = Keys(repaired).Single(c => c.ControlId?.Value == TargusKeypadLayout.Key(0x1C));
        Assert.Equal(300, enter.X);
        Assert.Equal(294, enter.Y);
        Assert.Equal(186, enter.Height * 106, 6);
        Assert.Equal("Enter (shared)", enter.Label);
        Assert.All(Keys(repaired), c => { Assert.True(c.X + c.Width * 134 <= 812.001); Assert.True(c.Y + c.Height * 106 <= 480.001); });
    }

    [Fact]
    public void Repaired_layout_is_stable_and_later_custom_positions_and_sizes_survive_reconnect()
    {
        var repaired = TargusKeypadLayout.MergeSavedLayout(LegacyLayout());
        var moved = Keys(repaired).Single(key => key.ControlId!.Value.Value == TargusKeypadLayout.Key(0x47));
        moved.X = 890;
        moved.Y = 230;
        moved.Width = 1.7;
        moved.Height = 1.2;
        var reopened = TargusKeypadLayout.MergeSavedLayout(repaired);
        foreach (var key in Keys(repaired))
        {
            var after = Keys(reopened).Single(item => item.ControlId == key.ControlId);
            Assert.Equal((key.X, key.Y, key.Width, key.Height), (after.X, after.Y, after.Width, after.Height));
        }
        AssertDisjoint(reopened);
    }

    [Fact]
    public void Newly_learned_reports_skip_custom_tiles_and_do_not_overlap_the_tall_enter()
    {
        var layout = TargusKeypadLayout.Create();
        var moved = Keys(layout).First();
        moved.X = 0;
        moved.Y = 580;
        moved.Width = 2;
        moved.Height = 2;
        for (ushort scan = 0x60; scan < 0x74; scan++)
        {
            var key = new LayoutControlDefinition { ControlId = new ControlId(TargusKeypadLayout.Key(scan)), Label = "Extra report" };
            TargusKeypadLayout.PlaceNewReport(layout, key);
            layout.Rows.Add(new LayoutRowDefinition { Id = $"extra-{scan}", Controls = [key] });
            AssertDisjoint(layout);
        }
    }

    internal static ControllerLayoutDefinition LegacyLayout()
    {
        var old = TargusKeypadLayout.CreatePhysical();
        old.Id = "targus-pauk10u-layout-v1";
        foreach (var key in Keys(old).Where(key => key.Height < 1)) { key.Width = 1; key.Height = 1; }
        old.Rows.Insert(0, new LayoutRowDefinition
        {
            Id = "learned",
            Controls =
        [
            new() { ControlId = new ControlId(TargusKeypadLayout.Key(0x45)), Label = "Num Lock (base report)" },
            new() { ControlId = new ControlId(TargusKeypadLayout.Key(0x1C)), Label = "Enter (base report)" },
            new() { ControlId = new ControlId(TargusKeypadLayout.Key(0x47, true)), Label = "Home" },
            new() { ControlId = new ControlId(TargusKeypadLayout.Key(0x48, true)), Label = "Up Arrow" },
            new() { ControlId = new ControlId(TargusKeypadLayout.Key(0x49, true)), Label = "Page Up" }
        ]
        });
        return old;
    }

    private static IEnumerable<LayoutControlDefinition> Keys(ControllerLayoutDefinition layout) => layout.Rows.SelectMany(row => row.Controls);
    private static void AssertDisjoint(ControllerLayoutDefinition layout)
    {
        foreach (var first in Keys(layout))
        {
            Assert.NotNull(first.X);
            Assert.NotNull(first.Y);
            foreach (var second in Keys(layout).Where(second => second != first))
            {
                Assert.False(first.X < second.X + second.Width * 134 && second.X < first.X + first.Width * 134 &&
                    first.Y < second.Y + second.Height * 106 && second.Y < first.Y + first.Height * 106,
                    $"Overlapping assignment tiles: {first.Label} / {second.Label}");
            }
        }
    }
}
