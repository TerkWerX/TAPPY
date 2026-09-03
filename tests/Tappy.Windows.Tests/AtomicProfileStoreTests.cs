using System.Text;
using Tappy.Core.Models;
using Tappy.Windows.Profiles;

namespace Tappy.Windows.Tests;

public sealed class AtomicProfileStoreTests
{
    [Fact]
    public async Task RoundTripsProfileAndCreatesLastKnownGoodCopy()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicProfileStore(directory.Path);
        var profile = new TappyProfile { Name = "Numpad editing" };

        await store.SaveAsync("main", profile);
        var loaded = await store.LoadWithRecoveryAsync("main");

        Assert.Equal("Numpad editing", loaded.Snapshot.Name);
        Assert.Equal(ProfileRecoveryState.Primary, loaded.RecoveryState);
        Assert.True(File.Exists(store.GetProfilePath("main")));
        Assert.True(File.Exists(store.GetLastKnownGoodPath("main")));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task CoreProfileStoreInterfaceUsesIsolatedDefaultProfile()
    {
        using var directory = new TemporaryDirectory();
        Tappy.Core.Abstractions.IProfileStore store = new AtomicProfileStore(directory.Path);
        var snapshot = new TappyProfile { Name = "Default through Core" }.CreateSnapshot();

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        Assert.Equal("Default through Core", loaded.Name);
        Assert.True(File.Exists(Path.Combine(
            directory.Path,
            AtomicProfileStore.DefaultProfileId + ProductIdentity.ProfileExtension)));
    }

    [Fact]
    public async Task SerializesImmutableSnapshotBeforeReturningSaveTask()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicProfileStore(directory.Path);
        var mutableProfile = new TappyProfile { Name = "Before" };

        var saveTask = store.SaveAsync("snapshot", mutableProfile);
        mutableProfile.Name = "After";
        await saveTask;

        var loaded = await store.LoadAsync("snapshot");
        Assert.Equal("Before", loaded.Name);
    }

    [Fact]
    public async Task CorruptPrimaryIsQuarantinedAndLastKnownGoodIsLoaded()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicProfileStore(directory.Path);
        await store.SaveAsync("main", new TappyProfile { Name = "Known good" });
        await store.SaveAsync("main", new TappyProfile { Name = "New primary" });
        await File.WriteAllTextAsync(store.GetProfilePath("main"), "{ definitely-not-json", Encoding.UTF8);

        var loaded = await store.LoadWithRecoveryAsync("main");

        Assert.Equal(ProfileRecoveryState.LastKnownGood, loaded.RecoveryState);
        Assert.Equal("Known good", loaded.Snapshot.Name);
        Assert.NotNull(loaded.QuarantinedFileName);
        Assert.True(File.Exists(Path.Combine(directory.Path, "quarantine", loaded.QuarantinedFileName)));
        Assert.False(File.Exists(store.GetProfilePath("main")));
    }

    [Fact]
    public async Task ProfileIdsRemainIsolated()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicProfileStore(directory.Path);
        await store.SaveAsync("controller-a", new TappyProfile { Name = "Alpha" });
        await store.SaveAsync("controller-b", new TappyProfile { Name = "Beta" });

        var alpha = await store.LoadAsync("controller-a");
        var beta = await store.LoadAsync("controller-b");

        Assert.Equal("Alpha", alpha.Name);
        Assert.Equal("Beta", beta.Name);
        Assert.NotEqual(store.GetProfilePath("controller-a"), store.GetProfilePath("controller-b"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("C:\\escape")]
    [InlineData("name/child")]
    public async Task RejectsProfilePathTraversal(string unsafeId)
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicProfileStore(directory.Path);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(unsafeId, new TappyProfile { Name = "Unsafe" }));
    }
}
