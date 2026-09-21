using System.Text.Json;
using System.Text.Json.Serialization;
using Tappy.Core.Models;

namespace Tappy.App.Services;

/// <summary>
/// Persists the shared, cross-device-tab LED animation library. Deliberately not
/// built on AtomicProfileStore (that class is a proven, critical path for the
/// main per-controller profile and should not be risked for an unrelated new
/// document type) nor on WindowPlacementStore's weaker "reset to empty on any
/// load error" behavior (a hand-built animation library is worth a stronger
/// guarantee than a window position). Follows the same discipline
/// AtomicProfileStore uses at a much smaller scale: write to a temp file, keep
/// the previous good copy as a ".lkg" backup, then atomically replace.
/// </summary>
public sealed class AnimationLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private readonly string _path;
    private readonly string _lkgPath;
    private readonly object _gate = new();

    public AnimationLibraryStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "animation-library.json");
        _lkgPath = _path + ".lkg";
    }

    public LedAnimationLibrarySnapshot Load()
    {
        lock (_gate)
        {
            if (TryReadFile(_path, out var fromPrimary))
            {
                return fromPrimary;
            }

            if (TryReadFile(_lkgPath, out var fromBackup))
            {
                return fromBackup;
            }

            return LedAnimationLibrarySnapshot.Empty;
        }
    }

    public void Save(LedAnimationLibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(snapshot.ToEditableProfile(), JsonOptions);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(_path))
            {
                File.Copy(_path, _lkgPath, overwrite: true);
            }

            File.Move(temp, _path, overwrite: true);
        }
    }

    private static bool TryReadFile(string path, out LedAnimationLibrarySnapshot snapshot)
    {
        snapshot = LedAnimationLibrarySnapshot.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var library = JsonSerializer.Deserialize<LedAnimationLibrary>(json, JsonOptions) ?? new LedAnimationLibrary();
            library.Normalize();
            snapshot = new LedAnimationLibrarySnapshot(library);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
