using Menuki.Authoring;
using Menuki.Config;
using Xunit;

namespace Menuki.Tests;

public class MenuValidatorTests
{
    private static MenuConfig One(ActionDefinition action, string menu = "main") => new()
    {
        Title = "T",
        StartMenu = "main",
        Menus = new()
        {
            [menu] = new MenuDefinition
            {
                Title = "M",
                Items = new() { new MenuItemDefinition { Name = "item", Action = action } }
            }
        }
    };

    private static bool HasError(MenuConfig c) =>
        MenuValidator.Validate(c).Issues.Any(i => i.Level == "error");

    [Fact]
    public void Valid_config_passes()
    {
        var c = One(new ActionDefinition { Type = ActionTypes.Shell, Command = "echo hi" });
        Assert.True(MenuValidator.Validate(c).Ok);
    }

    [Fact]
    public void Shell_without_command_is_an_error()
    {
        var c = One(new ActionDefinition { Type = ActionTypes.Shell });
        Assert.True(HasError(c));
    }

    [Fact]
    public void Dangling_submenu_is_an_error()
    {
        var c = One(new ActionDefinition { Type = ActionTypes.Submenu, Menu = "nowhere" });
        Assert.True(HasError(c));
    }

    [Fact]
    public void Open_config_without_path_is_an_error()
    {
        var c = One(new ActionDefinition { Type = ActionTypes.OpenConfig });
        Assert.True(HasError(c));
    }

    [Fact]
    public void Negative_retry_is_an_error()
    {
        var c = One(new ActionDefinition { Type = ActionTypes.Shell, Command = "echo hi", Retry = -1 });
        Assert.True(HasError(c));
    }

    [Fact]
    public void Choice_input_without_options_is_an_error()
    {
        var c = One(new ActionDefinition
        {
            Type = ActionTypes.InputShell,
            CommandTemplate = "echo {e}",
            Inputs = new() { new InputDefinition { Name = "e", Type = InputTypes.Choice, Prompt = "E" } }
        });
        Assert.True(HasError(c));
    }

    [Fact]
    public void Unreachable_menu_is_a_warning_not_an_error()
    {
        var c = new MenuConfig
        {
            Title = "T",
            StartMenu = "main",
            Menus = new()
            {
                ["main"] = new MenuDefinition { Title = "M", Items = new()
                    { new MenuItemDefinition { Name = "x", Action = new ActionDefinition { Type = ActionTypes.Exit } } } },
                ["orphan"] = new MenuDefinition { Title = "O", Items = new()
                    { new MenuItemDefinition { Name = "y", Action = new ActionDefinition { Type = ActionTypes.Exit } } } }
            }
        };

        var result = MenuValidator.Validate(c);
        Assert.True(result.Ok); // warning only
        Assert.Contains(result.Issues, i => i.Level == "warning" && i.Where == "orphan");
    }

    private static MenuConfig WithRequires(params RequirementDefinition[] req)
    {
        var c = One(new ActionDefinition { Type = ActionTypes.Exit });
        c.Requires = req.ToList();
        return c;
    }

    [Fact]
    public void Requires_entry_without_command_is_an_error()
    {
        var r = MenuValidator.Validate(WithRequires(new RequirementDefinition { Install = "brew install x" }));
        Assert.False(r.Ok);
        Assert.Contains(r.Issues, i => i.Level == "error" && i.Where == "requires[0]");
    }

    [Fact]
    public void Requires_with_unknown_os_is_an_error()
    {
        var r = MenuValidator.Validate(WithRequires(new RequirementDefinition { Command = "ls", Os = "beos" }));
        Assert.False(r.Ok);
        Assert.Contains(r.Issues, i => i.Level == "error" && i.Message.Contains("beos"));
    }

    [Fact]
    public void Null_requires_entry_is_an_error_not_a_crash()
    {
        var c = WithRequires(new RequirementDefinition[] { null! });
        var r = MenuValidator.Validate(c, Path.GetTempPath(), "linux");
        Assert.False(r.Ok);
        Assert.Contains(r.Issues, i => i.Level == "error" && i.Where == "requires[0]");
        Assert.Empty(Menuki.Engine.Requirements.Missing(c, path: "", os: "linux"));
    }

    [Fact]
    public void Missing_required_command_is_a_warning_and_validation_still_passes()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"menuki-val-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var c = WithRequires(new RequirementDefinition { Command = "menuki-absent-tool", Install = "brew install x" });
            var r = MenuValidator.Validate(c, dir, "linux");
            Assert.True(r.Ok);
            var w = Assert.Single(r.Issues, i => i.Level == "warning");
            Assert.Contains("menuki-absent-tool", w.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Requirement_for_another_os_gives_no_warning()
    {
        var c = WithRequires(new RequirementDefinition { Command = "menuki-absent-tool", Os = "macos" });
        var r = MenuValidator.Validate(c, "", "linux");
        Assert.True(r.Ok);
        Assert.DoesNotContain(r.Issues, i => i.Level == "warning");
    }
}
