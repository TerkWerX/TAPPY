using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Tappy.Core.Abstractions;
using Tappy.Core.Models;
using Tappy.Core.Profiles;

namespace Tappy.Windows.Profiles;

public enum ProfileRecoveryState
{
    Primary,
    LastKnownGood,
}

public sealed record ProfileLoadResult(
    TappyProfileSnapshot Snapshot,
    ProfileRecoveryState RecoveryState,
    string? QuarantinedFileName);

/// <summary>
/// Stores immutable Core snapshots using same-directory atomic replacement. The
/// serialized byte snapshot is created before asynchronous file work begins, so the
/// store never revisits shared mutable profile state while writing.
/// </summary>
public sealed partial class AtomicProfileStore : IProfileStore
{
    public const string DefaultProfileId = "default";
    private readonly string _rootDirectory;
    private readonly string _quarantineDirectory;
    private readonly ProfileSerializer _serializer;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _profileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public AtomicProfileStore(string? rootDirectory = null, ProfileSerializer? serializer = null)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory ?? ProductIdentity.LocalDataRoot);
        _quarantineDirectory = Path.Combine(_rootDirectory, "quarantine");
        _serializer = serializer ?? new ProfileSerializer();
    }

    public string RootDirectory => _rootDirectory;

    public async ValueTask<TappyProfileSnapshot> LoadAsync(
        CancellationToken cancellationToken = default) =>
        await LoadAsync(DefaultProfileId, cancellationToken).ConfigureAwait(false);

    public ValueTask SaveAsync(
        TappyProfileSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        new(SaveAsync(DefaultProfileId, snapshot, cancellationToken));

    public Task SaveAsync(
        string profileId,
        TappyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return SaveAsync(profileId, profile.CreateSnapshot(), cancellationToken);
    }

    public Task SaveAsync(
        string profileId,
        TappyProfileSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var normalizedId = NormalizeProfileId(profileId);
        var serializedSnapshot = Encoding.UTF8.GetBytes(_serializer.Serialize(snapshot));
        return SaveSerializedSnapshotAsync(normalizedId, serializedSnapshot, cancellationToken);
    }

    public async Task<TappyProfileSnapshot> LoadAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        (await LoadWithRecoveryAsync(profileId, cancellationToken).ConfigureAwait(false)).Snapshot;

    public async Task<ProfileLoadResult> LoadWithRecoveryAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = NormalizeProfileId(profileId);
        var profileLock = _profileLocks.GetOrAdd(normalizedId, static _ => new SemaphoreSlim(1, 1));
        await profileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var primaryPath = GetProfilePathCore(normalizedId);
            var lastKnownGoodPath = GetLastKnownGoodPathCore(normalizedId);
            if (!File.Exists(primaryPath))
            {
                if (!File.Exists(lastKnownGoodPath))
                {
                    throw new FileNotFoundException("The Tappy profile does not exist.", primaryPath);
                }

                var recovered = await ReadSnapshotAsync(lastKnownGoodPath, cancellationToken).ConfigureAwait(false);
                return new ProfileLoadResult(recovered, ProfileRecoveryState.LastKnownGood, null);
            }

            try
            {
                var primary = await ReadSnapshotAsync(primaryPath, cancellationToken).ConfigureAwait(false);
                return new ProfileLoadResult(primary, ProfileRecoveryState.Primary, null);
            }
            catch (InvalidDataException)
            {
                var quarantineName = Quarantine(primaryPath, normalizedId, "primary");
                if (!File.Exists(lastKnownGoodPath))
                {
                    throw new InvalidDataException(
                        "The Tappy profile was corrupt, was quarantined, and no last-known-good copy exists.");
                }

                try
                {
                    var recovered = await ReadSnapshotAsync(lastKnownGoodPath, cancellationToken).ConfigureAwait(false);
                    return new ProfileLoadResult(
                        recovered,
                        ProfileRecoveryState.LastKnownGood,
                        quarantineName);
                }
                catch (InvalidDataException exception)
                {
                    _ = Quarantine(lastKnownGoodPath, normalizedId, "last-known-good");
                    throw new InvalidDataException(
                        "Both the Tappy profile and its last-known-good copy were corrupt and were quarantined.",
                        exception);
                }
            }
        }
        finally
        {
            profileLock.Release();
        }
    }

    public string GetProfilePath(string profileId) =>
        GetProfilePathCore(NormalizeProfileId(profileId));

    public string GetLastKnownGoodPath(string profileId) =>
        GetLastKnownGoodPathCore(NormalizeProfileId(profileId));

    private async Task SaveSerializedSnapshotAsync(
        string normalizedId,
        byte[] serializedSnapshot,
        CancellationToken cancellationToken)
    {
        var profileLock = _profileLocks.GetOrAdd(normalizedId, static _ => new SemaphoreSlim(1, 1));
        await profileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_rootDirectory);
            var primaryPath = GetProfilePathCore(normalizedId);
            var lastKnownGoodPath = GetLastKnownGoodPathCore(normalizedId);
            temporaryPath = Path.Combine(
                _rootDirectory,
                $".{normalizedId}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(serializedSnapshot, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(primaryPath))
            {
                File.Replace(temporaryPath, primaryPath, lastKnownGoodPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, primaryPath);
                File.Copy(primaryPath, lastKnownGoodPath, overwrite: true);
            }

            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            profileLock.Release();
        }
    }

    private async Task<TappyProfileSnapshot> ReadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return _serializer.Deserialize(json);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is DecoderFallbackException or
            NotSupportedException or
            ArgumentException or
            FormatException or
            OverflowException)
        {
            throw new InvalidDataException("The Tappy profile could not be decoded.", exception);
        }
    }

    private string Quarantine(string path, string normalizedId, string kind)
    {
        Directory.CreateDirectory(_quarantineDirectory);
        var fileName = $"{normalizedId}.{kind}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.corrupt";
        File.Move(path, Path.Combine(_quarantineDirectory, fileName));
        return fileName;
    }

    private string GetProfilePathCore(string normalizedId) =>
        Path.Combine(_rootDirectory, normalizedId + ProductIdentity.ProfileExtension);

    private string GetLastKnownGoodPathCore(string normalizedId) =>
        GetProfilePathCore(normalizedId) + ".lkg";

    private static string NormalizeProfileId(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var normalized = profileId.EndsWith(ProductIdentity.ProfileExtension, StringComparison.OrdinalIgnoreCase)
            ? profileId[..^ProductIdentity.ProfileExtension.Length]
            : profileId;
        if (!ProfileIdRegex().IsMatch(normalized))
        {
            throw new ArgumentException(
                "A profile id must begin with a letter or digit and contain only letters, digits, '.', '-', or '_'.",
                nameof(profileId));
        }

        return normalized;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();
}
