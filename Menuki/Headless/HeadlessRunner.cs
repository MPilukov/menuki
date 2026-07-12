using System.Text;
using Menuki.Actions;
using Menuki.Config;
using Menuki.Engine;

namespace Menuki.Headless;

/// <summary>
/// A single menu item flattened into an agent-addressable action, keyed by a
/// stable id of the form "menuId/slug".
/// </summary>
public record HeadlessAction(string Id, string MenuId, string Name, ActionDefinition Action,
    string? Description = null, string? Help = null)
{
    public string Type => Action.Type;
}

/// <summary>
/// Outcome of a headless execution. Serialized to JSON for the agent. For a
/// sequence action, <see cref="Steps"/> holds the per-step results.
/// </summary>
public record ExecOutcome(
    bool Ok,
    string? Error,
    string? Command,
    int? ExitCode,
    string? StdOut,
    string? StdErr,
    List<StepResult>? Steps = null,
    bool? StoppedEarly = null);

/// <summary>
/// Non-interactive execution path: flattens the menu tree into addressable
/// actions and runs the headless-capable ones (shell / input+shell / script /
/// delay / sequence) with captured output. The interactive TUI is not involved.
/// </summary>
public static class HeadlessRunner
{
    /// <summary>Action types that can run without a TTY and produce capturable output.</summary>
    public static bool IsHeadless(string type) =>
        type is ActionTypes.Shell or ActionTypes.InputShell or ActionTypes.Script
            or ActionTypes.Sequence or ActionTypes.Parallel or ActionTypes.Delay;

    /// <summary>Flatten every menu item into an addressable action with a stable id.</summary>
    public static List<HeadlessAction> Enumerate(MenuConfig config)
    {
        var result = new List<HeadlessAction>();
        foreach (var (menuId, menu) in config.Menus)
        {
            var seen = new Dictionary<string, int>();
            foreach (var item in menu.Items)
            {
                var slug = Slug(item.Name);
                if (seen.TryGetValue(slug, out var n))
                {
                    seen[slug] = n + 1;
                    slug = $"{slug}-{n + 1}";
                }
                else
                {
                    seen[slug] = 1;
                }

                result.Add(new HeadlessAction($"{menuId}/{slug}", menuId, item.Name, item.Action,
                    item.Description, item.Help));
            }
        }
        return result;
    }

    public static HeadlessAction? Find(MenuConfig config, string id) =>
        Enumerate(config).FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// Build the agent-facing catalog (a serializable object) shared by the CLI
    /// `list` command and the MCP `list_actions` tool.
    /// </summary>
    public static object BuildCatalog(MenuConfig config, ActionExecutorRegistry registry)
    {
        var actions = Enumerate(config).Select(a => new
        {
            id = a.Id,
            menu = a.MenuId,
            name = a.Name,
            type = a.Type,
            headless = IsHeadless(a.Type),
            description = a.Description,
            help = a.Help,
            summary = Summary(a.Action),
            @params = Params(a.Action, registry)
        }).ToArray();

        return new
        {
            title = config.Title,
            start_menu = config.StartMenu,
            actions
        };
    }

    /// <summary>Short human-readable description of what an action does.</summary>
    public static string Summary(ActionDefinition a) => a.Type switch
    {
        ActionTypes.Shell => a.Command ?? "",
        ActionTypes.Script => a.Path + (string.IsNullOrEmpty(a.Args) ? "" : " " + a.Args),
        ActionTypes.InputShell => a.CommandTemplate ?? "",
        ActionTypes.Submenu => "→ " + a.Menu,
        ActionTypes.OpenUrl => a.Url ?? "",
        ActionTypes.OpenFile => a.Path ?? "",
        ActionTypes.Sequence => $"sequence: {a.Steps?.Count ?? 0} steps",
        ActionTypes.Parallel => $"parallel: {a.Steps?.Count ?? 0} steps",
        ActionTypes.Delay => $"delay {a.Seconds ?? 0}s",
        ActionTypes.Background => "bg: " + (a.Command ?? ""),
        ActionTypes.Jobs => "background job manager",
        ActionTypes.OpenConfig => "open config: " + a.Path,
        _ => ""
    };

    /// <summary>Parameter metadata (name/required/default) an agent must supply to run the action.</summary>
    public static object[] Params(ActionDefinition a, ActionExecutorRegistry registry)
    {
        if (a.Type == ActionTypes.InputShell && a.Inputs != null)
            return a.Inputs
                .Select(i => (object)new
                {
                    name = i.Name,
                    type = InputValidator.EffectiveType(i),
                    required = InputValidator.IsRequired(i),
                    secret = i.Secret,
                    @default = i.Default,
                    options = i.Options,
                    min = i.Min,
                    max = i.Max,
                    pattern = i.Pattern,
                    example = i.Example
                })
                .ToArray();

        var plugin = registry.GetPlugin(a.Type);
        if (plugin != null)
            return plugin.Parameters
                .Select(p => (object)new { name = p.Name, required = p.Required, @default = p.DefaultValue })
                .ToArray();

        return Array.Empty<object>();
    }

    /// <summary>
    /// Execute one action headlessly. <paramref name="supplied"/> provides values for
    /// input+shell parameters (from --param k=v); missing values fall back to the
    /// config default, and a required-but-absent value is a hard error.
    /// </summary>
    public static ExecOutcome Execute(HeadlessAction ha, IReadOnlyDictionary<string, string> supplied)
    {
        var a = ha.Action;

        if (!IsHeadless(a.Type))
            return Fail($"Action type '{a.Type}' is not headless-capable " +
                       "(interactive/desktop side effect). Only shell, input+shell, script, " +
                       "delay and sequence are supported.");

        // input+shell needs its parameters interpolated first; everything else
        // (shell / script / delay / sequence) is run by the shared step core.
        if (a.Type == ActionTypes.InputShell)
            return RunInputShell(a, supplied);

        var result = StepRunner.RunCaptured(a);

        // Apply format/query to a shell command's captured output (agents get the
        // extracted/pretty value too; copy/save stay TUI-only).
        if (a.Type == ActionTypes.Shell && result.Ok &&
            ResultFormatter.NeedsFormatting(a.Format, a.Query))
        {
            var formatted = ResultFormatter.Format(result.StdOut ?? "", a.Format, a.Query);
            result = result with { StdOut = formatted.Text };
        }

        return new ExecOutcome(
            result.Ok, result.Error, StepRunner.Label(a),
            result.ExitCode, result.StdOut, result.StdErr, result.Steps, result.StoppedEarly);
    }

    private static ExecOutcome RunInputShell(ActionDefinition a, IReadOnlyDictionary<string, string> supplied)
    {
        if (a.Inputs == null || string.IsNullOrEmpty(a.CommandTemplate))
            return Fail("input+shell action requires 'inputs' and 'command_template'.");

        var values = new Dictionary<string, string>();
        var errors = new List<string>();

        foreach (var input in a.Inputs)
        {
            supplied.TryGetValue(input.Name, out var v);
            var result = InputValidator.Resolve(input, v);
            if (result.Ok)
            {
                values[input.Name] = result.Value;
            }
            else
            {
                var msg = result.Error ?? $"invalid value for '{input.Name}'.";
                if (result.Allowed is { Length: > 0 } allowed)
                    msg += $" Allowed values: {string.Join(", ", allowed)}.";
                errors.Add(msg);
            }
        }

        if (errors.Count > 0)
            return Fail(string.Join(" ", errors) + " Pass values with --param name=value.");

        var secretNames = a.Inputs.Where(i => i.Secret).Select(i => i.Name).ToHashSet();
        var command = ShellEscaper.Interpolate(a.CommandTemplate, values);
        var display = ShellEscaper.InterpolateForDisplay(a.CommandTemplate, values, secretNames);

        return RunCommand(command, display);
    }

    private static ExecOutcome RunCommand(string command, string display)
    {
        var (exitCode, stdout, stderr) = ShellRunner.RunCaptured(command);
        return new ExecOutcome(true, null, display, exitCode, stdout, stderr);
    }

    private static ExecOutcome Fail(string message) =>
        new(false, message, null, null, null, null);

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is ' ' or '-' or '_' or '/' or '.')
                sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");

        return slug.Length == 0 ? "item" : slug;
    }
}
