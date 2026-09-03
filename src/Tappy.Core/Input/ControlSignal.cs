namespace Tappy.Core.Input;

public enum ControlSignalKind
{
    Press,
    Release,
    Repeat
}

public sealed record InputInjectionMetadata(bool IsInjected, ulong ExtraInfo, string? Source = null)
{
    public static InputInjectionMetadata Physical { get; } = new(false, 0);

    public static InputInjectionMetadata Injected(ulong extraInfo, string? source = null) =>
        new(true, extraInfo, source);
}

public sealed class ExecutionAncestry
{
    private readonly string[] _nodes;

    public ExecutionAncestry(string rootId, IEnumerable<string>? nodes = null)
    {
        RootId = string.IsNullOrWhiteSpace(rootId)
            ? throw new ArgumentException("An execution root id is required.", nameof(rootId))
            : rootId.Trim();
        _nodes = nodes?
            .Where(node => !string.IsNullOrWhiteSpace(node))
            .Select(node => node.Trim())
            .ToArray() ?? [];
        Nodes = Array.AsReadOnly(_nodes);
    }

    public string RootId { get; }
    public IReadOnlyList<string> Nodes { get; }
    public int Depth => _nodes.Length;

    public bool Contains(string node) =>
        _nodes.Contains(node, StringComparer.OrdinalIgnoreCase);

    public ExecutionAncestry Append(string node)
    {
        if (string.IsNullOrWhiteSpace(node))
        {
            throw new ArgumentException("An ancestry node is required.", nameof(node));
        }

        return new ExecutionAncestry(RootId, [.. _nodes, node.Trim()]);
    }
}

public sealed record ControlSignal(
    ControllerSessionId? ControllerSessionId,
    ControlId ControlId,
    ControlSignalKind Kind,
    long Timestamp,
    InputInjectionMetadata Injection,
    ExecutionAncestry? Ancestry = null)
{
    public static ControlSignal Physical(
        ControllerSessionId sessionId,
        ControlId controlId,
        ControlSignalKind kind,
        long timestamp) =>
        new(sessionId, controlId, kind, timestamp, InputInjectionMetadata.Physical);
}
