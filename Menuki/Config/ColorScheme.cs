using System.Text.Json.Serialization;

namespace Menuki.Config;

public class ColorScheme
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("selected")]
    public string Selected { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("info_border")]
    public string InfoBorder { get; set; } = "";

    [JsonPropertyName("info_label")]
    public string InfoLabel { get; set; } = "";

    [JsonPropertyName("info_value")]
    public string InfoValue { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
