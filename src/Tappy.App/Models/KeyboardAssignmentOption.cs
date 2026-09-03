using Tappy.App.Services;

namespace Tappy.App.Models;

public sealed record KeyboardAssignmentOption(
    string Category,
    string Name,
    string Shortcut,
    string Description,
    IReadOnlyList<string> Keys)
{
    public string SearchText => $"{Category} {Name} {Shortcut} {Description}";
}

public sealed record AssignmentCategory(string Name, int Count)
{
    public string DisplayName => Name == KeyboardAssignmentCatalog.AllCategory
        ? $"All assignments ({Count:N0})"
        : $"{Name} ({Count:N0})";
}
