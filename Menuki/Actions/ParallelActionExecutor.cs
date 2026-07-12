using Menuki.Config;
using Menuki.Engine;

namespace Menuki.Actions;

/// <summary>
/// Runs steps concurrently and shows their output once they all finish
/// (buffered - so live output from different steps doesn't interleave).
/// </summary>
public class ParallelActionExecutor : IActionExecutor
{
    private readonly ActionDefinition _action;

    public ParallelActionExecutor(ActionDefinition action)
    {
        _action = action;
    }

    public string? Execute()
    {
        Console.Clear();

        var count = _action.Steps?.Count ?? 0;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"▶ Running {count} step(s) in parallel - output shown when all finish...");
        Console.ResetColor();
        Console.WriteLine();

        var result = StepRunner.RunCaptured(_action);
        StepRunner.PrintBuffered(result);

        Console.ForegroundColor = result.Ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(result.Ok ? "All steps succeeded." : "Some steps failed.");
        Console.ResetColor();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);

        return null;
    }
}
