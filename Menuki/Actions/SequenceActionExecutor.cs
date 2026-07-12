using Menuki.Config;
using Menuki.Engine;

namespace Menuki.Actions;

/// <summary>
/// Runs a list of steps in order as a mini pipeline, streaming each step's output
/// live with ✓/✗ headers and a single pause at the end. Stops on the first failing
/// step when stop_on_error is set.
/// </summary>
public class SequenceActionExecutor : IActionExecutor
{
    private readonly ActionDefinition _action;

    public SequenceActionExecutor(ActionDefinition action)
    {
        _action = action;
    }

    public string? Execute()
    {
        Console.Clear();

        var ok = StepRunner.RunSequenceStreaming(
            _action.Steps ?? new List<ActionDefinition>(), _action.StopOnError,
            _action.OnSuccess, _action.OnFailure);

        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(ok ? "Sequence completed." : "Sequence failed.");
        Console.ResetColor();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);

        return null;
    }
}
