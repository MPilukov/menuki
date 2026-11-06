using System.Text.Json;
using Menuki.Config;
using Xunit;

namespace Menuki.Tests;

public class ConfigJsonTests
{
    [Fact]
    public void Round_trips_a_config()
    {
        var config = new MenuConfig
        {
            Title = "Demo",
            StartMenu = "main",
            Menus = new()
            {
                ["main"] = new MenuDefinition
                {
                    Title = "Main",
                    Items = new()
                    {
                        new MenuItemDefinition
                        {
                            Name = "Build",
                            Description = "compile",
                            Action = new ActionDefinition { Type = ActionTypes.Shell, Command = "make" }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(config);
        var back = JsonSerializer.Deserialize<MenuConfig>(json)!;

        Assert.Equal("Demo", back.Title);
        var item = back.Menus["main"].Items[0];
        Assert.Equal("Build", item.Name);
        Assert.Equal("compile", item.Description);
        Assert.Equal("make", item.Action.Command);
    }

    [Theory]
    [InlineData("100", "100")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("\"dev\"", "dev")]
    public void Default_accepts_string_number_or_boolean(string jsonValue, string expected)
    {
        var json = $$"""
        {
          "title": "t", "start_menu": "main",
          "menus": { "main": { "title": "M", "items": [
            { "name": "x", "action": { "type": "input+shell", "command_template": "echo {v}",
              "inputs": [ { "name": "v", "prompt": "V", "default": {{jsonValue}} } ] } }
          ] } }
        }
        """;

        var config = JsonSerializer.Deserialize<MenuConfig>(json)!;
        var input = config.Menus["main"].Items[0].Action.Inputs![0];

        Assert.Equal(expected, input.Default);
    }

    [Fact]
    public void Missing_action_type_deserializes_to_empty_string()
    {
        var json = """
        { "title": "t", "start_menu": "main",
          "menus": { "main": { "title": "M", "items": [ { "name": "x", "action": {} } ] } } }
        """;

        var config = JsonSerializer.Deserialize<MenuConfig>(json)!;
        Assert.Equal("", config.Menus["main"].Items[0].Action.Type);
    }

    [Fact]
    public void Parses_requires()
    {
        var json = """
        {"requires":[{"command":"iterm-layout","install":"brew install x","os":"macos"},{"command":"ls"}],"menus":{}}
        """;
        var c = JsonSerializer.Deserialize<MenuConfig>(json)!;

        Assert.Equal(2, c.Requires!.Count);
        Assert.Equal("iterm-layout", c.Requires[0].Command);
        Assert.Equal("brew install x", c.Requires[0].Install);
        Assert.Equal("macos", c.Requires[0].Os);
        Assert.Null(c.Requires[1].Install);
        Assert.Null(c.Requires[1].Os);
    }

    [Fact]
    public void Config_without_requires_has_null_requires()
    {
        var c = JsonSerializer.Deserialize<MenuConfig>("""{"menus":{}}""")!;
        Assert.Null(c.Requires);
    }
}
