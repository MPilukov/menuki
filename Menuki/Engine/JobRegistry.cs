using System.Diagnostics;

namespace Menuki.Engine;

/// <summary>A background process started from the menu, with its captured log.</summary>
public class BackgroundJob
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string LogPath { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public Process Process { get; init; } = null!;

    public bool IsRunning
    {
        get { try { return !Process.HasExited; } catch { return false; } }
    }

    public int? ExitCode
    {
        get { try { return Process.HasExited ? Process.ExitCode : null; } catch { return null; } }
    }

    public int? Pid
    {
        get { try { return Process.Id; } catch { return null; } }
    }
}

/// <summary>
/// Process-lifetime registry of background jobs. Session-scoped: jobs live while
/// the menu is open and are killed on exit (see <see cref="StopAll"/>, wired to
/// process exit in Program.cs).
/// </summary>
public static class JobRegistry
{
    private static readonly object Lock = new();
    private static readonly List<BackgroundJob> Items = new();
    private static int _counter;

    private static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki", "logs");

    public static BackgroundJob Start(string name, string command)
    {
        Directory.CreateDirectory(LogDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logPath = Path.Combine(LogDir, $"{Sanitize(name)}-{stamp}.log");

        var process = ShellRunner.StartBackground(command, logPath)
                      ?? throw new InvalidOperationException("Failed to start process.");

        lock (Lock)
        {
            var job = new BackgroundJob
            {
                Id = ++_counter,
                Name = name,
                Command = command,
                LogPath = logPath,
                StartedAt = DateTime.Now,
                Process = process
            };
            Items.Add(job);
            return job;
        }
    }

    public static IReadOnlyList<BackgroundJob> Jobs
    {
        get { lock (Lock) return Items.ToList(); }
    }

    public static BackgroundJob? Find(int id)
    {
        lock (Lock) return Items.FirstOrDefault(j => j.Id == id);
    }

    public static void Stop(int id)
    {
        if (Find(id) is { } job)
            TryKill(job);
    }

    public static void StopAll()
    {
        foreach (var job in Jobs)
            TryKill(job);
    }

    public static void RemoveFinished()
    {
        lock (Lock) Items.RemoveAll(j => !j.IsRunning);
    }

    private static void TryKill(BackgroundJob job)
    {
        try
        {
            if (!job.Process.HasExited)
                job.Process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone or not killable - nothing to do.
        }
    }

    private static string Sanitize(string name)
    {
        var s = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrEmpty(s) ? "job" : s;
    }
}
