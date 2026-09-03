using Tappy.Core.Output;

namespace Tappy.Core.Safety;

/// <summary>
/// Reference-counts held output keys by physical execution owner. A shared key is
/// released only after its final owner releases it.
/// </summary>
public sealed class HeldOutputLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<KeyboardOutputKey, int>> _owners =
        new(StringComparer.Ordinal);
    private readonly Dictionary<KeyboardOutputKey, int> _globalCounts = [];

    public HeldOutputDelta Acquire(string ownerId, IEnumerable<KeyboardOutputKey> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var normalized = Normalize(keys);
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerId, out var owner))
            {
                owner = [];
                _owners.Add(ownerId, owner);
            }

            List<KeyboardOutputKey> pressed = [];
            foreach (var key in normalized)
            {
                Increment(owner, key);
                if (Increment(_globalCounts, key) == 1)
                {
                    pressed.Add(key);
                }
            }

            return new HeldOutputDelta(pressed, []);
        }
    }

    public HeldOutputDelta Release(string ownerId, IEnumerable<KeyboardOutputKey> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var normalized = Normalize(keys);
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerId, out var owner))
            {
                return HeldOutputDelta.Empty;
            }

            List<KeyboardOutputKey> released = [];
            foreach (var key in normalized)
            {
                if (TryDecrement(owner, key) && TryDecrement(_globalCounts, key, out var finalOwner) && finalOwner)
                {
                    released.Add(key);
                }
            }

            if (owner.Count == 0)
            {
                _owners.Remove(ownerId);
            }

            return new HeldOutputDelta([], released);
        }
    }

    public HeldOutputDelta ReleaseOwner(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        lock (_gate)
        {
            if (!_owners.Remove(ownerId, out var owner))
            {
                return HeldOutputDelta.Empty;
            }

            List<KeyboardOutputKey> released = [];
            foreach (var pair in owner)
            {
                for (var index = 0; index < pair.Value; index++)
                {
                    if (TryDecrement(_globalCounts, pair.Key, out var finalOwner) && finalOwner)
                    {
                        released.Add(pair.Key);
                    }
                }
            }

            return new HeldOutputDelta([], released);
        }
    }

    public HeldOutputDelta ReleaseAll()
    {
        lock (_gate)
        {
            if (_globalCounts.Count == 0)
            {
                return HeldOutputDelta.Empty;
            }

            var released = _globalCounts.Keys.ToArray();
            _owners.Clear();
            _globalCounts.Clear();
            return new HeldOutputDelta([], released);
        }
    }

    public IReadOnlyList<KeyboardOutputKey> GetHeldKeys()
    {
        lock (_gate)
        {
            return _globalCounts.Keys.OrderBy(key => key.Value, StringComparer.Ordinal).ToArray();
        }
    }

    private static KeyboardOutputKey[] Normalize(IEnumerable<KeyboardOutputKey> keys) =>
        keys.Where(key => !key.IsEmpty).Distinct().ToArray();

    private static int Increment(IDictionary<KeyboardOutputKey, int> counts, KeyboardOutputKey key)
    {
        counts.TryGetValue(key, out var count);
        count++;
        counts[key] = count;
        return count;
    }

    private static bool TryDecrement(IDictionary<KeyboardOutputKey, int> counts, KeyboardOutputKey key) =>
        TryDecrement(counts, key, out _);

    private static bool TryDecrement(
        IDictionary<KeyboardOutputKey, int> counts,
        KeyboardOutputKey key,
        out bool releasedLast)
    {
        releasedLast = false;
        if (!counts.TryGetValue(key, out var count) || count <= 0)
        {
            return false;
        }

        if (count == 1)
        {
            counts.Remove(key);
            releasedLast = true;
        }
        else
        {
            counts[key] = count - 1;
        }

        return true;
    }
}

public sealed record HeldOutputDelta(
    IReadOnlyList<KeyboardOutputKey> KeysDown,
    IReadOnlyList<KeyboardOutputKey> KeysUp)
{
    public static HeldOutputDelta Empty { get; } = new([], []);
    public bool IsEmpty => KeysDown.Count == 0 && KeysUp.Count == 0;
}
