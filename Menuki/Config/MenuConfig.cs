using System.Text.Json.Serialization;

namespace Menuki.Config;

public class MenuConfig
{
    /// <summary>Optional path/URL to the JSON schema, for editor autocomplete. Ignored at runtime, preserved on save.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("start_menu")]
    public string StartMenu { get; set; } = "main";

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("colors")]
    public ColorScheme? Colors { get; set; }

    /// <summary>
    /// Editor command used by the E (Edit JSON) key, e.g. "subl -w", "code --wait",
    /// "nano", "vim". Overrides $VISUAL / $EDITOR. GUI editors get a --wait flag
    /// added automatically if missing.
    /// </summary>
    [JsonPropertyName("editor")]
    public string? Editor { get; set; }

    [JsonPropertyName("menus")]
    public Dictionary<string, MenuDefinition> Menus { get; set; } = new();
}
