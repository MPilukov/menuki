using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Menuki.Actions;
using Menuki.Authoring;
using Menuki.Config;
using Menuki.Headless;

namespace Menuki.Mcp;

/// <summary>
/// Minimal Model Context Protocol server over stdio (JSON-RPC 2.0), zero external
/// dependencies. It exposes Menuki as an authoring surface: the agent builds
/// menu configs of arbitrary nesting, validates them, saves them for a human to
/// run in the interactive TUI, and can test-run individual headless actions.
/// Nothing is bound at startup - every tool takes its config dynamically.
/// </summary>
internal sealed class McpServer
{
    private const string ServerName = "menuki";
    private const string ServerVersion = "0.2.0";
    private const string DefaultProtocol = "2024-11-05";

    private static readonly JsonSerializerOptions Wire = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _dir;
    private readonly ActionExecutorRegistry _registry;
    private readonly TextWriter _out;

    public McpServer(string dir, ActionExecutorRegistry registry)
    {
        _dir = dir;
        _registry = registry;
        _out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Run()
    {
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

        string? line;
        while ((line = input.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                HandleLine(line);
            }
            catch (JsonException)
            {
                SendError(null, -32700, "Parse error");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"menuki mcp: handler error: {ex.Message}");
            }
        }
    }

    private void HandleLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        var hasId = root.TryGetProperty("id", out var idEl);
        object? id = hasId ? idEl.Clone() : null;
        var hasParams = root.TryGetProperty("params", out var paramsEl);

        if (method == null)
            return;

        switch (method)
        {
            case "initialize":
                SendResult(id, Initialize(hasParams ? paramsEl : default));
                break;

            case "tools/list":
                SendResult(id, new { tools = ToolDefinitions() });
                break;

            case "tools/call":
                SendResult(id, ToolsCall(hasParams ? paramsEl : default));
                break;

            case "ping":
                SendResult(id, new { });
                break;

            default:
                if (hasId)
                    SendError(id, -32601, $"Method not found: {method}");
                break;
        }
    }

    private object Initialize(JsonElement @params)
    {
        var protocol = DefaultProtocol;
        if (@params.ValueKind == JsonValueKind.Object &&
            @params.TryGetProperty("protocolVersion", out var pv) &&
            pv.ValueKind == JsonValueKind.String)
        {
            protocol = pv.GetString()!;
        }

        return new
        {
            protocolVersion = protocol,
            capabilities = new { tools = new { } },
            serverInfo = new { name = ServerName, version = ServerVersion }
        };
    }

    private object[] ToolDefinitions() => new object[]
    {
        Tool("validate_menu",
            "Validate a menu config draft WITHOUT saving. Returns structural errors/warnings " +
            "(missing fields, dangling submenu targets, unused input variables, unreachable menus) " +
            "plus an ASCII tree of the nesting. Use this to iterate on a design before saving.",
            new
            {
                type = "object",
                properties = new { menu = MenuSchema() },
                required = new[] { "menu" }
            }),

        Tool("save_menu",
            "Validate a menu config and, if valid, save it as <name>.json in the configs directory " +
            "for a human to open in the interactive TUI. Menus may nest to any depth via submenu actions.",
            new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "File name without extension, e.g. 'devops'." },
                    menu = MenuSchema()
                },
                required = new[] { "name", "menu" }
            }),

        Tool("list_menus",
            "List saved menu configs in the configs directory (name, title, path).",
            new { type = "object", properties = new { }, additionalProperties = false }),

        Tool("get_menu",
            "Return the raw JSON of a saved menu config by name, for inspection or editing.",
            new
            {
                type = "object",
                properties = new { name = new { type = "string", description = "Saved config name (no extension)." } },
                required = new[] { "name" }
            }),

        Tool("list_actions",
            "List the runnable action ids of a saved menu config (for use with run_action).",
            new
            {
                type = "object",
                properties = new { name = new { type = "string", description = "Saved config name." } },
                required = new[] { "name" }
            }),

        Tool("run_action",
            "Test-run one headless-capable action (shell / input+shell / script) of a saved config by id, " +
            "returning stdout, stderr and exit code. Non-headless actions (submenu, open-url, open-file, exit) " +
            "are rejected.",
            new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Saved config name." },
                    action_id = new { type = "string", description = "Action id from list_actions." },
                    @params = new
                    {
                        type = "object",
                        description = "Values for input+shell parameters, keyed by name.",
                        additionalProperties = new { type = "string" }
                    }
                },
                required = new[] { "name", "action_id" }
            })
    };

    private object ToolsCall(JsonElement @params)
    {
        if (@params.ValueKind != JsonValueKind.Object ||
            !@params.TryGetProperty("name", out var nameEl) ||
            nameEl.ValueKind != JsonValueKind.String)
        {
            return ToolText("Error: tools/call requires a 'name'.", isError: true);
        }

        var tool = nameEl.GetString();
        @params.TryGetProperty("arguments", out var args);

        try
        {
            return tool switch
            {
                "validate_menu" => ValidateMenu(args),
                "save_menu" => SaveMenu(args),
                "list_menus" => ListMenus(),
                "get_menu" => GetMenu(args),
                "list_actions" => ListActions(args),
                "run_action" => RunAction(args),
                _ => ToolText($"Error: unknown tool '{tool}'.", isError: true)
            };
        }
        catch (Exception ex)
        {
            return ToolText($"Error: {ex.Message}", isError: true);
        }
    }

    // --- authoring tools ------------------------------------------------

    private object ValidateMenu(JsonElement args)
    {
        var config = DeserializeMenu(args);
        var result = MenuValidator.Validate(config);
        return ValidationOutput(result);
    }

    private object SaveMenu(JsonElement args)
    {
        var name = SafeName(RequireString(args, "name"));
        var config = DeserializeMenu(args);
        var result = MenuValidator.Validate(config);

        if (!result.Ok)
            return ToolTexts(true,
                "Not saved - validation failed:\n" + IssuesText(result.Issues),
                result.Tree);

        var path = Path.Combine(_dir, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(config, Pretty));

        var summary = JsonSerializer.Serialize(new
        {
            saved = true,
            name,
            path,
            warnings = result.Issues.Where(i => i.Level == "warning").Select(i => $"{i.Where}: {i.Message}"),
            launch = $"dotnet run --project Menuki -- --config \"{path}\""
        }, Pretty);

        return ToolTexts(false, summary, result.Tree);
    }

    private object ListMenus()
    {
        var menus = Directory.GetFiles(_dir, "*.json").Select(path =>
        {
            string title = "";
            try
            {
                var cfg = JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(path));
                title = cfg?.Title ?? "";
            }
            catch
            {
                title = "(unparseable)";
            }
            return new { name = Path.GetFileNameWithoutExtension(path), title, path };
        }).ToArray();

        return ToolText(JsonSerializer.Serialize(new { dir = _dir, menus }, Pretty));
    }

    private object GetMenu(JsonElement args)
    {
        var name = SafeName(RequireString(args, "name"));
        var path = Path.Combine(_dir, name + ".json");
        if (!File.Exists(path))
            return ToolText($"Error: no saved menu named '{name}' in {_dir}.", isError: true);

        return ToolText(File.ReadAllText(path));
    }

    // --- dynamic execution tools ---------------------------------------

    private object ListActions(JsonElement args)
    {
        var config = LoadNamed(RequireString(args, "name"));
        return ToolText(JsonSerializer.Serialize(HeadlessRunner.BuildCatalog(config, _registry), Pretty));
    }

    private object RunAction(JsonElement args)
    {
        var config = LoadNamed(RequireString(args, "name"));
        var actionId = RequireString(args, "action_id");

        var action = HeadlessRunner.Find(config, actionId);
        if (action == null)
            return ToolText($"Error: action '{actionId}' not found. Call list_actions for valid ids.", isError: true);

        var outcome = HeadlessRunner.Execute(action, ReadParams(args));

        var text = JsonSerializer.Serialize(new
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
        }, Pretty);

        var isError = !outcome.Ok || (outcome.ExitCode is int code && code != 0);
        return ToolText(text, isError);
    }

    // --- helpers --------------------------------------------------------

    private static object ValidationOutput(ValidationResult result)
    {
        var summary = JsonSerializer.Serialize(new
        {
            ok = result.Ok,
            errors = result.Issues.Where(i => i.Level == "error").Select(i => $"{i.Where}: {i.Message}"),
            warnings = result.Issues.Where(i => i.Level == "warning").Select(i => $"{i.Where}: {i.Message}")
        }, Pretty);

        return ToolTexts(!result.Ok, summary, result.Tree);
    }

    private static string IssuesText(IEnumerable<ValidationIssue> issues) =>
        string.Join("\n", issues.Select(i => $"[{i.Level}] {i.Where}: {i.Message}"));

    private MenuConfig LoadNamed(string name)
    {
        var path = Path.Combine(_dir, SafeName(name) + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"No saved menu named '{name}' in {_dir}.");
        return JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(path))
               ?? throw new InvalidOperationException("Config deserialized to null.");
    }

    private static MenuConfig DeserializeMenu(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("menu", out var menuEl) ||
            menuEl.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("expected a 'menu' object argument (the full config).");
        }

        return JsonSerializer.Deserialize<MenuConfig>(menuEl.GetRawText())
               ?? throw new ArgumentException("'menu' could not be parsed as a config.");
    }

    private static string RequireString(JsonElement args, string field)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(field, out var el) &&
            el.ValueKind == JsonValueKind.String)
        {
            return el.GetString()!;
        }
        throw new ArgumentException($"missing required string argument '{field}'.");
    }

    private static Dictionary<string, string> ReadParams(JsonElement args)
    {
        var result = new Dictionary<string, string>();
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("params", out var p) ||
            p.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var prop in p.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString() ?? ""
                : prop.Value.GetRawText();
        }
        return result;
    }

    private static string SafeName(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name = name[..^5];
        if (string.IsNullOrEmpty(name) || name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            throw new ArgumentException($"invalid menu name '{name}'.");
        return name;
    }

    private static object MenuSchema() => new
    {
        type = "object",
        description =
            "A full Menuki config: { title, start_menu, menus }. 'menus' is a flat map of " +
            "menuId -> { title, items:[{ name, description?, help?, action }] }. Add a short 'description' " +
            "(and optional longer 'help') to each item - it is shown to humans on the '?' key and returned " +
            "to agents by list_actions, so all three consumers read the same explanation. Actions: " +
            "shell{command}, submenu{menu}, input+shell{inputs,command_template}, script{path[,args]}, " +
            "open-url{url}, open-file{path}, exit. Each input in input+shell may be typed: " +
            "{name,prompt,type?,required?,default?} where type is string (with optional pattern/example), " +
            "choice (with options[]), number (with min/max), boolean, or date (with format). Typed inputs " +
            "give you a validated contract - list_actions reports the allowed values. Arbitrary nesting is " +
            "done with submenu actions that reference other menu ids."
    };

    private static object Tool(string name, string description, object inputSchema) =>
        new { name, description, inputSchema };

    // --- MCP result shaping ---------------------------------------------

    private static object ToolText(string text, bool isError = false) => new
    {
        content = new object[] { new { type = "text", text } },
        isError
    };

    private static object ToolTexts(bool isError, params string[] texts) => new
    {
        content = texts.Select(t => (object)new { type = "text", text = t }).ToArray(),
        isError
    };

    // --- JSON-RPC transport ---------------------------------------------

    private void SendResult(object? id, object result) =>
        Send(new { jsonrpc = "2.0", id, result });

    private void SendError(object? id, int code, string message) =>
        Send(new { jsonrpc = "2.0", id, error = new { code, message } });

    private void Send(object message) =>
        _out.WriteLine(JsonSerializer.Serialize(message, Wire));
}
