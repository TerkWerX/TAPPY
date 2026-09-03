using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tappy.App.Models;
using Tappy.App.Runtime;
using Tappy.App.Services;

namespace Tappy.App;

public partial class KeyboardAssignmentWindow : Window
{
    private readonly IReadOnlyList<KeyboardAssignmentOption> _allOptions;

    public KeyboardAssignmentWindow(string controlLabel)
    {
        _allOptions = KeyboardAssignmentCatalog.Create();
        InitializeComponent();
        ControlLabelText.Text = $"Selected controller control: {controlLabel}";
        CategoryBox.ItemsSource = KeyboardAssignmentCatalog.CreateCategories(_allOptions);
        CategoryBox.SelectedIndex = 0;
        RefreshFilter();
        SearchBox.Focus();
    }

    public KeyboardMappingAssignment? Result { get; private set; }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

    private void CategoryBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFilter();

    private void AssignmentList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var option = AssignmentList.SelectedItem as KeyboardAssignmentOption;
        AssignButton.IsEnabled = option is not null;
        SelectionSummaryText.Text = option is null
            ? "Choose an assignment above."
            : $"Selected: {option.Name} — {option.Shortcut}";
    }

    private void AssignmentList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AssignmentList.SelectedItem is KeyboardAssignmentOption)
        {
            CompleteSelection();
        }
    }

    private void AssignButton_OnClick(object sender, RoutedEventArgs e) => CompleteSelection();

    private void RefreshFilter()
    {
        if (AssignmentList is null || CountText is null)
        {
            return;
        }

        var category = (CategoryBox?.SelectedItem as AssignmentCategory)?.Name ??
                       KeyboardAssignmentCatalog.AllCategory;
        var terms = (SearchBox?.Text ?? string.Empty)
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var filtered = _allOptions
            .Where(option => category == KeyboardAssignmentCatalog.AllCategory || option.Category == category)
            .Where(option => terms.All(term => option.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        AssignmentList.ItemsSource = filtered;
        CountText.Text = $"Showing {filtered.Length:N0} of {_allOptions.Count:N0} keyboard assignments.";
    }

    private void CompleteSelection()
    {
        if (AssignmentList.SelectedItem is not KeyboardAssignmentOption option)
        {
            return;
        }

        var timing = (TimingBox.SelectedItem as ComboBoxItem)?.Tag as string;
        Result = timing switch
        {
            "HoldUntilRelease" => KeyboardMappingAssignment.HoldUntilRelease(
                $"Hold {option.Shortcut} until release", option.Keys),
            "ReleaseOnce" => KeyboardMappingAssignment.ReleaseOnce(
                $"On release: {option.Name} — {option.Shortcut}", option.Keys),
            _ => KeyboardMappingAssignment.PressOnce(
                $"{option.Name} — {option.Shortcut}", option.Keys)
        };
        DialogResult = true;
    }
}
