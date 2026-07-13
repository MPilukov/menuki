using Menuki.Engine;

namespace Menuki.Actions;

public class ShellActionExecutor : IActionExecutor
{
    private readonly string _command;
    private readonly string? _format;
    private readonly string? _query;

    public ShellActionExecutor(string command, string? format = null, string? query = null)
    {
        _command = command;
        _format = format;
        _query = query;
    }

    public string? Execute()
    {
        // With a format/query, capture the output and show a result screen; otherwise
        // stream live (so interactive commands like ssh/top still work).
        if (ResultFormatter.NeedsFormatting(_format, _query))
            return ExecuteFormatted();

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"> {_command}");
        Console.ResetColor();
        Console.WriteLine();

        ShellRunner.RunInteractive(_command);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);

        return null;
    }

    private string? ExecuteFormatted()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"> {_command}");
        Console.ResetColor();
        Console.WriteLine();

        var (exitCode, stdout, stderr) = ShellRunner.RunCaptured(_command);
        var result = ResultFormatter.Format(stdout, _format, _query);

        Console.WriteLine(result.Text);

        if (!string.IsNullOrEmpty(stderr))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(stderr);
            Console.ResetColor();
        }

        if (result.Warning != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"(note: {result.Warning})");
            Console.ResetColor();
        }

        if (exitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"(exit code {exitCode})");
            Console.ResetColor();
        }

        ResultActions(result.Text);
        return null;
    }

    private static void ResultActions(string text)
    {
        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("[C] Copy   [S] Save to file   [Enter] Back");
            Console.ResetColor();

            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.C:
                    if (Clipboard.Copy(text, out var copyErr))
                        Status("Copied to clipboard.", ConsoleColor.Green);
                    else
                        Status($"Copy failed: {copyErr}", ConsoleColor.Red);
                    break;

                case ConsoleKey.S:
                    SaveToFile(text);
                    break;

                case ConsoleKey.Enter:
                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    private static void SaveToFile(string text)
    {
        Console.Write("Save to path: ");
        var path = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            Status("Save cancelled.", ConsoleColor.DarkGray);
            return;
        }

        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);

        try
        {
            File.WriteAllText(path, text);
            Status($"Saved to {path}", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            Status($"Save failed: {ex.Message}", ConsoleColor.Red);
        }
    }

    private static void Status(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
