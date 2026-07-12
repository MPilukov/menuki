using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Menuki.Engine;

public static class ShellRunner
{
    public static string Run(string command)
    {
        var psi = ShellStart(command);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;

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
        var psi = ShellStart(command);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;

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
        var psi = ShellStart(command);

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
        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command} > \"{logPath}\" 2>&1"
            };
        }
        else
        {
            psi = new ProcessStartInfo { FileName = "/bin/sh" };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"{command} > '{logPath}' 2>&1");
        }

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

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

    /// <summary>
    /// Build a <see cref="ProcessStartInfo"/> that runs <paramref name="command"/> through
    /// the platform shell. On POSIX the command is passed as a single, verbatim argv element
    /// (<c>/bin/sh -c &lt;command&gt;</c>) via <see cref="ProcessStartInfo.ArgumentList"/>, so
    /// there is no intermediate quoted string for the shell to re-parse. Interpolated values
    /// must already be quoted with <see cref="ShellEscaper"/> before reaching here.
    /// </summary>
    private static ProcessStartInfo ShellStart(string command)
    {
        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            psi = new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c {command}" };
        }
        else
        {
            psi = new ProcessStartInfo { FileName = "/bin/sh" };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        psi.UseShellExecute = false;
        return psi;
    }
}
