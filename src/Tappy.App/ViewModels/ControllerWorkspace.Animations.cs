using Tappy.App.Services;
using Tappy.Core.Models;

namespace Tappy.App.ViewModels;

/// <summary>A confirmed tab eligible to participate in a shared LED animation.</summary>
public sealed record LedAnimationTarget(
    ControllerTab Tab, ILedGridAdapter Adapter, string ProviderId, string PersistentId, string DisplayName);

/// <summary>
/// Pure layout logic for the auto-arranged shared canvas: checking a device
/// adds it immediately to the right of every already-checked participant;
/// unchecking removes it and re-flows the rest from scratch. No manual
/// offset fields — the owner asked for checkboxes, not a coordinate editor.
/// A stored participant that no longer resolves against the current targets
/// (e.g. a stale entry loaded from disk while that unit is disconnected) is
/// dropped rather than kept with a stale offset.
/// </summary>
public static class LedAnimationParticipantLayout
{
    public static void Rearrange(LedAnimationDefinition definition, IReadOnlyList<LedAnimationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(targets);
        var resolved = definition.Participants
            .Select(participant => (Participant: participant, Target: targets.FirstOrDefault(target =>
                target.ProviderId == participant.ProviderId && target.PersistentId == participant.PersistentId)))
            .Where(item => item.Target is not null)
            .ToArray();

        definition.Participants = resolved.Select(item => item.Participant).ToList();

        var offsetColumn = 0;
        var maxRows = 0;
        foreach (var (participant, target) in resolved)
        {
            participant.CanvasOffsetColumn = offsetColumn;
            participant.CanvasOffsetRow = 0;
            offsetColumn += target!.Adapter.Columns;
            maxRows = Math.Max(maxRows, target.Adapter.Rows);
        }

        definition.CanvasColumns = Math.Max(1, offsetColumn);
        definition.CanvasRows = Math.Max(1, maxRows);
    }
}

public sealed partial class ControllerWorkspace
{
    /// <summary>
    /// Every confirmed tab with a registered LED grid adapter, safely
    /// deduplicated the same way GetBankTargets() is — a tab is excluded if
    /// more than one currently shares its exact identity. Reuses
    /// GetBankEndpoint() for identity/eligibility (it already gates out
    /// remembered-reconnect-pending, output-safety, and protected-keyboard
    /// states, all reasons a device also should not be animation-eligible)
    /// rather than adding a new runtime-level identity query.
    /// </summary>
    public IReadOnlyList<LedAnimationTarget> GetAnimationTargets() =>
        Tabs.Select(tab => (Tab: tab, Endpoint: tab.Runtime.GetBankEndpoint(), Capability: tab.Runtime.GetControllerLedColorCapability()))
            .Where(item => item.Endpoint is not null && item.Capability is not null)
            .Select(item => (item.Tab, item.Endpoint, Adapter: LedGridAdapterCatalog.Find(item.Capability!.ModelId)))
            .Where(item => item.Adapter is not null)
            .Select(item => new LedAnimationTarget(
                item.Tab, item.Adapter!, item.Endpoint!.ProviderId, item.Endpoint.PersistentId, item.Endpoint.DisplayName))
            .Where(target => Tabs.Count(tab => tab.Runtime.GetBankEndpoint() is { } candidate &&
                candidate.ProviderId == target.ProviderId && candidate.PersistentId == target.PersistentId) == 1)
            .ToArray();
}
