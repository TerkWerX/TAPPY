namespace Tappy.Core.Input;

public enum ControllerActivationState
{
    Idle,
    WaitingForNeutral,
    AwaitingIdentificationPress,
    AwaitingIdentificationRelease,
    AwaitingConfirmation,
    Active
}

public sealed class ControllerActivationGate
{
    private readonly object _gate = new();
    private readonly ControllerInputStateTracker _inputStates;
    private readonly HashSet<ControllerSessionId> _active = [];
    private ControllerSessionId? _candidate;
    private ControlId? _identificationControl;
    private ControllerActivationState _state;

    public ControllerActivationGate(ControllerInputStateTracker inputStates)
    {
        _inputStates = inputStates ?? throw new ArgumentNullException(nameof(inputStates));
    }

    public ControllerActivationState State
    {
        get { lock (_gate) return _state; }
    }

    public ControllerSessionId? Candidate
    {
        get { lock (_gate) return _candidate; }
    }

    public IReadOnlyList<ControllerSessionId> ActiveControllers
    {
        get
        {
            lock (_gate)
            {
                return _active.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
            }
        }
    }

    public ControllerActivationState SelectCandidate(ControllerSessionId sessionId)
    {
        if (sessionId.IsEmpty)
        {
            throw new ArgumentException("A controller session id is required.", nameof(sessionId));
        }

        lock (_gate)
        {
            _candidate = sessionId;
            _identificationControl = null;
            _state = _inputStates.GetPressedControls(sessionId).Count == 0
                ? ControllerActivationState.AwaitingIdentificationPress
                : ControllerActivationState.WaitingForNeutral;
            return _state;
        }
    }

    public ControllerActivationState Observe(ControlStateUpdate update)
    {
        if (!update.Accepted || update.ControllerSessionId is not { } sessionId)
        {
            return State;
        }

        lock (_gate)
        {
            if (_candidate is not { } candidate || candidate != sessionId)
            {
                return _state;
            }

            if (_state == ControllerActivationState.WaitingForNeutral && update.PressedControls.Count == 0)
            {
                _state = ControllerActivationState.AwaitingIdentificationPress;
                return _state;
            }

            if (_state == ControllerActivationState.AwaitingIdentificationPress &&
                update.EffectiveKind == ControlSignalKind.Press)
            {
                _identificationControl = update.ControlId;
                _state = ControllerActivationState.AwaitingIdentificationRelease;
                return _state;
            }

            if (_state == ControllerActivationState.AwaitingIdentificationRelease &&
                update.EffectiveKind == ControlSignalKind.Release &&
                _identificationControl is { } identificationControl &&
                !_inputStates.IsPressed(candidate, identificationControl) &&
                update.PressedControls.Count == 0)
            {
                _state = ControllerActivationState.AwaitingConfirmation;
            }

            return _state;
        }
    }

    public void Confirm()
    {
        lock (_gate)
        {
            if (_state != ControllerActivationState.AwaitingConfirmation || _candidate is not { } candidate)
            {
                throw new InvalidOperationException("Select, identify, and release the controller before confirming it.");
            }

            _active.Add(candidate);
            _candidate = null;
            _identificationControl = null;
            _state = ControllerActivationState.Active;
        }
    }

    public bool IsActive(ControllerSessionId sessionId)
    {
        lock (_gate)
        {
            return _active.Contains(sessionId);
        }
    }

    public void CancelCandidate()
    {
        lock (_gate)
        {
            _candidate = null;
            _identificationControl = null;
            _state = _active.Count == 0 ? ControllerActivationState.Idle : ControllerActivationState.Active;
        }
    }

    public void Deactivate(ControllerSessionId sessionId)
    {
        lock (_gate)
        {
            _active.Remove(sessionId);
            if (_candidate == sessionId)
            {
                _candidate = null;
                _identificationControl = null;
            }

            _state = _candidate is not null
                ? _state
                : _active.Count == 0 ? ControllerActivationState.Idle : ControllerActivationState.Active;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _active.Clear();
            _candidate = null;
            _identificationControl = null;
            _state = ControllerActivationState.Idle;
        }
    }
}
