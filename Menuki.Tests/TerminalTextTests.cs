using System.Text;
using Menuki.Engine;
using Xunit;

namespace Menuki.Tests;

public class TerminalTextTests
{
    [Theory]
    [InlineData('A', 1)]
    [InlineData('\u0301', 0)]
    [InlineData('\u200D', 0)]
    [InlineData('界', 2)]
    public void RuneWidth_counts_code_point_columns(char value, int expected) =>
        Assert.Equal(expected, TerminalText.RuneWidth(new Rune(value)));

    [Fact]
    public void RuneWidth_counts_supplementary_emoji_as_two_columns() =>
        Assert.Equal(2, TerminalText.RuneWidth(new Rune(0x1F680)));

    [Theory]
    [InlineData("e\u0301", 1)]
    [InlineData("❤️", 2)]
    [InlineData("\U0001F469‍\U0001F4BB", 2)]
    [InlineData("\U0001F1E9\U0001F1EA", 2)]
    public void ClusterWidth_counts_a_grapheme_once(string cluster, int expected) =>
        Assert.Equal(expected, TerminalText.ClusterWidth(cluster));

    [Theory]
    [InlineData("Dashboard", 9)]
    [InlineData("Гармиш (веб)", 12)]
    [InlineData("🚀 go", 5)]
    [InlineData("日本", 4)]
    [InlineData("é", 1)]   // e + combining acute
    [InlineData("❤️", 2)]                     // ❤️: VS16 makes it an emoji, two columns
    [InlineData("\U0001F469‍\U0001F4BB", 2)]       // 👩‍💻: one joined glyph
    [InlineData("\U0001F1E9\U0001F1EA", 2)]             // 🇩🇪: a flag is one glyph
    public void Width_counts_terminal_columns(string text, int width) =>
        Assert.Equal(width, TerminalText.Width(text));

    [Fact]
    public void Width_currently_counts_printable_parts_of_ansi_escape_sequences()
    {
        // Current behavior: ESC is zero-width, but the rest of each ANSI code is counted.
        Assert.Equal(10, TerminalText.Width("\u001b[31mred\u001b[0m"));
    }

    [Fact]
    public void Sanitize_replaces_each_control_character_with_one_space() =>
        Assert.Equal("a b c d", TerminalText.Sanitize("a\nb\tc\u001bd"));

    [Fact]
    public void Control_characters_become_spaces()
    {
        Assert.Equal("one two  three", TerminalText.Truncate("one\ntwo\r\tthree", 40));
        Assert.Equal(new[] { "a b" }, TerminalText.Wrap("a\nb", 40));
    }

    [Fact]
    public void Truncate_never_splits_a_joined_emoji()
    {
        var cut = TerminalText.Truncate("ab\U0001F469‍\U0001F4BBcd", 4);
        Assert.Equal("ab…", cut);
        Assert.True(TerminalText.Width(cut) <= 4);
    }

    [Fact]
    public void Truncate_cuts_to_the_width_with_an_ellipsis()
    {
        Assert.Equal("Брутто-н…", TerminalText.Truncate("Брутто-нетто калькулятор", 9));
        Assert.Equal("short", TerminalText.Truncate("short", 9));
        Assert.Equal("ab…", TerminalText.Truncate("ab🚀cd", 4));   // the wide char does not fit in 1 column
    }

    [Theory]
    [InlineData("界a", 0, "")]
    [InlineData("界a", 1, "…")]
    [InlineData("界a", 2, "…")]
    [InlineData("界a", 3, "界a")]
    public void Truncate_respects_the_column_budget_without_splitting_wide_text(
        string text, int maxWidth, string expected)
    {
        var result = TerminalText.Truncate(text, maxWidth);

        Assert.Equal(expected, result);
        Assert.True(TerminalText.Width(result) <= maxWidth);
    }

    [Fact]
    public void Wrap_breaks_at_spaces_and_indents_continuations()
    {
        var lines = TerminalText.Wrap("    one two three four", 14, "    ");
        Assert.Equal(new[] { "    one two", "    three four" }, lines);
        Assert.All(lines, l => Assert.True(TerminalText.Width(l) <= 14));
    }

    [Fact]
    public void Wrap_breaks_at_a_space_inside_the_indent_width_of_an_unindented_line()
    {
        Assert.Equal(new[] { "hi", "    world" }, TerminalText.Wrap("hi world", 7, "    "));
        Assert.Equal(new[] { "    abcdefgh", "    ijk" }, TerminalText.Wrap("    abcdefghijk", 12, "    "));
    }

    [Fact]
    public void Wrap_keeps_wide_clusters_within_the_budget_on_indented_lines()
    {
        var lines = TerminalText.Wrap("界界界", 3, " ");

        Assert.Equal(new[] { "界", " 界", " 界" }, lines);
        Assert.All(lines, line => Assert.InRange(TerminalText.Width(line), 0, 3));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "…")]
    public void Wrap_with_no_room_for_a_wide_cluster_returns_a_bounded_line(int maxWidth, string expected)
    {
        var lines = TerminalText.Wrap("界a", maxWidth);

        Assert.Equal(new[] { expected }, lines);
        Assert.All(lines, line => Assert.True(TerminalText.Width(line) <= maxWidth));
    }

    [Fact]
    public void JoinWrapped_keeps_each_hint_whole()
    {
        var hints = new[] { "Up/Down: Navigate", "Enter: Select", "E: Edit JSON", "Esc: Exit" };
        var lines = TerminalText.JoinWrapped(hints, "  |  ", "    ", 40);
        Assert.Equal(new[] { "    Up/Down: Navigate  |  Enter: Select", "    E: Edit JSON  |  Esc: Exit" }, lines);
        Assert.All(lines, line => Assert.InRange(TerminalText.Width(line), 0, 40));
    }

    [Fact]
    public void JoinWrapped_truncates_a_single_part_that_exceeds_the_budget()
    {
        var lines = TerminalText.JoinWrapped(new[] { "界界", "a" }, " | ", " ", 3);

        Assert.Equal(new[] { " …", " a" }, lines);
        Assert.All(lines, line => Assert.InRange(TerminalText.Width(line), 0, 3));
    }
}
