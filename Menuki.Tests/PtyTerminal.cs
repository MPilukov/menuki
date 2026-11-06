using System.Diagnostics;
using System.Text;
using Xunit;

namespace Menuki.Tests;

/// <summary>
/// Runs the real CLI under a pty for tests that need keyboard input.
/// Console.ReadKey and CursorTop require a terminal: `script` provides one, and this
/// harness answers the cursor-position queries .NET sends and records everything rendered.
/// </summary>
internal sealed class PtyTerminal : IDisposable
{
    public const string Up = "\u001bOA";   // Application cursor mode requested by the terminal.
    public const string Down = "\u001bOB";
    public const string Enter = "\r";
    public const string Escape = "\u001b";

    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private readonly object _gate = new();
    private readonly Task _reader;
    private int _cursor;
    private int _answeredQueries;

    public PtyTerminal(string configPath, string settingsHome)
    {
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Menuki/bin",
            AppContext.BaseDirectory.Contains("/Release/") ? "Release" : "Debug", "net8.0", "Menuki.dll"));
        var info = new ProcessStartInfo("script")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        if (OperatingSystem.IsMacOS())
        {
            foreach (var arg in new[] { "-q", "/dev/null", "dotnet", dll, "--config", configPath })
                info.ArgumentList.Add(arg);
        }
        else
        {
            info.ArgumentList.Add("-q");
            info.ArgumentList.Add("-e");   // exit with the CLI's status, not script's
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add($"dotnet {Quote(dll)} --config {Quote(configPath)}");
            info.ArgumentList.Add("/dev/null");
        }
        info.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        info.Environment["MENUKI_HOME"] = Path.Combine(settingsHome, "settings");
        // Input history and other ~/.menuki files follow the user profile: keep them out of the real home.
        info.Environment["HOME"] = settingsHome;
        info.Environment["USERPROFILE"] = settingsHome;
        _process = Process.Start(info)!;
        _reader = ReadOutput();
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private async Task ReadOutput()
    {
        var buffer = new char[4096];
        while (true)
        {
            var count = await _process.StandardOutput.ReadAsync(buffer);
            if (count == 0) return;
            int queries;
            lock (_gate)
            {
                _output.Append(buffer, 0, count);
                var total = _output.ToString().Split("\u001b[6n").Length - 1;
                queries = total - _answeredQueries;
                _answeredQueries = total;
            }
            for (var i = 0; i < queries; i++) Send("\u001b[1;1R");
        }
    }

    public void Send(string keys)
    {
        lock (_gate)
        {
            _process.StandardInput.Write(keys);
            _process.StandardInput.Flush();
        }
    }

    /// <summary>Wait for the next rendered menu and check its title.</summary>
    public async Task ExpectMenu(string title)
    {
        var frame = await Until("Up/Down: Navigate");
        Assert.Contains($"-------- {title} --------", frame);
    }

    public Task<string> ExpectText(string text) => Until(text);

    /// <summary>Wait for <paramref name="token"/> after the last match and return what was rendered up to it.</summary>
    private async Task<string> Until(string token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (_gate)
            {
                var output = _output.ToString();
                var found = output.IndexOf(token, _cursor, StringComparison.Ordinal);
                if (found >= 0)
                {
                    var frame = output[_cursor..(found + token.Length)];
                    _cursor = found + token.Length;
                    return frame;
                }
            }
            if (_process.HasExited) break;
            await Task.Delay(10);
        }
        throw new Xunit.Sdk.XunitException($"Timed out waiting for '{token}'. Output:\n{_output}");
    }

    public async Task ExpectExit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _process.WaitForExitAsync(timeout.Token);
        await _reader;
        Assert.Equal(0, _process.ExitCode);
    }

    public void Dispose()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }
}
