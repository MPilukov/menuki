using System.Text.Json;
using Menuki.Authoring;
using Menuki.Config;
using Menuki.Examples;
using Xunit;

namespace Menuki.Tests;

public class ExampleTests
{
    [Fact]
    public void List_is_nonempty_and_excludes_plugin_demo()
    {
        var names = ExampleCatalog.List().Select(e => e.Name).ToList();
        Assert.NotEmpty(names);
        Assert.Contains("git", names);
        Assert.Contains("docker", names);
        Assert.DoesNotContain("plugin-demo", names); // needs plugins/repo paths
        Assert.Equal(names, names.OrderBy(n => n, System.StringComparer.Ordinal).ToList()); // sorted
    }

    [Fact]
    public void Examples_are_grouped_into_known_categories()
    {
        var byName = ExampleCatalog.List().ToDictionary(e => e.Name, e => e.Category);
        Assert.Equal("dev", byName["git"]);
        Assert.Equal("devops", byName["docker"]);
        Assert.Equal("agents", byName["agent-mode"]);
        Assert.Equal("appearance", byName["colors-demo"]);
        Assert.Equal("demos", byName["typed-inputs-demo"]);
    }

    [Fact]
    public void Resolve_accepts_leaf_name_and_category_path()
    {
        Assert.Equal("docker", ExampleCatalog.Resolve("docker"));
        Assert.Equal("docker", ExampleCatalog.Resolve("devops/docker"));
        Assert.Equal("agent-mode", ExampleCatalog.Resolve("agents/agent-mode"));
        Assert.Equal("git", ExampleCatalog.Resolve("DEV/GIT")); // case-insensitive
        Assert.Null(ExampleCatalog.Resolve("agents/nope"));
        Assert.Null(ExampleCatalog.Resolve("wrongcat/docker")); // wrong category rejected
    }

    [Fact]
    public void ReadJson_unknown_returns_null()
    {
        Assert.Null(ExampleCatalog.ReadJson("does-not-exist"));
        Assert.Null(ExampleCatalog.ReadJson("plugin-demo")); // excluded
        Assert.False(ExampleCatalog.Exists("plugin-demo"));
    }

    public static IEnumerable<object[]> ExampleNames() =>
        ExampleCatalog.List().Select(e => new object[] { e.Name });

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_example_parses_and_validates(string name)
    {
        var json = ExampleCatalog.ReadJson(name);
        Assert.NotNull(json);

        var config = JsonSerializer.Deserialize<MenuConfig>(json!);
        Assert.NotNull(config);
        Assert.NotEmpty(config!.Menus);

        var result = MenuValidator.Validate(config);
        var errors = result.Issues.Where(i => i.Level == "error").Select(i => $"{i.Where}: {i.Message}");
        Assert.True(result.Ok, $"{name} has validation errors:\n{string.Join("\n", errors)}");
    }
}
