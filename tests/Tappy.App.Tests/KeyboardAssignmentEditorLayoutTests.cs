using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tappy.App.Tests;

public sealed partial class KeyboardAssignmentEditorTests
{
    [Theory]
    [InlineData("Dark", 880, 620, 1.0)]
    [InlineData("Dark", 1080, 760, 1.25)]
    [InlineData("Dark", 1440, 900, 1.5)]
    [InlineData("Light", 880, 620, 1.0)]
    [InlineData("Light", 1080, 760, 1.25)]
    [InlineData("Light", 1440, 900, 1.5)]
    [InlineData("HighContrast", 880, 620, 1.0)]
    [InlineData("HighContrast", 1080, 760, 1.25)]
    [InlineData("HighContrast", 1440, 900, 1.5)]
    public async Task Step_actions_remain_equal_aligned_and_unclipped(string theme, int width, int height, double scale)
    {
        await OnEditorThread(() =>
        {
            var resources = System.Windows.Application.Current.Resources;
            var originalThemes = resources.MergedDictionaries.ToArray();
            KeyboardAssignmentWindow? editor = null;
            try
            {
                resources.MergedDictionaries.Clear();
                resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"pack://application:,,,/Tappy;component/Themes/{theme}.xaml") });
                editor = CreateEditor(TextAssignment("Keep my saved assignment"));
                var content = (FrameworkElement)editor.Content;
                void Layout()
                {
                    content.Measure(new Size(width, height));
                    content.Arrange(new Rect(0, 0, width, height));
                    content.UpdateLayout();
                }
                Layout();
                var grid = Field<Grid>(editor, "StepActionsGrid");
                var buttons = new[] { "MoveUpButton", "MoveDownButton", "EditStepButton", "ApplyStepChangesButton", "RemoveStepButton", "ClearStepsButton" }
                    .Select(name => Field<Button>(editor, name)).ToArray();
                Rect Bounds(FrameworkElement element, Visual parent) => element.TransformToAncestor(parent)
                    .TransformBounds(new Rect(element.RenderSize));
                var initial = buttons.Select(button => Bounds(button, grid)).ToArray();
                Assert.All(initial, r =>
                {
                    Assert.Equal(initial[0].Width, r.Width, 3);
                    Assert.Equal(initial[0].Height, r.Height, 3);
                    Assert.True(r.Height >= 38);
                    Assert.True(r.Left >= 0 && r.Top >= 0 && r.Right <= grid.ActualWidth + .01 && r.Bottom <= grid.ActualHeight + .01);
                });
                for (var row = 0; row < 3; row++)
                {
                    var left = initial[row * 2]; var right = initial[row * 2 + 1];
                    Assert.Equal(left.Top, right.Top, 3);
                    Assert.Equal(8, right.Left - left.Right, 3);
                    if (row > 0) { Assert.Equal(8, left.Top - initial[(row - 1) * 2].Bottom, 3); }
                }
                Assert.True(Bounds(Field<ListBox>(editor, "SequenceList"), content).Bottom <= Bounds(grid, content).Top);
                foreach (var button in buttons)
                {
                    var label = new TextBlock { Text = (string)button.Content, FontFamily = button.FontFamily, FontSize = button.FontSize };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.True(label.DesiredSize.Width + button.Padding.Left + button.Padding.Right + 4 <= button.ActualWidth,
                        $"{button.Content} must fit without clipping at {width}.");
                }
                Assert.Equal(Field<Button>(editor, "AssignButton").ActualHeight, Field<Button>(editor, "CancelAssignmentButton").ActualHeight, 3);
                var sequence = Field<ListBox>(editor, "SequenceList");
                var selected = Assert.IsType<ListBoxItem>(sequence.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.True(selected.IsSelected);
                var selectionShell = Assert.IsType<Border>(selected.Template.FindName("AssignmentItemShell", selected));
                Assert.Equal(((SolidColorBrush)editor.FindResource("AssignmentListSelectionBrush")).Color,
                    Assert.IsType<SolidColorBrush>(selectionShell.Background).Color);
                Assert.Equal(((SolidColorBrush)editor.FindResource("AssignmentListSelectionTextBrush")).Color,
                    Assert.IsType<SolidColorBrush>(selected.Foreground).Color);
                SaveEditorLayoutRender(content, width, height, scale, $"editor-actions-{theme}-{width}.png");
                Click(editor, "EditStep_OnClick");
                Layout();
                Assert.True(Field<Button>(editor, "ApplyStepChangesButton").IsEnabled);
                Assert.Equal(initial, buttons.Select(button => Bounds(button, grid)).ToArray());
                Assert.Equal("Keep my saved assignment", Assert.Single(DraftSteps(editor)).Value);
            }
            finally
            {
                editor?.Close();
                resources.MergedDictionaries.Clear();
                foreach (var dictionary in originalThemes) { resources.MergedDictionaries.Add(dictionary); }
            }
        });
    }

    private static void SaveEditorLayoutRender(FrameworkElement content, int width, int height, double scale, string name)
    {
        var directory = Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
