using Tappy.App.Services;
using Tappy.Core.Models;

namespace Tappy.App.Tests;

public sealed class AnimationLibraryStoreTests
{
    [Fact]
    public void SaveThenLoadRoundTripsExactly()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var store = new AnimationLibraryStore(root);
            var library = new LedAnimationLibrary
            {
                Animations = [new LedAnimationDefinition { Name = "Rainbow" }],
                LastOpenAnimationId = null,
            };
            library.Normalize();
            library.LastOpenAnimationId = library.Animations[0].Id;
            store.Save(new LedAnimationLibrarySnapshot(library));

            var loaded = store.Load();

            var animation = Assert.Single(loaded.Animations);
            Assert.Equal("Rainbow", animation.Name);
            Assert.Equal(animation.Id, loaded.LastOpenAnimationId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadWithNoFilesYetReturnsAnEmptyLibrary()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var store = new AnimationLibraryStore(root);
            Assert.Empty(store.Load().Animations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ACorruptedPrimaryFileFallsBackToTheLastKnownGoodCopy()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var store = new AnimationLibraryStore(root);
            var goodLibrary = new LedAnimationLibrary { Animations = [new LedAnimationDefinition { Name = "Good save" }] };
            goodLibrary.Normalize();
            store.Save(new LedAnimationLibrarySnapshot(goodLibrary));

            // A second save creates the .lkg backup of the first (now-good) save,
            // then this save's own primary file is corrupted after the fact —
            // simulating a crash/interrupted write or on-disk corruption.
            var secondLibrary = new LedAnimationLibrary { Animations = [new LedAnimationDefinition { Name = "Second save" }] };
            secondLibrary.Normalize();
            store.Save(new LedAnimationLibrarySnapshot(secondLibrary));

            var primaryPath = Path.Combine(root, "animation-library.json");
            File.WriteAllText(primaryPath, "{ this is not valid json");

            var loaded = store.Load();

            var animation = Assert.Single(loaded.Animations);
            Assert.Equal("Good save", animation.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BothFilesCorruptOrMissingReturnsAnEmptyLibraryInsteadOfThrowing()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var store = new AnimationLibraryStore(root);
            File.WriteAllText(Path.Combine(root, "animation-library.json"), "not json at all");

            var loaded = store.Load();

            Assert.Empty(loaded.Animations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Tappy-AppTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
