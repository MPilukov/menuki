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
    /// <summary>
    /// The theme in effect for the current interactive session. Set to the most recently
    /// constructed manager so non-TUI-aware executors (input+shell / shell prompts) can
    /// color their output. Null in headless runs, where those executors are not used.
    /// </summary>
    public static ThemeManager? Ambient { get; private set; }

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
        // Precedence: the user's saved choice, then the config's theme, then a default. When
        // the config supplies colors but no theme, default to "custom" so they just apply.
        var fallback = _configColors != null ? ThemeCatalog.Custom : "auto";
        _activeThemeName = FirstValid(AppSettings.Theme, configTheme, fallback);

        Ambient = this;
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

    // Input-flow roles. These are optional in a scheme; when unset they fall back to the
    // built-in defaults that match the historical hardcoded colors.
    public void UsePrompt() => ApplyRole(Current.Prompt, "Cyan");
    public void UseInput() => ApplyRole(Current.Input, ThemeCatalog.DefaultColor);
    public void UseError() => ApplyRole(Current.Error, "Red");
    public void UseOption() => ApplyRole(Current.Option, ThemeCatalog.DefaultColor);
    public void UseOptionSelected() => ApplyRole(Current.OptionSelected, "Green");
    public void UseCommand() => ApplyRole(Current.Command, "Cyan");

    private static void ApplyRole(string? value, string fallback) =>
        Apply(string.IsNullOrWhiteSpace(value) ? fallback : value);

    private static void Apply(string? colorName)
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

        string Pick(string o, string d) => string.IsNullOrEmpty(o) ? d : o;
        return new ColorScheme
        {
            Text = Pick(overrides.Text, defaults.Text),
            Selected = Pick(overrides.Selected, defaults.Selected),
            Title = Pick(overrides.Title, defaults.Title),
            InfoBorder = Pick(overrides.InfoBorder, defaults.InfoBorder),
            InfoLabel = Pick(overrides.InfoLabel, defaults.InfoLabel),
            InfoValue = Pick(overrides.InfoValue, defaults.InfoValue),
            Message = Pick(overrides.Message, defaults.Message),
            // Input-flow roles: the base themes leave these empty, so an unset override stays
            // empty and the Use*() fallback supplies the built-in color.
            Prompt = Pick(overrides.Prompt, defaults.Prompt),
            Input = Pick(overrides.Input, defaults.Input),
            Error = Pick(overrides.Error, defaults.Error),
            Option = Pick(overrides.Option, defaults.Option),
            OptionSelected = Pick(overrides.OptionSelected, defaults.OptionSelected),
            Command = Pick(overrides.Command, defaults.Command),
        };
    }

    private static ConsoleColor Parse(string colorName) =>
        Enum.TryParse<ConsoleColor>(colorName, ignoreCase: true, out var color) ? color : ConsoleColor.Gray;
}
