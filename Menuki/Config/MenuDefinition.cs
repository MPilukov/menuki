using System.Text.Json.Serialization;

namespace Menuki.Config;

public class MenuDefinition
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("info")]
    public List<InfoPanelEntry>? Info { get; set; }

    [JsonPropertyName("items")]
    public List<MenuItemDefinition> Items { get; set; } = new();
}
