using Tappy.App.Services;
using Tappy.Core.Models;

namespace Tappy.App.ViewModels;

/// <summary>
/// Drives a shared LED animation across every one of its resolved
/// participants (see LedAnimationTarget/GetAnimationTargets), one tick at a
/// time. Owned by ControllerWorkspace; ticked by a DispatcherTimer owned by
/// MainWindow, following the same ownership convention as the existing
/// reconnect-poll and zoom-persist timers (timers live in code-behind, never
/// in a ViewModel or runtime). Every participant's cells are evaluated at
/// shared-canvas coordinates (its own local column/row plus its stored
/// CanvasOffsetColumn/Row) so a wave/chase visibly continues from one
/// physical grid into the next rather than each device playing an
/// independent copy.
/// </summary>
public sealed class LedAnimationPlayer
{
    private DateTimeOffset _startedAt;
    private ControllerWorkspace? _workspace;
    private readonly Dictionary<ControllerTab, bool> _sendInFlight = [];

    public LedAnimationDefinition? Current { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>Returns false without starting anything if none of the definition's participants currently resolve to a confirmed device with a registered LED grid adapter, or if a Frames-kind definition has nothing painted yet.</summary>
    public bool Play(LedAnimationDefinition definition, ControllerWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(workspace);
        if (ResolveParticipants(definition, workspace).Count == 0)
        {
            return false;
        }

        if (definition.Kind == LedAnimationKind.Frames && definition.Frames.Count == 0)
        {
            return false;
        }

        Current = definition;
        _workspace = workspace;
        _startedAt = DateTimeOffset.UtcNow;
        IsPlaying = true;
        return true;
    }

    /// <summary>Stops playback and returns every resolvable participant's pads to Default rather than leaving a frozen frame lit.</summary>
    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        var definition = Current;
        var workspace = _workspace;
        IsPlaying = false;
        Current = null;
        _workspace = null;
        if (definition is null || workspace is null)
        {
            return;
        }

        foreach (var (target, _, _) in ResolveParticipants(definition, workspace))
        {
            var offColors = BuildAllOffColors(target.Adapter);
            target.Tab.ViewModel.ApplyAnimationPreviewColors(offColors);
            PushToHardware(target.Tab, offColors);
        }
    }

    /// <summary>Called once per timer tick by MainWindow's animation timer.</summary>
    public void Tick(DateTimeOffset now)
    {
        if (!IsPlaying || Current is not { } definition || _workspace is not { } workspace)
        {
            return;
        }

        // Re-resolved every tick, mirroring RouteBankControlAsync's
        // re-validate-immediately-before-every-write rule (see
        // docs/CROSS_CONTROLLER_BANKS.md): a participant whose confirmed
        // device changed or disconnected is dropped from that tick only,
        // never halting the others.
        var participants = ResolveParticipants(definition, workspace);
        if (participants.Count == 0)
        {
            // Every participant changed or disconnected mid-play. Fail
            // visibly by stopping rather than guessing at a different
            // device's grid or silently doing nothing.
            Stop();
            return;
        }

        var elapsedMs = (long)((now - _startedAt).TotalMilliseconds * definition.SpeedMultiplier);
        var activeFrame = definition.Kind == LedAnimationKind.Frames &&
            LedFramePlayback.GetActiveFrameIndex(definition.Frames, elapsedMs, definition.Loop) is { } frameIndex
                ? definition.Frames[frameIndex]
                : null;
        foreach (var (target, offsetColumn, offsetRow) in participants)
        {
            var capability = target.Tab.Runtime.GetControllerLedColorCapability()!;
            var colorsByControlId = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var row = 0; row < target.Adapter.Rows; row++)
            {
                for (var column = 0; column < target.Adapter.Columns; column++)
                {
                    if (target.Adapter.ControlIdAt(column, row) is not { } controlId)
                    {
                        continue;
                    }

                    string requested;
                    if (definition.Kind == LedAnimationKind.Effect)
                    {
                        var (red, green, blue) = LedEffectMath.Evaluate(definition.Effect, elapsedMs,
                            column + offsetColumn, row + offsetRow, definition.CanvasColumns, definition.CanvasRows);
                        requested = $"#{red:X2}{green:X2}{blue:X2}";
                    }
                    else
                    {
                        var cellKey = $"{column + offsetColumn},{row + offsetRow}";
                        requested = activeFrame?.Cells.GetValueOrDefault(cellKey, "Default") ?? "Default";
                    }

                    colorsByControlId[controlId.Value] = target.Adapter.ResolveColor(requested, capability.SupportsFreeformColor);
                }
            }

            // Live preview updates synchronously on the UI thread; it never
            // triggers a profile save (see MainViewModel.ApplyAnimationPreviewColors).
            target.Tab.ViewModel.ApplyAnimationPreviewColors(colorsByControlId);

            // The actual hardware push blocks the calling thread until Windows
            // confirms delivery (WinMmMidiOutput.SendSysEx), so it must not run on
            // the UI thread. Only one send is ever in flight per participant: a
            // slow physical send simply causes that one participant's intermediate
            // ticks to be skipped, never queued up and sent late — it never stalls
            // any other participant's own sends.
            if (!_sendInFlight.GetValueOrDefault(target.Tab))
            {
                _sendInFlight[target.Tab] = true;
                PushToHardware(target.Tab, colorsByControlId);
            }
        }
    }

    private void PushToHardware(ControllerTab tab, IReadOnlyDictionary<string, string> colors)
    {
        var runtime = tab.Runtime;
        _ = Task.Run(() =>
        {
            try
            {
                runtime.SyncControllerLedColors(colors);
            }
            catch
            {
                // A single failed frame must not crash the animation loop or
                // leave an unobserved task exception; the next tick tries again.
            }
            finally
            {
                _sendInFlight[tab] = false;
            }
        });
    }

    private static IReadOnlyList<(LedAnimationTarget Target, int OffsetColumn, int OffsetRow)> ResolveParticipants(
        LedAnimationDefinition definition, ControllerWorkspace workspace)
    {
        var available = workspace.GetAnimationTargets();
        var resolved = new List<(LedAnimationTarget, int, int)>();
        foreach (var participant in definition.Participants)
        {
            var target = available.FirstOrDefault(candidate =>
                candidate.ProviderId == participant.ProviderId && candidate.PersistentId == participant.PersistentId);
            if (target is not null)
            {
                resolved.Add((target, participant.CanvasOffsetColumn, participant.CanvasOffsetRow));
            }
        }

        return resolved;
    }

    private static Dictionary<string, string> BuildAllOffColors(ILedGridAdapter adapter)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var row = 0; row < adapter.Rows; row++)
        {
            for (var column = 0; column < adapter.Columns; column++)
            {
                if (adapter.ControlIdAt(column, row) is { } controlId)
                {
                    colors[controlId.Value] = "Default";
                }
            }
        }

        return colors;
    }
}
