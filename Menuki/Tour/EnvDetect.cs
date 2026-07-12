using Menuki.Engine;

namespace Menuki.Tour;

/// <summary>What the tour found on this machine - used to adapt its sections.</summary>
public record DetectedEnv(
    string Os,
    string Shell,
    string Editor,
    IReadOnlyDictionary<string, bool> Tools)
{
    public bool Has(string tool) => Tools.TryGetValue(tool, out var v) && v;

    /// <summary>Whether command choices should use Windows syntax. Defaults to the host OS; overridable for tests.</summary>
    public bool IsWindows { get; init; } = OperatingSystem.IsWindows();
}

/// <summary>
/// Best-effort environment probing so the tour can show relevant sections and skip
/// examples for tools that aren't installed. Nothing here mutates anything.
/// </summary>
public static class EnvDetect
{
    private static readonly string[] Probe = { "git", "docker", "kubectl", "dotnet", "node", "python3" };

    public static DetectedEnv Detect()
    {
        var os = OperatingSystem.IsMacOS() ? "macOS"
            : OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsLinux() ? "Linux" : "Unknown";

        var shell = Environment.GetEnvironmentVariable("SHELL")
                    ?? Environment.GetEnvironmentVariable("ComSpec")
                    ?? "unknown";

        var editor = Environment.GetEnvironmentVariable("VISUAL")
                     ?? Environment.GetEnvironmentVariable("EDITOR")
                     ?? "not set";

        var tools = Probe.ToDictionary(t => t, IsAvailable);
        return new DetectedEnv(os, shell, editor, tools);
    }

    private static bool IsAvailable(string tool)
    {
        try
        {
            var probe = OperatingSystem.IsWindows() ? $"where {tool}" : $"command -v {tool}";
            var (exit, stdout, _) = ShellRunner.RunCaptured(probe);
            return exit == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }
}
