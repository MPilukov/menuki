using Menuki.Actions;
using Menuki.Config;
using Menuki.Editor;

namespace Menuki.Engine;

/// <summary>
/// The interactive navigation loop, shared by the normal launch path, the welcome
/// screen and the tour. Renders menus, handles the edit/reload/validate sentinels,
/// nested config loading (open-config), and an optional per-menu-entry hook used by
/// the tour to track progress.
/// </summary>
public static class Navigator
{
    /// <summary>Sentinel prefix returned by an open-config action: "__open_config__:&lt;path&gt;".</summary>
    public const string OpenConfigPrefix = "__open_config__:";

    /// <summary>Sentinel returned by the "Install missing tools" item.</summary>
    public const string InstallMissingSentinel = "__install_missing__";

    private const string InstallMissingName = "Install missing tools";

    private sealed class SentinelExecutor(string sentinel) : IActionExecutor
    {
        public string? Execute() => sentinel;
    }

    public static void Run(
        MenuConfig config,
        ThemeManager theme,
        string configPath,
        ActionExecutorRegistry registry,
        Action<string>? onEnterMenu = null)
    {
        var menuStack = new Stack<string>();
        var selectedByMenu = new Dictionary<string, int>();
        var currentMenuId = config.StartMenu;

        while (true)
        {
            Console.Title = config.Title;

            if (!config.Menus.TryGetValue(currentMenuId, out var menuDef))
            {
                currentMenuId = config.StartMenu;
                menuStack.Clear();
                continue;
            }

            // Progress / decoration hook: may mutate the config (labels, info panel)
            // before it is rendered, so visited markers appear live.
            onEnterMenu?.Invoke(currentMenuId);

            var infoPanel = InfoPanelResolver.Resolve(menuDef.Info);

            var menuItems = menuDef.Items
                .Select(item =>
                {
                    IActionExecutor executor;
                    try
                    {
                        executor = registry.Create(item.Action);
                    }
                    catch (Exception ex)
                    {
                        executor = new ErrorActionExecutor($"Error creating action: {ex.Message}");
                    }
                    return new MenuItem(item.Name, executor, item.Description, item.Help);
                })
                .ToList();

            var isRoot = menuStack.Count == 0;

            // Missing external tools (config "requires"): a notice above the items and an
            // install item first. Only the root menu, and nothing at all when all is present.
            string? missingNotice = null;
            if (isRoot && Requirements.Missing(config) is { Count: > 0 } missing)
            {
                missingNotice = Requirements.Describe(missing);
                menuItems.Insert(0, new MenuItem(InstallMissingName, new SentinelExecutor(InstallMissingSentinel),
                    "Run the install commands for the tools this menu needs."));
            }

            // selectedByMenu keeps indexes of the menu's own items; the install item shifts them on screen.
            var shift = missingNotice != null ? 1 : 0;
            // First visit: the cursor starts on the install item; coming back: on the item left last time.
            var startIndex = selectedByMenu.TryGetValue(currentMenuId, out var saved) ? saved + shift : 0;
            var engine = new MenuEngine(menuDef.Title, menuItems, theme, infoPanel.Count > 0 ? infoPanel : null, isRoot)
            {
                Notice = missingNotice
            };
            engine.WriteMenu(selectedItemIndex: startIndex);

            var result = engine.RunMenu(startIndex);

            // Whatever the item did (a submenu, another config, the editor), come back to it.
            if (result != null)
                selectedByMenu[currentMenuId] = Math.Max(0, engine.SelectedIndex - shift);

            if (result != null && result.StartsWith(OpenConfigPrefix, StringComparison.Ordinal))
            {
                OpenNested(result[OpenConfigPrefix.Length..], configPath, theme, registry);
                continue;
            }

            switch (result)
            {
                case "__edit_json__":
                    if (JsonEditFlow.EditJson(configPath, config.Editor) is { } editedConfig)
                    {
                        config = editedConfig;
                        selectedByMenu.Clear();
                        currentMenuId = ResetAfterReload(config, menuStack, currentMenuId);
                    }
                    break;

                case "__reload__":
                    if (JsonEditFlow.Reload(configPath, config.Editor) is { } reloadedConfig)
                    {
                        config = reloadedConfig;
                        selectedByMenu.Clear();
                        currentMenuId = ResetAfterReload(config, menuStack, currentMenuId);
                    }
                    break;

                case InstallMissingSentinel:
                    config = InstallMissing(config, configPath);
                    selectedByMenu.Clear();
                    currentMenuId = ResetAfterReload(config, menuStack, currentMenuId);
                    break;

                case "__validate__":
                    JsonEditFlow.Validate(configPath);
                    break;

                case "__quick_edit__":
                    new ConfigEditor(config, configPath, theme, registry).EditMenu(currentMenuId);
                    break;

                case SettingsActionExecutor.Sentinel:
                    SettingsScreen.Run(theme);
                    break;

                case null:
                    if (menuStack.Count > 0)
                        currentMenuId = menuStack.Pop();
                    else
                        return;
                    break;

                default:
                    menuStack.Push(currentMenuId);
                    currentMenuId = result;
                    break;
            }
        }
    }

    /// <summary>
    /// Shows the install commands of the missing tools, asks y/N, runs them with live output
    /// and reloads the config from disk (keeping the current one if it cannot be read).
    /// </summary>
    private static MenuConfig InstallMissing(MenuConfig config, string configPath)
    {
        var commands = Requirements.Missing(config)
            .Where(r => !string.IsNullOrWhiteSpace(r.Install))
            .Select(r => r.Install!.Trim())
            .Distinct()
            .ToList();

        Console.Clear();
        if (commands.Count == 0)
        {
            Console.WriteLine("None of the missing tools has an install command; install them by hand.");
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(intercept: true);
            return config;
        }

        Console.WriteLine("About to run:");
        foreach (var c in commands)
            Console.WriteLine($"  > {c}");
        Console.Write("\nRun these commands? [y/N] ");
        var answer = Console.ReadKey(intercept: false).Key;
        Console.WriteLine();
        if (answer != ConsoleKey.Y)
            return config;

        Console.WriteLine();
        foreach (var c in commands)
        {
            Console.WriteLine($"> {c}");
            var code = ShellRunner.RunInteractive(c);
            if (code != 0)
                Console.WriteLine($"(exit code {code})");
        }

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(intercept: true);

        try
        {
            if (System.Text.Json.JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(configPath)) is { } reloaded &&
                reloaded.Menus.ContainsKey(reloaded.StartMenu))
                return reloaded;
        }
        catch
        {
            // Keep the config already in memory.
        }
        return config;
    }

    /// <summary>
    /// Load another config (relative to the current one) and navigate it as a nested
    /// session. Returns here when the user backs out of its root menu.
    /// </summary>
    private static void OpenNested(string rawPath, string parentPath, ThemeManager theme, ActionExecutorRegistry registry)
    {
        var path = ResolvePath(rawPath, parentPath);

        if (!File.Exists(path))
        {
            Fail($"Config not found: {path}");
            return;
        }

        MenuConfig? nested;
        try
        {
            nested = System.Text.Json.JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Fail($"Could not parse {path}: {ex.Message}");
            return;
        }

        if (nested == null || !nested.Menus.ContainsKey(nested.StartMenu))
        {
            Fail($"Config {path} has no valid start menu.");
            return;
        }

        Run(nested, theme, path, registry);
    }

    private static string ResolvePath(string rawPath, string parentPath)
    {
        var path = rawPath.Trim();

        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = path.Length <= 1 ? home : Path.Combine(home, path[2..]);
        }

        if (!Path.IsPathRooted(path))
        {
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(parentPath)) ?? Directory.GetCurrentDirectory();
            path = Path.Combine(baseDir, path);
        }

        return Path.GetFullPath(path);
    }

    private static void Fail(string message)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\nPress any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);
    }

    private static string ResetAfterReload(MenuConfig config, Stack<string> menuStack, string currentMenuId)
    {
        menuStack.Clear();
        return config.Menus.ContainsKey(currentMenuId) ? currentMenuId : config.StartMenu;
    }
}
