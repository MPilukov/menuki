# Menuki

Build interactive terminal menus and runbooks from a single JSON file - the same catalog usable by humans (a keyboard-driven TUI), shell scripts (headless JSON in/out), and AI agents (MCP). No code required; ships as a single self-contained binary.

## Features

- **Zero dependencies** - only `System.Text.Json` (built into .NET 8)
- **JSON config** - define menus, actions, info panels in a single file
- **13 action types** - shell, submenu, exit, open-url, input+shell, open-file, open-config, script, sequence, parallel, delay, background, jobs
- **Guided tour** - `menuki tour` (and a welcome screen on no-args) walks through every feature, adapting to the tools on your machine
- **Composite actions** - `sequence` runs steps as a mini pipeline (stop-on-error, retry, hooks); `parallel` runs them concurrently
- **Background jobs** - `background` starts long-running processes without blocking; a `jobs` manager views/stops them
- **Result formatting** - pretty-print / table / JSONPath-extract a shell command's output, then copy or save it
- **Plugin system** - extend with custom action types via DLL plugins
- **Self-describing items** - `description` / `help` fields shown to humans on `?` and returned to agents by `list`
- **Agent / headless mode** - `list` / `exec` subcommands drive menus non-interactively (JSON in, JSON out)
- **MCP server** - let an AI agent author, validate and save nested menu configs
- **Navigation stack** - submenus push/pop automatically, X goes back
- **Pagination** - long menus paginated with Left/Right arrows
- **Info panel** - static values or dynamic output from shell commands
- **Cross-platform** - Windows, macOS, Linux

## Quick Start

Run straight from source:

```bash
dotnet build
dotnet run --project Menuki -- tour          # guided tour of every feature
dotnet run --project Menuki                  # welcome screen
dotnet run --project Menuki -- --config Menuki/examples/dev-runbook.json
```

## Install

Install a single self-contained `menuki` binary onto your PATH (bundles the
.NET runtime - end users don't need .NET installed):

```bash
./scripts/install.sh                    # builds for this machine, installs to ~/.local/bin
PREFIX=/usr/local/bin ./scripts/install.sh   # install elsewhere (may need sudo)
```

Then:

```bash
menuki                                                  # welcome screen
menuki tour                                             # guided feature tour
menuki --config Menuki/examples/dev-runbook.json   # interactive
menuki list --config <cfg>                              # headless catalog (JSON)
menuki exec --config <cfg> --action <id>                # headless run
menuki validate --config <cfg>                          # check a config (JSON errors/warnings)
```

## Tests

Unit + integration tests live in `Menuki.Tests` (xUnit - a dev-only dependency; the app itself stays zero-dependency):

```bash
dotnet test
```

They cover typed-input validation, sequence/parallel/retry/stop-on-error, headless exit codes, action-id generation, JSON round-trips, config validation, tour progress, per-OS command selection, JSON-schema conformance of the examples, and the `list` / `exec` / `validate` CLI contract end-to-end.

## Editor support (JSON Schema)

`menuki.schema.json` describes every field - action types, typed inputs, sequence/parallel/background, formatting, descriptions and themes. Reference it from a config to get autocomplete, valid-value lists, hover docs and errors-before-run in VS Code (and other schema-aware editors):

```json
{
  "$schema": "https://raw.githubusercontent.com/MPilukov/menuki/main/menuki.schema.json",
  "title": "My menu",
  "menus": { }
}
```

For a config living in this repo you can point at the file directly (works offline), e.g. `"$schema": "../../menuki.schema.json"` - see the example configs. Or map it globally in VS Code `settings.json`:

```json
{ "json.schemas": [ { "fileMatch": ["*.menu.json"], "url": "./menuki.schema.json" } ] }
```

> Custom **plugin** action types aren't in the schema's built-in list, so a config that uses them will flag an unknown `type`.

Building requires the .NET 8 SDK. For other platforms (e.g. Windows), publish manually:

```bash
dotnet publish Menuki/Menuki.csproj -c Release \
  -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Guided Tour

`menuki tour` (or picking "Take the interactive tour" on the welcome screen)
launches a hands-on walkthrough of every feature - navigation, typed inputs, safe
commands, output formatting, headless/AI usage, and more. It:

- **adapts to your machine** - detects git / docker / kubectl / .NET / node and shows only relevant examples;
- **shows its own JSON** - any screen can reveal the config that produced it;
- **runs itself headlessly** - demonstrates the exact `list` / `exec` an agent would use;
- **tracks progress** - visited sections are marked (✓ / ○) with an "explored" percentage;
- **is safe & editable** - it runs from a throwaway workspace (`~/.menuki/tour/workspace/`), so `E` to edit is harmless.

The tour is itself an ordinary config plus a few private `tour:*` actions - nothing it does requires features unavailable to your own menus.

## Navigation

| Key | Action |
|-----|--------|
| Up/Down | Move selection |
| Enter | Execute action |
| Left/Right | Pagination (long menus) |
| X / Esc | Back to previous menu |
| ? | Show the selected item's description / help |
| T | Toggle theme (dark / light / custom) |
| E / Q / V / R | Edit JSON / Quick Edit / Validate / Reload config |
| type to search | Filter items by name |

## Config Format

```json
{
  "title": "App Title",
  "start_menu": "main",
  "menus": {
    "main": {
      "title": "Main Menu",
      "info": [ ... ],
      "items": [ ... ]
    }
  }
}
```

### Root

| Field | Type | Description |
|-------|------|-------------|
| `title` | string | Window title |
| `start_menu` | string | ID of the first menu to display |
| `theme` | string? | Default theme: `"dark"` (default), `"light"`, or `"custom"` |
| `colors` | object? | Custom color scheme (used when theme is `"custom"` or as the third T-toggle option) |
| `menus` | object | Flat dictionary of menu definitions keyed by ID |

### Menu

| Field | Type | Description |
|-------|------|-------------|
| `title` | string | Displayed above menu items |
| `info` | array? | Info panel entries (displayed at top) |
| `items` | array | Menu items |

### Info Panel Entry

Static value:
```json
{ "label": "User", "value": "Max" }
```

Dynamic value (shell command output):
```json
{ "label": "Hostname", "command": "hostname" }
```

### Menu Item

```json
{
  "name": "Display name",
  "description": "One-line summary of what this does.",
  "help": "Optional longer explanation, shown alongside the description.",
  "action": { ... }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Displayed label |
| `description` | string? | One-line summary - shown on the `?` key and returned to agents by `list` / `list_actions` |
| `help` | string? | Longer help text, shown next to the description on `?` |
| `action` | object | The action to run (see below) |

`description` / `help` are the single explanation shared by all three consumers: a human reads them on `?`, and a script or AI agent gets them in the headless catalog.

### Action Types

**shell** - run a command, show output, wait for key:
```json
{ "type": "shell", "command": "kubectl get pods" }
```

*Formatting the result.* Add `format` and/or `query` to post-process a command's
output. When either is set, the output is **captured** (not streamed) and shown on
a result screen with `[C] Copy` / `[S] Save to file` / `[Enter] Back`:

```json
{ "type": "shell", "command": "kubectl get pods -o json", "format": "json" }
{ "type": "shell", "command": "docker ps --format '{{json .}}'", "format": "table" }
{ "type": "shell", "command": "kubectl get pods -o json", "query": "$.items[*].metadata.name" }
```

- `format`: `"json"` (pretty-print), `"table"` (a JSON array of flat objects → columns), or `"raw"` (default).
- `query`: a minimal JSONPath applied to JSON output *before* formatting - dot access `.a.b`, index `[0]`, wildcard `[*]`, bracket keys `['k']`. A wildcard yields an array. For anything more (filters, functions) pipe through `jq` in the command itself.

Because formatting requires capturing the output, use it only for quick,
non-interactive commands (not `ssh`, `top`, `-f` follows). Bad JSON or a
non-table shape degrades gracefully to raw / pretty output with a note. The
`format`/`query` fields also apply on the headless `exec` / `run_action` path
(copy/save stay TUI-only).

**submenu** - navigate to another menu by ID:
```json
{ "type": "submenu", "menu": "k8s" }
```

**exit** - terminate the application:
```json
{ "type": "exit" }
```

**open-url** - open URL in default browser:
```json
{ "type": "open-url", "url": "https://grafana.example.com" }
```

**input+shell** - prompt for variables, interpolate into command template:
```json
{
  "type": "input+shell",
  "inputs": [
    { "name": "host", "prompt": "Hostname", "default": "prod-01" }
  ],
  "command_template": "ssh deploy@{host}"
}
```

Variables are referenced as `{name}` in the template. If the user presses Enter without typing, the `default` value is used.

At any text prompt, **Up / Down** recalls previously entered values (a shared history saved to `~/.menuki/input_history` across runs) - so a value you typed for one field, like a date, can be pulled up at the next one instead of retyping. Left/Right/Home/End and Backspace/Delete edit the line.

*Typed inputs* - each input can declare a `type` so values are validated **before** the command runs (in the TUI and headless), and agents get a precise contract from `list` / `list_actions`:

```json
{
  "type": "input+shell",
  "inputs": [
    { "name": "environment", "type": "choice", "prompt": "Environment",
      "options": ["staging", "production"], "default": "staging" },
    { "name": "tail", "type": "number", "prompt": "Lines", "default": 100, "min": 1, "max": 10000 },
    { "name": "from", "type": "date", "prompt": "From", "format": "yyyy-MM-dd" },
    { "name": "force", "type": "boolean", "prompt": "Force?", "default": false },
    { "name": "ticket", "type": "string", "prompt": "Ticket",
      "pattern": "^OPS-[0-9]+$", "example": "OPS-1234", "required": true }
  ],
  "command_template": "..."
}
```

| Type | Fields | Behavior |
|------|--------|----------|
| `string` (default) | `pattern?`, `example?` | Optional regex check; `example` shown in prompt/errors |
| `choice` | `options` | Arrow-select in the TUI; value must be one of the options |
| `number` | `min?`, `max?` | Must parse as a number and sit within the range |
| `boolean` | - | Accepts yes/no/true/false/1/0; normalized to `true` / `false` |
| `date` | `format?` | Parsed with `format` (default `yyyy-MM-dd`) and normalized |

Common fields: `required` (no empty fallback), `default`, and `secret`. An invalid value is rejected with a clear message - headless returns `{ "ok": false, "error": "Invalid value 'prod' for parameter 'environment'. Allowed values: staging, production." }`. Bad specs (a `choice` with no `options`, `min > max`, a default that fails its own type) are caught by `validate_menu`. See `examples/typed-inputs-demo.json`.

Set `"secret": true` on an input to mark it sensitive: it is masked (`****`) while typing, never written to the input history (`~/.menuki/input_history`, which is created `0600`), and shown as `***` in any echoed or headless-returned command.

> Every value substituted into a `command_template` is shell-quoted before it reaches the shell, so metacharacters like `$( )`, backticks, `;`, `&&` and `|` are treated as literal text, not executed. Write placeholders bare (`ssh deploy@{host}`), not pre-quoted (`"{host}"`).

> Not yet typed: `file` / `directory` (path existence checks) - a planned follow-up.

**open-file** - open a file in the OS default app or a specific editor:
```json
{ "type": "open-file", "path": "~/notes.md" }
```

With a specific editor:
```json
{ "type": "open-file", "path": "~/.kube/config", "editor": "code" }
```

If `editor` is omitted, uses `open` (macOS), `start` (Windows), or `xdg-open` (Linux).

**open-config** - load another config as a nested menu (a hub over many packs):
```json
{ "type": "open-config", "path": "packs/git.json" }
```
The path is resolved relative to the current config (with `~` expansion). The target opens as a nested session; **Esc** returns to the calling menu. Great for a "menu of menus".

**script** - run an external script file (`.sh`, `.ps1`, etc.):
```json
{ "type": "script", "path": "~/scripts/deploy.sh" }
```

With arguments:
```json
{ "type": "script", "path": "~/scripts/backup.sh", "args": "--full --verbose" }
```

`~/` is expanded to the user's home directory. On Windows, `.ps1` files are executed via `powershell -ExecutionPolicy Bypass -File`.

**sequence** - run several steps in order as a mini pipeline:
```json
{
  "type": "sequence",
  "stop_on_error": true,
  "steps": [
    { "type": "shell", "command": "dotnet test" },
    { "type": "shell", "command": "dotnet publish -c Release" }
  ]
}
```

Steps run top to bottom. When `stop_on_error` is `true` (the default), the first
step that exits non-zero stops the sequence and the rest are skipped. Allowed
step types: `shell`, `script`, `delay`, and nested `sequence`. In the TUI each
step streams live with a ✓/✗ header; headless (`exec` / `run_action`) returns a
per-step result array. Each step may carry an optional `"id"` (reserved for
future output-passing between steps).

*Retry.* Any step can retry on failure:
```json
{ "type": "shell", "command": "flaky-network-call", "retry": 3, "retry_delay": 2 }
```
`retry` is the number of extra attempts (total = retry + 1); `retry_delay` is
seconds between them. The step's result reports `attempts` when it retried.

*Hooks.* A sequence can run a follow-up action based on its outcome:
```json
{
  "type": "sequence",
  "steps": [ { "type": "shell", "command": "deploy.sh" } ],
  "on_success": { "type": "shell", "command": "notify 'deploy ok'" },
  "on_failure": { "type": "shell", "command": "notify 'deploy FAILED'" }
}
```
The hook runs after the steps and is informational - it does not change the
sequence's own success/failure.

**parallel** - run steps concurrently instead of in order:
```json
{
  "type": "parallel",
  "max_parallel": 2,
  "steps": [
    { "type": "shell", "command": "npm run build:web" },
    { "type": "shell", "command": "npm run build:api" }
  ]
}
```

All steps start together (optionally capped by `max_parallel`); the block
succeeds only if every step does. Because live output from concurrent steps
would interleave, the output is **buffered** and shown once all steps finish -
both in the TUI and on the headless path. Steps may be any runnable type
(`shell`, `script`, `delay`, nested `sequence`/`parallel`), and `on_success` /
`on_failure` hooks work the same as for a sequence.

**delay** - wait a fixed number of seconds (mostly useful as a sequence step):
```json
{ "type": "delay", "seconds": 2 }
```

**background** - start a long-running process without blocking the menu:
```json
{ "type": "background", "name": "dev-server", "command": "npm run dev" }
```

Output is redirected to a log file under `~/.menuki/logs/`. The menu returns
immediately, so you can start several jobs and keep working. Jobs are
**session-scoped**: they are killed when the app exits. If `name` is omitted it
defaults to the command's first word.

**jobs** - open a manager for running background jobs (list, tail log, kill):
```json
{ "type": "jobs" }
```

In the jobs screen: type a job number to tail its log, `k<n>` to kill a job, `c`
to clear finished jobs, Enter to refresh, `q` to go back.

### Themes & Colors

Two built-in themes: **dark** (default) and **light**. Press **T** at runtime to cycle between them.

Add a `colors` block to define a custom theme (becomes the third option in the T-cycle):

```json
{
  "theme": "custom",
  "colors": {
    "text": "White",
    "selected": "DarkYellow",
    "title": "Red",
    "info_border": "Blue",
    "info_label": "DarkCyan",
    "info_value": "Cyan",
    "message": "Magenta"
  }
}
```

Any omitted field falls back to the dark theme defaults. Available color names: `Black`, `DarkBlue`, `DarkGreen`, `DarkCyan`, `DarkRed`, `DarkMagenta`, `DarkYellow`, `DarkGray`, `Gray`, `Blue`, `Green`, `Cyan`, `Red`, `Magenta`, `Yellow`, `White`.

The selected theme is saved to `~/.menuki/theme.json` and persists across restarts.

## Full Example

```json
{
  "title": "DevOps Tools",
  "start_menu": "main",
  "menus": {
    "main": {
      "title": "Main Menu",
      "info": [
        { "label": "User", "value": "Max" },
        { "label": "Hostname", "command": "hostname" }
      ],
      "items": [
        { "name": "AI Agents", "action": { "type": "submenu", "menu": "ai-agents" } },
        { "name": "List pods", "action": { "type": "shell", "command": "kubectl get pods" } },
        { "name": "Kubernetes", "action": { "type": "submenu", "menu": "k8s" } },
        { "name": "Open Grafana", "action": { "type": "open-url", "url": "https://grafana.example.com" } },
        {
          "name": "SSH to server",
          "action": {
            "type": "input+shell",
            "inputs": [
              { "name": "host", "prompt": "Hostname", "default": "prod-01" }
            ],
            "command_template": "ssh deploy@{host}"
          }
        },
        { "name": "Open notes", "action": { "type": "open-file", "path": "~/notes.md" } },
        { "name": "Edit config in VS Code", "action": { "type": "open-file", "path": "~/.kube/config", "editor": "code" } },
        { "name": "Run deploy script", "action": { "type": "script", "path": "~/scripts/deploy.sh", "args": "--env prod" } },
        { "name": "Exit", "action": { "type": "exit" } }
      ]
    },
    "ai-agents": {
      "title": "AI Agents",
      "items": [
        { "name": "Claude Code", "action": { "type": "shell", "command": "claude" } },
        { "name": "Codex", "action": { "type": "shell", "command": "codex" } },
        { "name": "Gemini CLI", "action": { "type": "shell", "command": "gemini" } }
      ]
    },
    "k8s": {
      "title": "Kubernetes",
      "items": [
        { "name": "Get pods", "action": { "type": "shell", "command": "kubectl get pods" } },
        { "name": "Get services", "action": { "type": "shell", "command": "kubectl get svc" } },
        { "name": "Get namespaces", "action": { "type": "shell", "command": "kubectl get namespaces" } },
        {
          "name": "Logs from pod",
          "action": {
            "type": "input+shell",
            "inputs": [
              { "name": "pod", "prompt": "Pod name" }
            ],
            "command_template": "kubectl logs {pod} --tail=50"
          }
        }
      ]
    }
  }
}
```

## Plugins

Menuki supports DLL plugins that add custom action types. Plugins are loaded from `~/.menuki/plugins/`.

### Creating a Plugin

1. Create a .NET 8 class library project:
```bash
dotnet new classlib -n MyPlugin -f net8.0
```

2. Add a reference to Menuki (or copy the interfaces):
```csharp
// Your plugin needs these interfaces from Menuki:
// - IActionPlugin (Plugins/IActionPlugin.cs)
// - IActionExecutor (Actions/IActionExecutor.cs)
// - PluginParameterInfo (Plugins/PluginParameterInfo.cs)
```

3. Implement `IActionPlugin`:
```csharp
using Menuki.Actions;
using Menuki.Plugins;

public class DockerExecPlugin : IActionPlugin
{
    public string ActionTypeName => "docker-exec";
    public string DisplayName => "Docker Exec";

    public IReadOnlyList<PluginParameterInfo> Parameters => new[]
    {
        new PluginParameterInfo
        {
            Name = "container",
            Prompt = "Container name",
            Required = true
        },
        new PluginParameterInfo
        {
            Name = "command",
            Prompt = "Command to run",
            Required = true,
            DefaultValue = "/bin/bash"
        }
    };

    public IActionExecutor CreateExecutor(IReadOnlyDictionary<string, string> parameters)
    {
        var container = parameters["container"];
        var command = parameters.TryGetValue("command", out var cmd) ? cmd : "/bin/bash";
        return new DockerExecExecutor(container, command);
    }
}

public class DockerExecExecutor : IActionExecutor
{
    private readonly string _container;
    private readonly string _command;

    public DockerExecExecutor(string container, string command)
    {
        _container = container;
        _command = command;
    }

    public string? Execute()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"> docker exec -it {_container} {_command}");
        Console.ResetColor();
        Console.WriteLine();

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"exec -it {_container} {_command}",
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(psi);
        process?.WaitForExit();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);
        return null;
    }
}
```

4. Build and copy the DLL:
```bash
dotnet build -c Release
cp bin/Release/net8.0/MyPlugin.dll ~/.menuki/plugins/
```

### Using a Plugin in Config

Once the DLL is in `~/.menuki/plugins/`, use the plugin's action type in your config:

```json
{
  "name": "Connect to container",
  "action": {
    "type": "docker-exec",
    "params": {
      "container": "web-app",
      "command": "/bin/bash"
    }
  }
}
```

The `params` dictionary passes parameters to the plugin. Parameter names must match what the plugin defines in its `Parameters` property.

If a config references a plugin type that isn't loaded, the menu item will show an error message instead of crashing.

### Plugin Parameters in the Editor

When editing a menu item with a plugin action type (press **E**), the editor automatically prompts for each parameter defined by the plugin, using the `Prompt` and `DefaultValue` from `PluginParameterInfo`.

## Agent / Headless Mode

Besides the interactive TUI, Menuki has a non-interactive path so the same
JSON menus can be driven by scripts or AI agents. The menu file becomes a
**curated catalog of allowed operations**, and each item is addressable by a
stable id of the form `menuId/slug`.

Two subcommands, both emitting JSON on stdout:

**`list`** - flatten every menu item into an addressable action:

```bash
dotnet run --project Menuki -- list --config Menuki/examples/devops-tools.json
```

```json
{
  "title": "DevOps Tools",
  "start_menu": "main",
  "actions": [
    { "id": "k8s/logs-from-pod", "menu": "k8s", "name": "Logs from pod",
      "type": "input+shell", "headless": true, "summary": "kubectl logs {pod} --tail=50",
      "params": [ { "name": "pod", "required": true, "default": null } ] }
  ]
}
```

**`exec`** - run one action headlessly and capture its result:

```bash
dotnet run --project Menuki -- exec \
  --config devops.json --action k8s/logs-from-pod --param pod=web-abc
```

```json
{ "id": "k8s/logs-from-pod", "type": "input+shell", "ok": true,
  "command": "kubectl logs web-abc --tail=50", "exit_code": 0,
  "stdout": "...", "stderr": "" }
```

Only `shell`, `input+shell`, `script`, `sequence` and `delay` are
headless-capable. Interactive, desktop or TUI-only actions (`submenu`,
`open-url`, `open-file`, `exit`, `background`, `jobs`) are marked
`"headless": false` and refused by `exec`, so an agent is confined to the
curated set. Exit codes: `0` success (or the command's own code), `2` bad input
(missing config/id), `3` a refused/invalid call.

## MCP Server (AI authoring)

The `mcp` subcommand starts a zero-dependency [Model Context Protocol](https://modelcontextprotocol.io)
server (JSON-RPC 2.0 over stdio) that turns Menuki into an **authoring
surface for an AI agent**: the agent dynamically builds menu configs of
arbitrary nesting, validates them, saves them for a human to open in the TUI,
and can test-run individual headless actions. Nothing is bound at startup -
every tool takes its config dynamically.

```bash
menuki mcp --dir ~/.menuki/configs
```

Register it in an MCP client (e.g. Claude Code) - one binary, `mcp` subcommand:

```json
{
  "mcpServers": {
    "menuki": {
      "command": "/absolute/path/to/menuki",
      "args": ["mcp"]
    }
  }
}
```

With no `--dir` the server stores configs under `~/.menuki/configs`; pass
`--dir /some/path` (or set `MENUKI_CONFIG_DIR`) to change it.

### Tools

| Tool | Purpose |
|------|---------|
| `validate_menu(menu)` | Validate a draft **without saving**; returns errors/warnings + an ASCII nesting tree |
| `save_menu(name, menu)` | Validate and save `<name>.json` for the human to run |
| `list_menus()` | List saved configs (name, title, path) |
| `get_menu(name)` | Return a saved config's raw JSON for editing |
| `list_actions(name)` | Runnable action ids of a saved config |
| `run_action(name, action_id, params)` | Test-run a headless action, capturing output |

`validate_menu` gives the agent a feedback loop - it catches missing fields,
dangling `submenu` targets, `{variables}` used without a matching input (and
inputs never used), and menus unreachable from `start_menu`, and renders the
structure so nesting is visible:

```
root  "Root"
├─ Say hi  [shell]
└─ Tools  → tools
   ├─ Deeper  → deep
   │  └─ Pwd  [shell]
   └─ Echo name  [input+shell]
```

Only `stderr` is used for diagnostics; `stdout` carries JSON-RPC exclusively.

## Project Structure

```
Menuki/
  Program.cs                    # Entry point: --config, JSON loading, navigation loop, plugin wiring
  Config/
    MenuConfig.cs               # Root model: Title, StartMenu, Menus
    MenuDefinition.cs           # Title, Info[], Items[]
    MenuItemDefinition.cs       # Name, Action
    ActionDefinition.cs         # Type (string), Command?, Url?, Menu?, Path?, Args?, Parameters?
    ActionType.cs               # Static class with string constants (shell, submenu, exit, ...)
    ColorScheme.cs              # Color fields: text, selected, title, info_*, message
    InfoPanelEntry.cs           # Label, Value?, Command?
    InputDefinition.cs          # Name, Prompt, Default?
  Engine/
    MenuEngine.cs               # Rendering, navigation, pagination, theme toggle (T)
    MenuItem.cs                 # Runtime model: Name + IActionExecutor
    InfoPanelResolver.cs        # Resolves info panel (static or command output)
    ShellRunner.cs              # Cross-platform shell execution (stream + capture + background)
    StepRunner.cs               # Shared step core for sequences (captured + streaming)
    JobRegistry.cs              # In-memory registry of background jobs (session-scoped)
    ResultFormatter.cs          # Format shell output: pretty JSON / table / query
    JsonQuery.cs                # Minimal zero-dep JSONPath subset
    TableRenderer.cs            # JSON array of objects → ASCII table
    Clipboard.cs                # Cross-platform copy (pbcopy/clip/xclip/…)
    ThemeManager.cs             # Dark/light/custom themes, persists to ~/.menuki/
  Actions/
    IActionExecutor.cs          # Execute() -> menu-id or null
    ActionExecutorRegistry.cs   # Registry: maps action types to executor factories
    ActionExecutorFactory.cs    # Registers all built-in action types
    ShellActionExecutor.cs      # Run command interactively
    SubmenuActionExecutor.cs    # Return menu-id for navigation
    ExitActionExecutor.cs       # Environment.Exit(0)
    OpenUrlActionExecutor.cs    # open/start/xdg-open
    InputShellActionExecutor.cs # Prompt inputs -> interpolate -> run
    OpenFileActionExecutor.cs   # Open file in editor or OS default
    ScriptActionExecutor.cs     # Run external .sh/.ps1 script
    SequenceActionExecutor.cs   # Run steps as a pipeline (stop-on-error, retry, hooks)
    ParallelActionExecutor.cs   # Run steps concurrently (buffered output)
    DelayActionExecutor.cs      # Wait N seconds
    BackgroundActionExecutor.cs # Start a non-blocking background job
    JobsActionExecutor.cs       # Manage background jobs (list/tail/kill)
    ErrorActionExecutor.cs      # Graceful error display for unknown types
  Plugins/
    IActionPlugin.cs            # Plugin interface: ActionTypeName, DisplayName, Parameters, CreateExecutor
    PluginParameterInfo.cs      # Parameter metadata: Name, Prompt, Required, DefaultValue
    PluginLoader.cs             # Scans ~/.menuki/plugins/*.dll for IActionPlugin implementations
  Editor/
    ConfigEditor.cs             # In-app config editor (E key), plugin-aware
    ConfigSaver.cs              # JSON serialization with backup
    PromptHelper.cs             # Console prompts for editor
  Headless/
    HeadlessCli.cs              # `list` / `exec` subcommands, JSON output
    HeadlessRunner.cs           # Flatten menus to actions; run headless (no TTY)
  Authoring/
    MenuValidator.cs            # Structural validation of a menu config
    MenuTree.cs                 # ASCII nesting-tree preview
  Mcp/
    McpEntry.cs                 # `mcp` subcommand: --dir configs directory
    McpServer.cs                # Zero-dep JSON-RPC 2.0 authoring server over stdio
  examples/
    dev-runbook.json            # Project dev runbook (build/test/release) with descriptions
    gh-ops.json                 # GitHub CLI ops - format:table / query over `gh --json`
    kubectl-ops.json            # Read-only Kubernetes inspection with query + format
    typed-inputs-demo.json      # Typed inputs: choice / number / date / boolean / pattern
    devops-tools.json           # Example config
    onboarding-demo.json        # Nested onboarding example
    ci-pipeline.json            # Composite `sequence` actions example
    parallel-demo.json          # Concurrent `parallel` steps example
    background-demo.json        # Background jobs + jobs manager example
    format-demo.json            # Result formatting (json/table/query) example
  ../scripts/
    install.sh                  # Build & install a self-contained `menuki` binary
  ../menuki.schema.json    # JSON schema for configs (editor autocomplete/validation)
  ../Menuki.Tests/         # xUnit unit + integration tests
```

## Requirements

- .NET 8 SDK
