using System.Text.Json;

namespace Menuki.Tour;

/// <summary>
/// Tracks which tour sections the user has already opened, so the hub can mark them
/// (✓ / ○) and show a simple "explored" percentage. Persisted; best-effort.
/// </summary>
public static class TourProgress
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki", "tour", "progress.json");

    public static HashSet<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new HashSet<string>();
            var visited = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
            return visited != null ? new HashSet<string>(visited) : new HashSet<string>();
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    public static void Save(HashSet<string> visited)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(visited.ToList()));
        }
        catch
        {
            // progress is a convenience; never let it break the tour.
        }
    }
}
