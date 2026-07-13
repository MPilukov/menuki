using System.Text.Json;
using Menuki.Actions;
using Menuki.Config;
using Menuki.Engine;

namespace Menuki.Examples;

/// <summary>
/// The <c>examples</c> subcommand: browse and use the built-in example packs.
///
///   menuki examples                 list the packs
///   menuki examples &lt;name&gt;           run a pack interactively
///   menuki examples &lt;name&gt; --print   print its JSON to stdout
///   menuki examples &lt;name&gt; --save [path]   write its JSON to a file (default ./&lt;name&gt;.json)
/// </summary>
public static class ExamplesCli
{
    public static int Run(string[] args)
    {
        var input = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;

        if (input == null)
        {
            PrintList();
            return 0;
        }

        // Accept "docker" or "devops/docker"; resolve to the canonical leaf name.
        var name = ExampleCatalog.Resolve(input);
        var json = name == null ? null : ExampleCatalog.ReadJson(name);
        if (json == null)
        {
            Console.Error.WriteLine($"No example named '{input}'.");
            PrintList();
            return 2;
        }

        if (HasFlag(args, "--print"))
        {
            Console.Out.Write(json);
            if (!json.EndsWith("\n", StringComparison.Ordinal)) Console.Out.WriteLine();
            return 0;
        }

        if (HasFlag(args, "--save"))
            return Save(name, json, OptionAfter(args, "--save"));

        return RunInteractive(name, json);
    }

    private static void PrintList()
    {
        var examples = ExampleCatalog.List();
        Console.WriteLine("Example packs (run with: menuki examples <name>):\n");

        var width = examples.Count == 0 ? 0 : examples.Max(e => e.Name.Length);
        foreach (var group in GroupByCategory(examples))
        {
            Console.WriteLine($"{CategoryLabel(group.Key)}:");
            foreach (var e in group)
                Console.WriteLine($"  {e.Name.PadRight(width)}   {e.Title}");
            Console.WriteLine();
        }

        Console.WriteLine("Add --save [path] to write the JSON, or --print to view it.");
    }

    /// <summary>
    /// Group packs by category in display order (see <see cref="ExampleCatalog.CategoryOrder"/>),
    /// with any unlisted categories following alphabetically and root packs last.
    /// </summary>
    internal static IEnumerable<IGrouping<string, ExampleInfo>> GroupByCategory(IEnumerable<ExampleInfo> examples) =>
        examples
            .GroupBy(e => e.Category)
            .OrderBy(g => CategoryRank(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal);

    private static int CategoryRank(string category)
    {
        if (string.IsNullOrEmpty(category)) return int.MaxValue;           // root packs last
        var i = ExampleCatalog.CategoryOrder
            .ToList()
            .FindIndex(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : ExampleCatalog.CategoryOrder.Count;            // known first, then the rest
    }

    internal static string CategoryLabel(string category) =>
        string.IsNullOrEmpty(category) ? "other" : category;

    private static int Save(string name, string json, string? path)
    {
        var target = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Directory.GetCurrentDirectory(), $"{name}.json")
            : path!;

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(target));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(target, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not save to '{target}': {ex.Message}");
            return 2;
        }

        Console.WriteLine($"Saved '{name}' to {target}");
        Console.WriteLine($"Run it with: menuki --config {target}");
        return 0;
    }

    private static int RunInteractive(string name, string json)
    {
        MenuConfig config;
        try
        {
            config = JsonSerializer.Deserialize<MenuConfig>(json)
                     ?? throw new InvalidOperationException("example deserialized to null");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Example '{name}' failed to load: {ex.Message}");
            return 2;
        }

        AppSettings.ApplyConfigDefaults(config.Settings);
        var theme = new ThemeManager(config.Theme, config.Colors);
        Launch(name, json, config, theme, ActionExecutorFactory.BuildDefault());
        return 0;
    }

    /// <summary>
    /// Run an example by name using an existing theme/registry (used by the welcome
    /// screen's "Browse examples"). No-op if the name is unknown or fails to parse.
    /// </summary>
    public static void Launch(string name, ThemeManager theme, ActionExecutorRegistry registry)
    {
        var json = ExampleCatalog.ReadJson(name);
        if (json == null) return;

        MenuConfig config;
        try { config = JsonSerializer.Deserialize<MenuConfig>(json)!; }
        catch { return; }
        if (config == null) return;

        Launch(name, json, config, theme, registry);
    }

    private static void Launch(string name, string json, MenuConfig config, ThemeManager theme, ActionExecutorRegistry registry)
    {
        // Materialize to a real file so open-config / edit / reload have a path to work with.
        var path = MaterializePath(name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        catch
        {
            // If we cannot write the cache, fall back to a name in the current directory.
            path = Path.Combine(Directory.GetCurrentDirectory(), $"{name}.json");
        }

        Console.Title = config.Title;
        Navigator.Run(config, theme, path, registry);
    }

    /// <summary>Pristine copy of the example under the menuki home, refreshed on each run.</summary>
    private static string MaterializePath(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki", "examples", $"{name}.json");

    private static bool HasFlag(string[] args, string flag) =>
        Array.IndexOf(args, flag) >= 0;

    private static string? OptionAfter(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        if (i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            return args[i + 1];
        return null;
    }
}
