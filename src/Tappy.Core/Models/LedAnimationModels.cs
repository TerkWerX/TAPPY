namespace Tappy.Core.Models;

public enum LedAnimationKind
{
    Effect,
    Frames
}

public enum LedEffectKind
{
    RainbowWave,
    ColorChase,
    Breathe,
    Sparkle,
    ColorWipe
}

public sealed class LedEffectDefinition
{
    public LedEffectKind Kind { get; set; } = LedEffectKind.RainbowWave;

    /// <summary>Named palette keys (matching the Fire's starter palette) or "#RRGGBB" hex.</summary>
    public List<string> Colors { get; set; } = ["Red", "Amber", "Yellow", "Green", "Blue", "Purple"];

    public double SpeedHz { get; set; } = 0.5;

    /// <summary>Sparkle: average lit fraction of cells per frame.</summary>
    public double Density { get; set; } = 0.15;

    /// <summary>ColorChase: number of lit cells trailing behind the head.</summary>
    public int TrailLength { get; set; } = 3;

    /// <summary>RainbowWave: spatial hue cycles across the canvas width.</summary>
    public double WaveDensity { get; set; } = 1.0;

    public void Normalize()
    {
        Colors = (Colors ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Take(16).ToList();
        if (Colors.Count == 0) { Colors.Add("Red"); }
        SpeedHz = Math.Clamp(double.IsFinite(SpeedHz) && SpeedHz > 0 ? SpeedHz : 0.5, 0.02, 20);
        Density = Math.Clamp(double.IsFinite(Density) ? Density : 0.15, 0.01, 1.0);
        TrailLength = Math.Clamp(TrailLength <= 0 ? 3 : TrailLength, 1, 32);
        WaveDensity = Math.Clamp(double.IsFinite(WaveDensity) && WaveDensity > 0 ? WaveDensity : 1.0, 0.1, 8.0);
    }

    public LedEffectDefinition Clone() => new()
    {
        Kind = Kind,
        Colors = [.. Colors],
        SpeedHz = SpeedHz,
        Density = Density,
        TrailLength = TrailLength,
        WaveDensity = WaveDensity,
    };
}

public sealed class LedAnimationFrame
{
    public int DurationMs { get; set; } = 100;

    /// <summary>Keyed "{column},{row}" in the animation's shared canvas coordinate space.</summary>
    public Dictionary<string, string> Cells { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        DurationMs = Math.Clamp(DurationMs <= 0 ? 100 : DurationMs, 16, 60_000);
        Cells = new Dictionary<string, string>(Cells ?? [], StringComparer.Ordinal);
    }

    public LedAnimationFrame Clone() => new() { DurationMs = DurationMs, Cells = new(Cells, StringComparer.Ordinal) };
}

public sealed class LedAnimationParticipant
{
    public string ProviderId { get; set; } = string.Empty;
    public string PersistentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int CanvasOffsetColumn { get; set; }
    public int CanvasOffsetRow { get; set; }

    public void Normalize()
    {
        ProviderId = (ProviderId ?? string.Empty).Trim();
        PersistentId = (PersistentId ?? string.Empty).Trim();
        DisplayName = (DisplayName ?? string.Empty).Trim();
        CanvasOffsetColumn = Math.Clamp(CanvasOffsetColumn, 0, 256);
        CanvasOffsetRow = Math.Clamp(CanvasOffsetRow, 0, 256);
    }

    public LedAnimationParticipant Clone() => new()
    {
        ProviderId = ProviderId,
        PersistentId = PersistentId,
        DisplayName = DisplayName,
        CanvasOffsetColumn = CanvasOffsetColumn,
        CanvasOffsetRow = CanvasOffsetRow,
    };
}

public sealed class LedAnimationDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New animation";
    public LedAnimationKind Kind { get; set; } = LedAnimationKind.Effect;
    public LedEffectDefinition Effect { get; set; } = new();
    public List<LedAnimationFrame> Frames { get; set; } = [];
    public bool Loop { get; set; } = true;
    public double SpeedMultiplier { get; set; } = 1.0;
    public int CanvasColumns { get; set; } = 16;
    public int CanvasRows { get; set; } = 4;

    /// <summary>
    /// Which confirmed device(s) render this animation and where their own grid
    /// sits in the shared canvas. A single-device animation still has exactly one
    /// entry (offset 0,0); this field exists from the start so multi-device
    /// playback never needs a breaking format change later.
    /// </summary>
    public List<LedAnimationParticipant> Participants { get; set; } = [];

    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    public void Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        var trimmedName = (Name ?? string.Empty).Trim();
        Name = trimmedName.Length == 0 ? "Untitled animation" : trimmedName[..Math.Min(trimmedName.Length, 80)];
        Effect ??= new();
        Effect.Normalize();
        Frames = (Frames ?? []).Where(f => f is not null).Take(512).ToList();
        foreach (var frame in Frames) { frame.Normalize(); }
        CanvasColumns = Math.Clamp(CanvasColumns <= 0 ? 16 : CanvasColumns, 1, 64);
        CanvasRows = Math.Clamp(CanvasRows <= 0 ? 4 : CanvasRows, 1, 64);
        SpeedMultiplier = Math.Clamp(double.IsFinite(SpeedMultiplier) && SpeedMultiplier > 0 ? SpeedMultiplier : 1.0, 0.1, 8.0);
        Participants = (Participants ?? []).Where(p => p is not null).ToList();
        foreach (var participant in Participants) { participant.Normalize(); }
        Participants = Participants
            .Where(p => !string.IsNullOrEmpty(p.ProviderId) && !string.IsNullOrEmpty(p.PersistentId))
            .DistinctBy(p => (p.ProviderId, p.PersistentId))
            .Take(8)
            .ToList();
    }

    public LedAnimationDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        Effect = Effect.Clone(),
        Frames = Frames.Select(f => f.Clone()).ToList(),
        Loop = Loop,
        SpeedMultiplier = SpeedMultiplier,
        CanvasColumns = CanvasColumns,
        CanvasRows = CanvasRows,
        Participants = Participants.Select(p => p.Clone()).ToList(),
        ModifiedUtc = ModifiedUtc,
    };
}

public sealed class LedAnimationLibrary
{
    public List<LedAnimationDefinition> Animations { get; set; } = [];
    public string? LastOpenAnimationId { get; set; }

    public void Normalize()
    {
        Animations = (Animations ?? []).Where(a => a is not null).ToList();
        foreach (var animation in Animations) { animation.Normalize(); }
        // Keep the last duplicate, matching TappyProfile.Normalize's controller-dedup convention.
        Animations = Animations.GroupBy(a => a.Id, StringComparer.Ordinal).Select(g => g.Last()).Take(200).ToList();
        if (LastOpenAnimationId is not null && Animations.All(a => a.Id != LastOpenAnimationId))
        {
            LastOpenAnimationId = null;
        }
    }

    public LedAnimationLibrary Clone() => new()
    {
        Animations = Animations.Select(a => a.Clone()).ToList(),
        LastOpenAnimationId = LastOpenAnimationId,
    };
}
