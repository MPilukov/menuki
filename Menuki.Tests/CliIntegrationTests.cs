using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Menuki.Tests;

/// <summary>
/// End-to-end tests that spawn the real binary: `list`, `exec`, `validate`.
/// These lock in the agent-facing CLI contract (JSON shape + exit codes).
/// </summary>
public class CliIntegrationTests
{
    private const string ValidConfig = """
    {
      "title": "t", "start_menu": "main",
      "menus": { "main": { "title": "M", "items": [
        { "name": "hello", "action": { "type": "shell", "command": "echo hi" } },
        { "name": "boom",  "action": { "type": "shell", "command": "exit 3" } }
      ] } }
    }
    """;

    private const string DanglingConfig = """
    {
      "title": "t", "start_menu": "main",
      "menus": { "main": { "title": "M", "items": [
        { "name": "go", "action": { "type": "submenu", "menu": "nowhere" } }
      ] } }
    }
    """;

    [Fact]
    public void List_returns_actions()
    {
        using var cfg = new TempConfig(ValidConfig);
        var (exit, output) = RunCli("list", "--config", cfg.Path);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        var ids = doc.RootElement.GetProperty("actions").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString()).ToList();
        Assert.Contains("main/hello", ids);
    }

    [Fact]
    public void Exec_runs_action_and_returns_stdout()
    {
        using var cfg = new TempConfig(ValidConfig);
        var (exit, output) = RunCli("exec", "--config", cfg.Path, "--action", "main/hello");

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("hi", doc.RootElement.GetProperty("stdout").GetString());
    }

    [Fact]
    public void Exec_propagates_nonzero_exit_code()
    {
        using var cfg = new TempConfig(ValidConfig);
        var (exit, _) = RunCli("exec", "--config", cfg.Path, "--action", "main/boom");
        Assert.Equal(3, exit);
    }

    [Fact]
    public void Exec_unknown_action_errors()
    {
        using var cfg = new TempConfig(ValidConfig);
        var (exit, output) = RunCli("exec", "--config", cfg.Path, "--action", "main/nope");
        Assert.Equal(2, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Validate_passes_for_good_config()
    {
        using var cfg = new TempConfig(ValidConfig);
        var (exit, output) = RunCli("validate", "--config", cfg.Path);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Validate_fails_for_dangling_submenu()
    {
        using var cfg = new TempConfig(DanglingConfig);
        var (exit, output) = RunCli("validate", "--config", cfg.Path);

        Assert.Equal(5, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotEmpty(doc.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void Version_flag_prints_and_exits_zero(string flag)
    {
        var (exit, output) = RunCli(flag);
        Assert.Equal(0, exit);
        Assert.StartsWith("menuki ", output.Trim());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Help_flag_prints_usage_and_exits_zero(string arg)
    {
        var (exit, output) = RunCli(arg);
        Assert.Equal(0, exit);
        Assert.Contains("Usage:", output);
        Assert.Contains("menuki examples", output);
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    public void Completions_prints_a_script_for_each_shell(string shell)
    {
        var (exit, output) = RunCli("completions", shell);
        Assert.Equal(0, exit);
        Assert.Contains("menuki", output);
        // Every script references the runtime-generated example list, proving it was
        // built from the catalog (not a stale hand-maintained copy).
        Assert.Contains("git", output);
    }

    [Fact]
    public void Completions_without_shell_is_usage_and_nonzero()
    {
        var (exit, output) = RunCli("completions");
        Assert.Equal(1, exit);
        Assert.Contains("bash", output);
    }

    [Fact]
    public void Man_prints_a_roff_page()
    {
        var (exit, output) = RunCli("man");
        Assert.Equal(0, exit);
        Assert.Contains(".TH MENUKI 1", output);
        Assert.Contains(".SH NAME", output);
        // The version line is injected from the running build, so it must be present.
        Assert.Contains("menuki ", output);
    }

    // --- harness --------------------------------------------------------

    private static (int Exit, string Output) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(BinaryPath.Value);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && p.ExitCode != 1 && p.ExitCode != 2 && p.ExitCode != 3 && p.ExitCode != 5)
            throw new Xunit.Sdk.XunitException($"CLI exit {p.ExitCode}. stderr:\n{err}\nstdout:\n{output}");
        return (p.ExitCode, output);
    }

    private static readonly Lazy<string> BinaryPath = new(() =>
    {
        // Match the build config of the test run so we invoke the freshly-built binary,
        // not a stale Release/publish copy sitting elsewhere under bin/.
        var baseDir = AppContext.BaseDirectory.Replace('\\', '/');
        var config = baseDir.Contains("/Release/") ? "Release" : "Debug";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Menuki.sln")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException("Could not locate the repo root (Menuki.sln).");

        var dll = Path.Combine(dir.FullName, "Menuki", "bin", config, "net8.0", "Menuki.dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException($"Built Menuki.dll not found at {dll}.");
        return dll;
    });

    private sealed class TempConfig : IDisposable
    {
        public string Path { get; }

        public TempConfig(string json)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mm-test-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, json);
        }

        public void Dispose()
        {
            try { File.Delete(Path); } catch { /* best effort */ }
        }
    }
}
