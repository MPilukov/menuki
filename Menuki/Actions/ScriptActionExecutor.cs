using System.Diagnostics;
using System.Runtime.InteropServices;
using Menuki.Engine;

namespace Menuki.Actions;

public class ScriptActionExecutor : IActionExecutor
{
    private readonly string _path;
    private readonly string? _args;

    public ScriptActionExecutor(string path, string? args)
    {
        _path = ExpandPath(path);
        _args = args;
    }

    public string? Execute()
    {
        Console.Clear();

        if (!File.Exists(_path))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Script not found: {_path}");
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Press any key to continue...");
            Console.ResetColor();
            Console.ReadKey(intercept: true);
            return null;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"> {_path}{(_args != null ? " " + _args : "")}");
        Console.ResetColor();
        Console.WriteLine();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            _path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var psArgs = $"-ExecutionPolicy Bypass -File \"{_path}\"";
            if (!string.IsNullOrEmpty(_args))
                psArgs += " " + _args;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = psArgs,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        else
        {
            var command = $"\"{_path}\"";
            if (!string.IsNullOrEmpty(_args))
                command += " " + _args;
            ShellRunner.RunInteractive(command);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);

        return null;
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }
        return path;
    }
}
