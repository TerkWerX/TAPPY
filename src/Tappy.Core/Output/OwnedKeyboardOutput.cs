namespace Tappy.Core.Output;

/// <summary>
/// Serializes physical keyboard transitions and retains ownership until a release succeeds.
/// The same instance must be shared by direct mappings and the action scheduler.
/// </summary>
public sealed class OwnedKeyboardOutput(IKeyboardOutput sink) : IKeyboardOutput
{
    private sealed class Owner(KeyboardOutputRequest request)
    {
        internal KeyboardOutputRequest Request { get; } = request;
        internal List<KeyboardOutputKey> Keys { get; } = [];
    }

    private readonly IKeyboardOutput _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    private readonly object _gate = new();
    private readonly Dictionary<string, Owner> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<KeyboardOutputKey, int> _counts = [];

    public void KeyDown(KeyboardOutputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwnerId);
        lock (_gate)
        {
            if (!_owners.TryGetValue(request.OwnerId, out var owner))
            {
                owner = new Owner(request);
                _owners.Add(request.OwnerId, owner);
            }

            List<KeyboardOutputKey> down = [];
            foreach (var key in Normalize(request.Keys))
            {
                if (owner.Keys.Contains(key))
                {
                    continue;
                }

                owner.Keys.Add(key);
                var count = _counts.GetValueOrDefault(key);
                _counts[key] = count + 1;
                if (count == 0)
                {
                    down.Add(key);
                }
            }

            // Record before dispatch: a throwing/partial native send may already have pressed keys.
            if (down.Count > 0)
            {
                _sink.KeyDown(request with { Keys = down });
            }
        }
    }

    public void KeyUp(KeyboardOutputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_owners.TryGetValue(request.OwnerId, out var owner))
            {
                ReleaseLocked(owner, request, Normalize(request.Keys));
            }
        }
    }

    public void ReleaseOwner(string ownerId)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(ownerId, out var owner))
            {
                ReleaseLocked(owner, owner.Request, owner.Keys.AsEnumerable().Reverse().ToArray());
            }
        }
    }

    public bool HasOwner(string ownerId)
    {
        lock (_gate)
        {
            return _owners.TryGetValue(ownerId, out var owner) && owner.Keys.Count > 0;
        }
    }

    private void ReleaseLocked(Owner owner, KeyboardOutputRequest request, IEnumerable<KeyboardOutputKey> keys)
    {
        var releasing = keys.Where(owner.Keys.Contains).Distinct().ToArray();
        var up = releasing.Where(key => _counts[key] == 1).ToArray();
        if (up.Length > 0)
        {
            _sink.KeyUp(request with { Keys = up });
        }

        // Keep both the owner and counts intact if the send fails so cleanup can be retried.
        foreach (var key in releasing)
        {
            owner.Keys.Remove(key);
            if (--_counts[key] == 0)
            {
                _counts.Remove(key);
            }
        }

        if (owner.Keys.Count == 0)
        {
            _owners.Remove(request.OwnerId);
        }
    }

    private static IEnumerable<KeyboardOutputKey> Normalize(IEnumerable<KeyboardOutputKey> keys) =>
        keys.Where(key => !key.IsEmpty).Distinct();
}
