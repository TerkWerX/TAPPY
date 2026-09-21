using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Models;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    private static async Task<ControllerWorkspace> NewEmptyWorkspace(string root)
    {
        var workspace = new ControllerWorkspace(_ =>
            new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput()),
            action => action());
        await workspace.InitializeAsync();
        return workspace;
    }

    private static LedAnimationEditorViewModel NewFrameEditor(ControllerWorkspace workspace, LedAnimationDefinition animation) =>
        new(new LedAnimationLibrary { Animations = [animation] }, workspace);

    [Fact]
    public async Task Switching_to_frames_mode_auto_adds_one_blank_frame()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition { Kind = LedAnimationKind.Effect };
            var editor = NewFrameEditor(workspace, animation);

            Assert.Empty(animation.Frames);
            editor.IsFramesMode = true;

            var frame = Assert.Single(animation.Frames);
            Assert.Equal(100, frame.DurationMs);
            Assert.Single(editor.FrameChoices);
            Assert.NotNull(editor.SelectedFrameChoice);
            Assert.Same(frame, editor.SelectedFrameChoice!.Frame);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_frames_kind_animation_loaded_with_zero_frames_self_heals_to_one_blank_frame()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            // Simulates a Frames-kind animation that somehow reached disk with no
            // frames (e.g. an older/partial write) — RefreshFrameChoices must
            // apply the same "never zero frames" invariant on load, not only on
            // the live IsFramesMode switch, so Play never dead-ends regardless of
            // how the animation got into that state.
            var animation = new LedAnimationDefinition { Kind = LedAnimationKind.Frames };
            var editor = NewFrameEditor(workspace, animation);

            var frame = Assert.Single(animation.Frames);
            Assert.Single(editor.FrameChoices);
            Assert.Same(frame, editor.SelectedFrameChoice!.Frame);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Switching_to_frames_mode_leaves_an_already_painted_animation_untouched()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition
            {
                Kind = LedAnimationKind.Effect,
                Frames = [new LedAnimationFrame { DurationMs = 250 }],
            };
            var editor = NewFrameEditor(workspace, animation);

            editor.IsFramesMode = true;

            Assert.Single(animation.Frames);
            Assert.Equal(250, animation.Frames[0].DurationMs);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AddFrame_appends_and_selects_the_new_frame()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition { Kind = LedAnimationKind.Frames };
            var editor = NewFrameEditor(workspace, animation);
            editor.IsFramesMode = true;
            var first = animation.Frames[0];

            editor.AddFrame();

            Assert.Equal(2, animation.Frames.Count);
            Assert.Same(first, animation.Frames[0]);
            Assert.Same(animation.Frames[1], editor.SelectedFrameChoice!.Frame);
            Assert.Equal(2, editor.FrameChoices.Count);
            Assert.Equal(2, editor.FrameChoices[1].Number);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateFrame_inserts_immediately_after_the_selected_frame_and_selects_the_copy()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition
            {
                Kind = LedAnimationKind.Frames,
                Frames =
                [
                    new LedAnimationFrame { DurationMs = 50, Cells = new() { ["0,0"] = "Red" } },
                    new LedAnimationFrame { DurationMs = 75 },
                ],
            };
            var editor = NewFrameEditor(workspace, animation);
            editor.SelectedFrameChoice = editor.FrameChoices[0]; // the "Red" frame

            editor.DuplicateFrame();

            Assert.Equal(3, animation.Frames.Count);
            var copy = animation.Frames[1];
            Assert.Equal(50, copy.DurationMs);
            Assert.Equal("Red", copy.Cells["0,0"]);
            Assert.NotSame(animation.Frames[0], copy); // a real clone, not the same instance
            Assert.Same(copy, editor.SelectedFrameChoice!.Frame);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteFrame_never_leaves_zero_frames()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition { Kind = LedAnimationKind.Frames };
            var editor = NewFrameEditor(workspace, animation);
            editor.IsFramesMode = true;
            var onlyFrame = animation.Frames[0];

            editor.DeleteFrame();

            var remaining = Assert.Single(animation.Frames);
            Assert.NotSame(onlyFrame, remaining);
            Assert.Same(remaining, editor.SelectedFrameChoice!.Frame);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteFrame_removes_only_the_selected_frame_when_others_remain()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition
            {
                Kind = LedAnimationKind.Frames,
                Frames = [new LedAnimationFrame(), new LedAnimationFrame(), new LedAnimationFrame()],
            };
            var editor = NewFrameEditor(workspace, animation);
            var middle = animation.Frames[1];
            editor.SelectedFrameChoice = editor.FrameChoices[1];

            editor.DeleteFrame();

            Assert.Equal(2, animation.Frames.Count);
            Assert.DoesNotContain(middle, animation.Frames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MoveFrameUp_and_MoveFrameDown_reorder_frames_and_keep_the_moved_frame_selected()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition
            {
                Kind = LedAnimationKind.Frames,
                Frames = [new LedAnimationFrame { DurationMs = 10 }, new LedAnimationFrame { DurationMs = 20 }],
            };
            var editor = NewFrameEditor(workspace, animation);
            var second = animation.Frames[1];
            editor.SelectedFrameChoice = editor.FrameChoices[1];

            editor.MoveFrameUp();

            Assert.Same(second, animation.Frames[0]);
            Assert.Same(second, editor.SelectedFrameChoice!.Frame);
            Assert.Equal(1, editor.FrameChoices[0].Number);

            editor.MoveFrameDown();

            Assert.Same(second, animation.Frames[1]);
            Assert.Same(second, editor.SelectedFrameChoice!.Frame);

            // Already at an edge — both are no-ops, not exceptions.
            editor.SelectedFrameChoice = editor.FrameChoices[0];
            var upException = Record.Exception(editor.MoveFrameUp);
            Assert.Null(upException);
            editor.SelectedFrameChoice = editor.FrameChoices[1];
            var downException = Record.Exception(editor.MoveFrameDown);
            Assert.Null(downException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PaintCell_sets_the_chosen_color_and_erases_when_painting_default()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition { Kind = LedAnimationKind.Frames };
            var editor = NewFrameEditor(workspace, animation);
            editor.IsFramesMode = true;
            var cell = editor.CanvasCells.Single(c => c.Column == 0 && c.Row == 0);

            editor.SelectedPaintColor = editor.PaintColorChoices.Single(c => c.Key == "Green");
            editor.PaintCell(cell);

            Assert.Equal("Green", cell.ColorKey);
            Assert.Equal("Green", animation.Frames[0].Cells["0,0"]);

            editor.SelectedPaintColor = editor.PaintColorChoices.Single(c => c.Key == "Default");
            editor.PaintCell(cell);

            Assert.Equal("Default", cell.ColorKey);
            Assert.False(animation.Frames[0].Cells.ContainsKey("0,0"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SetCustomPaintColor_selects_a_hex_backed_choice()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = await NewEmptyWorkspace(root);
            var animation = new LedAnimationDefinition { Kind = LedAnimationKind.Frames };
            var editor = NewFrameEditor(workspace, animation);
            editor.IsFramesMode = true;
            var cell = editor.CanvasCells.Single(c => c.Column == 0 && c.Row == 0);

            editor.SetCustomPaintColor("#123ABC");
            editor.PaintCell(cell);

            Assert.Equal("#123ABC", cell.ColorKey);
            Assert.Equal("#123ABC", animation.Frames[0].Cells["0,0"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshCanvasCells_assigns_each_participants_own_pad_aspect_ratio_to_its_slice_of_the_canvas()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var fireBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            using var apcBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 4, 5, 6));
            var (workspace, _, _) = await NewTwoConfirmedFiresWorkspace(root, fireBackend, apcBackend);
            await using var disposeWorkspace = workspace;
            var targets = workspace.GetAnimationTargets();

            var animation = new LedAnimationDefinition { Kind = LedAnimationKind.Frames };
            foreach (var target in targets)
            {
                animation.Participants.Add(new LedAnimationParticipant
                {
                    ProviderId = target.ProviderId,
                    PersistentId = target.PersistentId,
                    DisplayName = target.DisplayName,
                });
            }

            LedAnimationParticipantLayout.Rearrange(animation, targets);
            var editor = NewFrameEditor(workspace, animation);
            editor.IsFramesMode = true;

            var fireAdapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiFireV1Id)!;
            var apcAdapter = LedGridAdapterCatalog.Find(MidiControllerModelCatalog.AkaiApcMiniV1Id)!;
            var fireOffset = OffsetForModel(animation, targets, MidiControllerModelCatalog.AkaiFireV1Id);
            var apcOffset = OffsetForModel(animation, targets, MidiControllerModelCatalog.AkaiApcMiniV1Id);

            var fireCell = editor.CanvasCells.Single(cell => cell.Column == fireOffset && cell.Row == 0);
            var apcCell = editor.CanvasCells.Single(cell => cell.Column == apcOffset && cell.Row == 0);

            Assert.Equal(fireAdapter.PadAspectRatio, fireCell.AspectRatio);
            Assert.Equal(apcAdapter.PadAspectRatio, apcCell.AspectRatio);
            Assert.NotEqual(fireCell.AspectRatio, apcCell.AspectRatio);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int OffsetForModel(
        LedAnimationDefinition animation, IReadOnlyList<LedAnimationTarget> targets, string modelId) =>
        animation.Participants.Single(participant => targets.Single(target =>
            target.ProviderId == participant.ProviderId && target.PersistentId == participant.PersistentId)
            .Adapter.ModelId == modelId).CanvasOffsetColumn;
}
