using Tappy.Core.Models;

namespace Tappy.Core.Tests;

public sealed class LedAnimationModelsTests
{
    [Fact]
    public void NormalizeClampsSpeedMultiplierAndCanvasSize()
    {
        var animation = new LedAnimationDefinition
        {
            SpeedMultiplier = 999,
            CanvasColumns = 0,
            CanvasRows = -3,
        };

        animation.Normalize();

        Assert.Equal(8.0, animation.SpeedMultiplier);
        Assert.Equal(16, animation.CanvasColumns);
        Assert.Equal(4, animation.CanvasRows);
    }

    [Fact]
    public void NormalizeGivesAnEmptyOrWhitespaceNameAFallback()
    {
        var animation = new LedAnimationDefinition { Name = "   " };
        animation.Normalize();
        Assert.Equal("Untitled animation", animation.Name);
    }

    [Fact]
    public void NormalizeTruncatesAnOverlongName()
    {
        var animation = new LedAnimationDefinition { Name = new string('x', 200) };
        animation.Normalize();
        Assert.Equal(80, animation.Name.Length);
    }

    [Fact]
    public void NormalizeDedupesParticipantsByIdentity()
    {
        var animation = new LedAnimationDefinition
        {
            Participants =
            [
                new LedAnimationParticipant { ProviderId = "winmm-midi", PersistentId = "a" },
                new LedAnimationParticipant { ProviderId = "winmm-midi", PersistentId = "a" },
                new LedAnimationParticipant { ProviderId = "winmm-midi", PersistentId = "b" },
                new LedAnimationParticipant { ProviderId = "", PersistentId = "" },
            ],
        };

        animation.Normalize();

        Assert.Equal(2, animation.Participants.Count);
    }

    [Fact]
    public void NormalizeClampsEffectFields()
    {
        var effect = new LedEffectDefinition { SpeedHz = -5, Density = 5, TrailLength = 0, WaveDensity = -1, Colors = [] };
        effect.Normalize();

        Assert.True(effect.SpeedHz > 0);
        Assert.Equal(1.0, effect.Density);
        Assert.Equal(3, effect.TrailLength);
        Assert.True(effect.WaveDensity > 0);
        Assert.Single(effect.Colors);
    }

    [Fact]
    public void NormalizeClampsFrameDuration()
    {
        var frame = new LedAnimationFrame { DurationMs = 0 };
        frame.Normalize();
        Assert.Equal(100, frame.DurationMs);

        var tooSlow = new LedAnimationFrame { DurationMs = 999_999 };
        tooSlow.Normalize();
        Assert.Equal(60_000, tooSlow.DurationMs);
    }

    [Fact]
    public void CloneProducesAnIndependentDeepCopy()
    {
        var original = new LedAnimationDefinition
        {
            Name = "Original",
            Effect = new LedEffectDefinition { Colors = ["Red"] },
            Frames = [new LedAnimationFrame { Cells = { ["0,0"] = "Green" } }],
            Participants = [new LedAnimationParticipant { ProviderId = "p", PersistentId = "id" }],
        };

        var clone = original.Clone();
        clone.Name = "Changed";
        clone.Effect.Colors.Add("Blue");
        clone.Frames[0].Cells["0,0"] = "Purple";
        clone.Participants[0].DisplayName = "Changed device";

        Assert.Equal("Original", original.Name);
        Assert.Single(original.Effect.Colors);
        Assert.Equal("Green", original.Frames[0].Cells["0,0"]);
        Assert.Equal(string.Empty, original.Participants[0].DisplayName);
    }

    [Fact]
    public void LibraryNormalizeDedupesByIdAndClearsADanglingLastOpenId()
    {
        var library = new LedAnimationLibrary
        {
            Animations =
            [
                new LedAnimationDefinition { Id = "a", Name = "First" },
                new LedAnimationDefinition { Id = "a", Name = "Duplicate id, kept last" },
            ],
            LastOpenAnimationId = "does-not-exist",
        };

        library.Normalize();

        Assert.Single(library.Animations);
        Assert.Equal("Duplicate id, kept last", library.Animations[0].Name);
        Assert.Null(library.LastOpenAnimationId);
    }
}
