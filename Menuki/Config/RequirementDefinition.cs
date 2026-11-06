using System.Text.Json.Serialization;

namespace Menuki.Config;

/// <summary>
/// One external tool a menu pack needs: the command looked up on PATH, an optional shell
/// command that installs it, and an optional OS the requirement is limited to.
/// </summary>
public class RequirementDefinition
{
    public static readonly string[] KnownOs = { "macos", "linux", "windows" };

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("install")]
    public string? Install { get; set; }

    /// <summary>"macos", "linux" or "windows". When set, the requirement applies only on that OS.</summary>
    [JsonPropertyName("os")]
    public string? Os { get; set; }
}
