namespace Menuki.Engine;

/// <summary>
/// Persistent, shared history of values the user has typed at input+shell prompts,
/// so <c>Up</c> in the line editor recalls them - like a shell's command history.
/// Ordered most-recent-last. Best-effort: any IO failure is swallowed.
/// </summary>
public static class InputHistory
{
    private const int MaxEntries = 200;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki", "input_history");

    /// <summary>Load the saved history (oldest first, most recent last).</summary>
    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<string>();
            return File.ReadAllLines(FilePath)
                .Where(l => l.Length > 0)
                .TakeLast(MaxEntries)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Record a freshly entered value: move it to the end of <paramref name="history"/>
    /// (most recent) and persist. The same list is reused across prompts in one run, so a
    /// value typed for the first field is immediately recallable at the next one.
    /// </summary>
    public static void Remember(List<string> history, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        history.RemoveAll(v => v == value);
        history.Add(value);
        while (history.Count > MaxEntries)
            history.RemoveAt(0);

        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllLines(FilePath, history);
            HardenPermissions(dir, FilePath);
        }
        catch
        {
            // history is a convenience; never let it break input.
        }
    }

    /// <summary>
    /// Restrict the history to the owner only (dir 0700, file 0600) so no other local
    /// user can read previously entered values. No-op on Windows (POSIX modes only).
    /// </summary>
    private static void HardenPermissions(string dir, string file)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(dir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(file,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
