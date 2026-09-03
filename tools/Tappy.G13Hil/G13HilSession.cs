using System.Diagnostics;
using Tappy.Core.Input;
using Tappy.Windows.Input;

namespace Tappy.G13Hil;

internal enum G13HilPhase
{
    AwaitNeutral,
    Handshake,
    Controls,
    SimultaneousSets,
    DuplicateSweep,
    Complete,
}

internal sealed record G13HilPrompt(int Revision, G13HilPhase Phase, string Instruction);

internal sealed record G13HilSessionSnapshot(
    G13HilPrompt Prompt,
    bool NeutralObserved,
    bool HandshakePassed,
    bool AllControlsPassed,
    bool StickDirectionsPassed,
    bool SimultaneousSetsPassed,
    bool DuplicateSweepCompleted,
    bool BalancedTransitions,
    bool ExpectedControlGatingPassed,
    bool DuplicateSuppressionPassed,
    int CodeDefinedControlCount,
    int RequiredCyclesPerControl,
    int CompletedControlCycles,
    int AcceptedPresses,
    int AcceptedReleases,
    int UnexpectedTransitions,
    int DuplicateTransitions,
    int UnbalancedTransitions,
    int PromptRetries,
    int SimultaneousSetsRequired,
    int SimultaneousSetsPassedCount,
    int StickDirectionsPassedCount,
    long NeutralDurationMs,
    long HandshakeDurationMs,
    long ControlsDurationMs,
    long SimultaneousDurationMs,
    long DuplicateSweepDurationMs)
{
    internal bool IsComplete => Prompt.Phase == G13HilPhase.Complete;

    internal bool SessionAssertionsPassed =>
        IsComplete &&
        NeutralObserved &&
        HandshakePassed &&
        AllControlsPassed &&
        StickDirectionsPassed &&
        SimultaneousSetsPassed &&
        DuplicateSweepCompleted &&
        BalancedTransitions &&
        ExpectedControlGatingPassed &&
        DuplicateSuppressionPassed;
}

/// <summary>
/// Deterministic finite-state verifier over already-normalized G13 transitions.
/// Unexpected control identities are inspected only for expected-control gating;
/// they are never retained. Evidence is derived exclusively from aggregate fields.
/// </summary>
internal sealed class G13HilSession
{
    internal const int RequiredCyclesPerControl = 2;

    internal const int RequiredSweepCyclesPerDirection = 2;

    private static readonly LogitechG13Control[] StickDirections =
    [
        LogitechG13Control.StickLeft,
        LogitechG13Control.StickRight,
        LogitechG13Control.StickUp,
        LogitechG13Control.StickDown,
    ];

    private static readonly LogitechG13Control[][] SimultaneousSets =
    [
        [LogitechG13Control.G1, LogitechG13Control.G2],
        [LogitechG13Control.G3, LogitechG13Control.G4, LogitechG13Control.G5, LogitechG13Control.G6],
        [
            LogitechG13Control.G7,
            LogitechG13Control.M1,
            LogitechG13Control.JoystickPress,
            LogitechG13Control.StickRight,
        ],
    ];

    private readonly object _gate = new();
    private readonly IReadOnlyList<LogitechG13ControlDefinition> _definitions;
    private readonly IReadOnlyDictionary<LogitechG13Control, string> _displayNames;
    private readonly Func<long> _timestampProvider;
    private readonly long _timestampFrequency;
    private readonly Dictionary<G13HilPhase, long> _elapsedTicks = [];
    private readonly HashSet<LogitechG13Control> _simultaneousHeld = [];
    private readonly HashSet<LogitechG13Control> _simultaneousSeen = [];
    private readonly HashSet<LogitechG13Control> _sweepHeld = [];
    private G13HilPhase _phase = G13HilPhase.AwaitNeutral;
    private long _phaseStartedAt;
    private int _promptRevision = 1;
    private int _singleCycles;
    private bool _singleHeld;
    private int _controlIndex;
    private int _simultaneousIndex;
    private bool _simultaneousOverlap;
    private int _simultaneousSetsPassed;
    private int _completedControlCycles;
    private int _completedStickDirections;
    private int _acceptedPresses;
    private int _acceptedReleases;
    private int _unexpectedTransitions;
    private int _duplicateTransitions;
    private int _unbalancedTransitions;
    private int _promptRetries;
    private int _sweepLeftCycles;
    private int _sweepRightCycles;
    private bool _sweepAnchorPressed;
    private bool _sweepAnchorCompleted;
    private bool _sweepInvalid;
    private int _sweepSequenceErrors;
    private bool _neutralObserved;
    private bool _handshakePassed;
    private bool _allControlsPassed;
    private bool _duplicateSweepCompleted;

    internal G13HilSession(
        IReadOnlyList<LogitechG13ControlDefinition>? definitions = null,
        Func<long>? timestampProvider = null,
        long? timestampFrequency = null)
    {
        _definitions = definitions ?? LogitechG13InputProvider.SupportedControls;
        if (_definitions.Count != 39 ||
            _definitions.Select(definition => definition.Control).Distinct().Count() != 39)
        {
            throw new InvalidOperationException("The HIL contract requires 39 unique code-defined G13 controls.");
        }

        _displayNames = _definitions.ToDictionary(
            definition => definition.Control,
            definition => definition.DisplayName);
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _timestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
        if (_timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        _phaseStartedAt = _timestampProvider();
    }

    internal G13HilPhase Phase
    {
        get
        {
            lock (_gate)
            {
                return _phase;
            }
        }
    }

    internal void MarkNeutralObserved()
    {
        lock (_gate)
        {
            if (_phase != G13HilPhase.AwaitNeutral)
            {
                return;
            }

            _neutralObserved = true;
            TransitionTo(G13HilPhase.Handshake);
        }
    }

    internal void Accept(LogitechG13Control control, ControlSignalKind kind)
    {
        lock (_gate)
        {
            if (_phase is G13HilPhase.AwaitNeutral or G13HilPhase.Complete)
            {
                return;
            }

            if (kind == ControlSignalKind.Repeat)
            {
                _duplicateTransitions++;
                return;
            }

            switch (_phase)
            {
                case G13HilPhase.Handshake:
                    AcceptSingle(control, kind, LogitechG13Control.G1, requiredCycles: 1);
                    break;
                case G13HilPhase.Controls:
                    AcceptSingle(
                        control,
                        kind,
                        _definitions[_controlIndex].Control,
                        RequiredCyclesPerControl);
                    break;
                case G13HilPhase.SimultaneousSets:
                    AcceptSimultaneous(control, kind);
                    break;
                case G13HilPhase.DuplicateSweep:
                    AcceptDuplicateSweep(control, kind);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported active HIL phase.");
            }
        }
    }

    internal G13HilSessionSnapshot Snapshot()
    {
        lock (_gate)
        {
            var durations = SnapshotDurations();
            var balanced = _acceptedPresses == _acceptedReleases &&
                _unbalancedTransitions == 0 &&
                !_singleHeld &&
                _simultaneousHeld.Count == 0 &&
                _sweepHeld.Count == 0;
            return new G13HilSessionSnapshot(
                CreatePrompt(),
                _neutralObserved,
                _handshakePassed,
                _allControlsPassed,
                _completedStickDirections == StickDirections.Length,
                _simultaneousSetsPassed == SimultaneousSets.Length,
                _duplicateSweepCompleted,
                balanced,
                _unexpectedTransitions == 0,
                _duplicateTransitions == 0 && _sweepSequenceErrors == 0,
                _definitions.Count,
                RequiredCyclesPerControl,
                _completedControlCycles,
                _acceptedPresses,
                _acceptedReleases,
                _unexpectedTransitions,
                _duplicateTransitions,
                _unbalancedTransitions,
                _promptRetries,
                SimultaneousSets.Length,
                _simultaneousSetsPassed,
                _completedStickDirections,
                durations.GetValueOrDefault(G13HilPhase.AwaitNeutral),
                durations.GetValueOrDefault(G13HilPhase.Handshake),
                durations.GetValueOrDefault(G13HilPhase.Controls),
                durations.GetValueOrDefault(G13HilPhase.SimultaneousSets),
                durations.GetValueOrDefault(G13HilPhase.DuplicateSweep));
        }
    }

    private void AcceptSingle(
        LogitechG13Control control,
        ControlSignalKind kind,
        LogitechG13Control expected,
        int requiredCycles)
    {
        if (control != expected)
        {
            _unexpectedTransitions++;
            return;
        }

        if (kind == ControlSignalKind.Press)
        {
            if (_singleHeld)
            {
                _duplicateTransitions++;
                return;
            }

            _singleHeld = true;
            _acceptedPresses++;
            return;
        }

        if (!_singleHeld)
        {
            _unbalancedTransitions++;
            return;
        }

        _singleHeld = false;
        _acceptedReleases++;
        _singleCycles++;
        if (_singleCycles < requiredCycles)
        {
            return;
        }

        _singleCycles = 0;
        if (_phase == G13HilPhase.Handshake)
        {
            _handshakePassed = true;
            TransitionTo(G13HilPhase.Controls);
            return;
        }

        _completedControlCycles += requiredCycles;
        if (StickDirections.Contains(expected))
        {
            _completedStickDirections++;
        }

        _controlIndex++;
        if (_controlIndex == _definitions.Count)
        {
            _allControlsPassed = true;
            TransitionTo(G13HilPhase.SimultaneousSets);
        }
        else
        {
            _promptRevision++;
        }
    }

    private void AcceptSimultaneous(LogitechG13Control control, ControlSignalKind kind)
    {
        var expected = SimultaneousSets[_simultaneousIndex];
        if (!expected.Contains(control))
        {
            _unexpectedTransitions++;
            return;
        }

        if (kind == ControlSignalKind.Press)
        {
            if (!_simultaneousHeld.Add(control))
            {
                _duplicateTransitions++;
                return;
            }

            _simultaneousSeen.Add(control);
            _acceptedPresses++;
            if (_simultaneousHeld.Count == expected.Length)
            {
                _simultaneousOverlap = true;
            }

            return;
        }

        if (!_simultaneousHeld.Remove(control))
        {
            _unbalancedTransitions++;
            return;
        }

        _acceptedReleases++;
        if (_simultaneousHeld.Count != 0)
        {
            return;
        }

        if (_simultaneousOverlap &&
            _simultaneousSeen.Count == expected.Length &&
            expected.All(_simultaneousSeen.Contains))
        {
            _simultaneousSetsPassed++;
            _simultaneousIndex++;
            ResetSimultaneousAttempt();
            if (_simultaneousIndex == SimultaneousSets.Length)
            {
                TransitionTo(G13HilPhase.DuplicateSweep);
            }
            else
            {
                _promptRevision++;
            }

            return;
        }

        _promptRetries++;
        ResetSimultaneousAttempt();
        _promptRevision++;
    }

    private void AcceptDuplicateSweep(LogitechG13Control control, ControlSignalKind kind)
    {
        if (control is not (LogitechG13Control.G1 or
            LogitechG13Control.StickLeft or LogitechG13Control.StickRight))
        {
            _unexpectedTransitions++;
            return;
        }

        if (kind == ControlSignalKind.Press)
        {
            if (!_sweepHeld.Add(control))
            {
                _duplicateTransitions++;
                return;
            }

            _acceptedPresses++;
            if (control == LogitechG13Control.G1)
            {
                if (_sweepAnchorPressed)
                {
                    _sweepInvalid = true;
                    _sweepSequenceErrors++;
                }

                _sweepAnchorPressed = true;
            }
            else if (!_sweepHeld.Contains(LogitechG13Control.G1))
            {
                _sweepInvalid = true;
                _sweepSequenceErrors++;
            }

            return;
        }

        if (!_sweepHeld.Remove(control))
        {
            _unbalancedTransitions++;
            return;
        }

        _acceptedReleases++;
        if (control == LogitechG13Control.G1)
        {
            _sweepAnchorCompleted = _sweepAnchorPressed;
            if (_sweepHeld.Count != 0 ||
                _sweepLeftCycles < RequiredSweepCyclesPerDirection ||
                _sweepRightCycles < RequiredSweepCyclesPerDirection)
            {
                _sweepInvalid = true;
                _sweepSequenceErrors++;
            }
        }
        else if (_sweepHeld.Contains(LogitechG13Control.G1))
        {
            if (control == LogitechG13Control.StickLeft)
            {
                _sweepLeftCycles++;
            }
            else
            {
                _sweepRightCycles++;
            }
        }
        else
        {
            _sweepInvalid = true;
            _sweepSequenceErrors++;
        }

        if (_sweepHeld.Count != 0)
        {
            return;
        }

        if (!_sweepInvalid &&
            _sweepAnchorCompleted &&
            _sweepLeftCycles >= RequiredSweepCyclesPerDirection &&
            _sweepRightCycles >= RequiredSweepCyclesPerDirection)
        {
            _duplicateSweepCompleted = true;
            TransitionTo(G13HilPhase.Complete);
            return;
        }

        _promptRetries++;
        ResetSweepAttempt();
        _promptRevision++;
    }

    private void ResetSimultaneousAttempt()
    {
        _simultaneousHeld.Clear();
        _simultaneousSeen.Clear();
        _simultaneousOverlap = false;
    }

    private void ResetSweepAttempt()
    {
        _sweepHeld.Clear();
        _sweepLeftCycles = 0;
        _sweepRightCycles = 0;
        _sweepAnchorPressed = false;
        _sweepAnchorCompleted = false;
        _sweepInvalid = false;
    }

    private void TransitionTo(G13HilPhase next)
    {
        var now = _timestampProvider();
        _elapsedTicks[_phase] = _elapsedTicks.GetValueOrDefault(_phase) +
            Math.Max(0, now - _phaseStartedAt);
        _phase = next;
        _phaseStartedAt = now;
        _promptRevision++;
    }

    private Dictionary<G13HilPhase, long> SnapshotDurations()
    {
        var now = _timestampProvider();
        var ticks = new Dictionary<G13HilPhase, long>(_elapsedTicks);
        if (_phase != G13HilPhase.Complete)
        {
            ticks[_phase] = ticks.GetValueOrDefault(_phase) +
                Math.Max(0, now - _phaseStartedAt);
        }

        return ticks.ToDictionary(
            pair => pair.Key,
            pair => ToMilliseconds(pair.Value));
    }

    private long ToMilliseconds(long ticks)
    {
        var milliseconds = ticks * 1000d / _timestampFrequency;
        return milliseconds >= long.MaxValue
            ? long.MaxValue
            : Math.Max(0, checked((long)Math.Round(milliseconds, MidpointRounding.AwayFromZero)));
    }

    private G13HilPrompt CreatePrompt()
    {
        var instruction = _phase switch
        {
            G13HilPhase.AwaitNeutral =>
                "Release every G13 control. Move the stick once, return it to center, and keep everything released.",
            G13HilPhase.Handshake =>
                "Identity handshake: press and release G1 exactly once.",
            G13HilPhase.Controls =>
                $"Press and release {_definitions[_controlIndex].DisplayName} exactly twice; use no other control.",
            G13HilPhase.SimultaneousSets =>
                $"Hold this complete set at the same time, then release all: {FormatSet(SimultaneousSets[_simultaneousIndex])}.",
            G13HilPhase.DuplicateSweep =>
                "Hold G1 continuously. Sweep fully left, fully right, and back to center twice; then release G1.",
            G13HilPhase.Complete =>
                "Input sequence complete. Capture is being disarmed and aggregate assertions are being evaluated.",
            _ => throw new InvalidOperationException("Unsupported HIL phase."),
        };
        return new G13HilPrompt(_promptRevision, _phase, instruction);
    }

    private string FormatSet(IEnumerable<LogitechG13Control> controls) =>
        string.Join(" + ", controls.Select(control => _displayNames[control]));
}
