namespace Menuki.Config;

/// <summary>
/// Turns what the user typed into a config file path. A saved config can be opened by
/// name (<c>menuki work</c>, <c>--config work</c>) instead of by path: the name maps to
/// <c>&lt;configs dir&gt;/work.json</c>, the same directory the MCP server saves into.
/// </summary>
public static class ConfigResolver
{
    /// <summary>
    /// Where named configs live: <c>$MENUKI_CONFIG_DIR</c>, else <c>$MENUKI_HOME/configs</c>,
    /// else <c>~/.menuki/configs</c>.
    /// </summary>
    public static string ConfigsDir =>
        Environment.GetEnvironmentVariable("MENUKI_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(
                Environment.GetEnvironmentVariable("MENUKI_HOME") is { Length: > 0 } home
                    ? home
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki"),
                "configs");

    /// <summary>
    /// Resolves a path or a config name. An existing file always wins, so a local
    /// <c>ops.json</c> is never shadowed by a saved config. Returns null when neither exists.
    /// </summary>
    public static string? Resolve(string pathOrName) => Resolve(pathOrName, ConfigsDir);

    /// <inheritdoc cref="Resolve(string)"/>
    public static string? Resolve(string pathOrName, string configsDir)
    {
        if (File.Exists(pathOrName))
            return pathOrName;

        var name = NameOf(pathOrName);
        if (name == null)
            return null;

        var saved = Path.Combine(configsDir, name + ".json");
        return File.Exists(saved) ? saved : null;
    }

    /// <summary>Error text for an argument <see cref="Resolve"/> could not find.</summary>
    public static string NotFoundMessage(string pathOrName) =>
        IsName(pathOrName)
            ? $"No config file or saved config named '{pathOrName}' (looked in {ConfigsDir})."
            : $"Config file not found: {pathOrName}";

    /// <summary>True when the argument looks like a bare config name rather than a path or flag.</summary>
    public static bool IsName(string arg) => NameOf(arg) != null;

    /// <summary>Names of the saved configs, sorted.</summary>
    public static List<string> ListNames() => ListNames(ConfigsDir);

    /// <inheritdoc cref="ListNames()"/>
    public static List<string> ListNames(string configsDir)
    {
        if (!Directory.Exists(configsDir))
            return new List<string>();

        return Directory.GetFiles(configsDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // "work" and "work.json" are names; "./work.json", "a/b", "-x" and "" are not.
    private static string? NameOf(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-'))
            return null;
        if (arg.IndexOfAny(new[] { '/', '\\' }) >= 0)
            return null;

        var name = arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? arg[..^5] : arg;
        return name.Length > 0 && name != "." && name != ".." ? name : null;
    }
}
