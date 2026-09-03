using System.Diagnostics;

namespace Tappy.OutputWitness;

internal readonly record struct ConsoleKeyObservation(
    ushort VirtualKeyCode,
    bool IsKeyDown,
    ushort RepeatCount);

internal enum OutputWitnessPhase
{
    AwaitOriginalPress,
    AwaitOriginalRepeat,
    AwaitOriginalRelease,
    AwaitRequiredOutput,
    HeldUnplugReady,
    RehearsalQuietWindow,
    HeldUnplugObservationWindow,
    AwaitOutputReleaseAfterFailure,
    Complete,
    Failed,
}

internal sealed record OutputWitnessSessionSnapshot(
    OutputWitnessScenario Scenario,
    OutputWitnessPhase Phase,
    bool ExpectedOriginalKeyDownObserved,
    bool ExpectedOriginalKeyUpObserved,
    bool ExpectedOriginalRepeatObserved,
    bool ExpectedOutputKeyDownObserved,
    bool ExpectedOutputKeyUpObserved,
    bool HeldUnplugStageReached,
    bool HeldUnplugOutputReleaseObserved,
    bool PostConditionWindowCompleted,
    bool SourceKeyHeld,
    bool OutputKeyHeld,
    bool NoUnexpectedOrDuplicateOutputTransitions,
    bool ScenarioAssertionsPassed,
    bool HasFailed,
    int SourceKeyDownUnits,
    int SourceKeyUpUnits,
    int SourceRepeatUnits,
    int SourceUnbalancedReleaseUnits,
    int OutputKeyDownUnits,
    int OutputKeyUpUnits,
    int OutputDuplicateDownUnits,
    int OutputUnbalancedReleaseUnits,
    long PostConditionWindowObservedMs,
    long PostConditionWindowRequiredMs)
{
    internal bool IsComplete => Phase == OutputWitnessPhase.Complete;

    internal bool CanTerminateFailed =>
        HasFailed &&
        !OutputKeyHeld &&
        (Scenario == OutputWitnessScenario.HeldUnplug || !SourceKeyHeld);
}

/// <summary>
/// A deterministic aggregate verifier over two allowlisted virtual keys. Other
/// console key identities are discarded immediately and never represented in a
/// snapshot or evidence. No event chronology is retained.
/// </summary>
internal sealed class OutputWitnessSession
{
    internal static readonly TimeSpan PostConditionObservationWindow = TimeSpan.FromSeconds(2);

    private readonly OutputWitnessScenario _scenario;
    private readonly WitnessKeySpec _originalKey;
    private readonly WitnessKeySpec _outputKey;
    private readonly Func<long> _timestampProvider;
    private readonly long _timestampFrequency;
    private bool _sourceHeld;
    private bool _outputHeld;
    private bool _postConditionWindowStarted;
    private bool _heldUnplugStageReached;
    private bool _heldUnplugOutputReleaseObserved;
    private long _postConditionWindowStartedAt;
    private int _sourceInitialDowns;
    private int _sourceKeyDownUnits;
    private int _sourceKeyUpUnits;
    private int _sourceRepeatUnits;
    private int _sourceUnbalancedReleaseUnits;
    private int _outputInitialDowns;
    private int _outputKeyDownUnits;
    private int _outputKeyUpUnits;
    private int _outputDuplicateDownUnits;
    private int _outputUnbalancedReleaseUnits;

    internal OutputWitnessSession(
        OutputWitnessScenario scenario,
        WitnessKeySpec originalKey,
        WitnessKeySpec outputKey,
        Func<long>? timestampProvider = null,
        long? timestampFrequency = null)
    {
        if (!WitnessKeyCatalog.IsAllowedOriginal(originalKey))
        {
            throw new ArgumentOutOfRangeException(nameof(originalKey));
        }

        if (!WitnessKeyCatalog.IsAllowedOutput(outputKey))
        {
            throw new ArgumentOutOfRangeException(nameof(outputKey));
        }

        _scenario = scenario;
        _originalKey = originalKey;
        _outputKey = outputKey;
        _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        _timestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
        if (_timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }
    }

    internal void Accept(ConsoleKeyObservation observation)
    {
        if (observation.RepeatCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observation));
        }

        if (observation.VirtualKeyCode == _originalKey.VirtualKeyCode)
        {
            AcceptOriginal(observation.IsKeyDown, observation.RepeatCount);
        }
        else if (observation.VirtualKeyCode == _outputKey.VirtualKeyCode)
        {
            AcceptOutput(observation.IsKeyDown, observation.RepeatCount);
        }
    }

    internal OutputWitnessSessionSnapshot Snapshot()
    {
        var postConditionWindowObservedMs = PostConditionWindowObservedMilliseconds();
        var postConditionWindowRequiredMs = _scenario == OutputWitnessScenario.Basic
            ? 0L
            : checked((long)PostConditionObservationWindow.TotalMilliseconds);
        var postConditionWindowCompleted = _scenario == OutputWitnessScenario.Basic ||
            _postConditionWindowStarted &&
            postConditionWindowObservedMs >= postConditionWindowRequiredMs;
        var sourceFailed = SourceCardinalityFailed();
        var outputFailed = OutputCardinalityFailed();
        var scenarioPassed = ScenarioPassed(postConditionWindowCompleted) &&
            !sourceFailed &&
            !outputFailed;
        var failed = sourceFailed || outputFailed || ScenarioSequenceFailed();
        var phase = DeterminePhase(scenarioPassed, failed, postConditionWindowCompleted);

        return new OutputWitnessSessionSnapshot(
            _scenario,
            phase,
            _sourceInitialDowns == 1,
            _sourceKeyUpUnits == 1,
            _sourceRepeatUnits > 0,
            _outputInitialDowns == 1,
            _outputKeyUpUnits == 1 && _outputUnbalancedReleaseUnits == 0,
            _heldUnplugStageReached,
            _heldUnplugOutputReleaseObserved,
            postConditionWindowCompleted,
            _sourceHeld,
            _outputHeld,
            NoUnexpectedOrDuplicateOutputTransitions(),
            scenarioPassed,
            failed,
            _sourceKeyDownUnits,
            _sourceKeyUpUnits,
            _sourceRepeatUnits,
            _sourceUnbalancedReleaseUnits,
            _outputKeyDownUnits,
            _outputKeyUpUnits,
            _outputDuplicateDownUnits,
            _outputUnbalancedReleaseUnits,
            postConditionWindowObservedMs,
            postConditionWindowRequiredMs);
    }

    private void AcceptOriginal(bool isKeyDown, ushort repeatCount)
    {
        if (isKeyDown)
        {
            _sourceKeyDownUnits += repeatCount;
            if (_sourceHeld)
            {
                _sourceRepeatUnits += repeatCount;
            }
            else
            {
                _sourceHeld = true;
                _sourceInitialDowns++;
                _sourceRepeatUnits += repeatCount - 1;
            }

            TryReachHeldUnplugStage();
            return;
        }

        _sourceKeyUpUnits += repeatCount;
        if (!_sourceHeld)
        {
            _sourceUnbalancedReleaseUnits += repeatCount;
            return;
        }

        _sourceHeld = false;
        if (repeatCount > 1)
        {
            _sourceUnbalancedReleaseUnits += repeatCount - 1;
        }

        if (_scenario == OutputWitnessScenario.Rehearsal &&
            _sourceInitialDowns == 1 &&
            _sourceKeyUpUnits == 1)
        {
            StartPostConditionWindow();
        }
    }

    private void AcceptOutput(bool isKeyDown, ushort repeatCount)
    {
        if (isKeyDown)
        {
            _outputKeyDownUnits += repeatCount;
            if (_outputHeld)
            {
                _outputDuplicateDownUnits += repeatCount;
            }
            else
            {
                _outputHeld = true;
                _outputInitialDowns++;
                _outputDuplicateDownUnits += repeatCount - 1;
            }

            TryReachHeldUnplugStage();
            return;
        }

        _outputKeyUpUnits += repeatCount;
        if (!_outputHeld)
        {
            _outputUnbalancedReleaseUnits += repeatCount;
            return;
        }

        if (_scenario == OutputWitnessScenario.HeldUnplug &&
            _heldUnplugStageReached &&
            _sourceHeld &&
            repeatCount == 1)
        {
            _heldUnplugOutputReleaseObserved = true;
            StartPostConditionWindow();
        }

        _outputHeld = false;
        if (repeatCount > 1)
        {
            _outputUnbalancedReleaseUnits += repeatCount - 1;
        }
    }

    private void TryReachHeldUnplugStage()
    {
        if (_scenario == OutputWitnessScenario.HeldUnplug &&
            _sourceHeld &&
            _outputHeld &&
            _sourceInitialDowns == 1 &&
            _outputInitialDowns == 1)
        {
            _heldUnplugStageReached = true;
        }
    }

    private bool SourceCardinalityFailed() =>
        _sourceInitialDowns > 1 ||
        _sourceUnbalancedReleaseUnits > 0 ||
        (_scenario == OutputWitnessScenario.HeldUnplug
            ? _sourceKeyUpUnits > 0
            : _sourceKeyUpUnits > 1) ||
        (_scenario == OutputWitnessScenario.Basic &&
            _sourceKeyUpUnits == 1 &&
            _sourceRepeatUnits == 0);

    private bool OutputCardinalityFailed() =>
        _scenario == OutputWitnessScenario.Rehearsal
            ? _outputKeyDownUnits > 0 || _outputKeyUpUnits > 0
            : _outputInitialDowns > 1 ||
                _outputDuplicateDownUnits > 0 ||
                _outputKeyUpUnits > 1 ||
                _outputUnbalancedReleaseUnits > 0;

    private bool ScenarioSequenceFailed() =>
        _scenario == OutputWitnessScenario.HeldUnplug &&
        _outputKeyUpUnits > 0 &&
        !_heldUnplugOutputReleaseObserved;

    private bool ScenarioPassed(bool postConditionWindowCompleted) =>
        _scenario switch
        {
            OutputWitnessScenario.Basic =>
                _sourceInitialDowns == 1 &&
                _sourceRepeatUnits > 0 &&
                _sourceKeyUpUnits == 1 &&
                !_sourceHeld &&
                ExactOutputCycleObserved(),
            OutputWitnessScenario.Rehearsal =>
                _sourceInitialDowns == 1 &&
                _sourceKeyUpUnits == 1 &&
                !_sourceHeld &&
                _outputKeyDownUnits == 0 &&
                _outputKeyUpUnits == 0 &&
                !_outputHeld &&
                postConditionWindowCompleted,
            OutputWitnessScenario.HeldUnplug =>
                _sourceInitialDowns == 1 &&
                _sourceKeyUpUnits == 0 &&
                _sourceHeld &&
                _heldUnplugStageReached &&
                _heldUnplugOutputReleaseObserved &&
                postConditionWindowCompleted &&
                ExactOutputCycleObserved(),
            _ => throw new InvalidOperationException("Unsupported witness scenario."),
        };

    private bool ExactOutputCycleObserved() =>
        _outputInitialDowns == 1 &&
        _outputKeyDownUnits == 1 &&
        _outputKeyUpUnits == 1 &&
        _outputDuplicateDownUnits == 0 &&
        _outputUnbalancedReleaseUnits == 0 &&
        !_outputHeld;

    private bool NoUnexpectedOrDuplicateOutputTransitions() =>
        _scenario == OutputWitnessScenario.Rehearsal
            ? _outputKeyDownUnits == 0 && _outputKeyUpUnits == 0 && !_outputHeld
            : _outputKeyDownUnits <= 1 &&
                _outputKeyUpUnits <= 1 &&
                _outputDuplicateDownUnits == 0 &&
                _outputUnbalancedReleaseUnits == 0;

    private OutputWitnessPhase DeterminePhase(
        bool scenarioPassed,
        bool failed,
        bool postConditionWindowCompleted)
    {
        if (scenarioPassed)
        {
            return OutputWitnessPhase.Complete;
        }

        if (failed)
        {
            return _outputHeld
                ? OutputWitnessPhase.AwaitOutputReleaseAfterFailure
                : OutputWitnessPhase.Failed;
        }

        if (_scenario == OutputWitnessScenario.HeldUnplug)
        {
            if (_heldUnplugOutputReleaseObserved && !postConditionWindowCompleted)
            {
                return OutputWitnessPhase.HeldUnplugObservationWindow;
            }

            return _heldUnplugStageReached
                ? OutputWitnessPhase.HeldUnplugReady
                : OutputWitnessPhase.AwaitOriginalPress;
        }

        if (_sourceInitialDowns == 0)
        {
            return OutputWitnessPhase.AwaitOriginalPress;
        }

        if (_scenario == OutputWitnessScenario.Rehearsal)
        {
            return _postConditionWindowStarted && !postConditionWindowCompleted
                ? OutputWitnessPhase.RehearsalQuietWindow
                : OutputWitnessPhase.AwaitOriginalRelease;
        }

        if (_sourceHeld && _sourceRepeatUnits == 0)
        {
            return OutputWitnessPhase.AwaitOriginalRepeat;
        }

        if (_sourceHeld)
        {
            return OutputWitnessPhase.AwaitOriginalRelease;
        }

        return OutputWitnessPhase.AwaitRequiredOutput;
    }

    private void StartPostConditionWindow()
    {
        _postConditionWindowStarted = true;
        _postConditionWindowStartedAt = _timestampProvider();
    }

    private long PostConditionWindowObservedMilliseconds()
    {
        if (!_postConditionWindowStarted)
        {
            return 0;
        }

        var elapsedTicks = Math.Max(0, _timestampProvider() - _postConditionWindowStartedAt);
        return (long)(elapsedTicks * 1000d / _timestampFrequency);
    }
}
