using System.Text.Json;
using Menuki.Config;
using Menuki.Engine;
using Xunit;
using static Menuki.Tests.PtyTerminal;

namespace Menuki.Tests;

/// <summary>PATH lookup for <c>requires</c>, with PATH and OS passed in, plus the menu under a pty.</summary>
public class RequiresTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"menuki-requires-{Guid.NewGuid():N}");
    private string Bin => Path.Combine(_dir, "bin");

    public RequiresTests() => Directory.CreateDirectory(Bin);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Stub(string name)
    {
        var file = Path.Combine(Bin, name);
        File.WriteAllText(file, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static MenuConfig Config(params RequirementDefinition[] requires) => new()
    {
        Requires = requires.ToList(),
        Menus = { ["main"] = new MenuDefinition { Items = { new MenuItemDefinition { Name = "x" } } } }
    };

    [Fact]
    public void Finds_a_command_in_a_path_directory_and_reports_a_missing_one()
    {
        if (OperatingSystem.IsWindows()) return;
        Stub("menuki-tool-a");
        var config = Config(
            new RequirementDefinition { Command = "menuki-tool-a" },
            new RequirementDefinition { Command = "menuki-tool-b", Install = "brew install b" });

        var missing = Requirements.Missing(config, Bin, "linux");

        var only = Assert.Single(missing);
        Assert.Equal("menuki-tool-b", only.Command);
    }

    [Fact]
    public void A_file_without_the_execute_bit_is_not_a_command()
    {
        if (OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(Bin, "plain"), "data");
        Assert.False(Requirements.IsOnPath("plain", Bin));
    }

    [Fact]
    public void Os_filter_skips_requirements_for_another_os()
    {
        var config = Config(new RequirementDefinition { Command = "menuki-mac-only", Os = "macos" });

        Assert.Empty(Requirements.Missing(config, Bin, "linux"));
        Assert.Single(Requirements.Missing(config, Bin, "macos"));
    }

    [Fact]
    public void Os_filter_is_case_insensitive_and_no_os_applies_everywhere()
    {
        var config = Config(
            new RequirementDefinition { Command = "menuki-a", Os = "MacOS" },
            new RequirementDefinition { Command = "menuki-b" });

        Assert.Equal(new[] { "menuki-b" }, Requirements.Missing(config, Bin, "linux").Select(r => r.Command));
        Assert.Equal(2, Requirements.Missing(config, Bin, "macos").Count);
    }

    [Fact]
    public void Config_without_requires_has_nothing_missing()
    {
        Assert.Empty(Requirements.Missing(new MenuConfig(), Bin, "linux"));
    }

    [Fact]
    public void Describe_formats_the_missing_line_with_and_without_install()
    {
        var text = Requirements.Describe(new[]
        {
            new RequirementDefinition { Command = "a", Install = "brew install a" },
            new RequirementDefinition { Command = "b" }
        });
        Assert.Equal("Missing: a (install: brew install a)\nMissing: b", text);
    }

    private string WriteMenu(object[] requires)
    {
        var config = new
        {
            title = "Req test",
            start_menu = "main",
            requires,
            menus = new Dictionary<string, object>
            {
                ["main"] = new
                {
                    title = "Main",
                    items = new object[] { new { name = "Leave", action = new { type = "exit" } } }
                }
            }
        };
        var path = Path.Combine(_dir, "main.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return path;
    }

    [Fact]
    public async Task Missing_command_shows_notice_and_install_item()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = WriteMenu(new object[]
            { new { command = "menuki-absent-tool", install = "echo installing" } });

        using var terminal = new PtyTerminal(path, _dir);
        var frame = await terminal.ExpectMenu("Main");

        Assert.Contains("Missing: menuki-absent-tool (install: echo installing)", frame);
        Assert.Contains("Install missing tools", frame);
    }

    [Fact]
    public async Task Present_command_shows_neither_notice_nor_install_item()
    {
        if (OperatingSystem.IsWindows()) return;
        Stub("menuki-present-tool");
        var path = WriteMenu(new object[] { new { command = "menuki-present-tool", install = "echo no" } });

        using var terminal = new PtyTerminal(path, _dir, prependPath: Bin);
        var frame = await terminal.ExpectMenu("Main");

        Assert.Contains("Leave", frame);
        Assert.DoesNotContain("Missing:", frame);
        Assert.DoesNotContain("Install missing tools", frame);
    }

    [Fact]
    public async Task Notice_stays_after_a_redraw()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = WriteMenu(new object[] { new { command = "menuki-absent-tool", install = "echo hi" } });

        using var terminal = new PtyTerminal(path, _dir);
        await terminal.ExpectMenu("Main");
        terminal.Send("/");                         // opening the search replaces the line...
        await terminal.ExpectFrameWith("Search: _");
        terminal.Send(Escape);                      // ...closing it must bring it back
        Assert.Contains("Missing: menuki-absent-tool", await terminal.ExpectMenu("Main"));
    }

    [Fact]
    public async Task Start_menu_reached_as_a_submenu_keeps_the_right_selection()
    {
        if (OperatingSystem.IsWindows()) return;
        var config = new
        {
            title = "Req test",
            start_menu = "main",
            requires = new object[] { new { command = "menuki-absent-tool" } },
            menus = new Dictionary<string, object>
            {
                ["main"] = new
                {
                    title = "Main",
                    items = new object[]
                    {
                        new { name = "Go child", action = new { type = "submenu", menu = "child" } },
                        new { name = "Leave", action = new { type = "exit" } }
                    }
                },
                ["child"] = new
                {
                    title = "Child",
                    items = new object[] { new { name = "Back to main", action = new { type = "submenu", menu = "main" } } }
                }
            }
        };
        var path = Path.Combine(_dir, "main.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));

        using var terminal = new PtyTerminal(path, _dir);
        await terminal.ExpectMenu("Main");
        terminal.Send(Down + Enter);                // past "Install missing tools" to "Go child"
        await terminal.ExpectMenu("Child");
        terminal.Send(Enter);                       // main again, now as a submenu: no install item
        var frame = await terminal.ExpectMenu("Main");
        Assert.DoesNotContain("Install missing tools", frame);
        Assert.Contains("▶ Go child", frame);
    }

    [Fact]
    public async Task Install_item_runs_the_command_and_the_notice_disappears()
    {
        if (OperatingSystem.IsWindows()) return;
        var install = $"printf '#!/bin/sh\\n' > '{Bin}/menuki-late-tool' && chmod +x '{Bin}/menuki-late-tool'";
        var path = WriteMenu(new object[] { new { command = "menuki-late-tool", install } });

        using var terminal = new PtyTerminal(path, _dir, prependPath: Bin);
        var first = await terminal.ExpectMenu("Main");
        Assert.Contains("Missing: menuki-late-tool", first);

        terminal.Send(Enter);                       // "Install missing tools" is the first item
        await terminal.ExpectText("[y/N]");
        terminal.Send("y");
        await terminal.ExpectText("Press any key to continue...");
        terminal.Send(" ");

        var again = await terminal.ExpectMenu("Main");
        Assert.True(File.Exists(Path.Combine(Bin, "menuki-late-tool")));
        Assert.DoesNotContain("Missing:", again);
        Assert.DoesNotContain("Install missing tools", again);
        Assert.Contains("Leave", again);
    }
}
