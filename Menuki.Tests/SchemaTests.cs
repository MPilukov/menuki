using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;

namespace Menuki.Tests;

/// <summary>
/// Validates that the published JSON schema accepts every example config and
/// rejects malformed ones - so the schema stays in sync with the code.
/// </summary>
public class SchemaTests
{
    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Menuki.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    });

    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(RepoRoot.Value, "menuki.schema.json"))));

    private static readonly EvaluationOptions Options = new() { OutputFormat = OutputFormat.List };

    public static IEnumerable<object[]> ExampleConfigs()
    {
        var dir = Path.Combine(RepoRoot.Value, "Menuki", "examples");
        // Packs live in category subfolders (dev/, devops/, agents/, ...); recurse to find them all.
        foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            // plugin-demo uses custom plugin action types, which a schema of the
            // built-ins cannot enumerate - exclude it from strict conformance.
            if (Path.GetFileName(file) == "plugin-demo.json")
                continue;
            yield return new object[] { file };
        }
    }

    [Theory]
    [MemberData(nameof(ExampleConfigs))]
    public void Example_config_matches_schema(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        var result = Schema.Value.Evaluate(node, Options);
        Assert.True(result.IsValid, $"{Path.GetFileName(path)} failed schema:\n{Errors(result)}");
    }

    [Fact]
    public void Schema_rejects_shell_without_command()
    {
        var node = JsonNode.Parse("""
        { "title":"t","start_menu":"main","menus":{"main":{"items":[
          {"name":"x","action":{"type":"shell"}} ]}}}
        """);
        Assert.False(Schema.Value.Evaluate(node, Options).IsValid);
    }

    [Fact]
    public void Schema_rejects_choice_without_options()
    {
        var node = JsonNode.Parse("""
        { "title":"t","start_menu":"main","menus":{"main":{"items":[
          {"name":"x","action":{"type":"input+shell","command_template":"echo {e}",
            "inputs":[{"name":"e","type":"choice","prompt":"E"}]}} ]}}}
        """);
        Assert.False(Schema.Value.Evaluate(node, Options).IsValid);
    }

    [Fact]
    public void Schema_rejects_unknown_action_type()
    {
        var node = JsonNode.Parse("""
        { "title":"t","start_menu":"main","menus":{"main":{"items":[
          {"name":"x","action":{"type":"frobnicate"}} ]}}}
        """);
        Assert.False(Schema.Value.Evaluate(node, Options).IsValid);
    }

    [Fact]
    public void Schema_accepts_a_minimal_config()
    {
        var node = JsonNode.Parse("""
        { "start_menu":"main","menus":{"main":{"items":[
          {"name":"hi","action":{"type":"shell","command":"echo hi"}} ]}}}
        """);
        Assert.True(Schema.Value.Evaluate(node, Options).IsValid);
    }

    private static string Errors(EvaluationResults result) =>
        string.Join("\n", result.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => (d.Errors ?? new Dictionary<string, string>())
                .Where(e => e.Key != "const") // skip non-matching if-branch noise
                .Select(e => $"  {d.InstanceLocation}: {e.Key} - {e.Value}")));
}
