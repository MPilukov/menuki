using System.Text.Json.Serialization;

namespace Menuki.Config;

/// <summary>
/// Appearance/behavior defaults for a config. These are starting values: the user can
/// still change them at runtime (T to cycle themes, the Settings screen), and their
/// choice is remembered globally in ~/.menuki/settings.json and wins over these.
/// </summary>
public class ConfigSettings
{
    /// <summary>Show the "▶" marker on the selected row. Default true.</summary>
    [JsonPropertyName("show_selection_marker")]
    public bool? ShowSelectionMarker { get; set; }
}
