namespace Menuki.Actions;

/// <summary>
/// Waits for a fixed number of seconds. Mostly useful as a step inside a
/// sequence, but also works as a standalone menu item.
/// </summary>
public class DelayActionExecutor : IActionExecutor
{
    private readonly double _seconds;

    public DelayActionExecutor(double seconds)
    {
        _seconds = seconds;
    }

    public string? Execute()
    {
        Console.Clear();
        Console.WriteLine($"Waiting {_seconds}s...");
        if (_seconds > 0)
            Thread.Sleep((int)(_seconds * 1000));

        Console.WriteLine("Done.");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);

        return null;
    }
}
