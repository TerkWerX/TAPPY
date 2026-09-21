using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.Core.Models;

namespace Tappy.App.ViewModels;

/// <summary>What a confirmed Fire's OLED currently shows for this tab. Off sends nothing.</summary>
public enum FireOledDisplayMode
{
    Off,
    LiveStatus,
    TappyLogo,
    ScrollingWordmark,
}

/// <summary>
/// Keeps a confirmed Fire's OLED showing its selected content, re-sent
/// periodically. Owned by ControllerTab; ticked by a DispatcherTimer owned
/// by MainWindow, following the same ownership convention as
/// LedAnimationPlayer and the reconnect-poll/zoom-persist timers (timers
/// live in code-behind, never in a ViewModel or runtime). Exists because the
/// Fire's OLED reverts to black on its own a few seconds after its last
/// update (see docs/AKAI_FIRE.md, D-028) — a single write is only ever
/// temporary, so staying visible means re-sending, not a one-shot render.
/// Deliberately stateless about "which mode am I showing": every tick
/// re-reads ControllerTab.ViewModel.OledDisplayMode directly, so the mode
/// selector bound to that property can never drift out of sync with what is
/// actually being sent. The one thing this player does remember across
/// ticks — on purpose — is whether it has already played the one-time
/// welcome sequence (Tappy logo, then the scrolling wordmark) for this tab
/// this session; see Tick.
/// </summary>
public sealed class FireOledContentPlayer
{
    private const int Columns = DeviceAwareControllerRuntime.FireOledWidth;
    private const int Rows = DeviceAwareControllerRuntime.FireOledHeight;
    private const string ScrollText = "TerkWerX";
    // 9 is the largest integer scale whose 7-row glyph height still fits the
    // OLED's 64px height (9*7=63px) — "fat letters that fill the screen from
    // top to bottom," per the owner's request.
    private const int ScrollScale = 9;
    // A hollow stroke, not just a 1px wire outline — there is enough
    // resolution at ScrollScale for the stroke itself to read as fat.
    private const int ScrollOutlineThickness = 3;
    private const int ScrollGapPixels = Columns;
    // "TerkWerX" at ScrollScale plus the gap is ~551px; at 90px/s a full
    // cycle takes just over 6s, fitting inside the welcome sequence's own
    // scroll phase (WelcomeScrollSeconds) rather than only crawling partway
    // through the word before that phase ends.
    private const double ScrollPixelsPerSecond = 90;
    private const double WelcomeLogoSeconds = 3.0;
    private const double WelcomeScrollSeconds = 6.0;

    private static readonly bool[,] ScrollContent = FireOledTextRenderer.Outline(
        FireOledTextRenderer.RenderScaledLine(ScrollText, ScrollScale), ScrollOutlineThickness);

    // The scroll-relative time at which the whole word has fully exited to
    // the left and the screen is genuinely blank (offsetX == content width,
    // the one point in the cycle where none of the next repeat has started
    // entering from the right yet). The welcome sequence clamps to this so
    // it settles on a clean blank screen after one pass instead of landing
    // mid-way into a second, partial repeat depending on exactly which
    // 150ms tick the welcome's scroll phase happens to end on.
    private static readonly double ScrollBlankAtSeconds = ScrollContent.GetLength(0) / ScrollPixelsPerSecond;

    private volatile bool _sendInFlight;
    private FireOledDisplayMode _lastMode = FireOledDisplayMode.Off;
    private DateTimeOffset _scrollStartedAt;
    private bool _wasConfirmedFire;
    private bool _hasPlayedWelcomeThisSession;
    private DateTimeOffset? _welcomeStartedAt;

    /// <summary>
    /// Called once per timer tick by MainWindow's OLED content timer, for
    /// every tab. Returns the frame that was computed and handed off for
    /// sending (or null if nothing was sent this tick) purely so tests can
    /// observe which phase/content was chosen without needing real hardware
    /// to intercept the actual SysEx send.
    /// </summary>
    public bool[,]? Tick(ControllerTab tab, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var isConfirmedFire =
            tab.Runtime.GetControllerLedColorCapability()?.ModelId == MidiControllerModelCatalog.AkaiFireV1Id;

        // Fires at the moment this tab's Fire first becomes confirmed this
        // session — whether that's a fresh manual identification or a
        // silent startup reconnect (D-026), both land here the same way,
        // matching "when the program is opened or a Fire is plugged in and
        // recognized." Deliberately once per tab per running session, not
        // once per reconnect: _hasPlayedWelcomeThisSession never resets.
        if (isConfirmedFire && !_wasConfirmedFire && !_hasPlayedWelcomeThisSession)
        {
            _hasPlayedWelcomeThisSession = true;
            _welcomeStartedAt = now;
        }

        _wasConfirmedFire = isConfirmedFire;
        if (!isConfirmedFire)
        {
            _lastMode = FireOledDisplayMode.Off;
            // Clear any in-progress welcome sequence rather than leaving its
            // clock running: _hasPlayedWelcomeThisSession already guarantees
            // it never restarts, but without this a disconnect partway
            // through the welcome followed by a quick reconnect would
            // otherwise resume the stale clock and show the logo/scroll
            // again instead of treating the welcome as already used.
            _welcomeStartedAt = null;
            return null;
        }

        bool[,]? pixels;
        if (_welcomeStartedAt is { } startedAt)
        {
            pixels = BuildWelcomeFrame(startedAt, now, tab, out var stillPlaying);
            if (!stillPlaying) { _welcomeStartedAt = null; }
        }
        else
        {
            pixels = BuildModeFrame(tab, now);
        }

        if (pixels is null)
        {
            return null; // Mode is Off (or the welcome sequence just ended into an Off mode): nothing to send.
        }

        // The actual hardware push blocks the calling thread until Windows
        // confirms delivery (WinMmMidiOutput.SendSysEx), so it must not run
        // on the UI thread. Only one send is ever in flight per player: a
        // slow physical send simply causes intermediate ticks to be
        // skipped, never queued up and sent late — same reasoning as
        // LedAnimationPlayer.
        if (!_sendInFlight)
        {
            _sendInFlight = true;
            var runtime = tab.Runtime;
            _ = Task.Run(() =>
            {
                try
                {
                    runtime.SendFireOledFrame(pixels);
                }
                catch
                {
                    // A single failed frame must not crash the refresh loop
                    // or leave an unobserved task exception; the next tick
                    // tries again.
                }
                finally
                {
                    _sendInFlight = false;
                }
            });
        }

        return pixels;
    }

    private bool[,]? BuildWelcomeFrame(DateTimeOffset startedAt, DateTimeOffset now, ControllerTab tab, out bool stillPlaying)
    {
        var elapsed = (now - startedAt).TotalSeconds;
        if (elapsed < WelcomeLogoSeconds)
        {
            stillPlaying = true;
            return FireOledLogo.GetPixels();
        }

        if (elapsed < WelcomeLogoSeconds + WelcomeScrollSeconds)
        {
            stillPlaying = true;
            var scrollElapsed = Math.Min(elapsed - WelcomeLogoSeconds, ScrollBlankAtSeconds);
            return BuildScrollFrame(scrollElapsed);
        }

        stillPlaying = false;
        return BuildModeFrame(tab, now);
    }

    private bool[,]? BuildModeFrame(ControllerTab tab, DateTimeOffset now)
    {
        var mode = tab.ViewModel.OledDisplayMode;
        if (mode == FireOledDisplayMode.ScrollingWordmark && _lastMode != FireOledDisplayMode.ScrollingWordmark)
        {
            _scrollStartedAt = now;
        }

        _lastMode = mode;
        return mode switch
        {
            FireOledDisplayMode.LiveStatus => FireOledTextRenderer.Render(BuildStatusLines(tab), Columns, Rows),
            FireOledDisplayMode.TappyLogo => FireOledLogo.GetPixels(),
            FireOledDisplayMode.ScrollingWordmark => BuildScrollFrame((now - _scrollStartedAt).TotalSeconds),
            _ => null,
        };
    }

    private static bool[,] BuildScrollFrame(double elapsedSeconds)
    {
        var contentHeight = ScrollContent.GetLength(1);
        var offsetX = (int)(elapsedSeconds * ScrollPixelsPerSecond);
        var window = FireOledScroll.ExtractWindow(ScrollContent, Columns, contentHeight, offsetX, ScrollGapPixels);

        var frame = new bool[Columns, Rows];
        var verticalOffset = Math.Max(0, (Rows - contentHeight) / 2);
        for (var x = 0; x < Columns; x++)
        {
            for (var y = 0; y < contentHeight && verticalOffset + y < Rows; y++)
            {
                frame[x, verticalOffset + y] = window[x, y];
            }
        }

        return frame;
    }

    /// <summary>
    /// Extracted for direct testing without needing real hardware: line 1 is
    /// the confirmed device's display name with the "MIDI: " presentation
    /// prefix stripped (StripMidiDisplayPrefix already exists for matching
    /// output ports by their real WinMM name; reused here purely to shorten
    /// what fits on a 128px-wide line), line 2 is the active layer name.
    /// </summary>
    internal static string[] BuildStatusLines(ControllerTab tab) =>
    [
        DeviceAwareControllerRuntime.StripMidiDisplayPrefix(tab.ViewModel.ActiveControllerLabel),
        tab.ViewModel.ActiveLayerName,
    ];
}
