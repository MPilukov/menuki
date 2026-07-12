using System.Text.Json;
using Menuki.Config;

namespace Menuki.Engine;

/// <summary>
/// User-level preferences that persist across runs, stored in
/// <c>~/.menuki/settings.json</c>. These are global (not per-config): once the user
/// picks a theme or toggles the selection marker, that choice follows them everywhere
/// and overrides a config's declared defaults.
/// </summary>
public static class AppSettings
{
    // The menuki home directory. Honors $MENUKI_HOME so config can be relocated (and so
    // tests can point it at a scratch dir instead of the real ~/.menuki).
    private static string Dir =>
        Environment.GetEnvironmentVariable("MENUKI_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki");

    private static string File_ => Path.Combine(Dir, "settings.json");
    private static string LegacyThemeFile => Path.Combine(Dir, "theme.json");

    /// <summary>Active theme name, or null to fall back to the config's theme / the default.</summary>
    public static string? Theme { get; private set; }

    /// <summary>Whether the selected row shows the "▶" marker.</summary>
    public static bool ShowSelectionMarker { get; private set; } = true;

    // True once the marker was set explicitly (by the saved file or the user), so a
    // config default no longer overrides it.
    private static bool _markerExplicit;
    private static bool _loaded;

    /// <summary>Read the saved settings once. Migrates the older theme-only file.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (File.Exists(File_))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(File_));
                var root = doc.RootElement;
                if (root.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String)
                    Theme = t.GetString();
                if (root.TryGetProperty("show_selection_marker", out var m) &&
                    (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                {
                    ShowSelectionMarker = m.GetBoolean();
                    _markerExplicit = true;
                }
            }
            else if (File.Exists(LegacyThemeFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(LegacyThemeFile));
                if (doc.RootElement.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String)
                    Theme = t.GetString();
            }
        }
        catch
        {
            // Best-effort: fall back to defaults on any read/parse error.
        }
    }

    /// <summary>Apply a config's declared defaults, but only where the user has not chosen.</summary>
    public static void ApplyConfigDefaults(ConfigSettings? settings)
    {
        if (settings?.ShowSelectionMarker is { } marker && !_markerExplicit)
            ShowSelectionMarker = marker;
    }

    public static void SetTheme(string name)
    {
        Theme = name;
        Save();
    }

    public static void SetShowSelectionMarker(bool value)
    {
        ShowSelectionMarker = value;
        _markerExplicit = true;
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(
                new { theme = Theme, show_selection_marker = ShowSelectionMarker },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(File_, json);
        }
        catch
        {
            // Preferences are a convenience; never let a write failure break the app.
        }
    }
}
