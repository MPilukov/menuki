using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Menuki.Engine;

public static class ShellRunner
{
    public static string Run(string command)
    {
        var (fileName, arguments) = GetShellCommand(command);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return "";

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return string.IsNullOrEmpty(error) ? output.TrimEnd() : $"{output}\n{error}".TrimEnd();
    }

    /// <summary>
    /// Runs a command non-interactively, capturing stdout, stderr and the exit code.
    /// Used by the headless (agent-facing) execution path.
    /// </summary>
    public static (int ExitCode, string StdOut, string StdErr) RunCaptured(string command)
    {
        var (fileName, arguments) = GetShellCommand(command);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start process");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
    }

    /// <summary>
    /// Runs a command with its output streamed live to the console (inherited
    /// stdout/stderr) and returns the exit code. Used by the TUI and by sequence
    /// steps that want live progress.
    /// </summary>
    public static int RunInteractive(string command)
    {
        var (fileName, arguments) = GetShellCommand(command);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null)
            return -1;

        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Starts a command detached (non-blocking), redirecting stdout+stderr to a log
    /// file via the shell. Returns the process handle for status/kill; the caller
    /// does not wait on it. Used by background jobs.
    /// </summary>
    public static Process? StartBackground(string command, string logPath)
    {
        string fileName, arguments;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileName = "cmd.exe";
            arguments = $"/c {command} > \"{logPath}\" 2>&1";
        }
        else
        {
            fileName = "/bin/sh";
            var inner = $"{command} > '{logPath}' 2>&1";
            arguments = $"-c \"{inner.Replace("\"", "\\\"")}\"";
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return Process.Start(psi);
    }

    public static void OpenUrl(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }

    private static (string FileName, string Arguments) GetShellCommand(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("cmd.exe", $"/c {command}");
        return ("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");
    }
}
