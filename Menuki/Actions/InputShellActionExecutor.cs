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

        Use(t => t.UseCommand());
        Console.WriteLine($"\n> {display}");
        Console.ResetColor();
        Console.WriteLine();

        ShellRunner.RunInteractive(command);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);

        return null;
    }

    /// <summary>Apply a theme role via the ambient session theme, or reset if there is none.</summary>
    private static void Use(Action<ThemeManager> apply)
    {
        if (ThemeManager.Ambient is { } theme) apply(theme);
        else Console.ResetColor();
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
            Use(t => t.UsePrompt());
            Console.Write($"{input.Prompt}{hint}{defaultHint}: ");
            Use(t => t.UseInput());

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

            Use(t => t.UseError());
            Console.WriteLine(result.Error);
            if (result.Allowed is { Length: > 0 } allowed)
                Console.WriteLine($"  Allowed: {string.Join(", ", allowed)}");
            Console.ResetColor();
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
            Use(t => t.UsePrompt());
            Console.WriteLine(input.Prompt);
            for (var i = 0; i < options.Count; i++)
            {
                if (i == index)
                {
                    Use(t => t.UseOptionSelected());
                    Console.WriteLine($"  > {options[i]}    ");
                }
                else
                {
                    Use(t => t.UseOption());
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
                    Console.ResetColor();
                    return options[index];
            }
        }
    }
}
