using Menuki.Engine;

namespace Menuki.Editor;

public static class PromptHelper
{
    public static string PromptString(string label, string currentValue, ThemeManager theme)
    {
        Console.Clear();
        Console.ForegroundColor = theme.Title;
        Console.WriteLine($"  Edit: {label}");
        Console.WriteLine();

        Console.ForegroundColor = theme.InfoLabel;
        Console.Write("  Current value: ");
        Console.ForegroundColor = theme.InfoValue;
        Console.WriteLine(currentValue);
        Console.WriteLine();

        Console.ForegroundColor = theme.Text;
        Console.Write("  New value (Enter to keep current): ");
        var input = Console.ReadLine();

        return string.IsNullOrEmpty(input) ? currentValue : input;
    }

    public static string? PromptOptionalString(string label, string? currentValue, ThemeManager theme)
    {
        Console.Clear();
        Console.ForegroundColor = theme.Title;
        Console.WriteLine($"  Edit: {label}");
        Console.WriteLine();

        Console.ForegroundColor = theme.InfoLabel;
        Console.Write("  Current value: ");
        Console.ForegroundColor = theme.InfoValue;
        Console.WriteLine(currentValue ?? "(empty)");
        Console.WriteLine();

        Console.ForegroundColor = theme.Text;
        Console.Write("  New value (Enter to keep, '-' to clear): ");
        var input = Console.ReadLine();

        if (input == "-") return null;
        return string.IsNullOrEmpty(input) ? currentValue : input;
    }

    public static int? PromptChoice(string label, string[] choices, int currentIndex, ThemeManager theme)
    {
        var selected = currentIndex;

        void Render()
        {
            Console.Clear();
            Console.ForegroundColor = theme.Title;
            Console.WriteLine($"  {label}");
            Console.WriteLine();

            for (var i = 0; i < choices.Length; i++)
            {
                var marker = i == selected ? ">" : " ";
                var suffix = i == currentIndex ? " (current)" : "";

                if (i == selected)
                {
                    Console.ForegroundColor = theme.Selected;
                    Console.WriteLine($"  {marker} [{i + 1}] {choices[i]}{suffix}");
                }
                else
                {
                    Console.ForegroundColor = theme.Text;
                    Console.WriteLine($"  {marker} [{i + 1}] {choices[i]}{suffix}");
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = theme.InfoBorder;
            Console.WriteLine("  Up/Down + Enter to select  |  Number key  |  Esc = cancel");
            Console.ForegroundColor = theme.Text;
        }

        Render();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Escape)
                return null;

            if (key.Key == ConsoleKey.UpArrow)
            {
                selected = selected > 0 ? selected - 1 : choices.Length - 1;
                Render();
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                selected = selected < choices.Length - 1 ? selected + 1 : 0;
                Render();
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                return selected;
            }
            else if (key.KeyChar >= '1' && key.KeyChar <= '9')
            {
                var num = key.KeyChar - '0';
                if (num >= 1 && num <= choices.Length)
                    return num - 1;
            }
        }
    }

    public static bool PromptConfirm(string message, ThemeManager theme)
    {
        Console.ForegroundColor = theme.Message;
        Console.Write($"  {message} (y/n): ");
        Console.ForegroundColor = theme.Text;
        var input = Console.ReadLine();
        return input?.Trim().ToLower() == "y";
    }

    public static string? PromptFilePath(string label, ThemeManager theme)
    {
        Console.Clear();
        Console.ForegroundColor = theme.Title;
        Console.WriteLine($"  {label}");
        Console.WriteLine();

        Console.ForegroundColor = theme.Text;
        Console.Write("  File path: ");
        var path = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(path))
            return null;

        if (!File.Exists(path))
        {
            Console.ForegroundColor = theme.Message;
            Console.WriteLine($"  File not found: {path}");
            Console.ForegroundColor = theme.Text;
            Console.WriteLine("  Press any key...");
            Console.ReadKey(intercept: true);
            return null;
        }

        return path;
    }

    public static void ShowMessage(string message, ThemeManager theme)
    {
        Console.ForegroundColor = theme.Message;
        Console.WriteLine($"  {message}");
        Console.ForegroundColor = theme.Text;
        Console.WriteLine("  Press any key...");
        Console.ReadKey(intercept: true);
    }
}
