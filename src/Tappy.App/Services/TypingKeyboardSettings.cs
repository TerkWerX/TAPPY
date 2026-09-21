using System.Text.Json;

namespace Tappy.App.Services;

internal sealed record TypingKeyboardSettings(string? PersistentId, string? Name)
{
    [System.Text.Json.Serialization.JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    internal static TypingKeyboardSettings Load(string path)
    {
        if (!File.Exists(path)) { return new(null, null); }
        var settings = JsonSerializer.Deserialize<TypingKeyboardSettings>(File.ReadAllText(path))
            ?? throw new JsonException("Empty typing-keyboard settings.");
        if (settings.SchemaVersion != 1 || settings.PersistentId is not null && (string.IsNullOrWhiteSpace(settings.PersistentId) ||
            !settings.PersistentId.StartsWith("raw-keyboard:", StringComparison.Ordinal)))
        { throw new JsonException("Invalid typing-keyboard identity."); }
        return settings;
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
