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
            var startIndex = selectedByMenu.GetValueOrDefault(currentMenuId, 0);
            var engine = new MenuEngine(menuDef.Title, menuItems, theme, infoPanel.Count > 0 ? infoPanel : null, isRoot);
            engine.WriteMenu(selectedItemIndex: startIndex);

            var result = engine.RunMenu(startIndex);

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

                case "__validate__":
                    JsonEditFlow.Validate(configPath);
                    break;

                case "__quick_edit__":
                    new ConfigEditor(config, configPath, theme, registry).EditMenu(currentMenuId);
                    break;

                case null:
                    if (menuStack.Count > 0)
                        currentMenuId = menuStack.Pop();
                    else
                        return;
                    break;

                default:
                    // Navigating into a submenu - remember where we were so we can
                    // restore the selection when the user comes back.
                    selectedByMenu[currentMenuId] = engine.SelectedIndex;
                    menuStack.Push(currentMenuId);
                    currentMenuId = result;
                    break;
            }
        }
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
