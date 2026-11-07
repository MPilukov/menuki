using System.Text.Json;
using Menuki.Config;
using Xunit;
using static Menuki.Tests.PtyTerminal;

namespace Menuki.Tests;

public class NavigatorTests
{
    private static MenuItemDefinition Item(string name, string type, string? menu = null, string? path = null) =>
        new() { Name = name, Action = new ActionDefinition { Type = type, Menu = menu, Path = path } };

    private static MenuDefinition Menu(string title, params MenuItemDefinition[] items) =>
        new() { Title = title, Items = items.ToList() };

    private static MenuConfig Config(MenuDefinition main, params (string Id, MenuDefinition Menu)[] others) =>
        new()
        {
            Title = "Navigator test",
            StartMenu = "main",
            Editor = "/nonexistent/menuki-test-editor",
            Menus = new Dictionary<string, MenuDefinition> { ["main"] = main }
                .Concat(others.Select(x => new KeyValuePair<string, MenuDefinition>(x.Id, x.Menu)))
                .ToDictionary(x => x.Key, x => x.Value)
        };

    [Fact]
    public async Task Returning_from_submenu_selects_the_entered_item()
    {
        using var files = new ConfigFiles();
        var path = files.Save("main", Config(
            Menu("Main", Item("First", ActionTypes.Submenu, "child"),
                Item("Second", ActionTypes.Submenu, "child"),
                Item("Target", ActionTypes.Submenu, "child")),
            ("child", Menu("Child", Item("Only", ActionTypes.Exit)))));

        using var terminal = new PtyTerminal(path, files.Directory);
        await ExpectSelected(terminal, "Main", "First");
        terminal.Send(Down + Down + Enter);
        await ExpectSelected(terminal, "Child", "Only");
        terminal.Send(Escape);
        await ExpectSelected(terminal, "Main", "Target");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Returning_from_another_config_selects_the_open_config_item()
    {
        using var files = new ConfigFiles();
        files.Save("nested", Config(Menu("Nested", Item("Only", ActionTypes.Exit))));
        var path = files.Save("main", Config(Menu("Main",
            Item("First", ActionTypes.Exit),
            Item("Open nested", ActionTypes.OpenConfig, path: "nested.json"))));

        using var terminal = new PtyTerminal(path, files.Directory);
        await ExpectSelected(terminal, "Main", "First");
        terminal.Send(Down + Enter);
        await ExpectSelected(terminal, "Nested", "Only");
        terminal.Send(Escape);
        await ExpectSelected(terminal, "Main", "Open nested");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Theory]
    [InlineData("__edit_json__", "Failed to launch editor")]
    [InlineData("__validate__", "Press any key to continue")]
    public async Task Returning_from_editor_or_validate_keeps_the_selected_item(string result, string prompt)
    {
        using var files = new ConfigFiles();
        var path = files.Save("main", Config(Menu("Main",
            Item("First", ActionTypes.Exit), Item("Target", ActionTypes.Submenu, result))));

        using var terminal = new PtyTerminal(path, files.Directory);
        await ExpectSelected(terminal, "Main", "First");
        // A submenu executor can return a Navigator sentinel, making this an item
        // selection (where MenuEngine records SelectedIndex), not a keyboard shortcut.
        terminal.Send(Down + Enter);
        await terminal.ExpectText(prompt);
        if (prompt != "Press any key to continue")
            await terminal.ExpectText("Press any key to continue");
        terminal.Send("x");
        await ExpectSelected(terminal, "Main", "Target");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Theory]
    [InlineData("e", "Failed to launch editor")]
    [InlineData("v", "Press any key to continue")]
    public async Task Editor_hotkey_after_arrow_move_keeps_the_selected_item(string hotkey, string prompt)
    {
        using var files = new ConfigFiles();
        var path = files.Save("main", Config(Menu("Main",
            Item("First", ActionTypes.Exit), Item("Target", ActionTypes.Exit))));

        using var terminal = new PtyTerminal(path, files.Directory);
        await ExpectSelected(terminal, "Main", "First");
        terminal.Send(Down + hotkey);
        await terminal.ExpectText(prompt);
        if (prompt != "Press any key to continue")
            await terminal.ExpectText("Press any key to continue");
        terminal.Send("x");
        await ExpectSelected(terminal, "Main", "Target");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Returning_from_settings_selects_the_settings_item()
    {
        using var files = new ConfigFiles();
        var path = files.Save("main", Config(Menu("Main",
            Item("First", ActionTypes.Exit), Item("Settings item", ActionTypes.Settings))));

        using var terminal = new PtyTerminal(path, files.Directory);
        await ExpectSelected(terminal, "Main", "First");
        terminal.Send(Down + Enter);
        await ExpectSelected(terminal, "Settings", "Theme:");
        terminal.Send(Escape);
        await ExpectSelected(terminal, "Main", "Settings item");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Each_menu_remembers_its_own_selection()
    {
        using var files = new ConfigFiles();
        var path = files.Save("main", Config(
            Menu("Main", Item("First", ActionTypes.Exit), Item("Enter child", ActionTypes.Submenu, "child")),
            ("child", Menu("Child", Item("Zero", ActionTypes.Exit), Item("One", ActionTypes.Exit),
                Item("Enter grandchild", ActionTypes.Submenu, "grandchild"))),
            ("grandchild", Menu("Grandchild", Item("Only", ActionTypes.Exit)))));

        using var terminal = new PtyTerminal(path, files.Directory);
        await ExpectSelected(terminal, "Main", "First");
        terminal.Send(Down + Enter);
        await ExpectSelected(terminal, "Child", "Zero");
        terminal.Send(Down + Down + Enter);
        await ExpectSelected(terminal, "Grandchild", "Only");
        terminal.Send(Escape);
        await ExpectSelected(terminal, "Child", "Enter grandchild");
        terminal.Send(Escape);
        await ExpectSelected(terminal, "Main", "Enter child");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    private static async Task ExpectSelected(PtyTerminal terminal, string title, string selected) =>
        Assert.Contains($"▶ {selected}", await terminal.ExpectMenu(title));

    private sealed class ConfigFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"menuki-navigator-{Guid.NewGuid():N}");

        public ConfigFiles() => System.IO.Directory.CreateDirectory(Directory);

        public string Save(string name, MenuConfig config)
        {
            var path = Path.Combine(Directory, name + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(config));
            return path;
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
