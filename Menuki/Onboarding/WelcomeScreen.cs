using Menuki.Actions;
using Menuki.Config;
using Menuki.Engine;
using Menuki.Tour;

namespace Menuki.Onboarding;

/// <summary>
/// The first screen when `menuki` is run with no arguments: a friendly choice
/// between the tour, opening a config, creating one, and CLI help - instead of
/// requiring the user to already know a --config path.
/// </summary>
public static class WelcomeScreen
{
    public static void Run(ThemeManager theme)
    {
        while (true)
        {
            var items = new List<MenuItem>
            {
                Item("Take the interactive tour", "tour", "Guided, hands-on sections covering every feature."),
                Item("Browse example packs", "examples", "Run a bundled example (git, docker, kubernetes, and more)."),
                Item("Open an existing config", "open", "Type a path to a .json menu config and run it."),
                Item("Create a new menu", "create", "Scaffold your own config with the wizard."),
                Item("Settings", "settings", "Theme and appearance (also T anywhere to cycle themes)."),
                Item("Command-line help", "help", "Show the available commands and flags."),
                Item("Quit", "quit", null),
            };

            var info = new Dictionary<string, string>
            {
                ["Menuki"] = "Interactive menus for humans, scripts and AI agents.",
                ["Tip"] = "New here? Start with the tour.",
            };

            var engine = new MenuEngine("Welcome to Menuki", items, theme, info, isRoot: true, editable: false);
            engine.WriteMenu(selectedItemIndex: 0);

            switch (engine.RunMenu())
            {
                case "tour":
                    TourEntry.Run(theme);
                    break;
                case "examples":
                    Examples.ExamplesScreen.Run(theme);
                    break;
                case "open":
                    OpenExisting(theme);
                    break;
                case "create":
                    var result = OnboardingWizard.Run(theme);
                    if (result != null)
                        Navigator.Run(result.Value.config, theme, result.Value.configPath, ActionExecutorFactory.BuildDefault());
                    break;
                case "settings":
                    SettingsScreen.Run(theme);
                    break;
                case "help":
                    ShowHelp();
                    break;
                default: // "quit" or Esc
                    return;
            }
        }
    }

    private static MenuItem Item(string name, string tag, string? desc) =>
        new(name, new TagActionExecutor(tag), desc);

    private static void OpenExisting(ThemeManager theme)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Path to a config (.json): ");
        Console.ResetColor();

        var path = LineEditor.ReadLine().Trim();
        if (path.Length == 0)
            return;

        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = path.Length <= 1 ? home : Path.Combine(home, path[2..]);
        }

        if (!File.Exists(path))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not found: {path}");
            Console.ResetColor();
            Console.WriteLine("Press any key...");
            Console.ReadKey(intercept: true);
            return;
        }

        MenuConfig? config;
        try
        {
            config = System.Text.Json.JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Could not parse: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Press any key...");
            Console.ReadKey(intercept: true);
            return;
        }

        if (config == null || !config.Menus.ContainsKey(config.StartMenu))
        {
            Console.WriteLine("Config has no valid start menu. Press any key...");
            Console.ReadKey(intercept: true);
            return;
        }

        var configTheme = new ThemeManager(config.Theme, config.Colors);
        Navigator.Run(config, configTheme, Path.GetFullPath(path), ActionExecutorFactory.BuildDefault());
    }

    private static void ShowHelp()
    {
        Console.Clear();
        Console.WriteLine("""
        Menuki - interactive menus for humans, scripts and AI agents.

        Interactive:
          menuki                         Welcome screen (this)
          menuki tour                    Guided, hands-on feature tour
          menuki --config <path>         Open a config in the interactive TUI

        Headless (JSON in, JSON out):
          menuki list --config <path>              List runnable action ids
          menuki exec --config <path> --action <id> [--param k=v ...]
          menuki validate --config <path>          Check a config; JSON errors/warnings

        AI authoring:
          menuki mcp [--dir <configs>]   Run the MCP server over stdio

        In a menu: Up/Down move · Enter select · ? info · type to search ·
                   T theme · E/Q/V/R edit · Esc back.
        """);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\nPress any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);
    }
}
