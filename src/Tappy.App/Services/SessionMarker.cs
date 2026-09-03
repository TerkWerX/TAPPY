using System.Text.Json;

namespace Tappy.App.Services;

public sealed class SessionMarker
{
    private readonly string _path;

    public SessionMarker(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "running-session.json");
    }

    public PreviousSession? Begin()
    {
        PreviousSession? previous = null;
        if (File.Exists(_path))
        {
            try
            {
                previous = JsonSerializer.Deserialize<PreviousSession>(File.ReadAllText(_path));
            }
            catch
            {
                previous = new PreviousSession(0, DateTimeOffset.MinValue, "Unreadable recovery marker");
            }
        }

        var current = new PreviousSession(Environment.ProcessId, DateTimeOffset.UtcNow, "0.1.0");
        File.WriteAllText(_path, JsonSerializer.Serialize(current));
        return previous;
    }

    public void Complete()
    {
        try
        {
            File.Delete(_path);
        }
        catch
        {
            // Recovery is conservative: an undeletable marker prompts on next launch.
        }
    }
}

public sealed record PreviousSession(int ProcessId, DateTimeOffset StartedUtc, string Version);
