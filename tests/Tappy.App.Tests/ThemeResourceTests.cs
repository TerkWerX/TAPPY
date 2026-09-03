using System.Globalization;
using System.Xml.Linq;

namespace Tappy.App.Tests;

public sealed class ThemeResourceTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Dark.xaml", "#101318", "#F3F7FA")]
    [InlineData("Light.xaml", "#17202A", "#FFFFFF")]
    public void Standard_themes_define_readable_combo_box_text(
        string themeFile,
        string expectedForeground,
        string expectedBackground)
    {
        var document = XDocument.Load(SourcePath("src", "Tappy.App", "Themes", themeFile));

        var foreground = ResourceValue(document, "Color", "ComboBoxTextColor");
        var background = ResourceValue(document, "Color", "ComboBoxBackgroundColor");

        Assert.Equal(expectedForeground, foreground);
        Assert.Equal(expectedBackground, background);
        Assert.True(
            ContrastRatio(foreground, background) >= 4.5,
            $"{themeFile} ComboBox text contrast must meet WCAG AA normal-text contrast.");
        AssertComboBoxStyleUsesDedicatedBrushes(document);
    }

    [Fact]
    public void High_contrast_combo_box_uses_system_window_colors()
    {
        var document = XDocument.Load(
            SourcePath("src", "Tappy.App", "Themes", "HighContrast.xaml"));

        Assert.Contains(
            "SystemColors.WindowTextColorKey",
            ResourceValue(document, "SolidColorBrush", "ComboBoxTextBrush"),
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemColors.WindowColorKey",
            ResourceValue(document, "SolidColorBrush", "ComboBoxBackgroundBrush"),
            StringComparison.Ordinal);
        AssertComboBoxStyleUsesDedicatedBrushes(document);
    }

    [Fact]
    public void Main_window_combo_boxes_override_global_text_block_foreground()
    {
        var document = XDocument.Load(SourcePath("src", "Tappy.App", "MainWindow.xaml"));
        var devicePicker = NamedElement(document, "ComboBox", "DevicePicker");
        var outputPicker = NamedElement(document, "ComboBox", "OutputKeyPicker");

        Assert.Equal(
            "{StaticResource ControllerChoiceComboBoxItemTemplate}",
            devicePicker.Attribute("ItemTemplate")?.Value);
        Assert.Null(devicePicker.Attribute("DisplayMemberPath"));
        Assert.Equal(
            "{StaticResource OutputKeyComboBoxItemTemplate}",
            outputPicker.Attribute("ItemTemplate")?.Value);

        AssertReadableTemplate(document, "ControllerChoiceComboBoxItemTemplate", "{Binding DisplayName}");
        AssertReadableTemplate(document, "OutputKeyComboBoxItemTemplate", "{Binding}");
    }

    private static void AssertComboBoxStyleUsesDedicatedBrushes(XDocument document)
    {
        var style = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Style" &&
                element.Attribute("TargetType")?.Value == "ComboBox");
        var setters = style
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("{DynamicResource ComboBoxTextBrush}", setters["Foreground"]);
        Assert.Equal("{DynamicResource ComboBoxBackgroundBrush}", setters["Background"]);
    }

    private static void AssertReadableTemplate(
        XDocument document,
        string templateKey,
        string expectedTextBinding)
    {
        var template = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "DataTemplate" &&
                element.Attribute(XamlNamespace + "Key")?.Value == templateKey);
        var text = template.Descendants().Single(element => element.Name.LocalName == "TextBlock");

        Assert.Equal(expectedTextBinding, text.Attribute("Text")?.Value);
        Assert.Equal(
            "{DynamicResource ComboBoxTextBrush}",
            text.Attribute("Foreground")?.Value);
    }

    private static XElement NamedElement(XDocument document, string type, string name) =>
        document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == type &&
                element.Attribute(XamlNamespace + "Name")?.Value == name);

    private static string ResourceValue(XDocument document, string type, string key)
    {
        var resource = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == type &&
                element.Attribute(XamlNamespace + "Key")?.Value == key);
        return resource.Attribute("Color")?.Value ?? resource.Value.Trim();
    }

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tappy.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. segments]);
    }

    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        Assert.Matches("^#[0-9A-Fa-f]{6}$", color);
        var red = LinearChannel(color, 1);
        var green = LinearChannel(color, 3);
        var blue = LinearChannel(color, 5);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double LinearChannel(string color, int offset)
    {
        var channel = int.Parse(
            color.AsSpan(offset, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture) / 255d;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
