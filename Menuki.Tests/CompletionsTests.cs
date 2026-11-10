using System.Reflection;
using Menuki.Completions;
using Xunit;

namespace Menuki.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;

[Collection("Console")]
public class CompletionsTests
{
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    public void Run_prints_a_script_with_every_command_and_flag(string shell)
    {
        var (exitCode, stdout, stderr) = ConsoleCapture.Run(
            () => CompletionsCli.Run(["completions", shell]));

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(stdout);
        Assert.Empty(stderr);

        var commands = Table("Commands");
        var flags = Table("Flags");
        Assert.NotEmpty(commands);
        Assert.NotEmpty(flags);

        switch (shell)
        {
            case "bash":
                Assert.Contains("complete -F", stdout);
                Assert.Contains($"local commands=\"{string.Join(" ", commands)}\"", stdout);
                Assert.Contains($"local flags=\"{string.Join(" ", flags)}\"", stdout);
                break;
            case "zsh":
                Assert.StartsWith("#compdef", stdout);
                foreach (var command in commands)
                    Assert.Contains($"'{command}:", stdout);
                foreach (var flag in flags)
                    Assert.Contains(flag, stdout);
                break;
            case "fish":
                Assert.Contains("complete -c menuki", stdout);
                foreach (var command in commands)
                    Assert.Contains($"-a '{command}' -d '", stdout);
                foreach (var flag in flags)
                    Assert.Contains($"-l {flag.TrimStart('-')}", stdout);
                break;
        }
    }

    [Fact]
    public void Run_accepts_shell_name_case_insensitively()
    {
        var lower = ConsoleCapture.Run(() => CompletionsCli.Run(["completions", "bash"]));
        var upper = ConsoleCapture.Run(() => CompletionsCli.Run(["completions", "BASH"]));

        Assert.Equal(0, upper.ExitCode);
        Assert.Equal(lower.Stdout, upper.Stdout);
        Assert.Empty(upper.Stderr);
    }

    [Fact]
    public void Run_rejects_unknown_shell_on_stderr()
    {
        var (exitCode, stdout, stderr) = ConsoleCapture.Run(
            () => CompletionsCli.Run(["completions", "powershell"]));

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("Unknown shell 'powershell'", stderr);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("--help", 0)]
    [InlineData("-h", 0)]
    public void Run_prints_usage_when_shell_is_missing_or_help_is_requested(string? argument, int expectedExitCode)
    {
        var args = argument is null ? new[] { "completions" } : new[] { "completions", argument };
        var (exitCode, stdout, stderr) = ConsoleCapture.Run(() => CompletionsCli.Run(args));

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Contains("menuki completions <bash|zsh|fish>", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Zsh_escapes_colons_in_actual_descriptions()
    {
        var (_, stdout, _) = ConsoleCapture.Run(() => CompletionsCli.Run(["completions", "zsh"]));

        Assert.Contains("Headless\\: catalog", stdout);
        Assert.DoesNotContain("Headless: catalog", stdout);
    }

    [Fact]
    public void Escape_helpers_escape_apostrophes_and_colons()
    {
        var zsh = typeof(CompletionsCli).GetMethod("EscapeZsh", BindingFlags.NonPublic | BindingFlags.Static);
        var fish = typeof(CompletionsCli).GetMethod("EscapeFish", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(zsh);
        Assert.NotNull(fish);

        Assert.Equal("it''s\\: ready", zsh.Invoke(null, ["it's: ready"]));
        Assert.Equal("it\\'s: ready", fish.Invoke(null, ["it's: ready"]));
    }

    private static string[] Table(string fieldName)
    {
        var field = typeof(CompletionsCli).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var entries = Assert.IsType<(string Name, string Desc)[]>(field.GetValue(null));
        return entries.Select(entry => entry.Name).ToArray();
    }
}

[Collection("Console")]
public class ManPageTests
{
    [Fact]
    public void Run_prints_versioned_man_page()
    {
        var (exitCode, stdout, stderr) = ConsoleCapture.Run(() => ManPage.Run("9.9.9-test"));

        Assert.Equal(0, exitCode);
        Assert.StartsWith(".TH MENUKI 1", stdout);
        Assert.Contains("menuki 9.9.9-test", stdout);
        Assert.Contains(".SH NAME", stdout);
        Assert.Contains(".SH SYNOPSIS", stdout);
        Assert.Empty(stderr);
    }
}

internal static class ConsoleCapture
{
    public static (int ExitCode, string Stdout, string Stderr) Run(Func<int> action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = action();
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
