using System.Text.Json.Serialization;

namespace Menuki.Config;

/// <summary>Supported input types. Unknown/absent means <see cref="InputTypes.String"/>.</summary>
public static class InputTypes
{
    public const string String = "string";
    public const string Choice = "choice";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";

    public static readonly string[] All = { String, Choice, Number, Boolean, Date };
}

public class InputDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    /// <summary>Fallback value used when the user/agent supplies nothing. May be a JSON string, number or boolean.</summary>
    [JsonPropertyName("default")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Default { get; set; }

    /// <summary>Input type: string (default), choice, number, boolean, date.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>When true, a value must be provided (no empty fallback).</summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    /// <summary>
    /// When true, treat the value as sensitive: mask it while typing, never record it
    /// in the input history, and show it as <c>***</c> in any echoed/logged command.
    /// </summary>
    [JsonPropertyName("secret")]
    public bool Secret { get; set; }

    /// <summary>Allowed values for a <c>choice</c> input.</summary>
    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }

    /// <summary>Lower bound for a <c>number</c> input (inclusive).</summary>
    [JsonPropertyName("min")]
    public double? Min { get; set; }

    /// <summary>Upper bound for a <c>number</c> input (inclusive).</summary>
    [JsonPropertyName("max")]
    public double? Max { get; set; }

    /// <summary>Regex a <c>string</c> value must match, e.g. "^OPS-[0-9]+$".</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>Example value shown in the prompt / error to illustrate the expected format.</summary>
    [JsonPropertyName("example")]
    public string? Example { get; set; }

    /// <summary>Date format for a <c>date</c> input (default "yyyy-MM-dd").</summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }
}
