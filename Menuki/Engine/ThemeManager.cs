using System.Text.Json;
using Menuki.Config;

namespace Menuki.Engine;

public class ThemeManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "theme.json");

    private static readonly ColorScheme DarkTheme = new()
    {
        Text = "White",
        Selected = "DarkYellow",
        Title = "Red",
        InfoBorder = "Blue",
        InfoLabel = "DarkCyan",
        InfoValue = "Cyan",
        Message = "Magenta"
    };

    private static readonly ColorScheme LightTheme = new()
    {
        Text = "Black",
        Selected = "DarkBlue",
        Title = "DarkRed",
        InfoBorder = "DarkGray",
        InfoLabel = "DarkGreen",
        InfoValue = "DarkCyan",
        Message = "DarkMagenta"
    };

    private readonly ColorScheme? _configColors;
    private readonly string _configThemeName;
    private string _activeThemeName;

    public ThemeManager(string? configTheme, ColorScheme? configColors)
    {
        _configColors = configColors;
        _configThemeName = configTheme ?? "dark";

        var saved = LoadSavedTheme();
        _activeThemeName = saved ?? _configThemeName;
    }

    public ColorScheme Current => ResolveScheme(_activeThemeName);

    public ConsoleColor Text => Parse(Current.Text);
    public ConsoleColor Selected => Parse(Current.Selected);
    public ConsoleColor Title => Parse(Current.Title);
    public ConsoleColor InfoBorder => Parse(Current.InfoBorder);
    public ConsoleColor InfoLabel => Parse(Current.InfoLabel);
    public ConsoleColor InfoValue => Parse(Current.InfoValue);
    public ConsoleColor Message => Parse(Current.Message);

    public string ActiveThemeName => _activeThemeName;

    public void Toggle()
    {
        _activeThemeName = _activeThemeName switch
        {
            "dark" => "light",
            "light" => _configColors != null ? "custom" : "dark",
            "custom" => "dark",
            _ => "dark"
        };
        SaveTheme(_activeThemeName);
    }

    private ColorScheme ResolveScheme(string name)
    {
        return name switch
        {
            "light" => MergeWithDefaults(null, LightTheme),
            "custom" => MergeWithDefaults(_configColors, DarkTheme),
            _ => MergeWithDefaults(null, DarkTheme)
        };
    }

    private static ColorScheme MergeWithDefaults(ColorScheme? overrides, ColorScheme defaults)
    {
        if (overrides == null)
            return defaults;

        return new ColorScheme
        {
            Text = string.IsNullOrEmpty(overrides.Text) ? defaults.Text : overrides.Text,
            Selected = string.IsNullOrEmpty(overrides.Selected) ? defaults.Selected : overrides.Selected,
            Title = string.IsNullOrEmpty(overrides.Title) ? defaults.Title : overrides.Title,
            InfoBorder = string.IsNullOrEmpty(overrides.InfoBorder) ? defaults.InfoBorder : overrides.InfoBorder,
            InfoLabel = string.IsNullOrEmpty(overrides.InfoLabel) ? defaults.InfoLabel : overrides.InfoLabel,
            InfoValue = string.IsNullOrEmpty(overrides.InfoValue) ? defaults.InfoValue : overrides.InfoValue,
            Message = string.IsNullOrEmpty(overrides.Message) ? defaults.Message : overrides.Message,
        };
    }

    private static ConsoleColor Parse(string colorName)
    {
        if (Enum.TryParse<ConsoleColor>(colorName, ignoreCase: true, out var color))
            return color;
        return ConsoleColor.Gray;
    }

    private static string? LoadSavedTheme()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return null;
            var json = File.ReadAllText(SettingsFile);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("theme").GetString();
        }
        catch
        {
            return null;
        }
    }

    private static void SaveTheme(string themeName)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(new { theme = themeName });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // ignore write errors
        }
    }
}
