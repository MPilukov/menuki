using System.Diagnostics;
using System.Reflection;
using Menuki.Completions;
using Xunit;

namespace Menuki.Tests;

public class ZshCompletionEscapeTests
{
    private static string EscapeZsh(string s) =>
        (string)typeof(CompletionsCli).GetMethod("EscapeZsh", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { s })!;

    private static string ZshScript() =>
        (string)typeof(CompletionsCli).GetMethod("Zsh", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;

    private static string? FindZsh()
    {
        if (File.Exists("/bin/zsh")) return "/bin/zsh";
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "zsh");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static (int Exit, string Out, string Err) RunZsh(string zsh, params string[] args)
    {
        var psi = new ProcessStartInfo(zsh)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o, e);
    }

    private static (int Exit, string Out, string Err) RunScriptText(string zsh, string flags, string text)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, text);
            return RunZsh(zsh, flags, file);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EscapeZsh_ReplacesApostropheWithCloseEscapeReopen()
        => Assert.Equal("it'\\''s", EscapeZsh("it's"));

    [Fact]
    public void EscapeZsh_EscapesColon()
        => Assert.Equal("ready\\: now", EscapeZsh("ready: now"));

    [Fact]
    public void EscapeZsh_LeavesPlainTextUnchanged()
        => Assert.Equal("Open the menu", EscapeZsh("Open the menu"));

    [Fact]
    public void Zsh_ItemWithApostropheAndColon_IsOneElementKeepingApostrophe()
    {
        var zsh = FindZsh();
        if (zsh == null) return; // zsh not installed (e.g. CI): nothing to verify, do not fail.

        var item = "'name:" + EscapeZsh("it's ready: now") + "'";
        var (exit, output, err) = RunScriptText(zsh, "-f", $"a=( {item} )\nprint -r -- ${{#a}}\nprint -r -- \"$a[1]\"\n");

        Assert.True(exit == 0, err);
        var lines = output.Replace("\r", "").TrimEnd('\n').Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("name:it's ready\\: now", lines[1]);
    }

    [Fact]
    public void Zsh_AllApostropheDescription_ParsesWithoutError()
    {
        var zsh = FindZsh();
        if (zsh == null) return; // zsh not installed: skip silently.

        var item = "'name:" + EscapeZsh("''") + "'";
        var (exit, _, err) = RunScriptText(zsh, "-n", $"a=( {item} )\n");
        Assert.True(exit == 0, err);
    }

    [Fact]
    public void Zsh_GeneratedScript_PassesSyntaxCheck()
    {
        var zsh = FindZsh();
        if (zsh == null) return; // zsh not installed: skip silently.

        var (exit, _, err) = RunScriptText(zsh, "-n", ZshScript());
        Assert.True(exit == 0, err);
    }
}
