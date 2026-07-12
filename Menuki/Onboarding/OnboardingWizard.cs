using System.Text.Json;
using Menuki.Config;
using Menuki.Editor;
using Menuki.Engine;

namespace Menuki.Onboarding;

public static class OnboardingWizard
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki");

    private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "secrets.json", "state.json", "theme.json"
    };

    public static (MenuConfig config, string configPath)? Run(ThemeManager theme)
    {
        var configs = ScanConfigs();

        if (configs.Count > 0)
            return PickExistingOrCreate(configs, theme);

        return CreateNewConfig(theme);
    }

    private static List<(string path, string title)> ScanConfigs()
    {
        var result = new List<(string path, string title)>();

        if (!Directory.Exists(ConfigDir))
            return result;

        foreach (var file in Directory.GetFiles(ConfigDir, "*.json"))
        {
            var fileName = Path.GetFileName(file);
            if (IgnoredFiles.Contains(fileName))
                continue;

            var title = TryReadTitle(file);
            if (title != null)
                result.Add((file, title));
        }

        return result;
    }

    private static string? TryReadTitle(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<MenuConfig>(json);
            if (config?.Menus != null && config.Menus.Count > 0)
                return config.Title;
        }
        catch
        {
            // not a valid config
        }

        return null;
    }

    private static (MenuConfig config, string configPath)? PickExistingOrCreate(
        List<(string path, string title)> configs, ThemeManager theme)
    {
        var choices = configs
            .Select(c => $"{c.title}  ({Path.GetFileName(c.path)})")
            .Append("Create new config")
            .ToArray();

        Console.Clear();
        Console.ForegroundColor = theme.Title;
        Console.WriteLine("  Welcome to Menuki!");
        Console.ForegroundColor = theme.Text;
        Console.WriteLine();

        var idx = PromptHelper.PromptChoice("Select config", choices, 0, theme);

        if (idx == null)
            return null;

        if (idx.Value == configs.Count)
            return CreateNewConfig(theme);

        var selected = configs[idx.Value];
        var json = File.ReadAllText(selected.path);
        var config = JsonSerializer.Deserialize<MenuConfig>(json)!;
        return (config, selected.path);
    }

    private static (MenuConfig config, string configPath) CreateNewConfig(ThemeManager theme)
    {
        var title = PromptHelper.PromptString("Menu title", "My Menu", theme);

        var config = new MenuConfig
        {
            Title = title,
            StartMenu = "main",
            Menus = new Dictionary<string, MenuDefinition>
            {
                ["main"] = new MenuDefinition
                {
                    Title = title,
                    Items = new List<MenuItemDefinition>
                    {
                        new()
                        {
                            Name = "Exit",
                            Action = new ActionDefinition { Type = ActionTypes.Exit }
                        }
                    }
                }
            }
        };

        if (!Directory.Exists(ConfigDir))
            Directory.CreateDirectory(ConfigDir);

        var configPath = Path.Combine(ConfigDir, "config.json");

        // Don't overwrite existing file - pick a unique name
        if (File.Exists(configPath))
        {
            var i = 1;
            while (File.Exists(Path.Combine(ConfigDir, $"config-{i}.json")))
                i++;
            configPath = Path.Combine(ConfigDir, $"config-{i}.json");
        }

        ConfigSaver.Save(config, configPath);

        Console.Clear();
        Console.ForegroundColor = theme.InfoValue;
        Console.WriteLine($"  Config saved to {configPath}");
        Console.ForegroundColor = theme.Text;
        Console.WriteLine("  Press any key to continue...");
        Console.ReadKey(intercept: true);

        return (config, configPath);
    }
}
