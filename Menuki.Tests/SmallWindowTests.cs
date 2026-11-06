using System.Text.Json;
using Menuki.Engine;
using Xunit;
using static Menuki.Tests.PtyTerminal;

namespace Menuki.Tests;

/// <summary>
/// A menu taller than its window (an iTerm pane split in four) scrolls inside the window
/// instead of pushing the terminal, which left the marker on the wrong row and old text behind.
/// </summary>
public class SmallWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"menuki-small-{Guid.NewGuid():N}");

    public SmallWindowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Config(int items)
    {
        var config = new
        {
            title = "Small window test",
            start_menu = "main",
            menus = new
            {
                main = new
                {
                    title = "Main",
                    items = Enumerable.Range(1, items)
                        .Select(i => new { name = $"Item {i:D2}", action = new { type = "shell", command = "true" } })
                        .ToArray()
                }
            }
        };
        var path = Path.Combine(_dir, "main.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return path;
    }

    [Fact]
    public async Task A_menu_taller_than_the_window_scrolls_to_the_selected_item()
    {
        using var terminal = new PtyTerminal(Config(20), _dir, size: (12, 60));
        var frame = await terminal.ExpectText("Up/Down: Navigate");
        Assert.Contains("-------- Main --------  1/20", frame);
        Assert.Contains("Item 01", frame);
        Assert.DoesNotContain("Item 20", frame);   // off screen until scrolled to

        terminal.Send(Up);                          // wraps to the last item
        var scrolled = await terminal.ExpectText("20/20");
        Assert.Contains("Item 20", scrolled);

        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Theory]
    [InlineData(12, 60, 20, 0, 0)]
    [InlineData(22, 70, 20, 6, 4)]
    [InlineData(40, 100, 20, 6, 3)]
    public void The_frame_never_exceeds_the_window(int height, int width, int items, int header, int footer)
    {
        var fit = MenuLayout.Compute(height, header, footer, items);
        Assert.True(fit.HeaderLines + fit.ItemRows + fit.FooterLines <= height,
            $"{fit} does not fit {height}x{width}");
        Assert.True(fit.ItemRows >= Math.Min(items, height / 2));
    }

    [Fact]
    public void Everything_is_shown_when_the_height_is_unknown()
    {
        var fit = MenuLayout.Compute(0, 6, 4, 20);
        Assert.Equal(new MenuLayout.Fit(6, 4, 20), fit);
    }

    [Fact]
    public void The_title_is_the_last_header_line_to_go()
    {
        var fit = MenuLayout.Compute(6, 6, 4, 20);
        Assert.Equal(0, fit.FooterLines);
        Assert.Equal(3, fit.HeaderLines);   // the top of the header (info panel) went first
        Assert.Equal(3, fit.ItemRows);
    }
}

public class TerminalTextTests
{
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

    [Fact]
    public void Wrap_breaks_at_spaces_and_indents_continuations()
    {
        var lines = TerminalText.Wrap("    one two three four", 14, "    ");
        Assert.Equal(new[] { "    one two", "    three four" }, lines);
        Assert.All(lines, l => Assert.True(TerminalText.Width(l) <= 14));
    }

    [Fact]
    public void JoinWrapped_keeps_each_hint_whole()
    {
        var hints = new[] { "Up/Down: Navigate", "Enter: Select", "E: Edit JSON", "Esc: Exit" };
        var lines = TerminalText.JoinWrapped(hints, "  |  ", "    ", 40);
        Assert.Equal(new[] { "    Up/Down: Navigate  |  Enter: Select", "    E: Edit JSON  |  Esc: Exit" }, lines);
    }
}
