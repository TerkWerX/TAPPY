using Tappy.Core.Abstractions;
using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Core.Profiles;
using Tappy.Core.Safety;

namespace Tappy.Core.Execution;

public sealed class MappingEngineOptions
{
    public ulong SelfInjectionMarker { get; init; }
    public int MaximumAncestryDepth { get; init; } = 8;
    public int MaximumOutputTransitionsPerWindow { get; init; } = 200;
    public TimeSpan OutputRateWindow { get; init; } = TimeSpan.FromSeconds(1);

    internal void Validate()
    {
        if (SelfInjectionMarker == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SelfInjectionMarker),
                "The self-injection marker must be nonzero.");
        }


        if (SelfInjectionMarker > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(SelfInjectionMarker),
                "The marker must fit the 32-bit Raw Input ExtraInformation field.");
        }

        if (MaximumAncestryDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAncestryDepth));
        }

        if (MaximumOutputTransitionsPerWindow < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumOutputTransitionsPerWindow));
        }

        if (OutputRateWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputRateWindow));
        }
    }
}

public enum MappingDisposition
{
    Handled,
    Tracked,
    Rehearsal,
    ActivationPending,
    UnselectedController,
    DeviceLessRejected,
    SelfInjectedRejected,
    InvalidTransition,
    NoControllerProfile,
    NoBinding,
    SourceNeedsAttention,
    CycleRejected,
    DepthRejected,
    RateLimited,
    OutputFailed
}

public sealed record MappingResult(
    MappingDisposition Disposition,
    string Message,
    string? FrozenLayerId = null,
    EffectiveSourceMode? FrozenSourceMode = null,
    ExecutionAncestry? OutputAncestry = null)
{
    public bool ProducedOutput => Disposition == MappingDisposition.Handled;
}

public readonly record struct OutputCleanupResult(
    int ActivePressCount,
    bool OutputReleaseSucceeded);

public sealed class MappingEngine
{
    private sealed record ActivePress(
        string OwnerId,
        ControllerProfileSnapshot Controller,
        ControlBindingSnapshot Binding,
        string LayerId,
        SourceModeSnapshot SourceMode,
        ExecutionAncestry Ancestry,
        bool Rehearsal,
        bool HeldOutputAcquired);

    private readonly object _gate = new();
    private readonly IKeyboardOutput _keyboardOutput;
    private readonly IMonotonicClock _clock;
    private readonly MappingEngineOptions _options;
    private readonly HeldOutputLedger _heldOutputs = new();
    private readonly OutputRateGuard _rateGuard;
    private readonly Dictionary<ControllerSessionId, ControllerProfileSnapshot> _connected = [];
    private readonly Dictionary<ControllerSessionId, string> _activeLayers = [];
    private readonly Dictionary<(ControllerSessionId SessionId, ControlId ControlId), ActivePress> _activePresses = [];
    private TappyProfileSnapshot _profile = new TappyProfile().CreateSnapshot();
    private long _executionSequence;
    private bool _rehearsalMode;

    public MappingEngine(
        IKeyboardOutput keyboardOutput,
        MappingEngineOptions options,
        IMonotonicClock? clock = null)
    {
        _keyboardOutput = keyboardOutput ?? throw new ArgumentNullException(nameof(keyboardOutput));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _clock = clock ?? new SystemMonotonicClock();
        if (_clock.TimestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clock), "The clock frequency must be positive.");
        }

        InputStates = new ControllerInputStateTracker();
        Activation = new ControllerActivationGate(InputStates);
        _rateGuard = new OutputRateGuard(_options, _clock.TimestampFrequency);
    }

    public ControllerInputStateTracker InputStates { get; }
    public ControllerActivationGate Activation { get; }

    public bool RehearsalMode
    {
        get { lock (_gate) return _rehearsalMode; }
    }

    public OutputCleanupResult SetProfile(TappyProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            var cleanup = ReleaseAllOwnedOutputs("profile-change");
            InputStates.Clear();
            _profile = profile;
            _connected.Clear();
            _activeLayers.Clear();
            foreach (var controller in profile.Controllers)
            {
                _connected[controller.Identity.SessionId] = controller;
                _activeLayers[controller.Identity.SessionId] = controller.ActiveLayerId;
            }

            return cleanup;
        }
    }

    public bool ConnectController(ControllerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_gate)
        {
            var controller = _profile.FindController(identity);
            if (controller is null)
            {
                return false;
            }

            _connected[identity.SessionId] = controller;
            _activeLayers[identity.SessionId] = controller.ActiveLayerId;
            return true;
        }
    }

    public bool SetActiveLayer(ControllerSessionId sessionId, string layerId)
    {
        if (string.IsNullOrWhiteSpace(layerId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_connected.TryGetValue(sessionId, out var controller) || controller.FindLayer(layerId) is null)
            {
                return false;
            }

            _activeLayers[sessionId] = layerId.Trim();
            return true;
        }
    }

    public OutputCleanupResult SetRehearsalMode(bool enabled)
    {
        lock (_gate)
        {
            if (_rehearsalMode == enabled)
            {
                return new OutputCleanupResult(0, true);
            }

            var cleanup = ReleaseAllOwnedOutputs(enabled ? "enter-rehearsal" : "leave-rehearsal");
            _rehearsalMode = enabled;
            return cleanup;
        }
    }

    public MappingResult Process(ControlSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        lock (_gate)
        {
            if (signal.ControllerSessionId is not { } sessionId || sessionId.IsEmpty)
            {
                return Result(MappingDisposition.DeviceLessRejected,
                    "Input without an originating physical controller was rejected.");
            }

            if (signal.Injection.ExtraInfo == _options.SelfInjectionMarker)
            {
                return Result(MappingDisposition.SelfInjectedRejected,
                    "Tappy's self-injected input marker was rejected.");
            }

            var isActive = Activation.IsActive(sessionId);
            var isCandidate = Activation.Candidate == sessionId;
            if (!isActive && !isCandidate)
            {
                return Result(MappingDisposition.UnselectedController,
                    "Input from an unselected controller was discarded before state tracking.");
            }


            if (isActive && signal.Kind == ControlSignalKind.Press && signal.Ancestry is { } inboundAncestry)
            {
                var inboundRoute = RouteNode(sessionId, signal.ControlId);
                if (inboundAncestry.Contains(inboundRoute))
                {
                    return new MappingResult(MappingDisposition.CycleRejected,
                        "The execution ancestry contains this mapping already.",
                        OutputAncestry: inboundAncestry);
                }

                if (inboundAncestry.Depth >= _options.MaximumAncestryDepth)
                {
                    return new MappingResult(MappingDisposition.DepthRejected,
                        "The execution ancestry depth limit was reached.",
                        OutputAncestry: inboundAncestry);
                }
            }

            var state = InputStates.Apply(signal);
            Activation.Observe(state);
            if (!state.Accepted)
            {
                return Result(MappingDisposition.InvalidTransition,
                    "The signal did not describe a valid physical state transition.");
            }

            if (!Activation.IsActive(sessionId))
            {
                return Result(MappingDisposition.ActivationPending,
                    "The controller has not completed explicit select-identify-release-confirm activation.");
            }

            if (state.EffectiveKind == ControlSignalKind.Repeat)
            {
                return Result(MappingDisposition.Tracked,
                    "Hardware or operating-system repeat was tracked without inventing a new tap.");
            }

            return state.EffectiveKind == ControlSignalKind.Release
                ? ProcessRelease(sessionId, signal.ControlId)
                : ProcessPress(sessionId, signal);
        }
    }

    public OutputCleanupResult DisconnectController(ControllerSessionId sessionId)
    {
        lock (_gate)
        {
            InputStates.Disconnect(sessionId);
            Activation.Deactivate(sessionId);
            _connected.Remove(sessionId);
            _activeLayers.Remove(sessionId);
            return ReleaseControllerOutputs(sessionId, "disconnect");
        }
    }

    public OutputCleanupResult EmergencyStop()
    {
        lock (_gate)
        {
            var cleanup = ReleaseAllOwnedOutputs("emergency-stop");
            InputStates.Clear();
            return cleanup;
        }
    }

    public OutputCleanupResult ReleaseAll()
    {
        lock (_gate)
        {
            var cleanup = ReleaseAllOwnedOutputs("release-all");
            InputStates.Clear();
            return cleanup;
        }
    }

    public OutputCleanupResult ResetForLifecycleTransition()
    {
        lock (_gate)
        {
            var cleanup = ReleaseAllOwnedOutputs("lifecycle-transition");
            InputStates.Clear();
            return cleanup;
        }
    }

    private MappingResult ProcessPress(ControllerSessionId sessionId, ControlSignal signal)
    {
        if (!_connected.TryGetValue(sessionId, out var controller))
        {
            return Result(MappingDisposition.NoControllerProfile,
                "The active physical controller is not attached to a profile.");
        }

        var layerId = _activeLayers.GetValueOrDefault(sessionId, controller.ActiveLayerId);
        var layer = controller.FindLayer(layerId);
        var binding = layer?.FindBinding(signal.ControlId);
        if (layer is null || binding is null || !binding.Enabled)
        {
            return new MappingResult(MappingDisposition.NoBinding, "No enabled binding matched the control.",
                layerId, controller.SourceMode.Effective);
        }


        if (controller.SourceMode.Effective == EffectiveSourceMode.NeedsAttention)
        {
            return new MappingResult(MappingDisposition.SourceNeedsAttention,
                "The controller source backend needs attention, so the binding is not armed.",
                layerId, controller.SourceMode.Effective);
        }

        var routeNode = RouteNode(sessionId, signal.ControlId);
        var ancestry = signal.Ancestry ?? new ExecutionAncestry(
            $"{sessionId.Value}:{signal.Timestamp}:{++_executionSequence}");
        if (ancestry.Contains(routeNode))
        {
            return new MappingResult(MappingDisposition.CycleRejected,
                "The execution ancestry contains this mapping already.", layerId,
                controller.SourceMode.Effective, ancestry);
        }

        if (ancestry.Depth >= _options.MaximumAncestryDepth)
        {
            return new MappingResult(MappingDisposition.DepthRejected,
                "The execution ancestry depth limit was reached.", layerId,
                controller.SourceMode.Effective, ancestry);
        }

        var outputAncestry = ancestry.Append(routeNode);
        var ownerId = $"press:{sessionId.Value}:{signal.ControlId.Value}:{++_executionSequence}";
        var activeKey = (sessionId, signal.ControlId);
        var press = new ActivePress(ownerId, controller, binding, layerId, controller.SourceMode,
            outputAncestry, _rehearsalMode, false);
        _activePresses[activeKey] = press;

        if (_rehearsalMode)
        {
            return new MappingResult(MappingDisposition.Rehearsal,
                "The binding was recognized and output was suppressed by Rehearsal Mode.",
                layerId, controller.SourceMode.Effective, outputAncestry);
        }

        var disposition = ExecutePressAction(press, _clock.GetTimestamp(), out var heldAcquired);
        if (heldAcquired)
        {
            _activePresses[activeKey] = press with { HeldOutputAcquired = true };
        }

        return new MappingResult(disposition,
            disposition == MappingDisposition.Handled ? "The frozen press binding was dispatched." :
            disposition == MappingDisposition.RateLimited ? "The output rate limit suppressed the binding." :
            disposition == MappingDisposition.OutputFailed ? "The keyboard output backend rejected the binding." :
            "The binding was tracked without output.",
            layerId, controller.SourceMode.Effective, outputAncestry);
    }

    private MappingResult ProcessRelease(ControllerSessionId sessionId, ControlId controlId)
    {
        if (!_activePresses.Remove((sessionId, controlId), out var press))
        {
            return Result(MappingDisposition.Tracked,
                "The physical release was tracked; no frozen press was active.");
        }

        var outputFailed = false;
        var heldReleaseDispatched = false;
        if (press.HeldOutputAcquired)
        {
            var heldRelease = _heldOutputs.ReleaseOwner(press.OwnerId);
            heldReleaseDispatched = !heldRelease.IsEmpty;
            outputFailed = !TryDispatchDelta(press.OwnerId, heldRelease, press.Ancestry);
        }

        if (press.Rehearsal || _rehearsalMode)
        {
            return new MappingResult(MappingDisposition.Rehearsal,
                "The frozen release was recognized and output was suppressed by Rehearsal Mode.",
                press.LayerId, press.SourceMode.Effective, press.Ancestry);
        }

        var releaseDisposition = ExecuteTapAction(
            $"{press.OwnerId}:release", press.Binding.ReleaseAction, _clock.GetTimestamp(), press.Ancestry);
        if (outputFailed || releaseDisposition == MappingDisposition.OutputFailed)
        {
            releaseDisposition = MappingDisposition.OutputFailed;
        }
        else if (releaseDisposition == MappingDisposition.Tracked && heldReleaseDispatched)
        {
            releaseDisposition = MappingDisposition.Handled;
        }

        return new MappingResult(releaseDisposition,
            releaseDisposition == MappingDisposition.Handled ? "The frozen release binding was dispatched." :
            releaseDisposition == MappingDisposition.RateLimited ? "The output rate limit suppressed the release action." :
            releaseDisposition == MappingDisposition.OutputFailed ? "A release output failed." :
            "The frozen release completed without a separate release action.",
            press.LayerId, press.SourceMode.Effective, press.Ancestry);
    }

    private MappingDisposition ExecutePressAction(ActivePress press, long timestamp, out bool heldAcquired)
    {
        heldAcquired = false;
        var action = press.Binding.PressAction;
        if (action.Mode == KeyboardActionMode.None || action.Keys.Count == 0)
        {
            return MappingDisposition.Tracked;
        }

        if (action.Mode == KeyboardActionMode.Tap)
        {
            return ExecuteTapAction(press.OwnerId, action, timestamp, press.Ancestry);
        }

        if (!_rateGuard.TryConsume(timestamp, action.Keys.Count))
        {
            return MappingDisposition.RateLimited;
        }

        var delta = _heldOutputs.Acquire(press.OwnerId, action.Keys);
        heldAcquired = true;
        if (TryDispatchDelta(press.OwnerId, delta, press.Ancestry))
        {
            return delta.IsEmpty ? MappingDisposition.Tracked : MappingDisposition.Handled;
        }

        var cleanup = _heldOutputs.ReleaseOwner(press.OwnerId);
        TryDispatchDelta(press.OwnerId, cleanup, press.Ancestry);
        heldAcquired = false;
        return MappingDisposition.OutputFailed;
    }

    private MappingDisposition ExecuteTapAction(
        string ownerId,
        KeyboardActionSnapshot action,
        long timestamp,
        ExecutionAncestry ancestry)
    {
        if (action.Mode == KeyboardActionMode.None || action.Keys.Count == 0)
        {
            return MappingDisposition.Tracked;
        }

        if (!_rateGuard.TryConsume(timestamp, checked(action.Keys.Count * 2)))
        {
            return MappingDisposition.RateLimited;
        }

        var down = _heldOutputs.Acquire(ownerId, action.Keys);
        if (!TryDispatchDelta(ownerId, down, ancestry))
        {
            var cleanup = _heldOutputs.ReleaseOwner(ownerId);
            TryDispatchDelta(ownerId, cleanup, ancestry);
            return MappingDisposition.OutputFailed;
        }

        var up = _heldOutputs.ReleaseOwner(ownerId);
        return TryDispatchDelta(ownerId, up, ancestry)
            ? down.IsEmpty && up.IsEmpty ? MappingDisposition.Tracked : MappingDisposition.Handled
            : MappingDisposition.OutputFailed;
    }

    private bool TryDispatchDelta(string ownerId, HeldOutputDelta delta, ExecutionAncestry ancestry)
    {
        try
        {
            if (delta.KeysDown.Count > 0)
            {
                _keyboardOutput.KeyDown(Request(ownerId, delta.KeysDown, ancestry));
            }

            if (delta.KeysUp.Count > 0)
            {
                _keyboardOutput.KeyUp(Request(ownerId, delta.KeysUp.Reverse().ToArray(), ancestry));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private OutputCleanupResult ReleaseControllerOutputs(ControllerSessionId sessionId, string reason)
    {
        var active = _activePresses
            .Where(pair => pair.Key.SessionId == sessionId)
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();
        var releaseSucceeded = true;
        foreach (var (key, press) in active)
        {
            _activePresses.Remove(key);
            releaseSucceeded &= TryDispatchDelta(
                press.OwnerId,
                _heldOutputs.ReleaseOwner(press.OwnerId),
                press.Ancestry);
        }

        _ = reason;
        return new OutputCleanupResult(active.Length, releaseSucceeded);
    }

    private OutputCleanupResult ReleaseAllOwnedOutputs(string reason)
    {
        var count = _activePresses.Count;
        _activePresses.Clear();
        var ancestry = new ExecutionAncestry($"cleanup:{reason}:{_clock.GetTimestamp()}:{++_executionSequence}");
        var releaseSucceeded = TryDispatchDelta(
            $"cleanup:{reason}",
            _heldOutputs.ReleaseAll(),
            ancestry);
        _rateGuard.Clear();
        return new OutputCleanupResult(count, releaseSucceeded);
    }

    private KeyboardOutputRequest Request(
        string ownerId,
        IReadOnlyList<KeyboardOutputKey> keys,
        ExecutionAncestry ancestry) =>
        new(ownerId, keys, _options.SelfInjectionMarker, ancestry);

    private static string RouteNode(ControllerSessionId sessionId, ControlId controlId) =>
        $"{sessionId.Value}|{controlId.Value}";

    private static MappingResult Result(MappingDisposition disposition, string message) =>
        new(disposition, message);

    private sealed class OutputRateGuard
    {
        private readonly int _maximum;
        private readonly long _windowTicks;
        private readonly Queue<(long Timestamp, int Count)> _events = [];
        private int _currentCount;

        public OutputRateGuard(MappingEngineOptions options, long timestampFrequency)
        {
            _maximum = options.MaximumOutputTransitionsPerWindow;
            _windowTicks = Math.Max(1, checked((long)Math.Ceiling(
                options.OutputRateWindow.TotalSeconds * timestampFrequency)));
        }

        public bool TryConsume(long timestamp, int count)
        {
            while (_events.TryPeek(out var entry) &&
                   (timestamp < entry.Timestamp || timestamp - entry.Timestamp >= _windowTicks))
            {
                _events.Dequeue();
                _currentCount -= entry.Count;
            }

            if (count <= 0)
            {
                return true;
            }

            if (_currentCount > _maximum - count)
            {
                return false;
            }

            _events.Enqueue((timestamp, count));
            _currentCount += count;
            return true;
        }

        public void Clear()
        {
            _events.Clear();
            _currentCount = 0;
        }
    }
}
