using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    // Without this, real hardware attached to the machine running the test
    // (or a coincidental real-container match by vid/pid) can hand both fake
    // devices the same container-derived PersistentId, since the fallback
    // container-id resolver isn't scoped to the fake backend at all.
    private sealed class FakeContainerIdResolver(Func<string, Guid?> resolve) : IDeviceContainerIdResolver
    {
        public Guid? Resolve(string deviceInterfacePath) => resolve(deviceInterfacePath);
    }

    private static async Task<(ControllerWorkspace Workspace, ControllerTab TabA, ControllerTab TabB)> NewTwoConfirmedFiresWorkspace(
        string root, FakeMidiBackend backendA, FakeMidiBackend backendB)
    {
        var backends = new[] { backendA, backendB };
        var workspace = new ControllerWorkspace(slot =>
            new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(Path.Combine(root, $"slot-{slot}")),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: new WinMmMidiInputProvider(backends[slot - 1], monitorInterval: Timeout.InfiniteTimeSpan,
                    containerIdResolver: new FakeContainerIdResolver(_ => null), deviceInterfacePathResolver: _ => null)),
            action => action());
        await workspace.InitializeAsync();
        var tabA = workspace.Tabs[0];
        ConfirmFire(tabA, backendA);
        await workspace.AddAsync();
        var tabB = workspace.Tabs[1];
        ConfirmFire(tabB, backendB);
        return (workspace, tabA, tabB);
    }

    private static void ConfirmFire(ControllerTab tab, FakeMidiBackend backend)
    {
        var device = Assert.Single(tab.Runtime.Devices);
        Assert.True(tab.Runtime.BeginIdentification(device).Succeeded);
        backend.Publish(PackMidi(0x90, 60, 127));
        backend.Publish(PackMidi(0x80, 60, 0));
        Assert.True(tab.Runtime.ConfirmController().Succeeded);
    }

    private static void AddAllAsParticipants(LedAnimationDefinition animation, IReadOnlyList<LedAnimationTarget> targets)
    {
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
    }

    [Fact]
    public async Task GetAnimationTargets_returns_only_confirmed_devices_with_a_registered_grid_adapter()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var backendA = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            using var backendB = new FakeMidiBackend(new WinMmMidiInputDevice(0, "2 - FL STUDIO FIRE", 1, 2, 3));
            var (workspace, tabA, tabB) = await NewTwoConfirmedFiresWorkspace(root, backendA, backendB);
            await using var disposeWorkspace = workspace;

            var targets = workspace.GetAnimationTargets();

            Assert.Equal(2, targets.Count);
            Assert.All(targets, target => Assert.Equal(MidiControllerModelCatalog.AkaiFireV1Id, target.Adapter.ModelId));
            Assert.Contains(targets, target => ReferenceEquals(target.Tab, tabA));
            Assert.Contains(targets, target => ReferenceEquals(target.Tab, tabB));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetAnimationTargets_is_empty_when_nothing_is_confirmed()
    {
        var root = NewTemporaryDirectory();
        try
        {
            await using var workspace = new ControllerWorkspace(_ =>
                new DeviceAwareControllerRuntime(
                    new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                    logitechG13Provider: null,
                    new RecordingOutput(),
                    new AtomicProfileStore(root),
                    actionOutput: new RecordingActionOutput()),
                action => action());
            await workspace.InitializeAsync();

            Assert.Empty(workspace.GetAnimationTargets());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rearrange_lays_out_participants_left_to_right_and_sizes_the_shared_canvas()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var backendA = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            using var backendB = new FakeMidiBackend(new WinMmMidiInputDevice(0, "2 - FL STUDIO FIRE", 1, 2, 3));
            var (workspace, _, _) = await NewTwoConfirmedFiresWorkspace(root, backendA, backendB);
            await using var disposeWorkspace = workspace;
            var targets = workspace.GetAnimationTargets();

            var animation = new LedAnimationDefinition();
            AddAllAsParticipants(animation, targets);

            Assert.Equal(0, animation.Participants[0].CanvasOffsetColumn);
            Assert.Equal(16, animation.Participants[1].CanvasOffsetColumn);
            Assert.Equal(0, animation.Participants[0].CanvasOffsetRow);
            Assert.Equal(0, animation.Participants[1].CanvasOffsetRow);
            Assert.Equal(32, animation.CanvasColumns);
            Assert.Equal(4, animation.CanvasRows);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rearrange_reflows_remaining_participants_after_one_is_removed()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var backendA = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            using var backendB = new FakeMidiBackend(new WinMmMidiInputDevice(0, "2 - FL STUDIO FIRE", 1, 2, 3));
            var (workspace, _, _) = await NewTwoConfirmedFiresWorkspace(root, backendA, backendB);
            await using var disposeWorkspace = workspace;
            var targets = workspace.GetAnimationTargets();
            var animation = new LedAnimationDefinition();
            AddAllAsParticipants(animation, targets);

            animation.Participants.RemoveAt(0);
            LedAnimationParticipantLayout.Rearrange(animation, targets);

            Assert.Single(animation.Participants);
            Assert.Equal(0, animation.Participants[0].CanvasOffsetColumn);
            Assert.Equal(16, animation.CanvasColumns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rearrange_drops_a_participant_that_no_longer_resolves()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var backendA = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            using var backendB = new FakeMidiBackend(new WinMmMidiInputDevice(0, "2 - FL STUDIO FIRE", 1, 2, 3));
            var (workspace, _, _) = await NewTwoConfirmedFiresWorkspace(root, backendA, backendB);
            await using var disposeWorkspace = workspace;
            var targets = workspace.GetAnimationTargets();

            var animation = new LedAnimationDefinition();
            animation.Participants.Add(new LedAnimationParticipant
            {
                ProviderId = "winmm-midi",
                PersistentId = "stale-unit-that-is-gone",
                DisplayName = "Ghost Fire",
            });
            LedAnimationParticipantLayout.Rearrange(animation, targets);

            Assert.Empty(animation.Participants);
            Assert.Equal(1, animation.CanvasColumns);
            Assert.Equal(1, animation.CanvasRows);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Shared_canvas_participants_each_render_their_own_slice_not_a_mirrored_copy()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var backendA = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            using var backendB = new FakeMidiBackend(new WinMmMidiInputDevice(0, "2 - FL STUDIO FIRE", 1, 2, 3));
            var (workspace, tabA, tabB) = await NewTwoConfirmedFiresWorkspace(root, backendA, backendB);
            await using var disposeWorkspace = workspace;
            var targets = workspace.GetAnimationTargets();

            var animation = new LedAnimationDefinition
            {
                Effect = new LedEffectDefinition { Kind = LedEffectKind.RainbowWave, WaveDensity = 1.0 },
            };
            AddAllAsParticipants(animation, targets);
            Assert.Equal(32, animation.CanvasColumns);

            var player = new LedAnimationPlayer();
            Assert.True(player.Play(animation, workspace));
            player.Tick(DateTimeOffset.UtcNow);

            // Both tabs' own top-left pad is control id note-54 (row 0,
            // column 0 on that unit's own grid), but TabB's local column 0
            // is shared-canvas column 16 (its CanvasOffsetColumn), a
            // different position on the combined 32-wide wave than TabA's
            // shared-canvas column 0 — proving TabB rendered its own slice
            // of the shared canvas, not an identical mirrored copy of TabA's.
            var tabAColumnZero = tabA.ViewModel.Controls.Single(c => c.ControlId == "winmm-midi:channel-1:note-54").ColorKey;
            var tabBColumnZero = tabB.ViewModel.Controls.Single(c => c.ControlId == "winmm-midi:channel-1:note-54").ColorKey;
            Assert.NotEqual(tabAColumnZero, tabBColumnZero);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_disconnected_participant_is_dropped_from_the_tick_but_the_other_keeps_animating()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var backendA = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            using var backendB = new FakeMidiBackend(new WinMmMidiInputDevice(0, "2 - FL STUDIO FIRE", 1, 2, 3));
            var (workspace, tabA, tabB) = await NewTwoConfirmedFiresWorkspace(root, backendA, backendB);
            await using var disposeWorkspace = workspace;
            var targets = workspace.GetAnimationTargets();

            var animation = new LedAnimationDefinition
            {
                Effect = new LedEffectDefinition { Kind = LedEffectKind.Breathe, Colors = ["Red"] },
            };
            AddAllAsParticipants(animation, targets);

            var player = new LedAnimationPlayer();
            Assert.True(player.Play(animation, workspace));
            player.Tick(DateTimeOffset.UtcNow);

            Assert.True(tabA.Runtime.EmergencyStop("test").Succeeded);

            var exception = Record.Exception(() => player.Tick(DateTimeOffset.UtcNow));
            Assert.Null(exception);
            // TabB is still resolvable, so playback keeps going rather than
            // stopping entirely just because one participant dropped out.
            Assert.True(player.IsPlaying);

            var tabBPad = tabB.ViewModel.Controls.Single(c => c.ControlId == "winmm-midi:channel-1:note-54");
            Assert.NotEqual("Default", tabBPad.ColorKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
