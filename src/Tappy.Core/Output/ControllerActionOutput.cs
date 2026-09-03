using Tappy.Core.Profiles;
using Tappy.Core.Input;

namespace Tappy.Core.Output;

public sealed record ControllerActionOutputRequest(
    string OwnerId,
    string ScopeId,
    ControllerActionSequenceSnapshot Sequence,
    ulong InjectionMarker,
    ExecutionAncestry Ancestry);

public sealed record ControllerActionOutputFault(
    string OwnerId,
    string ScopeId,
    string Message,
    Exception Exception);

/// <summary>
/// Non-blocking boundary between raw controller input and the bounded Windows
/// action scheduler. Release calls must synchronously release held output even
/// when a background sequence is still cancelling.
/// </summary>
public interface IControllerActionOutput
{
    event EventHandler<ControllerActionOutputFault>? Faulted;
    bool Start(ControllerActionOutputRequest request);
    bool ReleaseOwner(string ownerId);
    bool ReleaseScope(string scopeId);
    bool ReleaseAll();
}

public sealed class NullControllerActionOutput : IControllerActionOutput
{
    public static NullControllerActionOutput Instance { get; } = new();

    private NullControllerActionOutput()
    {
    }

    public event EventHandler<ControllerActionOutputFault>? Faulted
    {
        add { }
        remove { }
    }

    public bool Start(ControllerActionOutputRequest request) => false;
    public bool ReleaseOwner(string ownerId) => true;
    public bool ReleaseScope(string scopeId) => true;
    public bool ReleaseAll() => true;
}
