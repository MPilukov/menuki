using Menuki.Config;

namespace Menuki.Engine;

/// <summary>
/// Resolves the active color palette and cycles through the available themes on the T key.
/// The available set is the built-in <see cref="ThemeCatalog"/> palettes plus a "custom"
/// entry when the config supplies its own <c>colors</c>. The user's pick persists globally
/// via <see cref="AppSettings"/> and takes precedence over the config's declared theme.
/// </summary>
public class ThemeManager
{
    private readonly ColorScheme? _configColors;
    private readonly List<string> _available;
    private string _activeThemeName;

    public ThemeManager(string? configTheme, ColorScheme? configColors)
    {
        _configColors = configColors;

        _available = new List<string>(ThemeCatalog.Names);
        if (_configColors != null)
            _available.Add(ThemeCatalog.Custom);

        AppSettings.Load();
        // Precedence: the user's saved choice, else the config's theme, else the default.
        _activeThemeName = FirstValid(AppSettings.Theme, configTheme, "auto");
    }

    public ColorScheme Current => ResolveScheme(_activeThemeName);
    public string ActiveThemeName => _activeThemeName;
    public IReadOnlyList<string> AvailableThemes => _available;

    // Apply a role's color to the console. A "default"/empty color resets to the terminal's
    // own foreground (Console.ResetColor), so body text stays readable on any background.
    public void UseText() => Apply(Current.Text);
    public void UseSelected() => Apply(Current.Selected);
    public void UseTitle() => Apply(Current.Title);
    public void UseInfoBorder() => Apply(Current.InfoBorder);
    public void UseInfoLabel() => Apply(Current.InfoLabel);
    public void UseInfoValue() => Apply(Current.InfoValue);
    public void UseMessage() => Apply(Current.Message);

    private static void Apply(string colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName) ||
            colorName.Equals(ThemeCatalog.DefaultColor, StringComparison.OrdinalIgnoreCase))
            Console.ResetColor();
        else
            Console.ForegroundColor = Parse(colorName);
    }

    /// <summary>Advance to the next theme in the cycle and remember it.</summary>
    public void Toggle()
    {
        _activeThemeName = ThemeCatalog.Next(_activeThemeName, _available);
        AppSettings.SetTheme(_activeThemeName);
    }

    /// <summary>Set a specific theme by name (ignored if not available) and remember it.</summary>
    public void SetTheme(string name)
    {
        if (!_available.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;
        _activeThemeName = name;
        AppSettings.SetTheme(_activeThemeName);
    }

    private ColorScheme ResolveScheme(string name) =>
        string.Equals(name, ThemeCatalog.Custom, StringComparison.OrdinalIgnoreCase)
            ? MergeWithDefaults(_configColors, ThemeCatalog.Get("auto"))
            : ThemeCatalog.Get(name);

    private string FirstValid(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (!string.IsNullOrEmpty(c) && _available.Contains(c, StringComparer.OrdinalIgnoreCase))
                return c!;
        return _available.Count > 0 ? _available[0] : "auto";
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

    private static ConsoleColor Parse(string colorName) =>
        Enum.TryParse<ConsoleColor>(colorName, ignoreCase: true, out var color) ? color : ConsoleColor.Gray;
}
