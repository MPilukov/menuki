using Menuki.Engine;

namespace Menuki.Actions;

/// <summary>
/// Interactive manager for background jobs: list them, tail a job's log, kill a
/// job, or clear finished ones. Line-based command loop.
/// </summary>
public class JobsActionExecutor : IActionExecutor
{
    public string? Execute()
    {
        while (true)
        {
            Console.Clear();
            Render();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Commands:  <n> view log   k<n> kill   c clear finished   Enter refresh   q back");
            Console.ResetColor();
            Console.Write("> ");

            var line = (Console.ReadLine() ?? "").Trim();

            if (line.Length == 0)
                continue;
            if (line is "q" or "Q" or "x" or "X")
                return null;
            if (line is "c" or "C")
            {
                JobRegistry.RemoveFinished();
                continue;
            }
            if ((line[0] is 'k' or 'K') && int.TryParse(line[1..].Trim(), out var killId))
            {
                JobRegistry.Stop(killId);
                continue;
            }
            if (int.TryParse(line, out var viewId))
                ViewLog(viewId);
        }
    }

    private static void Render()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Background Jobs");
        Console.ResetColor();
        Console.WriteLine();

        var jobs = JobRegistry.Jobs;
        if (jobs.Count == 0)
        {
            Console.WriteLine("  (no background jobs)");
            return;
        }

        foreach (var j in jobs)
        {
            var status = j.IsRunning ? "running" : $"exited({j.ExitCode})";
            var uptime = FormatUptime(DateTime.Now - j.StartedAt);
            Console.WriteLine($"  [{j.Id}] {Pad(j.Name, 16)} {Pad(status, 14)} up {Pad(uptime, 8)} {Truncate(j.Command, 40)}");
        }
    }

    private static void ViewLog(int id)
    {
        Console.Clear();
        var job = JobRegistry.Find(id);

        if (job == null)
        {
            Console.WriteLine($"No job with id {id}.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Log tail - [{job.Id}] {job.Name}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(job.LogPath);
            Console.ResetColor();
            Console.WriteLine();

            foreach (var l in Tail(job.LogPath, 40))
                Console.WriteLine(l);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press Enter to go back...");
        Console.ResetColor();
        Console.ReadLine();
    }

    private static IEnumerable<string> Tail(string path, int n)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var lines = sr.ReadToEnd().Split('\n');
            return lines.Length <= n ? lines : lines[^n..];
        }
        catch (Exception ex)
        {
            return new[] { $"(cannot read log: {ex.Message})" };
        }
    }

    private static string FormatUptime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:D2}m"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m{t.Seconds:D2}s"
        : $"{(int)t.TotalSeconds}s";

    private static string Pad(string s, int width) => s.Length >= width ? s : s.PadRight(width);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
