namespace Tappy.Core.Input;

public sealed record ControlStateUpdate(
    bool Accepted,
    ControllerSessionId? ControllerSessionId,
    ControlId ControlId,
    ControlSignalKind EffectiveKind,
    bool IsRepeat,
    IReadOnlyList<ControlId> PressedControls);

public sealed class ControllerInputStateTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<ControllerSessionId, HashSet<ControlId>> _pressedByController = [];

    public ControlStateUpdate Apply(ControlSignal signal)
    {
        if (signal.ControllerSessionId is not { } sessionId || sessionId.IsEmpty || signal.ControlId.IsEmpty)
        {
            return new ControlStateUpdate(false, signal.ControllerSessionId, signal.ControlId,
                signal.Kind, signal.Kind == ControlSignalKind.Repeat, []);
        }

        lock (_gate)
        {
            if (!_pressedByController.TryGetValue(sessionId, out var pressed))
            {
                pressed = [];
                _pressedByController.Add(sessionId, pressed);
            }

            var accepted = true;
            var effectiveKind = signal.Kind;
            switch (signal.Kind)
            {
                case ControlSignalKind.Press:
                    if (!pressed.Add(signal.ControlId))
                    {
                        effectiveKind = ControlSignalKind.Repeat;
                    }
                    break;
                case ControlSignalKind.Repeat:
                    accepted = pressed.Contains(signal.ControlId);
                    break;
                case ControlSignalKind.Release:
                    accepted = pressed.Remove(signal.ControlId);
                    break;
                default:
                    accepted = false;
                    break;
            }

            if (pressed.Count == 0)
            {
                _pressedByController.Remove(sessionId);
            }

            return new ControlStateUpdate(accepted, sessionId, signal.ControlId, effectiveKind,
                effectiveKind == ControlSignalKind.Repeat, Snapshot(pressed));
        }
    }

    public IReadOnlyList<ControlId> GetPressedControls(ControllerSessionId sessionId)
    {
        lock (_gate)
        {
            return _pressedByController.TryGetValue(sessionId, out var pressed)
                ? Snapshot(pressed)
                : [];
        }
    }

    public bool IsPressed(ControllerSessionId sessionId, ControlId controlId)
    {
        lock (_gate)
        {
            return _pressedByController.TryGetValue(sessionId, out var pressed) && pressed.Contains(controlId);
        }
    }

    public IReadOnlyList<ControlId> Disconnect(ControllerSessionId sessionId)
    {
        lock (_gate)
        {
            return _pressedByController.Remove(sessionId, out var pressed) ? Snapshot(pressed) : [];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pressedByController.Clear();
        }
    }

    private static IReadOnlyList<ControlId> Snapshot(IEnumerable<ControlId> controls) =>
        controls.OrderBy(control => control.Value, StringComparer.Ordinal).ToArray();
}
