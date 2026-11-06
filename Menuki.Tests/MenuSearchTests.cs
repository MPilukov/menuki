using System.Text.Json;
using Xunit;
using static Menuki.Tests.PtyTerminal;

namespace Menuki.Tests;

/// <summary>'/' opens the menu search; inside it every key, command letters included, goes to the query.</summary>
public class MenuSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"menuki-search-{Guid.NewGuid():N}");
    private string Settings => Path.Combine(_dir, "settings", "settings.json");
    private string SettingsText => File.Exists(Settings) ? File.ReadAllText(Settings) : "";

    public MenuSearchTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private PtyTerminal Start()
    {
        var item = (string name, object action) => new { name, description = $"About {name}", action };
        var config = new
        {
            title = "Search test",
            start_menu = "main",
            editor = "/nonexistent/menuki-test-editor",
            menus = new Dictionary<string, object>
            {
                ["main"] = new
                {
                    title = "Main",
                    items = new object[]
                    {
                        item("Deploy", new { type = "exit" }),
                        item("Terminal logs", new { type = "exit" }),
                        item("Test runner", new { type = "submenu", menu = "child" }),
                        item("Version info", new { type = "exit" })
                    }
                },
                ["child"] = new { title = "Child", items = new object[] { new { name = "Only", action = new { type = "exit" } } } }
            }
        };
        var path = Path.Combine(_dir, "main.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return new PtyTerminal(path, _dir);
    }

    [Fact]
    public async Task Slash_then_command_letters_filter_instead_of_running_commands()
    {
        using var terminal = Start();
        await terminal.ExpectMenu("Main");
        await terminal.ExpectText("/: Search");   // later in the same footer: wait for it, not a snapshot
        var settings = SettingsText;

        terminal.Send("/");
        await terminal.ExpectFrameWith("Search: _");
        terminal.Send("t");
        await terminal.ExpectFrameWith("Search: t");
        terminal.Send("e");
        var frame = await terminal.ExpectFrameWith("Search: te");
        Assert.Contains("Terminal logs", frame);
        Assert.Contains("Test runner", frame);
        Assert.DoesNotContain("Deploy", frame);
        await terminal.ExpectText("Esc: Close search");

        terminal.Send("v");   // Validate outside the search
        await terminal.ExpectFrameWith("Search: tev");
        Assert.Equal(settings, SettingsText);   // T did not cycle the theme
        Assert.DoesNotContain("Press any key to continue", terminal.Output);   // V did not validate

        terminal.Send(Escape);
        await terminal.ExpectMenu("Main");
        await terminal.ExpectText("Esc: Exit");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Space_question_mark_and_slash_go_into_the_query()
    {
        using var terminal = Start();
        await terminal.ExpectMenu("Main");
        terminal.Send("/t r");
        var frame = await terminal.ExpectFrameWith("Search: t r");
        Assert.Contains("Test runner", frame);
        Assert.DoesNotContain("Terminal logs", frame);
        terminal.Send("?/");
        await terminal.ExpectFrameWith("Search: t r?/");
        terminal.Send(Escape);
        await terminal.ExpectMenu("Main");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Esc_and_backspace_on_an_empty_query_close_the_search()
    {
        using var terminal = Start();
        await terminal.ExpectMenu("Main");
        terminal.Send("/x");
        await terminal.ExpectFrameWith("Search: x");
        terminal.Send(Backspace);
        await terminal.ExpectFrameWith("Search: _");
        terminal.Send(Backspace);   // empty query: closes the search
        Assert.DoesNotContain("Search:", await terminal.ExpectMenu("Main"));
        await terminal.ExpectText("Esc: Exit");

        terminal.Send("/zzz");      // nothing matches
        await terminal.ExpectFrameWith("Search: zzz");
        terminal.Send(Escape);
        var frame = await terminal.ExpectMenu("Main");
        Assert.DoesNotContain("Search:", frame);
        Assert.Contains("Deploy", frame);
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Closing_the_search_keeps_the_selected_item()
    {
        using var terminal = Start();
        await terminal.ExpectMenu("Main");
        terminal.Send("/info");   // only "Version info" matches
        await terminal.ExpectFrameWith("Search: info_");
        terminal.Send(Escape);
        var frame = await terminal.ExpectMenu("Main");
        Assert.DoesNotContain("Search:", frame);
        Assert.Contains("▶ Version info", frame);
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Enter_in_the_search_runs_the_found_item()
    {
        using var terminal = Start();
        await terminal.ExpectMenu("Main");
        terminal.Send("/runner");
        await terminal.ExpectFrameWith("Search: runner");
        terminal.Send(Enter);
        await terminal.ExpectMenu("Child");
        terminal.Send(Escape);
        // Coming back from a submenu builds the menu anew: the search is closed, one Esc exits.
        var frame = await terminal.ExpectMenu("Main");
        Assert.DoesNotContain("Search:", frame);
        Assert.Contains("▶ Test runner", frame);
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Without_slash_letters_are_commands_and_others_do_nothing()
    {
        using var terminal = Start();
        await terminal.ExpectMenu("Main");
        var settings = SettingsText;

        terminal.Send("d?");   // 'd' is no command: no search starts; '?' shows the description
        var frame = await terminal.ExpectText("About Deploy");   // from the first frame on
        Assert.DoesNotContain("Search:", frame);
        await terminal.ExpectText("Up/Down: Navigate");

        terminal.Send("t");
        await terminal.ExpectMenu("Main");
        Assert.NotEqual(settings, SettingsText);   // T cycled the theme and saved it

        terminal.Send("v");
        await terminal.ExpectText("Press any key to continue");
        terminal.Send("x");
        await terminal.ExpectMenu("Main");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }
}
