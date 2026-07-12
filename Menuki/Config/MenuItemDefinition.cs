using System.Text.Json.Serialization;

namespace Menuki.Config;

public class MenuItemDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// One-line summary of what the item does. Shown on the '?' info key in the
    /// TUI and exposed to agents (headless catalog / MCP list_actions) so a human,
    /// a script and an agent all read the same explanation.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Longer help text (multi-line allowed) shown alongside the description on '?'.</summary>
    [JsonPropertyName("help")]
    public string? Help { get; set; }

    [JsonPropertyName("action")]
    public ActionDefinition Action { get; set; } = new();
}
