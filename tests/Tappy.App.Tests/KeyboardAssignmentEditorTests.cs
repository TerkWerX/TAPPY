using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tappy.App.Runtime;
using Tappy.Core.Models;

namespace Tappy.App.Tests;

public sealed partial class KeyboardAssignmentEditorTests
{

    [Fact]
    public async Task Add_buttons_append_without_replacing_the_step_being_edited()
    {
        await OnEditorThread(() =>
        {
            var cases = new[]
            {
                ("AddKeyboardStep_OnClick", ControllerActionStepType.KeyboardChord),
                ("AddTextStep_OnClick", ControllerActionStepType.Text),
                ("AddDelayStep_OnClick", ControllerActionStepType.Delay),
                ("AddMouseStep_OnClick", ControllerActionStepType.MouseButton),
                ("AddProgramStep_OnClick", ControllerActionStepType.LaunchProgram),
                ("AddPowerShellStep_OnClick", ControllerActionStepType.PowerShellCommand),
                ("AddMidiStep_OnClick", ControllerActionStepType.Midi),
                ("AddOscStep_OnClick", ControllerActionStepType.Osc),
            };

            foreach (var (handler, type) in cases)
            {
                var assignment = TextAssignment("Original long macro");
                var editor = CreateEditor(assignment);
                try
                {
                    Click(editor, "EditStep_OnClick");
                    Field<TextBox>(editor, "TextValueBox").Text = "A new text step";
                    Field<TextBox>(editor, "DelayBox").Text = "150";
                    Field<TextBox>(editor, "ProgramPathBox").Text = "notepad.exe";
                    Field<TextBox>(editor, "PowerShellCommandBox").Text = "Write-Output example";
                    Field<ListBox>(editor, "AssignmentList").SelectedIndex = 0;
                    Click(editor, handler);

                    var steps = DraftSteps(editor);
                    Assert.Equal(2, steps.Count);
                    Assert.Equal(ControllerActionStepType.Text, steps[0].Type);
                    Assert.Equal("Original long macro", steps[0].Value);
                    Assert.Equal(type, steps[1].Type);
                    Assert.False(Field<Button>(editor, "ApplyStepChangesButton").IsEnabled);
                    Assert.Equal("Original long macro", assignment.PressSequence.Steps[0].Value);
                }
                finally
                {
                    editor.Close();
                }
            }
        });
    }

    [Fact]
    public async Task Apply_changes_updates_only_the_edited_step_and_preserves_the_source_assignment()
    {
        await OnEditorThread(() =>
        {
            var assignment = TextAssignment("Original text");
            assignment.PressSequence.Steps.Add(new ControllerActionStepDefinition
            {
                Type = ControllerActionStepType.Delay,
                DurationMs = 1234,
            });
            var editor = CreateEditor(assignment);
            try
            {
                Click(editor, "EditStep_OnClick");
                Field<TextBox>(editor, "TextValueBox").Text = "Edited text, with a comma";
                Click(editor, "ApplyStepChanges_OnClick");

                var steps = DraftSteps(editor);
                Assert.Equal(2, steps.Count);
                Assert.Equal("Edited text, with a comma", steps[0].Value);
                Assert.Equal(1234, steps[1].DurationMs);
                Assert.Equal("Original text", assignment.PressSequence.Steps[0].Value);
                Assert.False(Field<Button>(editor, "ApplyStepChangesButton").IsEnabled);
            }
            finally
            {
                editor.Close();
            }
        });
    }

    [Fact]
    public async Task Invalid_step_edits_and_failed_saves_keep_the_existing_draft_open()
    {
        await OnEditorThread(() =>
        {
            var saved = 0;
            var editor = new KeyboardAssignmentWindow("G1", TextAssignment("Do not lose me"), () => [])
            {
                SaveAssignment = (_, _) =>
                {
                    saved++;
                    return RuntimeOperation.Failed("The controller was disconnected.");
                },
            };
            try
            {
                Click(editor, "EditStep_OnClick");
                Field<TextBox>(editor, "TextValueBox").Text = string.Empty;
                Click(editor, "ApplyStepChanges_OnClick");
                Assert.Equal("Do not lose me", Assert.Single(DraftSteps(editor)).Value);
                Assert.True(Field<Button>(editor, "ApplyStepChangesButton").IsEnabled);

                Click(editor, "AssignButton_OnClick");
                Assert.Equal(1, saved);
                Assert.Null(editor.DialogResult);
                Assert.Equal("Do not lose me", Assert.Single(DraftSteps(editor)).Value);
                Assert.Contains("edits are still open", Field<TextBlock>(editor, "ValidationText").Text);
            }
            finally
            {
                editor.Close();
            }
        });
    }

    [Fact]
    public async Task Editing_a_saved_midi_step_preserves_an_unavailable_output_device()
    {
        await OnEditorThread(() =>
        {
            var assignment = new ControllerActionAssignment("MIDI", ControllerActionSequenceDefinition.Once("MIDI",
                new ControllerActionStepDefinition
                {
                    Type = ControllerActionStepType.Midi,
                    Target = "Studio synth",
                    Value = "cc:3:7:100",
                }), new ControllerActionSequenceDefinition());
            var editor = CreateEditor(assignment);
            try
            {
                Click(editor, "EditStep_OnClick");
                Field<TextBox>(editor, "MidiData2Box").Text = "90";
                Click(editor, "ApplyStepChanges_OnClick");
                var step = Assert.Single(DraftSteps(editor));
                Assert.Equal("Studio synth", step.Target);
                Assert.Equal("cc:3:7:90", step.Value);
            }
            finally
            {
                editor.Close();
            }
        });
    }

    private static KeyboardAssignmentWindow CreateEditor(ControllerActionAssignment assignment) =>
        new("G1", assignment, () => []);

    private static ControllerActionAssignment TextAssignment(string value) => new("Text macro",
        ControllerActionSequenceDefinition.Once("Text macro", new ControllerActionStepDefinition
        {
            Type = ControllerActionStepType.Text,
            Value = value,
        }), new ControllerActionSequenceDefinition());

    private static T Field<T>(KeyboardAssignmentWindow editor, string name) where T : FrameworkElement =>
        Assert.IsType<T>(editor.FindName(name));

    private static IReadOnlyList<ControllerActionStepDefinition> DraftSteps(KeyboardAssignmentWindow editor) =>
        Assert.IsType<List<ControllerActionStepDefinition>>(typeof(KeyboardAssignmentWindow)
            .GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor));

    private static void Click(KeyboardAssignmentWindow editor, string handler) =>
        typeof(KeyboardAssignmentWindow).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, [editor, new RoutedEventArgs()]);

    private static Task OnEditorThread(Action action) => WpfTestHost.RunAsync(action);
}
