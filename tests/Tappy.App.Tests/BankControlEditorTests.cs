using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tappy.App.Runtime;
using Tappy.Core.Input;
using Tappy.Core.Models;

namespace Tappy.App.Tests;

public sealed partial class KeyboardAssignmentEditorTests
{
    private static T Field<T>(BankControlWindow window, string name) => Assert.IsType<T>(window.FindName(name));
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("HighContrast")]
    public async Task Toggle_editor_preserves_both_banks_and_stays_readable_at_minimum_size(string theme)
    {
        await OnEditorThread(() =>
        {
            var resources = System.Windows.Application.Current.Resources;
            var originals = resources.MergedDictionaries.ToArray();
            try
            {
                resources.MergedDictionaries.Clear();
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Tappy;component/Themes/{theme}.xaml") });
                var source = new ControllerAssignmentTarget("s1", "p1", "raw-input", "layer-1", "combo", 1);
                var target = new ControllerBankEndpoint("s2", "p2", "raw-input", 1, "Razer Tartarus", "layer-1",
                    [new("layer-7", "7 · All three"), new("layer-8", "8 · All off")]);
                var binding = new BankControlBinding(new("combo"), "raw-input", "p2", "Tartarus", BankControlOperation.Toggle, "layer-7", AlternateLayerId: "layer-8");
                var window = new BankControlWindow("Combo: / + * + −", source, binding, [target]);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(520, 570)); content.Arrange(new Rect(0, 0, 520, 570)); content.UpdateLayout();
                Assert.Equal(Visibility.Visible, Field<StackPanel>(window, "AlternateBankPanel").Visibility);
                Assert.Equal("layer-7", Assert.IsType<ControllerBankChoice>(Field<ComboBox>(window, "BankBox").SelectedItem).Id);
                var alternate = Field<ComboBox>(window, "AlternateBankBox");
                Assert.Equal("layer-8", Assert.IsType<ControllerBankChoice>(alternate.SelectedItem).Id);
                var label = VisualDescendants<TextBlock>(alternate).Single(t => t.Text == "8 · All off");
                Assert.Equal(((SolidColorBrush)window.FindResource("ComboBoxTextBrush")).Color, ((SolidColorBrush)label.Foreground).Color);
                Assert.True(Field<Button>(window, "SaveButton").ActualHeight >= 40);
                SaveEditorLayoutRender(content, 520, 570, 1.25, $"toggle-{theme}.png");
                window.Close();
            }
            finally { resources.MergedDictionaries.Clear(); foreach (var resource in originals) { resources.MergedDictionaries.Add(resource); } }
        });
    }
    [Theory]
    [InlineData("Dark", 520, 570)]
    [InlineData("Light", 660, 650)]
    [InlineData("HighContrast", 520, 570)]
    public async Task Bank_editor_shows_readable_explicit_targets_and_never_substitutes_a_missing_device(string theme, int width, int height)
    {
        await OnEditorThread(() =>
        {
            var resources = System.Windows.Application.Current.Resources;
            var originals = resources.MergedDictionaries.ToArray();
            try
            {
                resources.MergedDictionaries.Clear();
                resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Tappy;component/Themes/{theme}.xaml") });
                var source = new ControllerAssignmentTarget("s1", "p1", "raw-input", "layer-1", "key1", 1);
                var destination = new ControllerBankEndpoint("s2", "p2", "raw-input", 1, "Razer Tartarus · ID ABC123", "layer-1",
                    [new("layer-1", "1 · Yellow"), new("layer-2", "2 · Green")]);
                var binding = new BankControlBinding(new ControlId("key1"), "raw-input", "p2", destination.DisplayName, BankControlOperation.Select, "layer-2");
                var window = new BankControlWindow("Microsoft pad · 1", source, binding, [destination]);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                var target = Field<ComboBox>(window, "TargetBox");
                Assert.Same(destination, target.SelectedItem);
                Assert.Equal("layer-2", Assert.IsType<ControllerBankChoice>(Field<ComboBox>(window, "BankBox").SelectedItem).Id);
                Assert.True(Field<Button>(window, "SaveButton").IsEnabled);
                foreach (var combo in new[] { target, Field<ComboBox>(window, "OperationBox"), Field<ComboBox>(window, "BankBox") })
                {
                    Assert.True(combo.ActualWidth > 350);
                    var presenter = VisualDescendants<TextBlock>(combo).FirstOrDefault(t => t.Text.Contains("Tartarus") || t.Text.Contains("Go to") || t.Text.Contains("Green"));
                    Assert.NotNull(presenter);
                    Assert.Equal(((SolidColorBrush)window.FindResource("ComboBoxTextBrush")).Color, ((SolidColorBrush)presenter.Foreground).Color);
                }
                var remove = Field<Button>(window, "RemoveButton"); var save = Field<Button>(window, "SaveButton");
                Assert.Equal(remove.ActualHeight, save.ActualHeight);
                var label = new TextBlock { Text = (string)remove.Content, FontSize = remove.FontSize, FontFamily = remove.FontFamily };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(label.DesiredSize.Width + remove.Padding.Left + remove.Padding.Right <= remove.ActualWidth);
                SaveEditorLayoutRender(content, width, height, 1.25, $"bank-control-{theme}.png");
                window.Close();
                var unavailable = new BankControlWindow("Microsoft pad · 1", source, binding,
                    [destination with { PersistentId = "other", SessionId = "s3" }]);
                Assert.Null(Field<ComboBox>(unavailable, "TargetBox").SelectedItem);
                Assert.False(Field<Button>(unavailable, "SaveButton").IsEnabled);
                Assert.True(Field<Button>(unavailable, "RemoveButton").IsEnabled);
                Assert.Contains("unavailable", Field<TextBlock>(unavailable, "StatusText").Text);
                unavailable.Close();
            }
            finally
            {
                resources.MergedDictionaries.Clear();
                foreach (var dictionary in originals) { resources.MergedDictionaries.Add(dictionary); }
            }
        });
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) { yield return typed; }
            foreach (var nested in VisualDescendants<T>(child)) { yield return nested; }
        }
    }
}
