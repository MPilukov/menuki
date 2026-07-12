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

    private static readonly Dictionary<string, ColorScheme> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
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

    /// <summary>Built-in theme names, in cycle order.</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "dark", "light", "ocean", "forest", "matrix", "high-contrast", "synthwave"
    };

    public static bool IsKnown(string name) => Schemes.ContainsKey(name);

    public static ColorScheme Get(string name) =>
        Schemes.TryGetValue(name, out var scheme) ? scheme : Schemes["dark"];

    /// <summary>Name of the theme after <paramref name="current"/> in <paramref name="available"/>, wrapping around.</summary>
    public static string Next(string current, IReadOnlyList<string> available)
    {
        if (available.Count == 0)
            return "dark";
        var i = available.ToList().FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
        return available[(i + 1) % available.Count];
    }
}
