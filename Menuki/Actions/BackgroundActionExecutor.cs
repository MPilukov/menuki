using Menuki.Engine;

namespace Menuki.Actions;

/// <summary>
/// Starts a command as a background job (non-blocking) and returns to the menu
/// immediately. Output goes to a log file; use a <c>jobs</c> menu item to view or
/// stop running jobs. Jobs are killed when the app exits (session-scoped).
/// </summary>
public class BackgroundActionExecutor : IActionExecutor
{
    private readonly string _command;
    private readonly string _name;

    public BackgroundActionExecutor(string command, string? name)
    {
        _command = command;
        _name = string.IsNullOrWhiteSpace(name) ? FirstWord(command) : name!;
    }

    public string? Execute()
    {
        Console.Clear();

        try
        {
            var job = JobRegistry.Start(_name, _command);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"▶ Started background job '{job.Name}' (pid {job.Pid?.ToString() ?? "?"})");
            Console.ResetColor();
            Console.WriteLine($"  command: {_command}");
            Console.WriteLine($"  log:     {job.LogPath}");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Open a \"Jobs\" menu item to view or stop running jobs.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to start background job: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);

        return null;
    }

    private static string FirstWord(string command)
    {
        var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : "job";
    }
}
