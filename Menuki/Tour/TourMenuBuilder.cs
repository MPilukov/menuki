using Menuki.Config;

namespace Menuki.Tour;

/// <summary>
/// Builds the tour as an ordinary <see cref="MenuConfig"/> (so it reuses the whole
/// TUI: search, ?, themes, editing) and decorates it with visited markers. The tour
/// is "just JSON plus a few private actions" - that's the point it teaches.
/// </summary>
public static class TourMenuBuilder
{
    /// <summary>Section menu ids that count toward the "explored" progress.</summary>
    private static readonly (string Id, string Title)[] Sections =
    {
        ("what",     "What is Menuki?"),
        ("nav",      "Menu navigation"),
        ("input",    "Typed inputs playground"),
        ("commands", "Run safe commands"),
        ("dynamic",  "Dynamic information"),
        ("format",   "Format command output"),
        ("headless", "Headless & AI usage"),
        ("packs",    "Real-world packs (open-config)"),
        ("create",   "Create your first menu"),
    };

    public static MenuConfig Build(DetectedEnv env)
    {
        var menus = new Dictionary<string, MenuDefinition>
        {
            ["main"] = Hub(env),
            ["what"] = What(),
            ["nav"] = Nav(),
            ["nav_big"] = NavBig(),
            ["input"] = InputPlayground(),
            ["commands"] = Commands(env),
            ["dynamic"] = Dynamic(env),
            ["format"] = Format(),
            ["headless"] = HeadlessAi(),
            ["packs"] = Packs(env),
            ["create"] = Create(),
        };

        return new MenuConfig { Title = "Menuki Tour", StartMenu = "main", Menus = menus };
    }

    // --- progress decoration -------------------------------------------------

    public static bool IsSection(string menuId) => Sections.Any(s => s.Id == menuId);

    /// <summary>Prefix ✓/○ on the hub's section links and refresh the "Explored" line, in place.</summary>
    public static void Decorate(MenuConfig config, ISet<string> visited)
    {
        var sectionIds = Sections.Select(s => s.Id).ToHashSet();

        // Only the hub ("main") carries visited markers - that's where section links live.
        foreach (var item in config.Menus["main"].Items)
            if (item.Action.Type == ActionTypes.Submenu &&
                item.Action.Menu != null && sectionIds.Contains(item.Action.Menu))
            {
                var marker = visited.Contains(item.Action.Menu) ? "✓ " : "○ ";
                item.Name = marker + Strip(item.Name);
            }

        var done = sectionIds.Count(visited.Contains);
        var pct = (int)Math.Round(100.0 * done / sectionIds.Count);
        var progress = config.Menus["main"].Info?.FirstOrDefault(e => e.Label == "Explored");
        if (progress != null)
            progress.Value = $"{done} / {sectionIds.Count} sections ({pct}%)";
    }

    private static string Strip(string name) =>
        name.StartsWith("✓ ") || name.StartsWith("○ ") ? name[2..] : name;

    // --- menus ---------------------------------------------------------------

    private static MenuDefinition Hub(DetectedEnv env) => new()
    {
        Title = "Welcome to the Menuki Tour",
        Info = new()
        {
            Info("What", "One JSON menu, three consumers: humans, shell scripts, AI agents."),
            Info("Tip", "Press ? on any item for its description. Type to search. T changes theme."),
            Info("Platform", $"{env.Os} · shell {Path.GetFileName(env.Shell)}"),
            Info("Explored", "0 / 9 sections (0%)"),
        },
        Items = Sections
            .Select(s => Sub(s.Title, s.Id, "A short, hands-on section - jump in any order."))
            .Append(Exit())
            .ToList()
    };

    private static MenuDefinition What() => new()
    {
        Title = "What is Menuki?",
        Info = new()
        {
            Info("Idea", "It turns a JSON config into an interactive terminal app."),
            Info("Shared", "The same menu is usable by a human (this screen), a script, and an AI agent."),
        },
        Items = new()
        {
            TourItem("Show the JSON behind this screen", TourActions.ShowJson,
                new() { ["menu"] = "what" }, "See the exact config that produced this menu."),
            TourItem("Show what an agent sees", TourActions.Headless, new(),
                "Runs this tour through the headless catalog - the agent-facing view."),
            Back(),
        }
    };

    private static MenuDefinition Nav() => new()
    {
        Title = "Menu navigation",
        Info = new()
        {
            Info("Search", "Just start typing to filter items by name."),
            Info("Keys", "Up/Down move · Enter selects · ? info · T theme · Esc back."),
        },
        Items = new()
        {
            Sub("Open a long list (search & pagination) →", "nav_big",
                "A 30-item menu - try typing to search or arrow through pages."),
            TourItem("Show this menu's JSON", TourActions.ShowJson,
                new() { ["menu"] = "nav" }, "The submenu action is one line of JSON."),
            Back(),
        }
    };

    private static MenuDefinition NavBig()
    {
        var items = new List<MenuItemDefinition>();
        for (var i = 1; i <= 30; i++)
            items.Add(Shell($"Item {i:D2} - echo it", $"echo 'you picked item {i}'",
                $"Sample item #{i}. Type part of the name to jump here."));
        items.Add(Back("nav"));
        return new MenuDefinition { Title = "Long list (try search / pages)", Items = items };
    }

    private static MenuDefinition InputPlayground() => new()
    {
        Title = "Typed inputs playground",
        Info = new()
        {
            Info("Safe", "Nothing dangerous runs - each item just echoes the validated value."),
            Info("History", "At any text prompt, press Up/Down to recall previous values."),
        },
        Items = new()
        {
            Input("Choice - pick an environment",
                new() { new() { Name = "env", Type = InputTypes.Choice, Prompt = "Environment",
                    Options = new() { "development", "staging", "production" }, Default = "development" } },
                "echo 'chosen environment = {env}'",
                "Arrow-select from a fixed list; typos impossible."),
            Input("Number - bounded 1..1000",
                new() { new() { Name = "n", Type = InputTypes.Number, Prompt = "How many", Default = "100", Min = 1, Max = 1000 } },
                "echo 'number accepted = {n} (1..1000)'",
                "Rejects non-numbers and out-of-range values before running."),
            Input("Date - yyyy-MM-dd",
                new() { new() { Name = "d", Type = InputTypes.Date, Prompt = "Pick a date", Format = "yyyy-MM-dd" } },
                "echo 'valid date = {d}'",
                "Only a real calendar date is accepted."),
            Input("Pattern - ticket id",
                new() { new() { Name = "ticket", Type = InputTypes.String, Prompt = "Ticket",
                    Pattern = "^OPS-[0-9]+$", Example = "OPS-1234" } },
                "echo 'ticket = {ticket}'",
                "A regex enforces the OPS-#### shape."),
            Input("Boolean - yes/no",
                new() { new() { Name = "force", Type = InputTypes.Boolean, Prompt = "Force", Default = "false" } },
                "echo 'force = {force}'",
                "yes/no/1/0 are normalized to true/false."),
            Back(),
        }
    };

    private static MenuDefinition Commands(DetectedEnv env)
    {
        var win = env.IsWindows;
        return new MenuDefinition
        {
            Title = $"Run safe commands ({env.Os})",
            Info = new() { Info("Safe", "Read-only commands picked for your platform. Nothing is modified.") },
            Items = new()
            {
                Shell("Show current directory", win ? "cd" : "pwd", "Where am I running?"),
                Shell("Show current user", "whoami", "Who am I running as?"),
                Shell("Show OS", win ? "ver" : "uname -a", "Operating system details."),
                Shell("Show date & time", win ? "date /t" : "date", "The current date/time - a dynamic value."),
                Shell("Demonstrate an error", win ? "dir Z:\\nope" : "ls /no/such/path",
                    "See how a non-zero exit code and stderr are shown."),
                Back(),
            }
        };
    }

    private static MenuDefinition Dynamic(DetectedEnv env)
    {
        var toolLines = env.Tools.Select(kv => Info(kv.Key, kv.Value ? "available" : "not found")).ToList();
        var info = new List<InfoPanelEntry>
        {
            Info("Note", "This panel is built live - values come from commands run right now."),
            InfoCmd("User", "whoami"),
            InfoCmd("Now", env.IsWindows ? "date /t" : "date"),
        };
        info.AddRange(toolLines);

        return new MenuDefinition
        {
            Title = "Dynamic information",
            Info = info,
            Items = new()
            {
                TourItem("Show this menu's JSON", TourActions.ShowJson,
                    new() { ["menu"] = "dynamic" }, "Info entries can be static or command-driven."),
                Back(),
            }
        };
    }

    private static MenuDefinition Format() => new()
    {
        Title = "Format command output",
        Info = new() { Info("How", "A shell command's JSON can be shaped: table, query (JSONPath), or pretty json.") },
        Items = new()
        {
            ShellFmt("As a table", "echo '[{\"name\":\"web\",\"status\":\"up\"},{\"name\":\"db\",\"status\":\"down\"}]'",
                format: ActionFormats.Table, query: null, "A JSON array of objects rendered as an ASCII table."),
            ShellFmt("Extract one field (query)", "echo '[{\"name\":\"web\"},{\"name\":\"db\"}]'",
                format: ActionFormats.Raw, query: "$[*].name", "JSONPath pulls out just the names."),
            ShellFmt("Pretty-print JSON", "echo '{\"service\":\"api\",\"replicas\":3,\"ok\":true}'",
                format: ActionFormats.Json, query: null, "Compact JSON, pretty-printed for reading."),
            Back(),
        }
    };

    private static MenuDefinition HeadlessAi() => new()
    {
        Title = "Headless & AI usage",
        Info = new()
        {
            Info("Same catalog", "Every action here is addressable by a stable id like 'commands/show-os'."),
            Info("CLI", "menuki list --config <cfg>  ·  menuki exec --config <cfg> --action <id>"),
            Info("MCP", "An AI agent connects over MCP to author, validate and run these same menus."),
        },
        Items = new()
        {
            TourItem("Show the action catalog (list)", TourActions.Headless, new(),
                "Exactly what a script or agent receives from this config."),
            TourItem("Run one action by id (exec)", TourActions.Headless,
                new() { ["action"] = "commands/show-current-directory" },
                "Runs a single action headlessly and shows the JSON result."),
            Back(),
        }
    };

    private static MenuDefinition Packs(DetectedEnv env) => new()
    {
        Title = "Real-world packs (open-config)",
        Info = new()
        {
            Info("open-config", "An action can load another config as a nested menu - a hub over many packs."),
            Info("Detected", env.Has("git") ? "git found - the git-style demo is relevant here."
                                             : "git not found - the demo still runs (it only echoes)."),
        },
        Items = new()
        {
            new()
            {
                Name = "Open a demo pack →",
                Description = "Loads a sibling config with open-config; Esc returns here.",
                Action = new ActionDefinition { Type = ActionTypes.OpenConfig, Path = "scenario.json" }
            },
            TourItem("Show this menu's JSON", TourActions.ShowJson,
                new() { ["menu"] = "packs" }, "Notice the open-config action and its relative path."),
            Back(),
        }
    };

    private static MenuDefinition Create() => new()
    {
        Title = "Create your first menu",
        Info = new() { Info("Finish", "The tour ends with a result, not a README: build your own menu.") },
        Items = new()
        {
            TourItem("Launch the menu-creation wizard", TourActions.CreateMenu, new(),
                "Hands off to the interactive wizard to scaffold a real config."),
            Back(),
        }
    };

    // --- small builders ------------------------------------------------------

    private static InfoPanelEntry Info(string label, string value) =>
        new() { Label = label, Value = value };

    private static InfoPanelEntry InfoCmd(string label, string command) =>
        new() { Label = label, Command = command };

    private static MenuItemDefinition Sub(string name, string menu, string? desc = null) => new()
    {
        Name = name,
        Description = desc,
        Action = new ActionDefinition { Type = ActionTypes.Submenu, Menu = menu }
    };

    private static MenuItemDefinition Back(string to = "main") => new()
    {
        Name = "← Back",
        Action = new ActionDefinition { Type = ActionTypes.Submenu, Menu = to }
    };

    private static MenuItemDefinition Exit() => new()
    {
        Name = "Exit tour",
        Action = new ActionDefinition { Type = ActionTypes.Exit }
    };

    private static MenuItemDefinition Shell(string name, string command, string? desc = null) => new()
    {
        Name = name,
        Description = desc,
        Action = new ActionDefinition { Type = ActionTypes.Shell, Command = command }
    };

    private static MenuItemDefinition ShellFmt(string name, string command, string format, string? query, string? desc) => new()
    {
        Name = name,
        Description = desc,
        Action = new ActionDefinition { Type = ActionTypes.Shell, Command = command, Format = format, Query = query }
    };

    private static MenuItemDefinition Input(string name, List<InputDefinition> inputs, string template, string? desc) => new()
    {
        Name = name,
        Description = desc,
        Action = new ActionDefinition { Type = ActionTypes.InputShell, Inputs = inputs, CommandTemplate = template }
    };

    private static MenuItemDefinition TourItem(string name, string type, Dictionary<string, string> pars, string? desc) => new()
    {
        Name = name,
        Description = desc,
        Action = new ActionDefinition { Type = type, Parameters = pars }
    };
}
