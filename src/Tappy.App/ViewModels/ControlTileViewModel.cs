namespace Tappy.App.ViewModels;

public sealed class ControlTileViewModel : ObservableObject
{
    private bool _isPressed;
    private bool _isIlluminated;
    private bool _isSelected;
    private string _action = "Unassigned";

    public required string ControlId { get; init; }
    public required string Label { get; init; }

    public string AutomationName => $"Controller control {Label}, {Action}, {StateText}";

    public bool IsPressed
    {
        get => _isPressed;
        set
        {
            if (Set(ref _isPressed, value))
            {
                Raise(nameof(StateText));
                Raise(nameof(AutomationName));
            }
        }
    }

    public bool IsIlluminated
    {
        get => _isIlluminated;
        set => Set(ref _isIlluminated, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string Action
    {
        get => _action;
        set
        {
            if (Set(ref _action, value))
            {
                Raise(nameof(AutomationName));
            }
        }
    }

    public string StateText => IsPressed ? "Pressed" : "Released";
}
