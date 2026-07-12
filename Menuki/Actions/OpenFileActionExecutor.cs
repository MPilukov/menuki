using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Menuki.Actions;

public class OpenFileActionExecutor : IActionExecutor
{
    private readonly string _path;
    private readonly string? _editor;

    public OpenFileActionExecutor(string path, string? editor)
    {
        _path = path;
        _editor = editor;
    }

    public string? Execute()
    {
        var expandedPath = _path.StartsWith("~/")
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), _path[2..])
            : _path;

        if (_editor != null)
        {
            var psi = new ProcessStartInfo(_editor) { UseShellExecute = false };
            psi.ArgumentList.Add(expandedPath);
            Process.Start(psi);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{expandedPath}\"") { CreateNoWindow = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var psi = new ProcessStartInfo("open");
            psi.ArgumentList.Add(expandedPath);
            Process.Start(psi);
        }
        else
        {
            var psi = new ProcessStartInfo("xdg-open");
            psi.ArgumentList.Add(expandedPath);
            Process.Start(psi);
        }

        return null;
    }
}
