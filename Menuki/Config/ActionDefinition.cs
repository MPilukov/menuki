using System.Text.Json.Serialization;

namespace Menuki.Config;

public class ActionDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("menu")]
    public string? Menu { get; set; }

    [JsonPropertyName("inputs")]
    public List<InputDefinition>? Inputs { get; set; }

    [JsonPropertyName("command_template")]
    public string? CommandTemplate { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("editor")]
    public string? Editor { get; set; }

    [JsonPropertyName("args")]
    public string? Args { get; set; }

    [JsonPropertyName("params")]
    public Dictionary<string, string>? Parameters { get; set; }

    // Composite actions (sequence) and helpers.

    /// <summary>Optional step identifier - reserved for later output-passing between steps.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Steps of a <c>sequence</c> action, run in order.</summary>
    [JsonPropertyName("steps")]
    public List<ActionDefinition>? Steps { get; set; }

    /// <summary>For <c>sequence</c>: stop when a step exits non-zero. Defaults to true.</summary>
    [JsonPropertyName("stop_on_error")]
    public bool StopOnError { get; set; } = true;

    /// <summary>Retry a failing step this many extra times (total attempts = retry + 1).</summary>
    [JsonPropertyName("retry")]
    public int? Retry { get; set; }

    /// <summary>Seconds to wait between retry attempts.</summary>
    [JsonPropertyName("retry_delay")]
    public double? RetryDelay { get; set; }

    /// <summary>For <c>sequence</c>: action to run after all steps succeed.</summary>
    [JsonPropertyName("on_success")]
    public ActionDefinition? OnSuccess { get; set; }

    /// <summary>For <c>sequence</c>: action to run if any step fails.</summary>
    [JsonPropertyName("on_failure")]
    public ActionDefinition? OnFailure { get; set; }

    /// <summary>For <c>parallel</c>: cap on concurrent steps (default: all at once).</summary>
    [JsonPropertyName("max_parallel")]
    public int? MaxParallel { get; set; }

    /// <summary>Seconds to wait for a <c>delay</c> action.</summary>
    [JsonPropertyName("seconds")]
    public double? Seconds { get; set; }

    /// <summary>Display name for a <c>background</c> job (defaults to the command's first word).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Post-process a shell command's output: "json" (pretty-print), "table"
    /// (JSON array → table), or "raw" (default). When set, the output is captured
    /// and shown on a result screen with copy/save, instead of streaming live.
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>Minimal JSONPath applied to a shell command's JSON output before formatting, e.g. "$.items[*].metadata.name".</summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }
}
