using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Tappy.App;

/// <summary>
/// A minimal dialog for setting an exact pixel width/height on every
/// checked (group-selected) controller square in one action — the bulk
/// counterpart to dragging one tile's resize Thumb at a time. The actual
/// clamp is enforced authoritatively by
/// MainViewModel.ResizeGroupSelectionAsync (MinimumTileSize=48 up to
/// 600 wide / 400 tall, the same bounds the per-tile resize Thumb already
/// uses); these two numbers are duplicated here only for the hint text and
/// to disable Apply on obviously-invalid input, mirroring how
/// MainWindow.xaml.cs already keeps its own MinControllerLayoutZoom/
/// MaxControllerLayoutZoom rather than reaching into MainViewModel's
/// private constants.
/// </summary>
public partial class ResizeTilesWindow : Window
{
    private const double MinimumTileSize = 48;
    private const double MaximumTileWidth = 600;
    private const double MaximumTileHeight = 400;

    private readonly int _affectedCount;

    public ResizeTilesWindow(double initialWidth, double initialHeight, int affectedCount)
    {
        InitializeComponent();
        _affectedCount = affectedCount;
        WidthTextBox.Text = Math.Round(initialWidth).ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = Math.Round(initialHeight).ToString(CultureInfo.InvariantCulture);
        AffectedCountText.Text = affectedCount switch
        {
            0 => "No resizable square is selected. Check one or more squares, or select a single one, first.",
            1 => "This will resize 1 square.",
            _ => $"This will resize {affectedCount} squares.",
        };
        RangeHintText.Text =
            $"Width {MinimumTileSize:0}–{MaximumTileWidth:0}px, height {MinimumTileSize:0}–{MaximumTileHeight:0}px.";
        UpdateApplyEnabled();
    }

    public double SelectedWidth { get; private set; }
    public double SelectedHeight { get; private set; }

    private void SizeTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => UpdateApplyEnabled();

    private void UpdateApplyEnabled() =>
        ApplyButton.IsEnabled = _affectedCount > 0 &&
                                 TryParseSize(WidthTextBox.Text, MaximumTileWidth, out _) &&
                                 TryParseSize(HeightTextBox.Text, MaximumTileHeight, out _);

    private static bool TryParseSize(string text, double maximum, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        value >= MinimumTileSize && value <= maximum;

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseSize(WidthTextBox.Text, MaximumTileWidth, out var width) ||
            !TryParseSize(HeightTextBox.Text, MaximumTileHeight, out var height))
        {
            return;
        }

        SelectedWidth = width;
        SelectedHeight = height;
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
