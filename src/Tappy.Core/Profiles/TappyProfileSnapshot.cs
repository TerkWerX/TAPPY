using Tappy.Core.Input;
using Tappy.Core.Models;
using Tappy.Core.Output;

namespace Tappy.Core.Profiles;

public sealed record SourceModeSnapshot(
    RequestedSourceMode Requested,
    EffectiveSourceMode Effective,
    string Status);

public sealed class KeyboardActionSnapshot
{
    private readonly KeyboardOutputKey[] _keys;

    internal KeyboardActionSnapshot(KeyboardActionDefinition source)
    {
        Mode = source.Mode;
        _keys = [.. source.Keys];
        Keys = Array.AsReadOnly(_keys);
    }

    public KeyboardActionMode Mode { get; }
    public IReadOnlyList<KeyboardOutputKey> Keys { get; }

    internal KeyboardActionDefinition ToEditable() => new()
    {
        Mode = Mode,
        Keys = [.. _keys]
    };
}

public sealed class ControlBindingSnapshot
{
    internal ControlBindingSnapshot(ControlBindingDefinition source)
    {
        ControlId = source.ControlId;
        Name = source.Name;
        Enabled = source.Enabled;
        PressAction = new KeyboardActionSnapshot(source.PressAction);
        ReleaseAction = new KeyboardActionSnapshot(source.ReleaseAction);
    }

    public ControlId ControlId { get; }
    public string Name { get; }
    public bool Enabled { get; }
    public KeyboardActionSnapshot PressAction { get; }
    public KeyboardActionSnapshot ReleaseAction { get; }

    internal ControlBindingDefinition ToEditable() => new()
    {
        ControlId = ControlId,
        Name = Name,
        Enabled = Enabled,
        PressAction = PressAction.ToEditable(),
        ReleaseAction = ReleaseAction.ToEditable()
    };
}

public sealed class InputLayerSnapshot
{
    private readonly ControlBindingSnapshot[] _bindings;
    private readonly IReadOnlyDictionary<ControlId, ControlBindingSnapshot> _bindingLookup;

    internal InputLayerSnapshot(InputLayerDefinition source)
    {
        Id = source.Id;
        Name = source.Name;
        _bindings = source.Bindings.Select(binding => new ControlBindingSnapshot(binding)).ToArray();
        Bindings = Array.AsReadOnly(_bindings);
        _bindingLookup = _bindings.ToDictionary(binding => binding.ControlId);
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<ControlBindingSnapshot> Bindings { get; }

    public ControlBindingSnapshot? FindBinding(ControlId controlId) =>
        _bindingLookup.GetValueOrDefault(controlId);

    internal InputLayerDefinition ToEditable() => new()
    {
        Id = Id,
        Name = Name,
        Bindings = _bindings.Select(binding => binding.ToEditable()).ToList()
    };
}

public sealed record LayoutControlSnapshot(
    ControlId? ControlId,
    LayoutControlKind Kind,
    string Label,
    double Width,
    double Height,
    double GapBefore,
    string Cluster)
{
    internal LayoutControlDefinition ToEditable() => new()
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

public sealed class LayoutRowSnapshot
{
    private readonly LayoutControlSnapshot[] _controls;

    internal LayoutRowSnapshot(LayoutRowDefinition source)
    {
        Id = source.Id;
        _controls = source.Controls.Select(control => new LayoutControlSnapshot(
            control.ControlId, control.Kind, control.Label, control.Width, control.Height,
            control.GapBefore, control.Cluster)).ToArray();
        Controls = Array.AsReadOnly(_controls);
    }

    public string Id { get; }
    public IReadOnlyList<LayoutControlSnapshot> Controls { get; }

    internal LayoutRowDefinition ToEditable() => new()
    {
        Id = Id,
        Controls = _controls.Select(control => control.ToEditable()).ToList()
    };
}

public sealed class ControllerLayoutSnapshot
{
    private readonly LayoutRowSnapshot[] _rows;

    internal ControllerLayoutSnapshot(ControllerLayoutDefinition source)
    {
        Id = source.Id;
        Name = source.Name;
        Orientation = source.Orientation;
        _rows = source.Rows.Select(row => new LayoutRowSnapshot(row)).ToArray();
        Rows = Array.AsReadOnly(_rows);
    }

    public string Id { get; }
    public string Name { get; }
    public ControllerLayoutOrientation Orientation { get; }
    public IReadOnlyList<LayoutRowSnapshot> Rows { get; }

    internal ControllerLayoutDefinition ToEditable() => new()
    {
        Id = Id,
        Name = Name,
        Orientation = Orientation,
        Rows = _rows.Select(row => row.ToEditable()).ToList()
    };
}

public sealed class ControllerProfileSnapshot
{
    private readonly InputLayerSnapshot[] _layers;
    private readonly IReadOnlyDictionary<string, InputLayerSnapshot> _layerLookup;

    internal ControllerProfileSnapshot(ControllerProfile source)
    {
        Id = source.Id;
        Identity = new ControllerIdentity(source.Identity.SessionId, source.Identity.PersistentId,
            source.Identity.Confidence, source.Identity.DisplayName, source.Identity.ProviderId,
            source.Identity.VendorId, source.Identity.ProductId, source.Identity.UsagePage,
            source.Identity.Usage);
        DisplayName = source.DisplayName;
        SourceMode = new SourceModeSnapshot(source.SourceMode.Requested, source.SourceMode.Effective,
            source.SourceMode.Status);
        ActiveLayerId = source.ActiveLayerId;
        _layers = source.Layers.Select(layer => new InputLayerSnapshot(layer)).ToArray();
        Layers = Array.AsReadOnly(_layers);
        _layerLookup = _layers.ToDictionary(layer => layer.Id, StringComparer.OrdinalIgnoreCase);
        Layout = new ControllerLayoutSnapshot(source.Layout);
    }

    public string Id { get; }
    public ControllerIdentity Identity { get; }
    public string DisplayName { get; }
    public SourceModeSnapshot SourceMode { get; }
    public string ActiveLayerId { get; }
    public IReadOnlyList<InputLayerSnapshot> Layers { get; }
    public ControllerLayoutSnapshot Layout { get; }

    public InputLayerSnapshot? FindLayer(string layerId) =>
        string.IsNullOrWhiteSpace(layerId) ? null : _layerLookup.GetValueOrDefault(layerId);

    internal ControllerProfile ToEditable() => new()
    {
        Id = Id,
        Identity = new ControllerIdentity(Identity.SessionId, Identity.PersistentId,
            Identity.Confidence, Identity.DisplayName, Identity.ProviderId, Identity.VendorId,
            Identity.ProductId, Identity.UsagePage, Identity.Usage),
        DisplayName = DisplayName,
        SourceMode = new SourceModeConfiguration
        {
            Requested = SourceMode.Requested,
            Effective = SourceMode.Effective,
            Status = SourceMode.Status
        },
        ActiveLayerId = ActiveLayerId,
        Layers = _layers.Select(layer => layer.ToEditable()).ToList(),
        Layout = Layout.ToEditable()
    };
}

public sealed class TappyProfileSnapshot
{
    private readonly ControllerProfileSnapshot[] _controllers;
    private readonly IReadOnlyDictionary<string, ControllerProfileSnapshot> _controllerLookup;

    private TappyProfileSnapshot(TappyProfile normalizedCopy)
    {
        SchemaVersion = normalizedCopy.SchemaVersion;
        Name = normalizedCopy.Name;
        _controllers = normalizedCopy.Controllers
            .Select(controller => new ControllerProfileSnapshot(controller))
            .ToArray();
        Controllers = Array.AsReadOnly(_controllers);
        _controllerLookup = _controllers.ToDictionary(controller => controller.Id, StringComparer.OrdinalIgnoreCase);
    }

    public int SchemaVersion { get; }
    public string Name { get; }
    public IReadOnlyList<ControllerProfileSnapshot> Controllers { get; }

    public static TappyProfileSnapshot Create(TappyProfile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new TappyProfile
        {
            SchemaVersion = source.SchemaVersion,
            Name = source.Name,
            Controllers = source.Controllers?.Select(controller => controller.Clone()).ToList() ?? []
        };
        copy.Normalize();
        return new TappyProfileSnapshot(copy);
    }

    public ControllerProfileSnapshot? FindController(string id) =>
        string.IsNullOrWhiteSpace(id) ? null : _controllerLookup.GetValueOrDefault(id);

    public ControllerProfileSnapshot? FindController(ControllerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.PersistentId is { } persistentId)
        {
            var persistent = _controllers.FirstOrDefault(controller =>
                controller.Identity.PersistentId is { } candidate &&
                candidate.Value.Equals(persistentId.Value, StringComparison.OrdinalIgnoreCase));
            if (persistent is not null)
            {
                return persistent;
            }
        }

        return _controllers.FirstOrDefault(controller =>
            controller.Identity.SessionId == identity.SessionId);
    }

    public TappyProfile ToEditableProfile() => new()
    {
        SchemaVersion = SchemaVersion,
        Name = Name,
        Controllers = _controllers.Select(controller => controller.ToEditable()).ToList()
    };
}
