using Tappy.Core.Input;

namespace Tappy.Core.Output;

public readonly record struct KeyboardOutputKey
{
    public KeyboardOutputKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A keyboard output key is required.", nameof(value));
        }

        Value = Normalize(value);
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;

    private static string Normalize(string value) => value.Trim().ToUpperInvariant() switch
    {
        "CONTROL" => "CTRL",
        "RETURN" => "ENTER",
        "ESC" => "ESCAPE",
        "WINDOWS" => "WIN",
        var normalized => normalized
    };
}

public sealed record KeyboardOutputRequest(
    string OwnerId,
    IReadOnlyList<KeyboardOutputKey> Keys,
    ulong InjectionMarker,
    ExecutionAncestry Ancestry);

public interface IKeyboardOutput
{
    void KeyDown(KeyboardOutputRequest request);
    void KeyUp(KeyboardOutputRequest request);
}
