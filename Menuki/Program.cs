using System.Text.Json;
using Menuki.Actions;
using Menuki.Config;
using Menuki.Editor;
using Menuki.Engine;
using Menuki.Onboarding;
using Menuki.Plugins;

namespace Menuki;

class Program
{
    static void Main(string[] args)
    {
        // Non-interactive, agent-facing paths: these emit JSON / speak JSON-RPC and
        // exit without ever touching the interactive TUI.
        if (Headless.HeadlessCli.IsHeadlessCommand(args))
        {
            Environment.Exit(Headless.HeadlessCli.Run(args));
        }

        // `mcp` subcommand: run the Model Context Protocol authoring server over stdio.
        if (args.Length > 0 && args[0] == "mcp")
        {
            Environment.Exit(Mcp.McpEntry.Run(args));
        }

        // Session-scoped background jobs: kill any still running when the app exits.
        // Registered up front so it covers the tour / welcome paths too. Also restore the
        // cursor the menu hides, so exiting (including via `exit` / Ctrl-C) never leaves the
        // terminal without a caret.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { Engine.JobRegistry.StopAll(); Engine.MenuEngine.SetCursorVisible(true); };
        Console.CancelKeyPress += (_, _) => { Engine.JobRegistry.StopAll(); Engine.MenuEngine.SetCursorVisible(true); };

        // Load the user's saved appearance preferences (theme, selection marker) up front.
        Engine.AppSettings.Load();

        // `tour` subcommand: the guided, hands-on feature tour.
        if (args.Length > 0 && args[0] == "tour")
        {
            Tour.TourEntry.Run();
            return;
        }

        var configPath = GetConfigPath(args);

        // No config given: show the welcome screen (tour / open / create / help).
        if (configPath == null)
        {
            WelcomeScreen.Run(new ThemeManager(null, null));
            return;
        }

        MenuConfig config;
        {
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                Environment.Exit(1);
            }

            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<MenuConfig>(json)!;
            if (config == null)
            {
                Console.WriteLine("Failed to parse config file.");
                Environment.Exit(1);
            }
        }

        if (!config.Menus.ContainsKey(config.StartMenu))
        {
            Console.WriteLine($"Start menu '{config.StartMenu}' not found in config.");
            Environment.Exit(1);
        }

        // A config may declare appearance defaults; they apply only where the user
        // has not already made a choice of their own.
        Engine.AppSettings.ApplyConfigDefaults(config.Settings);

        var registry = ActionExecutorFactory.BuildDefault();
        var theme = new ThemeManager(config.Theme, config.Colors);

        Console.Title = config.Title;
        Navigator.Run(config, theme, configPath!, registry);
    }

    private static string? GetConfigPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config")
                return args[i + 1];
        }

        return null;
    }

}
