using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Menuki.Config;
using Xunit;

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

        using var terminal = new Terminal(path, files.Directory);
        await terminal.ExpectMenu("Main", "First");
        terminal.Send(Down + Down + Enter);
        await terminal.ExpectMenu("Child", "Only");
        terminal.Send(Escape);
        await terminal.ExpectMenu("Main", "Target");
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

        using var terminal = new Terminal(path, files.Directory);
        await terminal.ExpectMenu("Main", "First");
        terminal.Send(Down + Enter);
        await terminal.ExpectMenu("Nested", "Only");
        terminal.Send(Escape);
        await terminal.ExpectMenu("Main", "Open nested");
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

        using var terminal = new Terminal(path, files.Directory);
        await terminal.ExpectMenu("Main", "First");
        // A submenu executor can return a Navigator sentinel, making this an item
        // selection (where MenuEngine records SelectedIndex), not a keyboard shortcut.
        terminal.Send(Down + Enter);
        await terminal.ExpectText(prompt);
        if (prompt != "Press any key to continue")
            await terminal.ExpectText("Press any key to continue");
        terminal.Send("x");
        await terminal.ExpectMenu("Main", "Target");
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

        using var terminal = new Terminal(path, files.Directory);
        await terminal.ExpectMenu("Main", "First");
        terminal.Send(Down + hotkey);
        await terminal.ExpectText(prompt);
        if (prompt != "Press any key to continue")
            await terminal.ExpectText("Press any key to continue");
        terminal.Send("x");
        await terminal.ExpectMenu("Main", "Target");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    [Fact]
    public async Task Returning_from_settings_selects_the_settings_item()
    {
        using var files = new ConfigFiles();
        var path = files.Save("main", Config(Menu("Main",
            Item("First", ActionTypes.Exit), Item("Settings item", ActionTypes.Settings))));

        using var terminal = new Terminal(path, files.Directory);
        await terminal.ExpectMenu("Main", "First");
        terminal.Send(Down + Enter);
        await terminal.ExpectMenu("Settings", "Theme:");
        terminal.Send(Escape);
        await terminal.ExpectMenu("Main", "Settings item");
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

        using var terminal = new Terminal(path, files.Directory);
        await terminal.ExpectMenu("Main", "First");
        terminal.Send(Down + Enter);
        await terminal.ExpectMenu("Child", "Zero");
        terminal.Send(Down + Down + Enter);
        await terminal.ExpectMenu("Grandchild", "Only");
        terminal.Send(Escape);
        await terminal.ExpectMenu("Child", "Enter grandchild");
        terminal.Send(Escape);
        await terminal.ExpectMenu("Main", "Enter child");
        terminal.Send(Escape);
        await terminal.ExpectExit();
    }

    private const string Down = "\u001bOB"; // Application cursor mode requested by the terminal.
    private const string Enter = "\r";
    private const string Escape = "\u001b";

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

    // Console.ReadKey and CursorTop require a terminal. `script` gives the real CLI a pty;
    // this harness answers its cursor-position queries and records each rendered menu.
    private sealed class Terminal : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new();
        private readonly object _gate = new();
        private readonly Task _reader;
        private int _cursor;
        private int _answeredQueries;

        public Terminal(string configPath, string settingsHome)
        {
            var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Menuki/bin",
                AppContext.BaseDirectory.Contains("/Release/") ? "Release" : "Debug", "net8.0", "Menuki.dll"));
            var info = new ProcessStartInfo("script")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            if (OperatingSystem.IsMacOS())
            {
                foreach (var arg in new[] { "-q", "/dev/null", "dotnet", dll, "--config", configPath })
                    info.ArgumentList.Add(arg);
            }
            else
            {
                info.ArgumentList.Add("-q");
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add($"dotnet {Quote(dll)} --config {Quote(configPath)}");
                info.ArgumentList.Add("/dev/null");
            }
            info.Environment["DOTNET_ROLL_FORWARD"] = "Major";
            info.Environment["MENUKI_HOME"] = Path.Combine(settingsHome, "settings");
            _process = Process.Start(info)!;
            _reader = ReadOutput();
        }

        private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

        private async Task ReadOutput()
        {
            var buffer = new char[4096];
            while (true)
            {
                var count = await _process.StandardOutput.ReadAsync(buffer);
                if (count == 0) return;
                int queries;
                lock (_gate)
                {
                    _output.Append(buffer, 0, count);
                    var total = _output.ToString().Split("\u001b[6n").Length - 1;
                    queries = total - _answeredQueries;
                    _answeredQueries = total;
                }
                for (var i = 0; i < queries; i++) Send("\u001b[1;1R");
            }
        }

        public void Send(string keys)
        {
            lock (_gate)
            {
                _process.StandardInput.Write(keys);
                _process.StandardInput.Flush();
            }
        }

        public async Task ExpectMenu(string title, string selected)
        {
            var frame = await Until("Up/Down: Navigate");
            Assert.Contains($"-------- {title} --------", frame);
            Assert.Contains($"▶ {selected}", frame);
        }

        public async Task ExpectText(string text) => await Until(text);

        private async Task<string> Until(string token)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    var output = _output.ToString();
                    var found = output.IndexOf(token, _cursor, StringComparison.Ordinal);
                    if (found >= 0)
                    {
                        var frame = output[_cursor..(found + token.Length)];
                        _cursor = found + token.Length;
                        return frame;
                    }
                }
                if (_process.HasExited) break;
                await Task.Delay(10);
            }
            throw new Xunit.Sdk.XunitException($"Timed out waiting for '{token}'. Output:\n{_output}");
        }

        public async Task ExpectExit()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _process.WaitForExitAsync(timeout.Token);
            await _reader;
            Assert.Equal(0, _process.ExitCode);
        }

        public void Dispose()
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }
    }
}
