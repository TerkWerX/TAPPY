using Tappy.App.Runtime;

namespace Tappy.App.ViewModels;

internal sealed class VisualUpdateBuffer
{
    private const int DefaultCapacity = 4096;
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Queue<SequencedUpdate> _transitions = new();
    private readonly Dictionary<string, SequencedUpdate> _latestByControl = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SequencedUpdate> _latestPressByControl = new(StringComparer.Ordinal);
    private long _nextSequence;
    private bool _isCompacted;

    public VisualUpdateBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public void Enqueue(RuntimeControlUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            var sequenced = new SequencedUpdate(++_nextSequence, update);
            _latestByControl[update.ControlId] = sequenced;
            if (update.IsPressed && !update.IsRepeat)
            {
                _latestPressByControl[update.ControlId] = sequenced;
            }

            if (_isCompacted)
            {
                return;
            }

            if (_transitions.Count < _capacity)
            {
                _transitions.Enqueue(sequenced);
                return;
            }

            // Presentation must be bounded even if the UI thread is stalled. Once
            // the FIFO fills, retain each control's newest state and newest press
            // edge so releases cannot leave a stale tile and quick taps still pulse.
            _transitions.Clear();
            _isCompacted = true;
        }
    }

    public VisualUpdateBatch Drain()
    {
        lock (_gate)
        {
            RuntimeControlUpdate[] updates;
            if (_isCompacted)
            {
                var compacted = new List<SequencedUpdate>(_latestByControl.Count * 2);
                foreach (var pair in _latestByControl)
                {
                    if (_latestPressByControl.TryGetValue(pair.Key, out var press) &&
                        press.Sequence < pair.Value.Sequence)
                    {
                        compacted.Add(press);
                    }

                    compacted.Add(pair.Value);
                }

                updates = compacted
                    .OrderBy(item => item.Sequence)
                    .Select(item => item.Update)
                    .ToArray();
            }
            else
            {
                updates = _transitions.Select(item => item.Update).ToArray();
            }

            var wasCompacted = _isCompacted;
            _transitions.Clear();
            _latestByControl.Clear();
            _latestPressByControl.Clear();
            _isCompacted = false;
            return new VisualUpdateBatch(updates, wasCompacted);
        }
    }

    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _isCompacted ? _latestByControl.Count > 0 : _transitions.Count > 0;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _transitions.Clear();
            _latestByControl.Clear();
            _latestPressByControl.Clear();
            _isCompacted = false;
        }
    }

    private sealed record SequencedUpdate(long Sequence, RuntimeControlUpdate Update);
}

internal sealed record VisualUpdateBatch(
    IReadOnlyList<RuntimeControlUpdate> Updates,
    bool WasCompacted);
