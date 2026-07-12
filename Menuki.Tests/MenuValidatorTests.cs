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
}
