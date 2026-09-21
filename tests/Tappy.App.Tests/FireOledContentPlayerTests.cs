using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Core.Models;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    private static (ControllerTab Tab, DeviceAwareControllerRuntime Runtime) NewConfirmedFireTab(
        string root, FakeMidiBackend midiBackend)
    {
        var midiProvider = new WinMmMidiInputProvider(midiBackend);
        var runtime = new DeviceAwareControllerRuntime(
            new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
            logitechG13Provider: null,
            new RecordingOutput(),
            new AtomicProfileStore(root),
            actionOutput: new RecordingActionOutput(),
            midiInputProvider: midiProvider);
        var viewModel = new MainViewModel(runtime, action => action());
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        var device = Assert.Single(runtime.Devices);
        Assert.True(runtime.BeginIdentification(device).Succeeded);
        midiBackend.Publish(PackMidi(0x90, 60, 127));
        midiBackend.Publish(PackMidi(0x80, 60, 0));
        Assert.True(runtime.ConfirmController().Succeeded);
        return (new ControllerTab(1, runtime, viewModel), runtime);
    }

    [Fact]
    public async Task FireOledContentPlayer_status_lines_show_the_stripped_device_name_and_active_layer()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;
            Assert.True(tab.ViewModel.HasOledDisplay);

            var lines = FireOledContentPlayer.BuildStatusLines(tab);

            Assert.Equal(["FL STUDIO FIRE", "Layer 1"], lines);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FireOledContentPlayer_tick_is_inert_when_the_tab_has_not_opted_in_and_the_welcome_already_played()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;
            Assert.Equal(FireOledDisplayMode.Off, tab.ViewModel.OledDisplayMode);

            var player = new FireOledContentPlayer();
            var start = DateTimeOffset.UtcNow;
            player.Tick(tab, start); // Consumes the one-time welcome sequence.

            var result = player.Tick(tab, start.AddSeconds(20)); // Well past both welcome phases.

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FireOledContentPlayer_tick_does_not_throw_when_the_device_is_confirmed_but_not_a_fire()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "APC MINI", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;
            Assert.False(tab.ViewModel.HasOledDisplay);

            tab.ViewModel.OledDisplayMode = FireOledDisplayMode.LiveStatus;
            var player = new FireOledContentPlayer();

            var result = Record.Exception(() => player.Tick(tab, DateTimeOffset.UtcNow));

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(FireOledDisplayMode.LiveStatus)]
    [InlineData(FireOledDisplayMode.TappyLogo)]
    [InlineData(FireOledDisplayMode.ScrollingWordmark)]
    public async Task FireOledContentPlayer_tick_does_not_throw_for_an_opted_in_confirmed_fire_with_no_real_output_wired(
        FireOledDisplayMode mode)
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;

            tab.ViewModel.OledDisplayMode = mode;
            var player = new FireOledContentPlayer();
            var start = DateTimeOffset.UtcNow;

            // Tick past the welcome sequence first so these ticks exercise
            // the selected mode, then tick it twice more: once to enter the
            // mode, once to exercise the "already in this mode" continuation.
            var exception = Record.Exception(() =>
            {
                player.Tick(tab, start.AddSeconds(20));
                player.Tick(tab, start.AddSeconds(20.15));
                player.Tick(tab, start.AddSeconds(20.30));
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FireOledContentPlayer_welcome_sequence_shows_the_tappy_logo_first()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;

            var player = new FireOledContentPlayer();
            var start = DateTimeOffset.UtcNow;

            var firstTick = player.Tick(tab, start);
            var stillLogo = player.Tick(tab, start.AddSeconds(1));

            Assert.Equal(FireOledLogo.GetPixels(), firstTick);
            Assert.Equal(FireOledLogo.GetPixels(), stillLogo);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FireOledContentPlayer_welcome_sequence_moves_on_to_the_scrolling_wordmark_after_the_logo_phase()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;

            var player = new FireOledContentPlayer();
            var start = DateTimeOffset.UtcNow;
            player.Tick(tab, start);

            // Past the 3s logo phase, still inside the 6s scroll phase.
            var duringScroll = player.Tick(tab, start.AddSeconds(4));

            Assert.NotNull(duringScroll);
            Assert.NotEqual(FireOledLogo.GetPixels(), duringScroll);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FireOledContentPlayer_welcome_sequence_falls_through_to_the_selected_mode_once_finished()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;
            tab.ViewModel.OledDisplayMode = FireOledDisplayMode.TappyLogo;

            var player = new FireOledContentPlayer();
            var start = DateTimeOffset.UtcNow;
            player.Tick(tab, start);

            // Past both welcome phases (3s logo + 6s scroll = 9s).
            var afterWelcome = player.Tick(tab, start.AddSeconds(10));

            Assert.Equal(FireOledLogo.GetPixels(), afterWelcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FireOledContentPlayer_welcome_sequence_never_replays_after_a_reconnect_in_the_same_session()
    {
        var root = NewTemporaryDirectory();
        try
        {
            using var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var (tab, runtime) = NewConfirmedFireTab(root, midiBackend);
            await using var disposeRuntime = runtime;

            var player = new FireOledContentPlayer();
            var start = DateTimeOffset.UtcNow;
            player.Tick(tab, start); // Plays and consumes the one-time welcome.

            Assert.True(runtime.EmergencyStop("test disconnect").Succeeded);
            var whileDisconnected = player.Tick(tab, start.AddSeconds(1));
            Assert.Null(whileDisconnected);

            // Re-identify and reconfirm the same physical device.
            Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
            midiBackend.Publish(PackMidi(0x90, 60, 127));
            midiBackend.Publish(PackMidi(0x80, 60, 0));
            Assert.True(runtime.ConfirmController().Succeeded);
            tab.ViewModel.OledDisplayMode = FireOledDisplayMode.Off;

            var afterReconnect = player.Tick(tab, start.AddSeconds(2));

            // Off, and not the logo again — the welcome already played once.
            Assert.Null(afterReconnect);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
