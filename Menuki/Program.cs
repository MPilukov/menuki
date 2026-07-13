using System.Reflection;
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
        // Non-interactive info flags come first, so `menuki --help` never opens the TUI.
        if (args.Contains("--version") || args.Contains("-v"))
        {
            Console.WriteLine($"menuki {Version()}");
            Environment.Exit(0);
        }
        // `completions` subcommand: print a shell tab-completion script and exit. Checked
        // before the global --help so `menuki completions --help` shows its own usage.
        if (args.Length > 0 && args[0] == "completions")
        {
            Environment.Exit(Completions.CompletionsCli.Run(args));
        }

        // `man` subcommand: print the roff man page (installed by the Homebrew formula).
        if (args.Length > 0 && args[0] == "man")
        {
            Environment.Exit(Completions.ManPage.Run(Version()));
        }

        if (args.Contains("--help") || args.Contains("-h") || (args.Length > 0 && args[0] == "help"))
        {
            PrintHelp();
            Environment.Exit(0);
        }

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

        // `examples` subcommand: list / run / print / save the built-in example packs.
        if (args.Length > 0 && args[0] == "examples")
        {
            Environment.Exit(Examples.ExamplesCli.Run(args));
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

    /// <summary>Version string, from the assembly's informational version (tag-injected in releases).</summary>
    private static string Version()
    {
        var info = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var v = string.IsNullOrEmpty(info)
            ? typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
            : info;
        var plus = v.IndexOf('+'); // drop source-revision build metadata
        return plus >= 0 ? v[..plus] : v;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            menuki {Version()} - interactive terminal menus and runbooks from a single JSON file

            Usage:
              menuki                            Welcome screen (interactive)
              menuki tour                       Guided, hands-on feature tour
              menuki examples [name]            List, or run a built-in example pack
              menuki --config <path>            Run a menu config (interactive)

            Headless (JSON in, JSON out):
              menuki list --config <path>                       Catalog of runnable actions
              menuki exec --config <path> --action <id> [--param k=v ...]
              menuki validate --config <path>                   Check a config

            Example packs:
              menuki examples                   List the bundled packs
              menuki examples <name>            Run one
              menuki examples <name> --save [path]   Copy its JSON to a file
              menuki examples <name> --print    Print its JSON to stdout

            Other:
              menuki mcp                        Run the MCP server (stdio)
              menuki completions <shell>        Print a bash/zsh/fish completion script
              menuki man                         Print the manual page (roff)
              menuki --version, -v              Print the version
              menuki --help, -h, help           Show this help

            Docs: https://github.com/MPilukov/menuki
            """);
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
