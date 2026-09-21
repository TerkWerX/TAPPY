using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Models;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    private static async Task<(ControllerWorkspace Workspace, ControllerTab Tab)> NewSingleConfirmedFireWorkspace(
        string root, FakeMidiBackend midiBackend)
    {
        var workspace = new ControllerWorkspace(_ =>
            new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: new WinMmMidiInputProvider(midiBackend)),
            action => action());
        await workspace.InitializeAsync();
        var tab = workspace.Tabs[0];
        var device = Assert.Single(tab.Runtime.Devices);
        Assert.True(tab.Runtime.BeginIdentification(device).Succeeded);
        midiBackend.Publish(PackMidi(0x90, 60, 127));
        midiBackend.Publish(PackMidi(0x80, 60, 0));
        Assert.True(tab.Runtime.ConfirmController().Succeeded);
        return (workspace, tab);
    }

    private static LedAnimationDefinition WithSoleParticipant(LedAnimationDefinition animation, ControllerWorkspace workspace)
    {
        var target = Assert.Single(workspace.GetAnimationTargets());
        animation.Participants.Add(new LedAnimationParticipant
        {
            ProviderId = target.ProviderId,
            PersistentId = target.PersistentId,
            DisplayName = target.DisplayName,
        });
        return animation;
    }

    [Fact]
    public async Task LedAnimationPlayer_ticks_update_preview_without_writing_the_profile()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (workspace, tab) = await NewSingleConfirmedFireWorkspace(root, midiBackend);
            await using var disposeWorkspace = workspace;
            Assert.True(tab.ViewModel.HasAnimatableLighting);

            var player = new LedAnimationPlayer();
            var animation = WithSoleParticipant(new LedAnimationDefinition
            {
                Effect = new LedEffectDefinition { Kind = LedEffectKind.Breathe, Colors = ["Red"], SpeedHz = 1 },
            }, workspace);
            Assert.True(player.Play(animation, workspace));
            Assert.True(player.IsPlaying);

            var profilePath = Path.Combine(root, "default.tappy.json");
            var writeTimeBefore = File.Exists(profilePath) ? File.GetLastWriteTimeUtc(profilePath) : (DateTime?)null;

            var padOne = tab.ViewModel.Controls.Single(c => c.ControlId == "winmm-midi:channel-1:note-54");
            var start = DateTimeOffset.UtcNow;
            player.Tick(start);
            var colorAtStart = padOne.ColorKey;
            player.Tick(start.AddMilliseconds(250));
            var colorAQuarterPeriodLater = padOne.ColorKey;

            Assert.NotEqual(colorAtStart, colorAQuarterPeriodLater);

            var writeTimeAfter = File.Exists(profilePath) ? File.GetLastWriteTimeUtc(profilePath) : (DateTime?)null;
            Assert.Equal(writeTimeBefore, writeTimeAfter);

            player.Stop();
            Assert.False(player.IsPlaying);
            Assert.Null(player.Current);
            Assert.Equal("Default", padOne.ColorKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LedAnimationPlayer_stops_cleanly_when_the_device_disconnects_mid_play()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (workspace, tab) = await NewSingleConfirmedFireWorkspace(root, midiBackend);
            await using var disposeWorkspace = workspace;

            var player = new LedAnimationPlayer();
            var animation = WithSoleParticipant(new LedAnimationDefinition
            {
                Effect = new LedEffectDefinition { Kind = LedEffectKind.RainbowWave },
            }, workspace);
            Assert.True(player.Play(animation, workspace));

            Assert.True(tab.Runtime.EmergencyStop("test").Succeeded);

            // The confirmed device is gone; Tick must stop itself rather than
            // throw or keep sending to a controller that is no longer confirmed.
            var exception = Record.Exception(() => player.Tick(DateTimeOffset.UtcNow));
            Assert.Null(exception);
            Assert.False(player.IsPlaying);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LedAnimationPlayer_play_and_tick_are_inert_before_anything_has_played()
    {
        var player = new LedAnimationPlayer();
        Assert.False(player.IsPlaying);
        Assert.Null(player.Current);
        var exception = Record.Exception(() => player.Tick(DateTimeOffset.UtcNow));
        Assert.Null(exception);
        var stopException = Record.Exception(player.Stop);
        Assert.Null(stopException);
    }

    [Fact]
    public async Task LedAnimationPlayer_frames_kind_shows_the_active_frames_painted_colors()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (workspace, tab) = await NewSingleConfirmedFireWorkspace(root, midiBackend);
            await using var disposeWorkspace = workspace;

            var player = new LedAnimationPlayer();
            var animation = WithSoleParticipant(new LedAnimationDefinition
            {
                Kind = LedAnimationKind.Frames,
                Frames =
                [
                    new LedAnimationFrame { DurationMs = 100, Cells = new() { ["0,0"] = "Red" } },
                    new LedAnimationFrame { DurationMs = 100, Cells = new() { ["0,0"] = "Blue" } },
                ],
            }, workspace);
            Assert.True(player.Play(animation, workspace));

            var padOne = tab.ViewModel.Controls.Single(c => c.ControlId == "winmm-midi:channel-1:note-54");
            var start = DateTimeOffset.UtcNow;
            player.Tick(start);
            Assert.Equal("Red", padOne.ColorKey);
            player.Tick(start.AddMilliseconds(150));
            Assert.Equal("Blue", padOne.ColorKey);

            // A cell never painted in either frame falls back to Default rather
            // than carrying over some other cell's color.
            var padTwo = tab.ViewModel.Controls.Single(c => c.ControlId == "winmm-midi:channel-1:note-55");
            Assert.Equal("Default", padTwo.ColorKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LedAnimationPlayer_refuses_to_play_a_frames_kind_animation_with_no_frames()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (workspace, _) = await NewSingleConfirmedFireWorkspace(root, midiBackend);
            await using var disposeWorkspace = workspace;

            var player = new LedAnimationPlayer();
            var animation = WithSoleParticipant(
                new LedAnimationDefinition { Kind = LedAnimationKind.Frames }, workspace);

            Assert.False(player.Play(animation, workspace));
            Assert.False(player.IsPlaying);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
