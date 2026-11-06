using System.Runtime.InteropServices;
using Menuki.Config;

namespace Menuki.Engine;

/// <summary>Checks a config's <c>requires</c> list against PATH and the current OS.</summary>
public static class Requirements
{
    public static string CurrentOs() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";

    /// <summary>True when the requirement is not limited to another OS.</summary>
    public static bool AppliesTo(RequirementDefinition req, string os) =>
        string.IsNullOrWhiteSpace(req.Os) || string.Equals(req.Os.Trim(), os, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The applicable requirements whose command is not found. <paramref name="path"/> and
    /// <paramref name="os"/> default to the real PATH and OS; tests pass their own.
    /// </summary>
    public static List<RequirementDefinition> Missing(MenuConfig config, string? path = null, string? os = null)
    {
        var result = new List<RequirementDefinition>();
        if (config.Requires == null)
            return result;

        os ??= CurrentOs();
        path ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var req in config.Requires)
        {
            if (string.IsNullOrWhiteSpace(req.Command) || !AppliesTo(req, os))
                continue;
            if (!IsOnPath(req.Command.Trim(), path))
                result.Add(req);
        }
        return result;
    }

    /// <summary>The "Missing: x (install: y)" lines shown above the menu.</summary>
    public static string Describe(IEnumerable<RequirementDefinition> missing) =>
        string.Join("\n", missing.Select(r =>
            string.IsNullOrWhiteSpace(r.Install)
                ? $"Missing: {r.Command}"
                : $"Missing: {r.Command} (install: {r.Install})"));

    public static bool IsOnPath(string command, string path)
    {
        if (command.Contains('/') || command.Contains('\\'))
            return IsRunnable(command);

        var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var extensions = new List<string> { "" };
        if (windows)
            extensions.AddRange((Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries));

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in extensions)
            {
                try
                {
                    if (IsRunnable(Path.Combine(dir.Trim('"'), command + ext)))
                        return true;
                }
                catch (ArgumentException) { }
            }
        return false;
    }

    private static bool IsRunnable(string file)
    {
        if (!File.Exists(file))
            return false;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;
        var mode = File.GetUnixFileMode(file);
        return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
}
