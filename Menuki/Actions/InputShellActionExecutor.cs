using Menuki.Config;
using Menuki.Engine;

namespace Menuki.Actions;

public class InputShellActionExecutor : IActionExecutor
{
    private readonly List<InputDefinition> _inputs;
    private readonly string _commandTemplate;

    public InputShellActionExecutor(List<InputDefinition> inputs, string commandTemplate)
    {
        _inputs = inputs;
        _commandTemplate = commandTemplate;
    }

    public string? Execute()
    {
        Console.Clear();

        var history = InputHistory.Load();
        var values = new Dictionary<string, string>();
        foreach (var input in _inputs)
        {
            var value = InputValidator.EffectiveType(input) == InputTypes.Choice
                ? SelectChoice(input)
                : PromptValue(input, history);
            values[input.Name] = value;
        }

        var secretNames = _inputs.Where(i => i.Secret).Select(i => i.Name).ToHashSet();
        var command = ShellEscaper.Interpolate(_commandTemplate, values);
        var display = ShellEscaper.InterpolateForDisplay(_commandTemplate, values, secretNames);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n> {display}");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();

        ShellRunner.RunInteractive(command);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ForegroundColor = ConsoleColor.White;
        Console.ReadKey(intercept: true);

        return null;
    }

    /// <summary>
    /// Read a value, re-prompting until it passes the input's type validation.
    /// Uses the shared history so Up/Down recalls previously entered values.
    /// </summary>
    private static string PromptValue(InputDefinition input, List<string> history)
    {
        var hint = InputValidator.PromptHint(input);
        // A secret must never be shown as a default; and its history is not recalled.
        var defaultHint = !input.Secret && input.Default != null ? $" [{input.Default}]" : "";

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{input.Prompt}{hint}{defaultHint}: ");
            Console.ForegroundColor = ConsoleColor.White;

            // Secrets are masked while typing and never offered history recall.
            var line = input.Secret
                ? LineEditor.ReadLine(history: null, mask: true)
                : LineEditor.ReadLine(history);
            var result = InputValidator.Resolve(input, line);
            if (result.Ok)
            {
                if (!input.Secret)
                    InputHistory.Remember(history, line);
                return result.Value;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            if (result.Allowed is { Length: > 0 } allowed)
                Console.WriteLine($"  Allowed: {string.Join(", ", allowed)}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

    /// <summary>Arrow-select one of the declared options for a choice input.</summary>
    private static string SelectChoice(InputDefinition input)
    {
        var options = input.Options ?? new List<string>();
        if (options.Count == 0)
            return input.Default ?? "";

        var index = input.Default != null ? Math.Max(0, options.IndexOf(input.Default)) : 0;

        var top = Console.CursorTop;
        while (true)
        {
            Console.SetCursorPosition(0, top);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(input.Prompt);
            for (var i = 0; i < options.Count; i++)
            {
                if (i == index)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  > {options[i]}    ");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"    {options[i]}    ");
                }
            }

            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.UpArrow:
                    index = index > 0 ? index - 1 : options.Count - 1;
                    break;
                case ConsoleKey.DownArrow:
                    index = index < options.Count - 1 ? index + 1 : 0;
                    break;
                case ConsoleKey.Enter:
                    Console.ForegroundColor = ConsoleColor.White;
                    return options[index];
            }
        }
    }
}
