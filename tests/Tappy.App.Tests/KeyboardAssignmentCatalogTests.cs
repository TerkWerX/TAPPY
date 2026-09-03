using Tappy.App.Services;
using Tappy.Core.Output;

namespace Tappy.App.Tests;

public sealed class KeyboardAssignmentCatalogTests
{
    [Fact]
    public void Catalog_exposes_well_over_a_thousand_searchable_keyboard_assignments()
    {
        var catalog = KeyboardAssignmentCatalog.Create();

        Assert.True(catalog.Count > 1_500, $"Expected more than 1,500 assignments, found {catalog.Count}.");
        Assert.Contains(catalog, option => option.Name == "Copy" && option.Shortcut == "Ctrl + C");
        Assert.Contains(catalog, option => option.Shortcut == "Ctrl + Shift + F24");
        Assert.Contains(catalog, option => option.Shortcut == "Media Play/Pause");
        Assert.Contains(catalog, option => option.Shortcut == "Right Ctrl");
        Assert.All(catalog.SelectMany(option => option.Keys), key =>
            Assert.True(KeyboardOutputCapabilities.IsSupported(key), $"Unsupported catalog key: {key}"));
    }

    [Fact]
    public void Categories_cover_every_assignment_and_report_truthful_counts()
    {
        var catalog = KeyboardAssignmentCatalog.Create();
        var categories = KeyboardAssignmentCatalog.CreateCategories(catalog);

        Assert.Equal(catalog.Count, categories[0].Count);
        Assert.Equal(KeyboardAssignmentCatalog.AllCategory, categories[0].Name);
        Assert.Equal(catalog.Count, categories.Skip(1).Sum(category => category.Count));
        Assert.Contains(categories, category => category.Name == "Windows · Media & volume");
        Assert.Contains(categories, category => category.Name == "Key combinations · Ctrl + Shift");
    }
}
