using Tappy.Core.Input;
using Tappy.Core.Output;
using Tappy.Core.Profiles;

namespace Tappy.Core.Models;

public enum RequestedSourceMode
{
    PassThrough,
    GlobalBlock,
    Exclusive
}

public enum EffectiveSourceMode
{
    PassThrough,
    GlobalBlock,
    Exclusive,
    NeedsAttention
}

public sealed class SourceModeConfiguration
{
    public RequestedSourceMode Requested { get; set; } = RequestedSourceMode.PassThrough;
    public EffectiveSourceMode Effective { get; set; } = EffectiveSourceMode.PassThrough;
    public string Status { get; set; } = "Device-aware pass-through";

    public void Normalize()
    {
        Status = string.IsNullOrWhiteSpace(Status) ? Effective.ToString() : Status.Trim();
    }

    public SourceModeConfiguration Clone() => new()
    {
        Requested = Requested,
        Effective = Effective,
        Status = Status
    };
}

public enum KeyboardActionMode
{
    None,
    Tap,
    HoldUntilRelease
}

public sealed class KeyboardActionDefinition
{
    public KeyboardActionMode Mode { get; set; }
    public List<KeyboardOutputKey> Keys { get; set; } = [];

    public void Normalize()
    {
        Keys ??= [];
        Keys = Keys.Where(key => !key.IsEmpty).Distinct().ToList();
        if (Keys.Count == 0)
        {
            Mode = KeyboardActionMode.None;
        }
    }

    public KeyboardActionDefinition Clone() => new()
    {
        Mode = Mode,
        Keys = Keys is null ? [] : [.. Keys]
    };

    public static KeyboardActionDefinition Tap(params string[] keys) => new()
    {
        Mode = KeyboardActionMode.Tap,
        Keys = keys.Select(key => new KeyboardOutputKey(key)).ToList()
    };

    public static KeyboardActionDefinition Hold(params string[] keys) => new()
    {
        Mode = KeyboardActionMode.HoldUntilRelease,
        Keys = keys.Select(key => new KeyboardOutputKey(key)).ToList()
    };
}

public sealed class ControlBindingDefinition
{
    public ControlId ControlId { get; set; }
    public string Name { get; set; } = "Unassigned";
    public bool Enabled { get; set; } = true;
    public KeyboardActionDefinition PressAction { get; set; } = new();
    public KeyboardActionDefinition ReleaseAction { get; set; } = new();
    public ControllerActionSequenceDefinition PressSequence { get; set; } = new();
    public ControllerActionSequenceDefinition ReleaseSequence { get; set; } = new();

    public void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Unassigned" : Name.Trim();
        PressAction ??= new KeyboardActionDefinition();
        ReleaseAction ??= new KeyboardActionDefinition();
        PressSequence ??= new ControllerActionSequenceDefinition();
        ReleaseSequence ??= new ControllerActionSequenceDefinition();
        PressAction.Normalize();
        ReleaseAction.Normalize();
        PressSequence.Normalize();
        ReleaseSequence.Normalize();
        if (ReleaseAction.Mode == KeyboardActionMode.HoldUntilRelease)
        {
            ReleaseAction.Mode = KeyboardActionMode.Tap;
        }
        ReleaseSequence.Mode = ControllerActionSequenceMode.RunOnce;
    }

    public ControlBindingDefinition Clone() => new()
    {
        ControlId = ControlId,
        Name = Name,
        Enabled = Enabled,
        PressAction = PressAction?.Clone() ?? new KeyboardActionDefinition(),
        ReleaseAction = ReleaseAction?.Clone() ?? new KeyboardActionDefinition(),
        PressSequence = PressSequence?.Clone() ?? new ControllerActionSequenceDefinition(),
        ReleaseSequence = ReleaseSequence?.Clone() ?? new ControllerActionSequenceDefinition()
    };
}

public sealed class InputLayerDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Layer";
    public List<ControlBindingDefinition> Bindings { get; set; } = [];

    public static InputLayerDefinition Create(int index) => new()
    {
        Id = $"layer-{index + 1}",
        Name = $"Layer {index + 1}"
    };

    public void Normalize(int index)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? $"layer-{index + 1}" : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? $"Layer {index + 1}" : Name.Trim();
        Bindings ??= [];
        foreach (var binding in Bindings)
        {
            binding.Normalize();
        }

        Bindings = Bindings
            .Where(binding => !binding.ControlId.IsEmpty)
            .GroupBy(binding => binding.ControlId)
            .Select(group => group.Last())
            .ToList();
    }

    public InputLayerDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Bindings = Bindings?.Select(binding => binding.Clone()).ToList() ?? []
    };
}

public enum ControllerLayoutOrientation
{
    Standard,
    RotatedClockwise,
    RotatedCounterClockwise,
    UpsideDown
}

public enum LayoutControlKind
{
    Key,
    Button,
    Spacer,
    Encoder,
    Axis
}

public sealed class LayoutControlDefinition
{
    public ControlId? ControlId { get; set; }
    public LayoutControlKind Kind { get; set; } = LayoutControlKind.Key;
    public string Label { get; set; } = string.Empty;
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
    public double GapBefore { get; set; }
    public string Cluster { get; set; } = string.Empty;

    public void Normalize()
    {
        Label = Label?.Trim() ?? string.Empty;
        Cluster = Cluster?.Trim() ?? string.Empty;
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 0.25, 20) : 1;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 0.25, 20) : 1;
        GapBefore = double.IsFinite(GapBefore) ? Math.Clamp(GapBefore, 0, 20) : 0;
        if (Kind == LayoutControlKind.Spacer)
        {
            ControlId = null;
        }
    }

    public LayoutControlDefinition Clone() => new()
    {
        ControlId = ControlId,
        Kind = Kind,
        Label = Label,
        Width = Width,
        Height = Height,
        GapBefore = GapBefore,
        Cluster = Cluster
    };
}

public sealed class LayoutRowDefinition
{
    public string Id { get; set; } = string.Empty;
    public List<LayoutControlDefinition> Controls { get; set; } = [];

    public void Normalize(int index)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? $"row-{index + 1}" : Id.Trim();
        Controls ??= [];
        foreach (var control in Controls)
        {
            control.Normalize();
        }
    }

    public LayoutRowDefinition Clone() => new()
    {
        Id = Id,
        Controls = Controls?.Select(control => control.Clone()).ToList() ?? []
    };
}

public sealed class ControllerLayoutDefinition
{
    public string Id { get; set; } = "generated-grid";
    public string Name { get; set; } = "Generated key grid";
    public ControllerLayoutOrientation Orientation { get; set; }
    public List<LayoutRowDefinition> Rows { get; set; } = [];

    public static ControllerLayoutDefinition CreateGrid(IEnumerable<ControlId> controls, int columns = 6)
    {
        columns = Math.Max(1, columns);
        var rows = controls
            .Where(control => !control.IsEmpty)
            .Distinct()
            .Chunk(columns)
            .Select((row, rowIndex) => new LayoutRowDefinition
            {
                Id = $"row-{rowIndex + 1}",
                Controls = row.Select(control => new LayoutControlDefinition
                {
                    ControlId = control,
                    Label = control.Value
                }).ToList()
            })
            .ToList();
        return new ControllerLayoutDefinition { Rows = rows };
    }

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? "generated-grid" : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "Controller layout" : Name.Trim();
        Rows ??= [];
        for (var index = 0; index < Rows.Count; index++)
        {
            Rows[index].Normalize(index);
        }
    }

    public ControllerLayoutDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Orientation = Orientation,
        Rows = Rows?.Select(row => row.Clone()).ToList() ?? []
    };
}

public sealed class ControllerProfile
{
    public string Id { get; set; } = string.Empty;
    public ControllerIdentity Identity { get; set; } = null!;
    public string DisplayName { get; set; } = "Controller";
    public SourceModeConfiguration SourceMode { get; set; } = new();
    public string ActiveLayerId { get; set; } = "layer-1";
    public List<InputLayerDefinition> Layers { get; set; } = [];
    public ControllerLayoutDefinition Layout { get; set; } = new();

    public static ControllerProfile Create(
        ControllerIdentity identity,
        IEnumerable<ControlId>? controls = null,
        int defaultLayerCount = 3)
    {
        ArgumentNullException.ThrowIfNull(identity);
        defaultLayerCount = Math.Max(1, defaultLayerCount);
        var controlArray = controls?.Where(control => !control.IsEmpty).Distinct().ToArray() ?? [];
        var stableId = identity.PersistentId?.Value ?? identity.SessionId.Value;
        var result = new ControllerProfile
        {
            Id = stableId,
            Identity = identity,
            DisplayName = identity.DisplayName,
            Layers = Enumerable.Range(0, defaultLayerCount).Select(InputLayerDefinition.Create).ToList(),
            Layout = ControllerLayoutDefinition.CreateGrid(controlArray)
        };
        result.Normalize();
        return result;
    }

    public void Normalize()
    {
        if (Identity is null)
        {
            throw new InvalidDataException("Every controller profile requires an identity.");
        }

        Id = string.IsNullOrWhiteSpace(Id)
            ? Identity.PersistentId?.Value ?? Identity.SessionId.Value
            : Id.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Identity.DisplayName : DisplayName.Trim();
        SourceMode ??= new SourceModeConfiguration();
        SourceMode.Normalize();
        Layers ??= [];
        if (Layers.Count == 0)
        {
            Layers.AddRange(Enumerable.Range(0, 3).Select(InputLayerDefinition.Create));
        }

        for (var index = 0; index < Layers.Count; index++)
        {
            Layers[index].Normalize(index);
        }

        Layers = Layers
            .GroupBy(layer => layer.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        if (Layers.All(layer => !layer.Id.Equals(ActiveLayerId, StringComparison.OrdinalIgnoreCase)))
        {
            ActiveLayerId = Layers[0].Id;
        }

        Layout ??= new ControllerLayoutDefinition();
        Layout.Normalize();
    }

    public ControllerProfile Clone()
    {
        if (Identity is null)
        {
            throw new InvalidDataException("Every controller profile requires an identity.");
        }

        return new ControllerProfile
        {
            Id = Id,
            Identity = new ControllerIdentity(Identity.SessionId, Identity.PersistentId, Identity.Confidence,
                Identity.DisplayName, Identity.ProviderId, Identity.VendorId, Identity.ProductId,
                Identity.UsagePage, Identity.Usage),
            DisplayName = DisplayName,
            SourceMode = SourceMode?.Clone() ?? new SourceModeConfiguration(),
            ActiveLayerId = ActiveLayerId,
            Layers = Layers?.Select(layer => layer.Clone()).ToList() ?? [],
            Layout = Layout?.Clone() ?? new ControllerLayoutDefinition()
        };
    }
}

public sealed class TappyProfile
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Name { get; set; } = "Default";
    public List<ControllerProfile> Controllers { get; set; } = [];

    public void Normalize()
    {
        if (SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Profile schema {SchemaVersion} is newer than supported schema {CurrentSchemaVersion}.");
        }

        SchemaVersion = CurrentSchemaVersion;
        Name = string.IsNullOrWhiteSpace(Name) ? "Default" : Name.Trim();
        Controllers ??= [];
        foreach (var controller in Controllers)
        {
            controller.Normalize();
        }

        Controllers = Controllers
            .GroupBy(controller => controller.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
    }

    public TappyProfileSnapshot CreateSnapshot() => TappyProfileSnapshot.Create(this);
}
