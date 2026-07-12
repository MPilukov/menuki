using System.Text.Json.Serialization;

namespace Menuki.Config;

public class InfoPanelEntry
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }
}
