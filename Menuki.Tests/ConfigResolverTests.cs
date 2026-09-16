using Menuki.Config;
using Xunit;

namespace Menuki.Tests;

public class ConfigResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mm-resolver-{Guid.NewGuid():N}");
    private readonly string _configs;

    public ConfigResolverTests()
    {
        _configs = Path.Combine(_root, "configs");
        Directory.CreateDirectory(_configs);
        File.WriteAllText(Path.Combine(_configs, "work.json"), "{}");
        File.WriteAllText(Path.Combine(_configs, "per-project.json"), "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("work")]
    [InlineData("work.json")]
    [InlineData("per-project")]
    public void Name_resolves_to_saved_config(string arg)
    {
        var path = ConfigResolver.Resolve(arg, _configs);
        Assert.NotNull(path);
        Assert.Equal(_configs, Path.GetDirectoryName(path));
    }

    [Fact]
    public void Existing_file_wins_over_saved_name()
    {
        var local = Path.Combine(_root, "work.json");
        File.WriteAllText(local, "{}");
        Assert.Equal(local, ConfigResolver.Resolve(local, _configs));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("./work.json")]
    [InlineData("sub/work")]
    [InlineData("-work")]
    [InlineData("")]
    [InlineData("..")]
    public void Unknown_names_and_missing_paths_resolve_to_null(string arg)
    {
        Assert.Null(ConfigResolver.Resolve(arg, _configs));
    }

    [Fact]
    public void Lists_saved_names_sorted_without_extension()
    {
        Assert.Equal(new[] { "per-project", "work" }, ConfigResolver.ListNames(_configs));
        Assert.Empty(ConfigResolver.ListNames(Path.Combine(_root, "missing")));
    }
}
