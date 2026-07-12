using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Menuki.Actions;
using Menuki.Authoring;
using Menuki.Config;
using Menuki.Plugins;

namespace Menuki.Headless;

/// <summary>
/// Non-interactive, agent-facing entry point. Provides two subcommands:
///   list --config &lt;path&gt;
///   exec --config &lt;path&gt; --action &lt;id&gt; [--param k=v ...]
/// Everything is emitted as JSON on stdout; the process exit code signals success/failure.
/// </summary>
public static class HeadlessCli
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool IsHeadlessCommand(string[] args) =>
        args.Length > 0 && args[0] is "list" or "exec" or "validate";

    public static int Run(string[] args)
    {
        var command = args[0];
        var configPath = GetOption(args, "--config");

        if (configPath == null)
            return Error("Missing required --config <path>.");
        if (!File.Exists(configPath))
            return Error($"Config file not found: {configPath}");

        MenuConfig config;
        try
        {
            config = JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(configPath))
                     ?? throw new InvalidOperationException("Config deserialized to null.");
        }
        catch (Exception ex)
        {
            return Error($"Failed to parse config: {ex.Message}");
        }

        var registry = new ActionExecutorRegistry();
        ActionExecutorFactory.RegisterBuiltIns(registry);
        foreach (var plugin in PluginLoader.LoadAll())
            registry.RegisterPlugin(plugin);

        return command switch
        {
            "list" => RunList(config, registry),
            "exec" => RunExec(config, registry, args),
            "validate" => RunValidate(config),
            _ => Error($"Unknown command '{command}'.")
        };
    }

    private static int RunValidate(MenuConfig config)
    {
        var result = MenuValidator.Validate(config);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok = result.Ok,
            errors = result.Issues.Where(i => i.Level == "error").Select(i => $"{i.Where}: {i.Message}"),
            warnings = result.Issues.Where(i => i.Level == "warning").Select(i => $"{i.Where}: {i.Message}")
        }, JsonOpts));
        return result.Ok ? 0 : 5;
    }

    private static int RunList(MenuConfig config, ActionExecutorRegistry registry)
    {
        Console.WriteLine(JsonSerializer.Serialize(HeadlessRunner.BuildCatalog(config, registry), JsonOpts));
        return 0;
    }

    private static int RunExec(MenuConfig config, ActionExecutorRegistry registry, string[] args)
    {
        var id = GetOption(args, "--action");
        if (id == null)
            return Error("Missing required --action <id>. Run 'list' to see available action ids.");

        var action = HeadlessRunner.Find(config, id);
        if (action == null)
            return Error($"Action '{id}' not found. Run 'list' to see available action ids.");

        var supplied = GetParams(args);
        var outcome = HeadlessRunner.Execute(action, supplied);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            id = action.Id,
            type = action.Type,
            ok = outcome.Ok,
            error = outcome.Error,
            command = outcome.Command,
            exit_code = outcome.ExitCode,
            stdout = outcome.StdOut,
            stderr = outcome.StdErr,
            steps = outcome.Steps,
            stopped_early = outcome.StoppedEarly
        }, JsonOpts));

        if (!outcome.Ok)
            return 3;
        return outcome.ExitCode ?? 0;
    }

    // --- arg parsing ----------------------------------------------------

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }

    private static Dictionary<string, string> GetParams(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--param")
                continue;

            var kv = args[i + 1];
            var eq = kv.IndexOf('=');
            if (eq > 0)
                result[kv[..eq]] = kv[(eq + 1)..];
        }
        return result;
    }

    private static int Error(string message)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOpts));
        return 2;
    }
}
