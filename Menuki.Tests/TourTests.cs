using Menuki.Config;
using Menuki.Tour;
using Xunit;

namespace Menuki.Tests;

public class TourTests
{
    private static DetectedEnv Env(bool windows) =>
        new("TestOS", "sh", "nano", new Dictionary<string, bool> { ["git"] = false, ["docker"] = false })
        {
            IsWindows = windows
        };

    [Fact]
    public void IsSection_recognizes_sections_only()
    {
        Assert.True(TourMenuBuilder.IsSection("commands"));
        Assert.True(TourMenuBuilder.IsSection("what"));
        Assert.False(TourMenuBuilder.IsSection("main"));
        Assert.False(TourMenuBuilder.IsSection("nav_big"));
    }

    [Fact]
    public void Decorate_marks_visited_and_updates_percentage()
    {
        var config = TourMenuBuilder.Build(Env(windows: false));
        TourMenuBuilder.Decorate(config, new HashSet<string> { "commands" });

        var items = config.Menus["main"].Items;
        var visited = items.First(i => i.Action.Menu == "commands");
        var notVisited = items.First(i => i.Action.Menu == "what");

        Assert.StartsWith("✓ ", visited.Name);
        Assert.StartsWith("○ ", notVisited.Name);

        var explored = config.Menus["main"].Info!.First(e => e.Label == "Explored").Value;
        Assert.Contains("1 / 9", explored);
    }

    [Fact]
    public void Decorate_is_idempotent()
    {
        var config = TourMenuBuilder.Build(Env(windows: false));
        var visited = new HashSet<string> { "commands" };

        TourMenuBuilder.Decorate(config, visited);
        TourMenuBuilder.Decorate(config, visited);

        var name = config.Menus["main"].Items.First(i => i.Action.Menu == "commands").Name;
        Assert.Equal("✓ Run safe commands", name); // no doubled marker
    }

    [Fact]
    public void Commands_adapt_to_windows()
    {
        var win = TourMenuBuilder.Build(Env(windows: true)).Menus["commands"].Items;
        var unix = TourMenuBuilder.Build(Env(windows: false)).Menus["commands"].Items;

        Assert.Equal("cd", Command(win, "Show current directory"));
        Assert.Equal("ver", Command(win, "Show OS"));

        Assert.Equal("pwd", Command(unix, "Show current directory"));
        Assert.Equal("uname -a", Command(unix, "Show OS"));
    }

    private static string? Command(List<MenuItemDefinition> items, string name) =>
        items.First(i => i.Name == name).Action.Command;
}
