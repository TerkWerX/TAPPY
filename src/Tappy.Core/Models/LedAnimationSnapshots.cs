namespace Tappy.Core.Models;

public sealed class LedEffectSnapshot
{
    private readonly string[] _colors;

    internal LedEffectSnapshot(LedEffectDefinition source)
    {
        Kind = source.Kind;
        _colors = [.. source.Colors];
        Colors = Array.AsReadOnly(_colors);
        SpeedHz = source.SpeedHz;
        Density = source.Density;
        TrailLength = source.TrailLength;
        WaveDensity = source.WaveDensity;
    }

    public LedEffectKind Kind { get; }
    public IReadOnlyList<string> Colors { get; }
    public double SpeedHz { get; }
    public double Density { get; }
    public int TrailLength { get; }
    public double WaveDensity { get; }

    internal LedEffectDefinition ToEditable() => new()
    {
        Kind = Kind,
        Colors = [.. _colors],
        SpeedHz = SpeedHz,
        Density = Density,
        TrailLength = TrailLength,
        WaveDensity = WaveDensity,
    };
}

public sealed class LedAnimationFrameSnapshot
{
    private readonly Dictionary<string, string> _cells;

    internal LedAnimationFrameSnapshot(LedAnimationFrame source)
    {
        DurationMs = source.DurationMs;
        _cells = new Dictionary<string, string>(source.Cells, StringComparer.Ordinal);
        Cells = _cells;
    }

    public int DurationMs { get; }
    public IReadOnlyDictionary<string, string> Cells { get; }

    internal LedAnimationFrame ToEditable() => new()
    {
        DurationMs = DurationMs,
        Cells = new Dictionary<string, string>(_cells, StringComparer.Ordinal),
    };
}

public sealed class LedAnimationParticipantSnapshot
{
    internal LedAnimationParticipantSnapshot(LedAnimationParticipant source)
    {
        ProviderId = source.ProviderId;
        PersistentId = source.PersistentId;
        DisplayName = source.DisplayName;
        CanvasOffsetColumn = source.CanvasOffsetColumn;
        CanvasOffsetRow = source.CanvasOffsetRow;
    }

    public string ProviderId { get; }
    public string PersistentId { get; }
    public string DisplayName { get; }
    public int CanvasOffsetColumn { get; }
    public int CanvasOffsetRow { get; }

    internal LedAnimationParticipant ToEditable() => new()
    {
        ProviderId = ProviderId,
        PersistentId = PersistentId,
        DisplayName = DisplayName,
        CanvasOffsetColumn = CanvasOffsetColumn,
        CanvasOffsetRow = CanvasOffsetRow,
    };
}

public sealed class LedAnimationSnapshot
{
    private readonly LedAnimationFrameSnapshot[] _frames;
    private readonly LedAnimationParticipantSnapshot[] _participants;

    internal LedAnimationSnapshot(LedAnimationDefinition source)
    {
        Id = source.Id;
        Name = source.Name;
        Kind = source.Kind;
        Effect = new LedEffectSnapshot(source.Effect);
        _frames = [.. source.Frames.Select(f => new LedAnimationFrameSnapshot(f))];
        Frames = Array.AsReadOnly(_frames);
        Loop = source.Loop;
        SpeedMultiplier = source.SpeedMultiplier;
        CanvasColumns = source.CanvasColumns;
        CanvasRows = source.CanvasRows;
        _participants = [.. source.Participants.Select(p => new LedAnimationParticipantSnapshot(p))];
        Participants = Array.AsReadOnly(_participants);
        ModifiedUtc = source.ModifiedUtc;
    }

    public string Id { get; }
    public string Name { get; }
    public LedAnimationKind Kind { get; }
    public LedEffectSnapshot Effect { get; }
    public IReadOnlyList<LedAnimationFrameSnapshot> Frames { get; }
    public bool Loop { get; }
    public double SpeedMultiplier { get; }
    public int CanvasColumns { get; }
    public int CanvasRows { get; }
    public IReadOnlyList<LedAnimationParticipantSnapshot> Participants { get; }
    public DateTimeOffset ModifiedUtc { get; }

    internal LedAnimationDefinition ToEditable() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        Effect = Effect.ToEditable(),
        Frames = [.. _frames.Select(f => f.ToEditable())],
        Loop = Loop,
        SpeedMultiplier = SpeedMultiplier,
        CanvasColumns = CanvasColumns,
        CanvasRows = CanvasRows,
        Participants = [.. _participants.Select(p => p.ToEditable())],
        ModifiedUtc = ModifiedUtc,
    };
}

public sealed class LedAnimationLibrarySnapshot
{
    private readonly LedAnimationSnapshot[] _animations;

    public LedAnimationLibrarySnapshot(LedAnimationLibrary source)
    {
        var normalized = source.Clone();
        normalized.Normalize();
        _animations = [.. normalized.Animations.Select(a => new LedAnimationSnapshot(a))];
        Animations = Array.AsReadOnly(_animations);
        LastOpenAnimationId = normalized.LastOpenAnimationId;
    }

    public static LedAnimationLibrarySnapshot Empty { get; } = new(new LedAnimationLibrary());

    public IReadOnlyList<LedAnimationSnapshot> Animations { get; }
    public string? LastOpenAnimationId { get; }

    public LedAnimationLibrary ToEditableProfile() => new()
    {
        Animations = [.. _animations.Select(a => a.ToEditable())],
        LastOpenAnimationId = LastOpenAnimationId,
    };
}
