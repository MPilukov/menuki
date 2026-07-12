using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Menuki.Engine;

/// <summary>
/// Copies text to the system clipboard by piping to the platform tool: pbcopy
/// (macOS), clip (Windows), or xclip/xsel/wl-copy (Linux, whichever is present).
/// </summary>
public static class Clipboard
{
    public static bool Copy(string text, out string? error)
    {
        foreach (var (cmd, args) in Candidates())
        {
            if (TryCopy(cmd, args, text, out error))
                return true;
        }

        error = "no clipboard tool found (install xclip, xsel or wl-clipboard on Linux).";
        return false;
    }

    private static IEnumerable<(string Cmd, string Args)> Candidates()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            yield return ("pbcopy", "");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            yield return ("clip", "");
        else
        {
            yield return ("xclip", "-selection clipboard");
            yield return ("xsel", "--clipboard --input");
            yield return ("wl-copy", "");
        }
    }

    private static bool TryCopy(string cmd, string args, string text, out string? error)
    {
        error = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
