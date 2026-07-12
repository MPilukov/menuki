using System.Diagnostics;

namespace Menuki.Editor;

/// <summary>
/// Hands the terminal to an external text editor to edit a file, then returns.
/// Resolution order: $VISUAL, $EDITOR, then a platform fallback (nano/vim/vi on
/// Unix, notepad on Windows).
/// </summary>
public static class ExternalEditor
{
    // GUI editors that return immediately unless told to wait for the file to close.
    private static readonly Dictionary<string, string> WaitFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["subl"] = "--wait",
        ["sublime_text"] = "--wait",
        ["code"] = "--wait",
        ["code-insiders"] = "--wait",
        ["codium"] = "--wait",
        ["atom"] = "--wait",
        ["mate"] = "-w"
    };

    /// <summary>
    /// Launch an editor on <paramref name="path"/> and wait for it to close.
    /// <paramref name="preferred"/> (from the config's "editor" field) takes
    /// precedence over $VISUAL / $EDITOR / platform fallbacks. Returns false
    /// (with a message printed) if no editor could be found or started.
    /// </summary>
    public static bool Launch(string path, string? preferred = null)
    {
        var editor = Resolve(preferred);
        if (editor == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No text editor found.");
            Console.ResetColor();
            Console.WriteLine("Set the $VISUAL or $EDITOR environment variable, or install nano/vim.");
            return false;
        }

        // The editor command may include arguments (e.g. "code --wait"): split the
        // first token as the executable and keep the rest as an argument prefix.
        var (fileName, argsPrefix) = SplitCommand(EnsureWaitFlag(editor));
        var arguments = string.IsNullOrEmpty(argsPrefix) ? $"\"{path}\"" : $"{argsPrefix} \"{path}\"";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process != null;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to launch editor '{fileName}': {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    private static string? Resolve(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred;

        var visual = Environment.GetEnvironmentVariable("VISUAL");
        if (!string.IsNullOrWhiteSpace(visual))
            return visual;

        var editor = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrWhiteSpace(editor))
            return editor;

        if (OperatingSystem.IsWindows())
            return "notepad";

        foreach (var candidate in new[] { "nano", "vim", "vi" })
            if (ExistsOnPath(candidate))
                return candidate;

        return null;
    }

    // For a known GUI editor with no wait flag, append one so we block until the
    // file is closed (otherwise the reload fires before the user finishes editing).
    private static string EnsureWaitFlag(string command)
    {
        var (fileName, argsPrefix) = SplitCommand(command);
        var exe = Path.GetFileNameWithoutExtension(fileName);

        if (!WaitFlags.TryGetValue(exe, out var flag))
            return command;

        var hasWait = argsPrefix.Split(' ').Any(a => a is "-w" or "--wait");
        return hasWait ? command : $"{command} {flag}";
    }

    private static (string FileName, string ArgsPrefix) SplitCommand(string command)
    {
        command = command.Trim();
        var space = command.IndexOf(' ');
        return space < 0
            ? (command, "")
            : (command[..space], command[(space + 1)..].Trim());
    }

    private static bool ExistsOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, exe)))
                    return true;
            }
            catch
            {
                // Malformed PATH entry - skip it.
            }
        }
        return false;
    }
}
