using Menuki.Actions;
using Menuki.Config;
using Menuki.Plugins;

namespace Menuki.Mcp;

/// <summary>
/// MCP server entry, reached via the `menuki mcp` subcommand. The server is
/// NOT bound to any single config - it lets an AI agent dynamically author,
/// validate, save and run menu configs of arbitrary nesting, stored under a
/// configs directory.
///
/// Usage: menuki mcp [--dir &lt;configs-dir&gt;]
///        (default: $MENUKI_CONFIG_DIR, else $MENUKI_HOME/configs, else ~/.menuki/configs)
///
/// Only stderr is used for diagnostics; stdout carries JSON-RPC exclusively.
/// </summary>
internal static class McpEntry
{
    public static int Run(string[] args)
    {
        var dir = GetOption(args, "--dir") ?? ConfigResolver.ConfigsDir;

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"menuki mcp: cannot create configs dir '{dir}': {ex.Message}");
            return 1;
        }

        // Plugins are loaded once so validation/run recognise their action types.
        var registry = new ActionExecutorRegistry();
        ActionExecutorFactory.RegisterBuiltIns(registry);
        foreach (var plugin in PluginLoader.LoadAll())
            registry.RegisterPlugin(plugin);

        Console.Error.WriteLine($"menuki mcp: authoring server over stdio, configs dir '{dir}'.");

        new McpServer(dir, registry).Run();
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }
}
