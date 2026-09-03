using Tappy.Windows.Input;

namespace Tappy.Windows.Diagnostics;

public sealed record ControllerDiagnosticSnapshot(
    string PersistentDeviceId,
    long PressCount,
    long ReleaseCount,
    long RepeatCount,
    int CurrentlyHeldCount,
    long DisconnectCount);

/// <summary>
/// Aggregate-only diagnostics. It intentionally stores no key identities, ordering,
/// typed text, raw paths, macro content, or per-event timestamps.
/// </summary>
public sealed class InputDiagnosticAggregate
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MutableCounts> _counts = new(StringComparer.Ordinal);

    public void Observe(NormalizedKeyboardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (_gate)
        {
            var counts = GetOrAdd(input.PersistentDeviceId);
            if (input.Transition == KeyTransition.Release)
            {
                counts.ReleaseCount++;
                counts.CurrentlyHeldCount = Math.Max(0, counts.CurrentlyHeldCount - 1);
            }
            else if (input.IsRepeat)
            {
                counts.RepeatCount++;
            }
            else
            {
                counts.PressCount++;
                counts.CurrentlyHeldCount++;
            }
        }
    }

    public void ObserveDisconnect(string persistentDeviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistentDeviceId);
        lock (_gate)
        {
            var counts = GetOrAdd(persistentDeviceId);
            counts.DisconnectCount++;
            counts.CurrentlyHeldCount = 0;
        }
    }

    public IReadOnlyList<ControllerDiagnosticSnapshot> Snapshot()
    {
        lock (_gate)
        {
            return _counts
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ControllerDiagnosticSnapshot(
                    pair.Key,
                    pair.Value.PressCount,
                    pair.Value.ReleaseCount,
                    pair.Value.RepeatCount,
                    pair.Value.CurrentlyHeldCount,
                    pair.Value.DisconnectCount))
                .ToArray();
        }
    }

    private MutableCounts GetOrAdd(string persistentDeviceId)
    {
        if (!_counts.TryGetValue(persistentDeviceId, out var counts))
        {
            counts = new MutableCounts();
            _counts.Add(persistentDeviceId, counts);
        }

        return counts;
    }

    private sealed class MutableCounts
    {
        internal long PressCount;
        internal long ReleaseCount;
        internal long RepeatCount;
        internal int CurrentlyHeldCount;
        internal long DisconnectCount;
    }
}
