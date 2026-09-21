using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Profiles;

namespace Tappy.Core.Execution;

public sealed partial class MappingEngine
{
    private sealed class CombinationGesture(string layer, ControlSignal first, IReadOnlyList<ControlCombinationSnapshot> combinations)
    {
        internal readonly string Layer = layer;
        internal readonly ControlSignal First = first;
        internal readonly IReadOnlyList<ControlCombinationSnapshot> Combinations = combinations;
        internal readonly HashSet<ControlId> Down = [];
        internal readonly HashSet<ControlId> Seen = [];
        internal readonly Dictionary<ControlId, ControlCombinationSnapshot> Matches = [];
        internal bool Cancelled;
    }
    private readonly Dictionary<ControllerSessionId, CombinationGesture> _combinationGestures = [];

    private MappingResult? ProcessCombination(ControllerSessionId session, ControlSignal signal)
    {
        _combinationGestures.TryGetValue(session, out var gesture);
        if (gesture is null)
        {
            if (signal.Kind != ControlSignalKind.Press || !_connected.TryGetValue(session, out var controller)) { return null; }
            var layer = _activeLayers.GetValueOrDefault(session, controller.ActiveLayerId);
            var combinations = controller.Combinations.Where(c =>
                controller.BankControls.Any(b => b.ControlId == c.ControlId) || controller.FindLayer(layer)?.FindBinding(c.ControlId) is { Enabled: true }).ToArray();
            if (!combinations.Any(c => c.Members.Contains(signal.ControlId))) { return null; }
            gesture = new CombinationGesture(layer, signal, combinations);
            _combinationGestures.Add(session, gesture);
        }
        if (!gesture.Combinations.Any(c => c.Members.Contains(signal.ControlId))) { return null; }
        if (signal.IsCleanup) { gesture.Cancelled = true; }
        if (signal.Kind == ControlSignalKind.Press)
        {
            // A second stroke before releasing the group is ambiguous, not a new tap.
            if (!gesture.Seen.Add(signal.ControlId)) { gesture.Cancelled = true; }
            gesture.Down.Add(signal.ControlId);
            foreach (var combination in gesture.Combinations.Where(c => c.Members.All(gesture.Down.Contains)))
            { gesture.Matches[combination.ControlId] = combination; }
        }
        else { gesture.Down.Remove(signal.ControlId); }
        if (gesture.Down.Count != 0) { return Result(MappingDisposition.Tracked, "Combination pending; release all participating buttons to run it."); }
        _combinationGestures.Remove(session);
        if (gesture.Cancelled) { return Result(MappingDisposition.Tracked, "Combination cancelled; no assignment ran."); }
        var largest = gesture.Matches.Values.OrderByDescending(c => c.Members.Count).ToArray();
        ControlId chosen;
        if (largest.Length != 0 && (largest.Length == 1 || largest[0].Members.Count > largest[1].Members.Count))
        { chosen = largest[0].ControlId; }
        else if (gesture.Seen.Count == 1) { chosen = gesture.Seen.Single(); }
        else { return Result(MappingDisposition.Tracked, "No unique combination matched. Release all buttons and try again."); }

        // No timers or speculative output: participating singles and combinations
        // dispatch only after the gesture ends. Imported held/repeat assignments
        // fail closed instead of silently becoming shortened macros.
        if (_connected.TryGetValue(session, out var current) && !current.BankControls.Any(b => b.ControlId == chosen) &&
            current.FindLayer(gesture.Layer)?.FindBinding(chosen) is { } binding &&
            (binding.PressAction.Mode == KeyboardActionMode.HoldUntilRelease ||
             (!binding.PressSequence.IsEmpty && binding.PressSequence.Mode != ControllerActionSequenceMode.RunOnce)))
        { return Result(MappingDisposition.SourceNeedsAttention, "Combination buttons require run-once assignments. The saved held/repeat assignment was not run."); }
        var press = ProcessPress(session, gesture.First with { ControlId = chosen }, gesture.Layer);
        var release = ProcessRelease(session, chosen, press.Disposition == MappingDisposition.OutputFailed);
        var result = press.Disposition is not (MappingDisposition.Tracked or MappingDisposition.Handled or MappingDisposition.Rehearsal or MappingDisposition.NoBinding) ? press :
            release.BankControl is not null ? release : press.ProducedOutput && release.Disposition != MappingDisposition.OutputFailed ? press : release;
        return result with { TriggeredCombination = chosen.Value.StartsWith("combination:", StringComparison.Ordinal) ? chosen : null };
    }
}
