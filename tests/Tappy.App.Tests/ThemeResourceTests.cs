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

        var assignmentForeground = ResourceValue(document, "Color", "AssignmentListTextColor");
        var assignmentMuted = ResourceValue(document, "Color", "AssignmentListMutedTextColor");
        var assignmentBackground = ResourceValue(document, "Color", "AssignmentListBackgroundColor");
        var assignmentSelection = ResourceValue(document, "Color", "AssignmentListSelectionColor");
        var assignmentSelectionText = ResourceValue(document, "Color", "AssignmentListSelectionTextColor");
        Assert.True(
            ContrastRatio(assignmentForeground, assignmentBackground) >= 7,
            $"{themeFile} assignment titles must meet enhanced contrast on the native light list surface.");
        Assert.True(
            ContrastRatio(assignmentMuted, assignmentBackground) >= 7,
            $"{themeFile} assignment descriptions must meet enhanced contrast on the native light list surface.");
        Assert.True(
            ContrastRatio(assignmentSelectionText, assignmentSelection) >= 7,
            $"{themeFile} selected assignment text must meet enhanced contrast.");
        AssertComboBoxStyleUsesDedicatedBrushes(document);
        AssertLightSurfaceControlStyles(document);
        AssertToolTipUsesReadableThemeBrushes(document);
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
        Assert.Contains(
            "SystemColors.WindowTextColorKey",
            ResourceValue(document, "SolidColorBrush", "AssignmentListTextBrush"),
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemColors.WindowColorKey",
            ResourceValue(document, "SolidColorBrush", "AssignmentListBackgroundBrush"),
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemColors.HighlightTextColorKey",
            ResourceValue(document, "SolidColorBrush", "AssignmentListSelectionTextBrush"),
            StringComparison.Ordinal);
        AssertComboBoxStyleUsesDedicatedBrushes(document);
        AssertLightSurfaceControlStyles(document);
        AssertToolTipUsesReadableThemeBrushes(document);
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
    public void Main_window_device_dropdown_assignment_launcher_and_control_labels_are_readable()
    {
        var document = XDocument.Load(SourcePath("src", "Tappy.App", "MainWindow.xaml"));
        var devicePicker = NamedElement(document, "ComboBox", "DevicePicker");

        Assert.Equal(
            "{StaticResource ControllerChoiceComboBoxItemTemplate}",
            devicePicker.Attribute("ItemTemplate")?.Value);
        Assert.Null(devicePicker.Attribute("DisplayMemberPath"));

        AssertReadableTemplate(document, "ControllerChoiceComboBoxItemTemplate", "{Binding DisplayName}");

        var assignmentButton = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button" &&
                element.Attribute("Content")?.Value == "{Binding AssignmentEditorButtonLabel}");
        Assert.Equal("{Binding CanAssignSelectedControl}", assignmentButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("OpenAssignmentEditor_OnClick", assignmentButton.Attribute("Click")?.Value);

        var photoSplitter = NamedElement(document, "GridSplitter", "ControllerPhotoSplitter");
        Assert.Equal("ControllerPhotoSplitter_OnDragCompleted", photoSplitter.Attribute("DragCompleted")?.Value);
        Assert.Equal("PreviousAndNext", photoSplitter.Attribute("ResizeBehavior")?.Value);

        var tileButton = document.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            element.Attribute("PreviewMouseLeftButtonUp")?.Value ==
            "ControlTile_OnPreviewMouseLeftButtonUp");
        Assert.Equal("ControlTile_OnPreviewMouseMove", tileButton.Attribute("PreviewMouseMove")?.Value);

        var layoutCanvas = NamedElement(document, "ItemsControl", "ControllerLayoutCanvas");
        var workspaceHost = NamedElement(document, "Grid", "ControllerWorkspaceHost");
        Assert.Equal("{Binding LayoutSurfaceWidth}", workspaceHost.Attribute("MinWidth")?.Value);
        Assert.Equal("{Binding LayoutSurfaceHeight}", workspaceHost.Attribute("MinHeight")?.Value);
        Assert.Equal("ControllerWorkspace_OnPreviewMouseLeftButtonDown",
            workspaceHost.Attribute("PreviewMouseLeftButtonDown")?.Value);
        Assert.Equal("ControllerWorkspace_OnPreviewMouseMove", workspaceHost.Attribute("PreviewMouseMove")?.Value);
        Assert.Equal("ControllerWorkspace_OnPreviewMouseLeftButtonUp",
            workspaceHost.Attribute("PreviewMouseLeftButtonUp")?.Value);
        Assert.NotNull(NamedElement(document, "Border", "SelectionMarquee"));
        var layoutViewport = NamedElement(document, "ScrollViewer", "ControllerLayoutViewport");
        Assert.Equal("Stretch", layoutViewport.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Stretch", layoutViewport.Attribute("VerticalContentAlignment")?.Value);
        Assert.Equal("ControllerLayoutViewport_OnSizeChanged", layoutViewport.Attribute("SizeChanged")?.Value);
        Assert.Contains(layoutCanvas.Descendants(), element =>
            element.Name.LocalName == "Canvas" &&
            element.Attribute("IsItemsHost")?.Value == "True");
        foreach (var comboName in new[] { "LayoutColumnsBox", "LayoutRowsBox" })
        {
            var combo = NamedElement(document, "ComboBox", comboName);
            var text = combo.Descendants().Single(element => element.Name.LocalName == "TextBlock");
            Assert.Equal("{DynamicResource ComboBoxTextBrush}", text.Attribute("Foreground")?.Value);
        }
        var resizeThumb = document.Descendants().Single(element =>
            element.Name.LocalName == "Thumb" &&
            element.Attribute("DragDelta")?.Value == "ResizeTile_OnDragDelta");
        Assert.Equal("ResizeTile_OnDragCompleted", resizeThumb.Attribute("DragCompleted")?.Value);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "CheckBox" &&
            element.Attribute("Checked")?.Value == "TileGroupCheckBox_OnChecked");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            element.Attribute("Click")?.Value == "ApplyTileColor_OnClick");
        var colorPicker = document.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox" &&
            element.Attribute("AutomationProperties.Name")?.Value == "Square color");
        Assert.Equal("{Binding HasSupportedTileColors}", colorPicker.Attribute("IsEnabled")?.Value);

        var tileLabel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attribute("Text")?.Value == "{Binding Label}" &&
                element.Attribute("FontSize")?.Value == "13");
        Assert.Equal("13", tileLabel.Attribute("FontSize")?.Value);
        Assert.Equal("Wrap", tileLabel.Attribute("TextWrapping")?.Value);
        Assert.Equal("None", tileLabel.Attribute("TextTrimming")?.Value);
        Assert.Equal("Center", tileLabel.Attribute("TextAlignment")?.Value);

        var photoHotspots = NamedElement(document, "ItemsControl", "ControllerPhotoHotspots");
        Assert.Equal("{Binding ControllerPhotoHotspots}", photoHotspots.Attribute("ItemsSource")?.Value);
        var photo = document.Descendants().Single(element =>
            element.Name.LocalName == "Image" &&
            element.Attribute("AutomationProperties.Name")?.Value == "{Binding ControllerPhotoName}");
        Assert.Equal("{Binding ControllerPhotoSource, Converter={StaticResource ControllerPhotoSourceConverter}}", photo.Attribute("Source")?.Value);
        Assert.DoesNotContain(photo.Ancestors(), element => element.Name.LocalName == "Button");
        var photoTriggers = photoHotspots.Descendants()
            .Where(element => element.Name.LocalName == "DataTrigger")
            .Select(element => element.Attribute("Binding")?.Value)
            .ToArray();
        // One physical face can have multiple reports (e.g. Num Lock modes).
        // The template must observe the aggregate, not only its first tile.
        Assert.Contains("{Binding IsSelected}", photoTriggers);
        Assert.Contains("{Binding IsIlluminated}", photoTriggers);
        Assert.Contains("{Binding HasCustomColor}", photoTriggers);
        Assert.DoesNotContain("{Binding Tile.IsIlluminated}", photoTriggers);

        var rehearsalLabel = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock" &&
                element.Attribute("Text")?.Value?.StartsWith(
                    "Rehearsal Mode —",
                    StringComparison.Ordinal) == true);
        Assert.Equal("Wrap", rehearsalLabel.Attribute("TextWrapping")?.Value);
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("KeyboardAssignmentWindow.xaml")]
    [InlineData("BankControlWindow.xaml")]
    [InlineData("LedAnimationWindow.xaml")]
    [InlineData("FireColorPickerWindow.xaml")]
    [InlineData("AboutWindow.xaml")]
    [InlineData("SplashWindow.xaml")]
    public void Every_top_level_window_has_an_explicitly_themed_content_root(string windowFile)
    {
        // LedAnimationWindow and FireColorPickerWindow both shipped with a
        // root Grid that had no Background at all, so WPF's plain white
        // default showed through while every TextBlock still used the
        // theme's light-on-dark Foreground (an implicit style covering the
        // whole app) — nearly unreadable light text on a white window,
        // reported directly by the owner for LedAnimationWindow. A window's
        // content root must always set an explicit themed Background,
        // either a Grid using WindowBrush (most windows) or a Border using
        // PanelBrush (windows built as a single rounded card, e.g.
        // AboutWindow/SplashWindow).
        var document = XDocument.Load(SourcePath("src", "Tappy.App", windowFile));
        // Skip XAML property-element syntax (e.g. <Window.Resources>) — only
        // the actual content child (a Grid or Border) is the window's root.
        var contentRoot = document.Root!.Elements()
            .Single(element => !element.Name.LocalName.Contains('.'));

        var background = contentRoot.Attribute("Background")?.Value;
        Assert.False(string.IsNullOrEmpty(background),
            $"{windowFile}'s content root ({contentRoot.Name.LocalName}) has no Background — " +
            "it will render on WPF's plain white default instead of the app's theme.");
        Assert.StartsWith("{DynamicResource ", background, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("KeyboardAssignmentWindow.xaml")]
    [InlineData("BankControlWindow.xaml")]
    [InlineData("LedAnimationWindow.xaml")]
    [InlineData("FireColorPickerWindow.xaml")]
    public void Every_data_bound_combo_box_has_an_item_template(string windowFile)
    {
        // A ComboBox bound via ItemsSource (a plain object list, e.g. an
        // enum or string) with no ItemTemplate renders its closed-state
        // selection text using WPF's default template, which does not
        // reliably pick up the app-wide ComboBox Style's Foreground setter —
        // it comes out illegible (very low contrast) even though the
        // dropdown list itself, and every other themed control, looks fine.
        // LedAnimationWindow's Effect selector shipped exactly this way and
        // was reported unreadable directly by the owner. Every other
        // data-bound ComboBox in the app already works around it with an
        // explicit ItemTemplate (inline or a named StaticResource); a
        // ComboBox with static <ComboBoxItem> children instead of
        // ItemsSource sets Foreground directly on each item and is exempt.
        var document = XDocument.Load(SourcePath("src", "Tappy.App", windowFile));
        var boundComboBoxes = document.Descendants()
            .Where(element => element.Name.LocalName == "ComboBox" && element.Attribute("ItemsSource") is not null);

        Assert.All(boundComboBoxes, comboBox =>
        {
            var name = comboBox.Attribute(XamlNamespace + "Name")?.Value ?? "(unnamed)";
            var hasInlineTemplate = comboBox.Elements()
                .Any(child => child.Name.LocalName == "ComboBox.ItemTemplate");
            var hasReferencedTemplate = comboBox.Attribute("ItemTemplate") is not null;
            Assert.True(hasInlineTemplate || hasReferencedTemplate,
                $"{windowFile}'s ComboBox '{name}' is bound via ItemsSource with no ItemTemplate — " +
                "its selected text will render illegibly.");
        });
    }

    [Fact]
    public void Every_data_bound_list_box_sets_its_item_text_foreground_explicitly()
    {
        // A ListBox using DisplayMemberPath auto-generates a TextBlock per item
        // that does not reliably pick up ListBoxItem's themed Foreground setter —
        // the same class of bug the ComboBox test above guards, but for ListBox.
        // LedAnimationWindow's Library and Frame-timeline lists both shipped this
        // way: their auto-generated text rendered in the app-wide light TextBrush
        // instead of the deliberately white-backgrounded AssignmentListTextBrush,
        // i.e. invisible white-on-white text, confirmed live in the running app
        // (the row highlighted blue on click, proving an item existed, but no
        // label was ever visible). The fix is the same as the ComboBox case: an
        // explicit ItemTemplate that sets Foreground directly on the TextBlock,
        // instead of DisplayMemberPath.
        var document = XDocument.Load(SourcePath("src", "Tappy.App", "LedAnimationWindow.xaml"));
        var listBoxes = document.Descendants().Where(element => element.Name.LocalName == "ListBox");

        Assert.All(listBoxes, listBox =>
        {
            var name = listBox.Attribute(XamlNamespace + "Name")?.Value ??
                listBox.Attribute("AutomationProperties.Name")?.Value ?? "(unnamed)";
            Assert.Null(listBox.Attribute("DisplayMemberPath"));
            var hasItemTemplate = listBox.Elements().Any(child => child.Name.LocalName == "ListBox.ItemTemplate");
            Assert.True(hasItemTemplate,
                $"LedAnimationWindow.xaml's ListBox '{name}' has no ItemTemplate — its item text will render illegibly.");
        });
    }

    [Fact]
    public void Assignment_editor_exposes_search_category_behavior_and_virtualized_results()
    {
        var document = XDocument.Load(
            SourcePath("src", "Tappy.App", "KeyboardAssignmentWindow.xaml"));

        Assert.NotNull(NamedElement(document, "TextBox", "SearchBox"));
        var category = NamedElement(document, "ComboBox", "CategoryBox");
        Assert.Equal("{StaticResource AssignmentCategoryItemTemplate}", category.Attribute("ItemTemplate")?.Value);
        AssertReadableTemplate(document, "MidiDeviceItemTemplate", "{Binding DisplayName}");
        var midiDevice = NamedElement(document, "ComboBox", "MidiDeviceBox");
        Assert.Equal("{StaticResource MidiDeviceItemTemplate}", midiDevice.Attribute("ItemTemplate")?.Value);

        var sequence = NamedElement(document, "ListBox", "SequenceList");
        Assert.Equal("SequenceList_OnMouseDoubleClick", sequence.Attribute("MouseDoubleClick")?.Value);
        Assert.NotNull(NamedElement(document, "Button", "EditStepButton"));
        Assert.NotNull(NamedElement(document, "Button", "ApplyStepChangesButton"));

        foreach (var comboBoxName in new[] { "MouseActionBox", "PowerShellHostBox", "MidiKindBox", "TimingBox" })
        {
            var comboBox = NamedElement(document, "ComboBox", comboBoxName);
            var items = comboBox.Elements().Where(element => element.Name.LocalName == "ComboBoxItem").ToArray();
            Assert.NotEmpty(items);
            Assert.All(items, item =>
            {
                var label = item.Elements().Single(element => element.Name.LocalName == "TextBlock");
                Assert.False(string.IsNullOrWhiteSpace(label.Attribute("Text")?.Value));
                Assert.Equal("{DynamicResource ComboBoxTextBrush}", label.Attribute("Foreground")?.Value);
            });
        }

        var behavior = NamedElement(document, "ComboBox", "TimingBox");
        Assert.Equal(4, behavior.Elements().Count(element => element.Name.LocalName == "ComboBoxItem"));
        Assert.All(
            behavior.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            text => Assert.Equal(
                "{DynamicResource ComboBoxTextBrush}",
                text.Attribute("Foreground")?.Value));

        var list = NamedElement(document, "ListBox", "AssignmentList");
        Assert.Equal("{DynamicResource AssignmentListBackgroundBrush}", list.Attribute("Background")?.Value);
        Assert.Equal("{DynamicResource AssignmentListTextBrush}", list.Attribute("Foreground")?.Value);
        Assert.Equal("{StaticResource AssignmentListItemStyle}", list.Attribute("ItemContainerStyle")?.Value);
        Assert.Equal("True", list.Attribute("VirtualizingPanel.IsVirtualizing")?.Value);
        Assert.Equal("Recycling", list.Attribute("VirtualizingPanel.VirtualizationMode")?.Value);

        var tabHeaders = document.Descendants()
            .Where(element => element.Name.LocalName == "TabItem")
            .Select(element => element.Attribute("Header")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(
            ["Keyboard", "Text + delay", "Mouse", "Program", "PowerShell", "MIDI", "OSC"],
            tabHeaders);
        var tabs = NamedElement(document, "TabControl", "ActionTabs");
        Assert.Equal("{DynamicResource PanelBrush}", tabs.Attribute("Background")?.Value);
        Assert.Equal("{DynamicResource TextBrush}", tabs.Attribute("Foreground")?.Value);
        Assert.Equal("{StaticResource AssignmentTabItemStyle}", tabs.Attribute("ItemContainerStyle")?.Value);
        var tabStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            element.Attribute(XamlNamespace + "Key")?.Value == "AssignmentTabItemStyle");
        var tabSetters = tabStyle.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .Where(element => element.Attribute("Value") is not null)
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);
        Assert.Equal("{DynamicResource TextBrush}", tabSetters["Foreground"]);
        Assert.Equal("{DynamicResource RaisedBrush}", tabSetters["Background"]);
        Assert.Equal("72", tabSetters["MinWidth"]);

        var tabTemplate = tabStyle.Descendants().Single(element =>
            element.Name.LocalName == "ControlTemplate" &&
            element.Attribute("TargetType")?.Value == "TabItem");
        var headerText = tabTemplate.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" &&
            element.Attribute(XamlNamespace + "Name")?.Value == "HeaderText");
        Assert.Equal("{TemplateBinding Header}", headerText.Attribute("Text")?.Value);
        Assert.Equal("{TemplateBinding Foreground}", headerText.Attribute("Foreground")?.Value);

        var selectedTabTrigger = tabTemplate.Descendants().Single(element =>
            element.Name.LocalName == "Trigger" &&
            element.Attribute("Property")?.Value == "IsSelected" &&
            element.Attribute("Value")?.Value == "True");
        var selectedTabSetters = selectedTabTrigger.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);
        Assert.Equal("{DynamicResource PressedTextBrush}", selectedTabSetters["Foreground"]);
        Assert.Equal("{DynamicResource AccentBrush}", selectedTabSetters["Background"]);
        var sequenceList = NamedElement(document, "ListBox", "SequenceList");
        Assert.Equal("{DynamicResource AssignmentListBackgroundBrush}", sequenceList.Attribute("Background")?.Value);
        Assert.Equal("{DynamicResource AssignmentListTextBrush}", sequenceList.Attribute("Foreground")?.Value);
        Assert.Null(sequenceList.Attribute("DisplayMemberPath"));
        var summary = sequenceList.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" &&
            element.Attribute("Text")?.Value == "{Binding Summary}");
        Assert.Equal(
            "{StaticResource AssignmentPrimaryTextStyle}",
            summary.Attribute("Style")?.Value);
        Assert.NotNull(NamedElement(document, "Button", "AssignButton"));
    }

    private static void AssertLightSurfaceControlStyles(XDocument document)
    {
        AssertStyleUsesBrushes(
            document,
            "TextBox",
            "{DynamicResource ComboBoxTextBrush}",
            "{DynamicResource ComboBoxBackgroundBrush}");
        AssertStyleUsesBrushes(
            document,
            "ComboBoxItem",
            "{DynamicResource ComboBoxTextBrush}",
            "{DynamicResource ComboBoxBackgroundBrush}");
        AssertStyleUsesBrushes(
            document,
            "ListBox",
            "{DynamicResource AssignmentListTextBrush}",
            "{DynamicResource AssignmentListBackgroundBrush}");

        var listItemStyle = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            element.Attribute("TargetType")?.Value == "ListBoxItem");
        var selectedTrigger = listItemStyle.Descendants().Single(element =>
            element.Name.LocalName == "Trigger" &&
            element.Attribute("Property")?.Value == "IsSelected" &&
            element.Attribute("Value")?.Value == "True");
        var setters = selectedTrigger.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToArray();
        Assert.Contains(setters, setter =>
            setter.Attribute("Property")?.Value == "Foreground" &&
            setter.Attribute("Value")?.Value == "{DynamicResource AssignmentListSelectionTextBrush}");
        Assert.Contains(setters, setter =>
            setter.Attribute("Property")?.Value == "Background" &&
            setter.Attribute("Value")?.Value == "{DynamicResource AssignmentListSelectionBrush}");
    }

    private static void AssertStyleUsesBrushes(
        XDocument document,
        string targetType,
        string foreground,
        string background)
    {
        var style = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            element.Attribute("TargetType")?.Value == targetType);
        var setters = style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);
        Assert.Equal(foreground, setters["Foreground"]);
        Assert.Equal(background, setters["Background"]);
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

    private static void AssertToolTipUsesReadableThemeBrushes(XDocument document)
    {
        var style = document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            element.Attribute("TargetType")?.Value == "ToolTip");
        var setters = style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("{DynamicResource TextBrush}", setters["Foreground"]);
        Assert.Equal("{DynamicResource RaisedBrush}", setters["Background"]);
        Assert.Equal("{DynamicResource BorderBrush}", setters["BorderBrush"]);
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
