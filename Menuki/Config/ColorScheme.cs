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

    // --- input+shell / shell prompt colors (optional; fall back to sensible built-ins) ---

    /// <summary>Color of an input prompt label, e.g. "Branch name:".</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    /// <summary>Color of the text the user types at a prompt.</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    /// <summary>Color of a validation error message.</summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";

    /// <summary>Color of a non-selected choice option.</summary>
    [JsonPropertyName("option")]
    public string Option { get; set; } = "";

    /// <summary>Color of the highlighted choice option.</summary>
    [JsonPropertyName("option_selected")]
    public string OptionSelected { get; set; } = "";

    /// <summary>Color of the echoed command line ("> ...").</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";
}
