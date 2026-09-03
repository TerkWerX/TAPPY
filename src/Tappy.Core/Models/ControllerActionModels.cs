using Tappy.Core.Output;

namespace Tappy.Core.Models;

/// <summary>
/// Describes how a controller action sequence is tied to the physical control.
/// RunOnce survives the physical release; the held modes are cancelled and
/// cleaned up when the physical control is released.
/// </summary>
public enum ControllerActionSequenceMode
{
    RunOnce,
    WhileHeld,
    RepeatWhileHeld
}

public enum ControllerActionStepType
{
    KeyboardChord,
    KeyDown,
    KeyUp,
    Text,
    Delay,
    MouseButton,
    MouseMove,
    MouseWheel,
    LaunchProgram,
    PowerShellCommand,
    Midi,
    Osc
}

/// <summary>
/// Platform-neutral action step. Fields have type-specific meanings so profile
/// files remain readable and new step kinds can be added without executable
/// script embedded in the profile format.
/// </summary>
public sealed class ControllerActionStepDefinition
{
    public ControllerActionStepType Type { get; set; }
    public List<KeyboardOutputKey> Keys { get; set; } = [];
    public string Value { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public int DurationMs { get; set; } = 25;
    public int Amount { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public void Normalize()
    {
        Keys ??= [];
        Keys = Keys.Where(key => !key.IsEmpty).Distinct().Take(8).ToList();
        Value ??= string.Empty;
        Arguments ??= string.Empty;
        Target = Target?.Trim() ?? string.Empty;
        WorkingDirectory = WorkingDirectory?.Trim() ?? string.Empty;
        DurationMs = Math.Clamp(DurationMs, 1, 600_000);
        Amount = Math.Clamp(Amount, -1_000_000, 1_000_000);
        X = Math.Clamp(X, -100_000, 100_000);
        Y = Math.Clamp(Y, -100_000, 100_000);
    }

    public ControllerActionStepDefinition Clone() => new()
    {
        Type = Type,
        Keys = Keys is null ? [] : [.. Keys],
        Value = Value,
        Arguments = Arguments,
        Target = Target,
        WorkingDirectory = WorkingDirectory,
        DurationMs = DurationMs,
        Amount = Amount,
        X = X,
        Y = Y
    };

    public string ToSummary() => Type switch
    {
        ControllerActionStepType.KeyboardChord => $"Tap {string.Join(" + ", Keys.Select(key => key.Value))}",
        ControllerActionStepType.KeyDown => $"Key down: {string.Join(" + ", Keys.Select(key => key.Value))}",
        ControllerActionStepType.KeyUp => $"Key up: {string.Join(" + ", Keys.Select(key => key.Value))}",
        ControllerActionStepType.Text => $"Type text ({Value.Length:N0} characters)",
        ControllerActionStepType.Delay => $"Wait {DurationMs:N0} ms",
        ControllerActionStepType.MouseButton => $"Mouse: {Value}",
        ControllerActionStepType.MouseMove => $"Move mouse {X}, {Y}",
        ControllerActionStepType.MouseWheel => $"{(Value.Equals("horizontal", StringComparison.OrdinalIgnoreCase) ? "Horizontal" : "Vertical")} scroll {Amount}",
        ControllerActionStepType.LaunchProgram => $"Launch {Path.GetFileName(Value)}",
        ControllerActionStepType.PowerShellCommand => "Run PowerShell command",
        ControllerActionStepType.Midi => $"MIDI {Value}",
        ControllerActionStepType.Osc => $"OSC {Value} → {Target}:{Amount}",
        _ => Type.ToString()
    };
}

public sealed class ControllerActionSequenceDefinition
{
    public string Name { get; set; } = string.Empty;
    public ControllerActionSequenceMode Mode { get; set; } = ControllerActionSequenceMode.RunOnce;
    public List<ControllerActionStepDefinition> Steps { get; set; } = [];

    public bool IsEmpty => Steps is null || Steps.Count == 0;

    public void Normalize()
    {
        Name = Name?.Trim() ?? string.Empty;
        Steps ??= [];
        Steps = Steps.Where(step => step is not null).Take(500).ToList();
        foreach (var step in Steps)
        {
            step.Normalize();
        }
    }

    public ControllerActionSequenceDefinition Clone() => new()
    {
        Name = Name,
        Mode = Mode,
        Steps = Steps?.Select(step => step.Clone()).ToList() ?? []
    };

    public static ControllerActionSequenceDefinition Once(
        string name,
        params ControllerActionStepDefinition[] steps) => new()
        {
            Name = name,
            Mode = ControllerActionSequenceMode.RunOnce,
            Steps = [.. steps]
        };
}
