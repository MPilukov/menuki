using Menuki.Config;

namespace Menuki.Engine;

/// <summary>
/// The built-in named color palettes and the order the T key cycles them in. Palettes
/// use the 16 standard console colors so they render the same everywhere. A config may
/// also define a "custom" palette via its <c>colors</c> block, appended to the cycle.
/// </summary>
public static class ThemeCatalog
{
    public const string Custom = "custom";

    /// <summary>Text color sentinel meaning "use the terminal's own default foreground".</summary>
    public const string DefaultColor = "default";

    private static readonly Dictionary<string, ColorScheme> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Background-agnostic default: body text uses the terminal's own foreground (readable
        // on light AND dark), and accents use the "dark" ANSI variants, which keep contrast
        // on both. This is what a fresh user sees, so it must never be invisible anywhere.
        ["auto"] = new()
        {
            Text = DefaultColor, Selected = "DarkYellow", Title = "Red",
            InfoBorder = "DarkGray", InfoLabel = "DarkCyan", InfoValue = DefaultColor, Message = "Magenta"
        },
        ["dark"] = new()
        {
            Text = "White", Selected = "DarkYellow", Title = "Red",
            InfoBorder = "Blue", InfoLabel = "DarkCyan", InfoValue = "Cyan", Message = "Magenta"
        },
        ["light"] = new()
        {
            Text = "Black", Selected = "DarkBlue", Title = "DarkRed",
            InfoBorder = "DarkGray", InfoLabel = "DarkGreen", InfoValue = "DarkCyan", Message = "DarkMagenta"
        },
        ["ocean"] = new()
        {
            Text = "Gray", Selected = "Cyan", Title = "Blue",
            InfoBorder = "DarkCyan", InfoLabel = "Blue", InfoValue = "Cyan", Message = "DarkYellow"
        },
        ["forest"] = new()
        {
            Text = "Gray", Selected = "Green", Title = "DarkGreen",
            InfoBorder = "DarkGreen", InfoLabel = "Green", InfoValue = "DarkCyan", Message = "Yellow"
        },
        ["matrix"] = new()
        {
            Text = "DarkGreen", Selected = "Green", Title = "Green",
            InfoBorder = "DarkGreen", InfoLabel = "Green", InfoValue = "Green", Message = "Green"
        },
        ["high-contrast"] = new()
        {
            Text = "White", Selected = "Yellow", Title = "White",
            InfoBorder = "White", InfoLabel = "Yellow", InfoValue = "White", Message = "Cyan"
        },
        ["synthwave"] = new()
        {
            Text = "White", Selected = "Magenta", Title = "Magenta",
            InfoBorder = "DarkMagenta", InfoLabel = "Magenta", InfoValue = "Cyan", Message = "Yellow"
        },
    };

    /// <summary>Built-in theme names, in cycle order. "auto" is first so it is the default.</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "auto", "dark", "light", "ocean", "forest", "matrix", "high-contrast", "synthwave"
    };

    public static bool IsKnown(string name) => Schemes.ContainsKey(name);

    public static ColorScheme Get(string name) =>
        Schemes.TryGetValue(name, out var scheme) ? scheme : Schemes["auto"];

    /// <summary>Name of the theme after <paramref name="current"/> in <paramref name="available"/>, wrapping around.</summary>
    public static string Next(string current, IReadOnlyList<string> available)
    {
        if (available.Count == 0)
            return "dark";
        var i = available.ToList().FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
        return available[(i + 1) % available.Count];
    }
}
