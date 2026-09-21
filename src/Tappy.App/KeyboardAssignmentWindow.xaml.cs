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
    private readonly Func<IReadOnlyList<WinMmMidiOutput.Device>> _enumerateMidiDevices;
    private readonly List<ControllerActionStepDefinition> _steps = [];
    private ControllerActionSequenceDefinition _preservedReleaseSequence = new();
    private int _editingStepIndex = -1;

    public KeyboardAssignmentWindow(
        string controlLabel,
        ControllerActionAssignment? existingAssignment = null)
        : this(controlLabel, existingAssignment, () => WinMmMidiOutput.GetDevices())
    {
    }

    internal KeyboardAssignmentWindow(
        string controlLabel,
        ControllerActionAssignment? existingAssignment,
        Func<IReadOnlyList<WinMmMidiOutput.Device>> enumerateMidiDevices)
    {
        _enumerateMidiDevices = enumerateMidiDevices ?? throw new ArgumentNullException(nameof(enumerateMidiDevices));
        _allOptions = KeyboardAssignmentCatalog.Create();
        InitializeComponent();
        ControlLabelText.Text = $"Selected controller control: {controlLabel}";
        CategoryBox.ItemsSource = KeyboardAssignmentCatalog.CreateCategories(_allOptions);
        CategoryBox.SelectedIndex = 0;
        RefreshFilter();
        RefreshMidiDevices();
        if (existingAssignment is null)
        {
            RefreshStepList();
        }
        else
        {
            LoadExistingAssignment(existingAssignment);
        }

        SearchBox.Focus();
    }

    /// <summary>Retained for the fast, well-tested single-keyboard-action path.</summary>
    public KeyboardMappingAssignment? Result { get; private set; }

    public ControllerActionAssignment? ActionResult { get; private set; }

    internal Func<KeyboardMappingAssignment?, ControllerActionAssignment?, RuntimeOperation>? SaveAssignment { get; init; }

    private void LoadExistingAssignment(ControllerActionAssignment assignment)
    {
        var press = assignment.PressSequence?.Clone() ?? new ControllerActionSequenceDefinition();
        var release = assignment.ReleaseSequence?.Clone() ?? new ControllerActionSequenceDefinition();
        var editRelease = press.IsEmpty && !release.IsEmpty;
        var editable = editRelease ? release : press;
        _steps.AddRange(editable.Steps.Select(step => step.Clone()));
        _preservedReleaseSequence = !editRelease && !release.IsEmpty
            ? release
            : new ControllerActionSequenceDefinition();
        TimingBox.SelectedIndex = editRelease
            ? 3
            : editable.Mode switch
            {
                ControllerActionSequenceMode.WhileHeld => 1,
                ControllerActionSequenceMode.RepeatWhileHeld => 2,
                _ => 0,
            };
        Title = "Edit Tappy assignment";
        EditorHeading.Text = "Edit an assignment";
        AssignButton.Content = "Save assignment changes";
        RefreshStepList(_steps.Count > 0 ? 0 : -1);
        ShowStatus(
            "The existing assignment was loaded intact. Select a step and click Edit step (or double-click it), then apply and save your changes.");
    }

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

    private void AddKeyboardStep() => AddStepFromEditor(ControllerActionStepType.KeyboardChord);

    private void AddTextStep_OnClick(object sender, RoutedEventArgs e) => AddStepFromEditor(ControllerActionStepType.Text);

    private void AddDelayStep_OnClick(object sender, RoutedEventArgs e) => AddStepFromEditor(ControllerActionStepType.Delay);

    private void AddMouseStep_OnClick(object sender, RoutedEventArgs e) => AddStepFromEditor(ControllerActionStepType.MouseButton);

    private void AddProgramStep_OnClick(object sender, RoutedEventArgs e) => AddStepFromEditor(ControllerActionStepType.LaunchProgram);

    private void AddPowerShellStep_OnClick(object sender, RoutedEventArgs e) => AddStepFromEditor(ControllerActionStepType.PowerShellCommand);

    private void AddMidiStep_OnClick(object sender, RoutedEventArgs e) => AddStepFromEditor(ControllerActionStepType.Midi);

    private void AddOscStep_OnClick(object sender, RoutedEventArgs e) => AddStepFromEditor(ControllerActionStepType.Osc);

    private ControllerActionStepDefinition? BuildKeyboardStep()
    {
        if (AssignmentList.SelectedItem is not KeyboardAssignmentOption option)
        {
            ShowError("Choose a keyboard assignment first.");
            return null;
        }

        return new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.KeyboardChord,
            Keys = option.Keys.Select(key => new KeyboardOutputKey(key)).ToList(),
            DurationMs = 25
        };
    }

    private ControllerActionStepDefinition? BuildTextStep()
    {
        if (string.IsNullOrEmpty(TextValueBox.Text))
        {
            ShowError("Enter the text Tappy should type.");
            return null;
        }

        if (TextValueBox.Text.Length > 32_768)
        {
            ShowError("Text is limited to 32,768 characters per step.");
            return null;
        }

        return new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Text,
            Value = TextValueBox.Text
        };
    }

    private ControllerActionStepDefinition? BuildDelayStep()
    {
        if (!TryInteger(DelayBox.Text, 1, 600_000, "Delay", out var milliseconds))
        {
            return null;
        }

        return new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Delay,
            DurationMs = milliseconds
        };
    }

    private ControllerActionStepDefinition? BuildMouseStep()
    {
        var action = SelectedTag(MouseActionBox);
        ControllerActionStepDefinition step;
        switch (action)
        {
            case "Move":
                if (!TryInteger(MouseXBox.Text, -100_000, 100_000, "Mouse X", out var x) ||
                    !TryInteger(MouseYBox.Text, -100_000, 100_000, "Mouse Y", out var y))
                {
                    return null;
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
                    return null;
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
                    return null;
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

        return step;
    }

    private ControllerActionStepDefinition? BuildProgramStep()
    {
        if (string.IsNullOrWhiteSpace(ProgramPathBox.Text))
        {
            ShowError("Enter a program, file, folder, or URL to launch.");
            return null;
        }

        return new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.LaunchProgram,
            Value = ProgramPathBox.Text.Trim(),
            Arguments = ProgramArgumentsBox.Text,
            WorkingDirectory = ProgramWorkingDirectoryBox.Text.Trim()
        };
    }

    private ControllerActionStepDefinition? BuildPowerShellStep()
    {
        if (string.IsNullOrWhiteSpace(PowerShellCommandBox.Text))
        {
            ShowError("Enter a PowerShell command.");
            return null;
        }

        return new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.PowerShellCommand,
            Value = PowerShellCommandBox.Text,
            Target = SelectedTag(PowerShellHostBox),
            WorkingDirectory = PowerShellWorkingDirectoryBox.Text.Trim()
        };
    }

    private void RefreshMidi_OnClick(object sender, RoutedEventArgs e) => RefreshMidiDevices();

    private void RefreshMidiDevices()
    {
        try
        {
            MidiDeviceBox.ItemsSource = _enumerateMidiDevices();
            MidiDeviceBox.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            MidiDeviceBox.ItemsSource = Array.Empty<WinMmMidiOutput.Device>();
            ShowError($"MIDI outputs could not be enumerated: {exception.Message}");
        }
    }

    private ControllerActionStepDefinition? BuildMidiStep(ControllerActionStepDefinition? originalStep)
    {
        if (!TryInteger(MidiChannelBox.Text, 1, 16, "MIDI channel", out var channel) ||
            !TryInteger(MidiData1Box.Text, 0, 127, "MIDI data", out var data1))
        {
            return null;
        }

        var kind = SelectedTag(MidiKindBox);
        var description = $"{kind}:{channel}:{data1}";
        if (kind != "pc")
        {
            if (!TryInteger(MidiData2Box.Text, 0, 127, "MIDI velocity/value", out var data2))
            {
                return null;
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
            return null;
        }

        var device = MidiDeviceBox.SelectedItem as WinMmMidiOutput.Device;
        var target = device?.Name;
        if (string.IsNullOrWhiteSpace(target) &&
            originalStep?.Type == ControllerActionStepType.Midi)
        {
            target = originalStep.Target;
        }

        return new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Midi,
            Value = description,
            Target = target ?? string.Empty
        };
    }

    private ControllerActionStepDefinition? BuildOscStep()
    {
        if (!TryInteger(OscPortBox.Text, 1, 65_535, "OSC UDP port", out var port))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(OscHostBox.Text))
        {
            ShowError("Enter an OSC host name or IP address.");
            return null;
        }

        try
        {
            _ = OscPacketBuilder.Build(OscAddressBox.Text.Trim(), OscValuesBox.Text);
        }
        catch (ArgumentException exception)
        {
            ShowError(exception.Message);
            return null;
        }

        return new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Osc,
            Target = OscHostBox.Text.Trim(),
            Amount = port,
            Value = OscAddressBox.Text.Trim(),
            Arguments = OscValuesBox.Text
        };
    }

    private ControllerActionStepDefinition? BuildStep(
        ControllerActionStepType type,
        ControllerActionStepDefinition? originalStep = null) => type switch
        {
            ControllerActionStepType.KeyboardChord => BuildKeyboardStep(),
            ControllerActionStepType.Text => BuildTextStep(),
            ControllerActionStepType.Delay => BuildDelayStep(),
            ControllerActionStepType.MouseButton or ControllerActionStepType.MouseMove or ControllerActionStepType.MouseWheel => BuildMouseStep(),
            ControllerActionStepType.LaunchProgram => BuildProgramStep(),
            ControllerActionStepType.PowerShellCommand => BuildPowerShellStep(),
            ControllerActionStepType.Midi => BuildMidiStep(originalStep),
            ControllerActionStepType.Osc => BuildOscStep(),
            _ => null,
        };

    private void AddStepFromEditor(ControllerActionStepType type)
    {
        if (_steps.Count >= 500)
        {
            ShowError("An assignment is limited to 500 steps.");
            return;
        }

        var step = BuildStep(type);
        if (step is null)
        {
            return;
        }

        step.Normalize();
        _steps.Add(step);
        EndStepEdit();
        RefreshStepList(_steps.Count - 1);
        ShowCommittedStepStatus(step, $"Step added: {step.ToSummary()}.");
    }

    private void ShowCommittedStepStatus(ControllerActionStepDefinition step, string status)
    {
        ShowStatus(status);
        if (step.Type == ControllerActionStepType.Midi &&
            step.Value.StartsWith("note:", StringComparison.Ordinal) &&
            SelectedTag(TimingBox) == "PressOnce")
        {
            TimingBox.SelectedIndex = 1;
            ShowStatus(status + " Behavior was set to stop and send note-off when the controller key is released.");
        }
    }

    private void SequenceList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStepButtons();

    private void SequenceList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => BeginStepEdit();

    private void EditStep_OnClick(object sender, RoutedEventArgs e) => BeginStepEdit();

    private void BeginStepEdit()
    {
        var index = SequenceList.SelectedIndex;
        if (index < 0 || index >= _steps.Count)
        {
            return;
        }

        if (!PopulateEditor(_steps[index]))
        {
            EndStepEdit();
            return;
        }

        _editingStepIndex = index;
        ApplyStepChangesButton.IsEnabled = true;
        ShowStatus($"Editing step {index + 1:N0}. Change its fields, then click Apply changes. Nothing is saved until Save assignment changes is clicked.");
    }

    private void ApplyStepChanges_OnClick(object sender, RoutedEventArgs e)
    {
        if (_editingStepIndex < 0 || _editingStepIndex >= _steps.Count)
        {
            return;
        }

        var index = _editingStepIndex;
        var original = _steps[index];
        var replacement = BuildStep(original.Type, original);
        if (replacement is null)
        {
            return;
        }

        replacement.Normalize();
        _steps[index] = replacement;
        EndStepEdit();
        RefreshStepList(index);
        ShowCommittedStepStatus(replacement,
            $"Step {index + 1:N0} updated. Save assignment changes when the sequence is ready.");
    }

    private bool PopulateEditor(ControllerActionStepDefinition step)
    {
        switch (step.Type)
        {
            case ControllerActionStepType.KeyboardChord:
                {
                    ActionTabs.SelectedIndex = 0;
                    var keys = step.Keys.Select(key => key.Value).ToArray();
                    var option = _allOptions.FirstOrDefault(candidate =>
                        candidate.Keys.SequenceEqual(keys, StringComparer.OrdinalIgnoreCase));
                    if (option is null)
                    {
                        ShowError("This imported keyboard chord is not in the searchable catalog and cannot be rewritten safely yet.");
                        return false;
                    }

                    CategoryBox.SelectedIndex = 0;
                    SearchBox.Text = option.Shortcut;
                    RefreshFilter();
                    AssignmentList.SelectedItem = option;
                    AssignmentList.ScrollIntoView(option);
                    return true;
                }
            case ControllerActionStepType.Text:
                ActionTabs.SelectedIndex = 1;
                TextValueBox.Text = step.Value;
                TextValueBox.Focus();
                return true;
            case ControllerActionStepType.Delay:
                ActionTabs.SelectedIndex = 1;
                DelayBox.Text = step.DurationMs.ToString();
                DelayBox.Focus();
                return true;
            case ControllerActionStepType.MouseButton:
                ActionTabs.SelectedIndex = 2;
                SelectTag(MouseActionBox, step.Value);
                return true;
            case ControllerActionStepType.MouseMove:
                ActionTabs.SelectedIndex = 2;
                SelectTag(MouseActionBox, "Move");
                MouseXBox.Text = step.X.ToString();
                MouseYBox.Text = step.Y.ToString();
                return true;
            case ControllerActionStepType.MouseWheel:
                ActionTabs.SelectedIndex = 2;
                if (step.Value.Equals("horizontal", StringComparison.OrdinalIgnoreCase))
                {
                    SelectTag(MouseActionBox, "Horizontal wheel");
                    MouseXBox.Text = step.Amount.ToString();
                }
                else
                {
                    SelectTag(MouseActionBox, "Vertical wheel");
                    MouseYBox.Text = step.Amount.ToString();
                }

                return true;
            case ControllerActionStepType.LaunchProgram:
                ActionTabs.SelectedIndex = 3;
                ProgramPathBox.Text = step.Value;
                ProgramArgumentsBox.Text = step.Arguments;
                ProgramWorkingDirectoryBox.Text = step.WorkingDirectory;
                ProgramPathBox.Focus();
                return true;
            case ControllerActionStepType.PowerShellCommand:
                ActionTabs.SelectedIndex = 4;
                SelectTag(PowerShellHostBox, step.Target);
                PowerShellCommandBox.Text = step.Value;
                PowerShellWorkingDirectoryBox.Text = step.WorkingDirectory;
                PowerShellCommandBox.Focus();
                return true;
            case ControllerActionStepType.Midi:
                {
                    ActionTabs.SelectedIndex = 5;
                    MidiShortMessage message;
                    try
                    {
                        message = MidiMessageParser.Parse(step.Value);
                    }
                    catch (ArgumentException exception)
                    {
                        ShowError($"The saved MIDI step could not be opened safely: {exception.Message}");
                        return false;
                    }

                    SelectTag(MidiKindBox, message.Kind switch
                    {
                        MidiShortMessageKind.NoteOn => "note",
                        MidiShortMessageKind.NoteOff => "noteoff",
                        MidiShortMessageKind.ControlChange => "cc",
                        _ => "pc",
                    });
                    MidiChannelBox.Text = message.Channel.ToString();
                    MidiData1Box.Text = message.Data1.ToString();
                    MidiData2Box.Text = message.Data2.ToString();
                    SelectMidiDevice(step.Target);
                    return true;
                }
            case ControllerActionStepType.Osc:
                ActionTabs.SelectedIndex = 6;
                OscHostBox.Text = step.Target;
                OscPortBox.Text = step.Amount.ToString();
                OscAddressBox.Text = step.Value;
                OscValuesBox.Text = step.Arguments;
                OscValuesBox.Focus();
                return true;
            default:
                ShowError("This low-level imported step can be moved or removed, but this editor cannot safely rewrite it yet.");
                return false;
        }
    }

    private static void SelectTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectMidiDevice(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            MidiDeviceBox.SelectedIndex = 0;
            return;
        }

        MidiDeviceBox.SelectedItem = MidiDeviceBox.Items
            .OfType<WinMmMidiOutput.Device>()
            .FirstOrDefault(device => device.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    private void EndStepEdit()
    {
        _editingStepIndex = -1;
        ApplyStepChangesButton.IsEnabled = false;
    }

    private void MoveStepUp_OnClick(object sender, RoutedEventArgs e)
    {
        var index = SequenceList.SelectedIndex;
        if (index <= 0)
        {
            return;
        }

        (_steps[index - 1], _steps[index]) = (_steps[index], _steps[index - 1]);
        EndStepEdit();
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
        EndStepEdit();
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
        EndStepEdit();
        RefreshStepList(Math.Min(index, _steps.Count - 1));
    }

    private void ClearSteps_OnClick(object sender, RoutedEventArgs e)
    {
        _steps.Clear();
        EndStepEdit();
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
        EditStepButton.IsEnabled = index >= 0;
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
        if (_preservedReleaseSequence.IsEmpty &&
            _steps.Count == 1 && _steps[0].Type == ControllerActionStepType.KeyboardChord &&
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
            ActionResult = null;
            AcceptAssignment();
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
            : new ControllerActionAssignment(name, sequence, _preservedReleaseSequence.Clone());
        Result = null;
        AcceptAssignment();
    }

    private void AcceptAssignment()
    {
        if (SaveAssignment?.Invoke(Result, ActionResult) is { Succeeded: false } failure)
        {
            ShowError(failure.Message + " Your edits are still open in this window.");
            return;
        }

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
