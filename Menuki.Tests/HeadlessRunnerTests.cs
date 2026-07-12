using Menuki.Actions;
using Menuki.Config;
using Menuki.Headless;
using Xunit;

namespace Menuki.Tests;

public class HeadlessRunnerTests
{
    private static MenuConfig Config(params MenuItemDefinition[] items) => new()
    {
        Title = "T",
        StartMenu = "main",
        Menus = new() { ["main"] = new MenuDefinition { Title = "Main", Items = items.ToList() } }
    };

    private static MenuItemDefinition Item(string name, ActionDefinition action) =>
        new() { Name = name, Action = action };

    [Fact]
    public void Action_ids_are_slugged_and_deduped()
    {
        var config = Config(
            Item("Deploy to Prod!", new ActionDefinition { Type = ActionTypes.Shell, Command = "echo x" }),
            Item("Deploy to Prod!", new ActionDefinition { Type = ActionTypes.Shell, Command = "echo y" }));

        var ids = HeadlessRunner.Enumerate(config).Select(a => a.Id).ToList();

        Assert.Equal("main/deploy-to-prod", ids[0]);
        Assert.Equal("main/deploy-to-prod-2", ids[1]);
    }

    [Fact]
    public void Execute_reports_nonzero_exit_code()
    {
        var config = Config(Item("fail", new ActionDefinition { Type = ActionTypes.Shell, Command = "exit 7" }));
        var action = HeadlessRunner.Find(config, "main/fail")!;

        var outcome = HeadlessRunner.Execute(action, new Dictionary<string, string>());

        Assert.False(outcome.Ok);
        Assert.Equal(7, outcome.ExitCode);
    }

    [Fact]
    public void Execute_captures_stdout()
    {
        var config = Config(Item("hi", new ActionDefinition { Type = ActionTypes.Shell, Command = "echo hello" }));
        var action = HeadlessRunner.Find(config, "main/hi")!;

        var outcome = HeadlessRunner.Execute(action, new Dictionary<string, string>());

        Assert.True(outcome.Ok);
        Assert.Equal("hello", outcome.StdOut);
    }

    [Fact]
    public void Execute_rejects_non_headless_action()
    {
        var config = Config(Item("go", new ActionDefinition { Type = ActionTypes.Submenu, Menu = "other" }));
        var action = HeadlessRunner.Find(config, "main/go")!;

        var outcome = HeadlessRunner.Execute(action, new Dictionary<string, string>());

        Assert.False(outcome.Ok);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public void InputShell_validates_typed_params()
    {
        var action = new ActionDefinition
        {
            Type = ActionTypes.InputShell,
            CommandTemplate = "echo {env}",
            Inputs = new() { new InputDefinition { Name = "env", Type = InputTypes.Choice, Prompt = "Env",
                Options = new() { "staging", "production" } } }
        };
        var config = Config(Item("deploy", action));
        var found = HeadlessRunner.Find(config, "main/deploy")!;

        var bad = HeadlessRunner.Execute(found, new Dictionary<string, string> { ["env"] = "prod" });
        Assert.False(bad.Ok);
        Assert.Contains("production", bad.Error);

        var good = HeadlessRunner.Execute(found, new Dictionary<string, string> { ["env"] = "production" });
        Assert.True(good.Ok);
        Assert.Equal("production", good.StdOut);
    }

    [Fact]
    public void Catalog_exposes_typed_input_contract()
    {
        var action = new ActionDefinition
        {
            Type = ActionTypes.InputShell,
            CommandTemplate = "echo {env}",
            Inputs = new() { new InputDefinition { Name = "env", Type = InputTypes.Choice, Prompt = "Env",
                Options = new() { "staging", "production" } } }
        };
        var config = Config(Item("deploy", action));

        var registry = ActionExecutorFactory.BuildDefault();
        var json = System.Text.Json.JsonSerializer.Serialize(HeadlessRunner.BuildCatalog(config, registry));

        Assert.Contains("\"type\":\"choice\"", json);
        Assert.Contains("production", json);
    }
}
