using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tappy.App.Models;
using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Windows.Output;

namespace Tappy.App;

public partial class KeyboardAssignmentWindow : Window
{
    private sealed record StepRow(ControllerActionStepDefinition Step, string Summary);

    private readonly IReadOnlyList<KeyboardAssignmentOption> _allOptions;
    private readonly List<ControllerActionStepDefinition> _steps = [];

    public KeyboardAssignmentWindow(string controlLabel)
    {
        _allOptions = KeyboardAssignmentCatalog.Create();
        InitializeComponent();
        ControlLabelText.Text = $"Selected controller control: {controlLabel}";
        CategoryBox.ItemsSource = KeyboardAssignmentCatalog.CreateCategories(_allOptions);
        CategoryBox.SelectedIndex = 0;
        RefreshFilter();
        RefreshMidiDevices();
        RefreshStepList();
        SearchBox.Focus();
    }

    /// <summary>Retained for the fast, well-tested single-keyboard-action path.</summary>
    public KeyboardMappingAssignment? Result { get; private set; }

    public ControllerActionAssignment? ActionResult { get; private set; }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

    private void CategoryBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFilter();

    private void AssignmentList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssignmentList.SelectedItem is KeyboardAssignmentOption option)
        {
            ShowStatus($"Selected: {option.Name} — {option.Shortcut}");
        }
    }

    private void AssignmentList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => AddKeyboardStep();

    private void AddKeyboardStep_OnClick(object sender, RoutedEventArgs e) => AddKeyboardStep();

    private void AddKeyboardStep()
    {
        if (AssignmentList.SelectedItem is not KeyboardAssignmentOption option)
        {
            ShowError("Choose a keyboard assignment first.");
            return;
        }

        AddStep(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.KeyboardChord,
            Keys = option.Keys.Select(key => new KeyboardOutputKey(key)).ToList(),
            DurationMs = 25
        }, $"Keyboard step added: {option.Shortcut}.");
    }

    private void AddTextStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TextValueBox.Text))
        {
            ShowError("Enter the text Tappy should type.");
            return;
        }

        if (TextValueBox.Text.Length > 32_768)
        {
            ShowError("Text is limited to 32,768 characters per step.");
            return;
        }

        AddStep(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Text,
            Value = TextValueBox.Text
        }, "Text step added.");
    }

    private void AddDelayStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryInteger(DelayBox.Text, 1, 600_000, "Delay", out var milliseconds))
        {
            return;
        }

        AddStep(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Delay,
            DurationMs = milliseconds
        }, $"Delay step added: {milliseconds:N0} ms.");
    }

    private void AddMouseStep_OnClick(object sender, RoutedEventArgs e)
    {
        var action = SelectedTag(MouseActionBox);
        ControllerActionStepDefinition step;
        switch (action)
        {
            case "Move":
                if (!TryInteger(MouseXBox.Text, -100_000, 100_000, "Mouse X", out var x) ||
                    !TryInteger(MouseYBox.Text, -100_000, 100_000, "Mouse Y", out var y))
                {
                    return;
                }

                step = new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.MouseMove,
                    X = x,
                    Y = y
                };
                break;
            case "Vertical wheel":
                if (!TryInteger(MouseYBox.Text, -1_000_000, 1_000_000, "Scroll amount", out var vertical))
                {
                    return;
                }

                step = new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.MouseWheel,
                    Value = "vertical",
                    Amount = vertical
                };
                break;
            case "Horizontal wheel":
                if (!TryInteger(MouseXBox.Text, -1_000_000, 1_000_000, "Scroll amount", out var horizontal))
                {
                    return;
                }

                step = new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.MouseWheel,
                    Value = "horizontal",
                    Amount = horizontal
                };
                break;
            default:
                step = new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.MouseButton,
                    Value = action
                };
                break;
        }

        AddStep(step, $"Mouse step added: {action}.");
    }

    private void AddProgramStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProgramPathBox.Text))
        {
            ShowError("Enter a program, file, folder, or URL to launch.");
            return;
        }

        AddStep(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.LaunchProgram,
            Value = ProgramPathBox.Text.Trim(),
            Arguments = ProgramArgumentsBox.Text,
            WorkingDirectory = ProgramWorkingDirectoryBox.Text.Trim()
        }, "Launch step added.");
    }

    private void AddPowerShellStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PowerShellCommandBox.Text))
        {
            ShowError("Enter a PowerShell command.");
            return;
        }

        AddStep(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.PowerShellCommand,
            Value = PowerShellCommandBox.Text,
            Target = SelectedTag(PowerShellHostBox),
            WorkingDirectory = PowerShellWorkingDirectoryBox.Text.Trim()
        }, "PowerShell step added. It will run hidden, non-interactive, and without a profile.");
    }

    private void RefreshMidi_OnClick(object sender, RoutedEventArgs e) => RefreshMidiDevices();

    private void RefreshMidiDevices()
    {
        try
        {
            MidiDeviceBox.ItemsSource = WinMmMidiOutput.GetDevices();
            MidiDeviceBox.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            MidiDeviceBox.ItemsSource = Array.Empty<WinMmMidiOutput.Device>();
            ShowError($"MIDI outputs could not be enumerated: {exception.Message}");
        }
    }

    private void AddMidiStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryInteger(MidiChannelBox.Text, 1, 16, "MIDI channel", out var channel) ||
            !TryInteger(MidiData1Box.Text, 0, 127, "MIDI data", out var data1))
        {
            return;
        }

        var kind = SelectedTag(MidiKindBox);
        var description = $"{kind}:{channel}:{data1}";
        if (kind != "pc")
        {
            if (!TryInteger(MidiData2Box.Text, 0, 127, "MIDI velocity/value", out var data2))
            {
                return;
            }

            description += $":{data2}";
        }

        try
        {
            _ = MidiMessageParser.Parse(description);
        }
        catch (ArgumentException exception)
        {
            ShowError(exception.Message);
            return;
        }

        var device = MidiDeviceBox.SelectedItem as WinMmMidiOutput.Device;
        AddStep(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Midi,
            Value = description,
            Target = device?.Name ?? string.Empty
        }, $"MIDI step added: {description}.");

        if (kind == "note" && SelectedTag(TimingBox) == "PressOnce")
        {
            TimingBox.SelectedIndex = 1;
            ShowStatus("MIDI note-on added. Behavior was set to stop and send note-off when the controller key is released.");
        }
    }

    private void AddOscStep_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryInteger(OscPortBox.Text, 1, 65_535, "OSC UDP port", out var port))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(OscHostBox.Text))
        {
            ShowError("Enter an OSC host name or IP address.");
            return;
        }

        try
        {
            _ = OscPacketBuilder.Build(OscAddressBox.Text.Trim(), OscValuesBox.Text);
        }
        catch (ArgumentException exception)
        {
            ShowError(exception.Message);
            return;
        }

        AddStep(new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Osc,
            Target = OscHostBox.Text.Trim(),
            Amount = port,
            Value = OscAddressBox.Text.Trim(),
            Arguments = OscValuesBox.Text
        }, $"OSC step added: {OscAddressBox.Text.Trim()} → {OscHostBox.Text.Trim()}:{port}.");
    }

    private void AddStep(ControllerActionStepDefinition step, string status)
    {
        if (_steps.Count >= 500)
        {
            ShowError("An assignment is limited to 500 steps.");
            return;
        }

        step.Normalize();
        _steps.Add(step);
        RefreshStepList(_steps.Count - 1);
        ShowStatus(status);
    }

    private void SequenceList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStepButtons();

    private void MoveStepUp_OnClick(object sender, RoutedEventArgs e)
    {
        var index = SequenceList.SelectedIndex;
        if (index <= 0)
        {
            return;
        }

        (_steps[index - 1], _steps[index]) = (_steps[index], _steps[index - 1]);
        RefreshStepList(index - 1);
    }

    private void MoveStepDown_OnClick(object sender, RoutedEventArgs e)
    {
        var index = SequenceList.SelectedIndex;
        if (index < 0 || index >= _steps.Count - 1)
        {
            return;
        }

        (_steps[index + 1], _steps[index]) = (_steps[index], _steps[index + 1]);
        RefreshStepList(index + 1);
    }

    private void RemoveStep_OnClick(object sender, RoutedEventArgs e)
    {
        var index = SequenceList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _steps.RemoveAt(index);
        RefreshStepList(Math.Min(index, _steps.Count - 1));
    }

    private void ClearSteps_OnClick(object sender, RoutedEventArgs e)
    {
        _steps.Clear();
        RefreshStepList();
        ShowStatus("Assignment steps cleared.");
    }

    private void RefreshStepList(int selectedIndex = -1)
    {
        SequenceList.ItemsSource = _steps.Select(step => new StepRow(step, step.ToSummary())).ToArray();
        SequenceList.SelectedIndex = selectedIndex;
        SequenceCountText.Text = $"{_steps.Count:N0} of 500 steps";
        AssignButton.IsEnabled = _steps.Count > 0;
        UpdateStepButtons();
    }

    private void UpdateStepButtons()
    {
        var index = SequenceList.SelectedIndex;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < _steps.Count - 1;
        RemoveStepButton.IsEnabled = index >= 0;
    }

    private void ActionTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, ActionTabs))
        {
            return;
        }

        ShowStatus("Choose an action, add it to the sequence, then arrange the steps on the right.");
    }

    private void AssignButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0)
        {
            ShowError("Add one or more steps first.");
            return;
        }

        var timing = SelectedTag(TimingBox);
        if (timing == "RepeatWhileHeld" && _steps.Any(step =>
                step.Type is ControllerActionStepType.LaunchProgram or ControllerActionStepType.PowerShellCommand))
        {
            ShowError("Program and PowerShell steps cannot repeat while held.");
            return;
        }

        // Preserve the established synchronous keyboard path for the common one-step case.
        if (_steps.Count == 1 && _steps[0].Type == ControllerActionStepType.KeyboardChord &&
            timing != "RepeatWhileHeld")
        {
            var keys = _steps[0].Keys.Select(key => key.Value).ToArray();
            var shortcut = string.Join(" + ", keys);
            Result = timing switch
            {
                "HoldUntilRelease" => KeyboardMappingAssignment.HoldUntilRelease(
                    $"Hold {shortcut} until release", keys),
                "ReleaseOnce" => KeyboardMappingAssignment.ReleaseOnce(
                    $"On release: {shortcut}", keys),
                _ => KeyboardMappingAssignment.PressOnce($"Tap {shortcut}", keys)
            };
            DialogResult = true;
            return;
        }

        var mode = timing switch
        {
            "HoldUntilRelease" => ControllerActionSequenceMode.WhileHeld,
            "RepeatWhileHeld" => ControllerActionSequenceMode.RepeatWhileHeld,
            _ => ControllerActionSequenceMode.RunOnce
        };
        var name = SummarizeAssignment(_steps);
        var sequence = new ControllerActionSequenceDefinition
        {
            Name = name,
            Mode = mode,
            Steps = _steps.Select(step => step.Clone()).ToList()
        };
        ActionResult = timing == "ReleaseOnce"
            ? new ControllerActionAssignment(name, new ControllerActionSequenceDefinition(), sequence)
            : new ControllerActionAssignment(name, sequence, new ControllerActionSequenceDefinition());
        DialogResult = true;
    }

    private void RefreshFilter()
    {
        if (AssignmentList is null || CountText is null)
        {
            return;
        }

        var category = (CategoryBox?.SelectedItem as AssignmentCategory)?.Name ?? KeyboardAssignmentCatalog.AllCategory;
        var terms = (SearchBox?.Text ?? string.Empty)
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var filtered = _allOptions
            .Where(option => category == KeyboardAssignmentCatalog.AllCategory || option.Category == category)
            .Where(option => terms.All(term => option.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        AssignmentList.ItemsSource = filtered;
        CountText.Text = $"Showing {filtered.Length:N0} of {_allOptions.Count:N0} keyboard assignments.";
    }

    private static string SummarizeAssignment(IReadOnlyList<ControllerActionStepDefinition> steps)
    {
        var summary = string.Join(" → ", steps.Take(3).Select(step => step.ToSummary()));
        return steps.Count > 3 ? $"{summary} → +{steps.Count - 3:N0} more" : summary;
    }

    private static string SelectedTag(System.Windows.Controls.ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

    private bool TryInteger(string value, int minimum, int maximum, string label, out int result)
    {
        if (int.TryParse(value, out result) && result >= minimum && result <= maximum)
        {
            return true;
        }

        ShowError($"{label} must be a whole number from {minimum:N0} through {maximum:N0}.");
        return false;
    }

    private void ShowError(string message)
    {
        ValidationText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        ValidationText.Text = message;
    }

    private void ShowStatus(string message)
    {
        ValidationText.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        ValidationText.Text = message;
    }
}
