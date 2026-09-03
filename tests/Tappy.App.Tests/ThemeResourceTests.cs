using System.Globalization;
using System.Xml.Linq;

namespace Tappy.App.Tests;

public sealed class ThemeResourceTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Dark.xaml", "#101318", "#F3F7FA", "#A8B3BE", "#2A313B")]
    [InlineData("Light.xaml", "#17202A", "#FFFFFF", "#56616B", "#E5EAEE")]
    public void Standard_themes_define_readable_combo_box_and_disabled_button_text(
        string themeFile,
        string expectedForeground,
        string expectedBackground,
        string expectedDisabledForeground,
        string expectedDisabledBackground)
    {
        var document = XDocument.Load(SourcePath("src", "Tappy.App", "Themes", themeFile));

        var foreground = ResourceValue(document, "Color", "ComboBoxTextColor");
        var background = ResourceValue(document, "Color", "ComboBoxBackgroundColor");

        Assert.Equal(expectedForeground, foreground);
        Assert.Equal(expectedBackground, background);
        Assert.True(
            ContrastRatio(foreground, background) >= 4.5,
            $"{themeFile} ComboBox text contrast must meet WCAG AA normal-text contrast.");

        var disabledForeground = ResourceValue(document, "Color", "ButtonDisabledTextColor");
        var disabledBackground = ResourceValue(document, "Color", "ButtonDisabledBackgroundColor");
        Assert.Equal(expectedDisabledForeground, disabledForeground);
        Assert.Equal(expectedDisabledBackground, disabledBackground);
        Assert.True(
            ContrastRatio(disabledForeground, disabledBackground) >= 4.5,
            $"{themeFile} disabled button labels must remain plainly readable.");
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
        Assert.Contains(
            "SystemColors.GrayTextColorKey",
            ResourceValue(document, "SolidColorBrush", "ButtonDisabledTextBrush"),
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemColors.ControlColorKey",
            ResourceValue(document, "SolidColorBrush", "ButtonDisabledBackgroundBrush"),
            StringComparison.Ordinal);
        AssertComboBoxStyleUsesDedicatedBrushes(document);
    }

    [Fact]
    public void Shared_button_template_explicitly_styles_disabled_state()
    {
        var document = XDocument.Load(SourcePath("src", "Tappy.App", "App.xaml"));
        var buttonTemplate = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ControlTemplate" &&
                element.Attribute("TargetType")?.Value == "{x:Type Button}");
        var disabledTrigger = buttonTemplate
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Trigger" &&
                element.Attribute("Property")?.Value == "IsEnabled" &&
                element.Attribute("Value")?.Value == "False");
        var setters = disabledTrigger.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToArray();

        Assert.Contains(setters, setter =>
            setter.Attribute("Property")?.Value == "Foreground" &&
            setter.Attribute("Value")?.Value == "{DynamicResource ButtonDisabledTextBrush}");
        Assert.Contains(setters, setter =>
            setter.Attribute("TargetName")?.Value == "ButtonBorder" &&
            setter.Attribute("Property")?.Value == "Background" &&
            setter.Attribute("Value")?.Value == "{DynamicResource ButtonDisabledBackgroundBrush}");
        Assert.Contains(setters, setter =>
            setter.Attribute("TargetName")?.Value == "ButtonBorder" &&
            setter.Attribute("Property")?.Value == "BorderBrush" &&
            setter.Attribute("Value")?.Value == "{DynamicResource ButtonDisabledBorderBrush}");
    }

    [Fact]
    public void Main_window_dropdowns_and_control_labels_are_readable()
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

        var tilePanel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "WrapPanel" &&
                element.Attribute("IsItemsHost")?.Value == "True");
        Assert.Equal("144", tilePanel.Attribute("ItemWidth")?.Value);
        Assert.Equal("116", tilePanel.Attribute("ItemHeight")?.Value);
        Assert.Equal("760", tilePanel.Attribute("MaxWidth")?.Value);

        var tileLabel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attribute("Text")?.Value == "{Binding Label}");
        Assert.Equal("13", tileLabel.Attribute("FontSize")?.Value);
        Assert.Equal("Wrap", tileLabel.Attribute("TextWrapping")?.Value);
        Assert.Equal("None", tileLabel.Attribute("TextTrimming")?.Value);
        Assert.Equal("Center", tileLabel.Attribute("TextAlignment")?.Value);

        var rehearsalLabel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attribute("Text")?.Value?.StartsWith(
                    "Rehearsal Mode —",
                    StringComparison.Ordinal) == true);
        Assert.Equal("Wrap", rehearsalLabel.Attribute("TextWrapping")?.Value);
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
