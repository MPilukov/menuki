using System.Text.RegularExpressions;
using Menuki.Config;

namespace Menuki.Authoring;

public record ValidationIssue(string Level, string Where, string Message);

public record ValidationResult(bool Ok, List<ValidationIssue> Issues, string Tree);

/// <summary>
/// Structural validation for menu configs. Gives an author (agent or human) a
/// feedback loop: draft a config, see exactly what's wrong, fix it. Warnings do
/// not fail validation; only errors do.
/// </summary>
public static class MenuValidator
{
    private static readonly Regex TemplateVar = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static ValidationResult Validate(MenuConfig config)
    {
        var issues = new List<ValidationIssue>();

        if (config.Menus.Count == 0)
            issues.Add(new("error", "menus", "Config has no menus."));

        if (string.IsNullOrEmpty(config.StartMenu))
            issues.Add(new("error", "start_menu", "start_menu is not set."));
        else if (!config.Menus.ContainsKey(config.StartMenu))
            issues.Add(new("error", "start_menu", $"start_menu '{config.StartMenu}' is not defined in menus."));

        foreach (var (menuId, menu) in config.Menus)
        {
            if (menu.Items.Count == 0)
                issues.Add(new("warning", menuId, "Menu has no items."));

            for (var i = 0; i < menu.Items.Count; i++)
            {
                var item = menu.Items[i];
                var where = $"{menuId}[{i}]";

                if (string.IsNullOrWhiteSpace(item.Name))
                    issues.Add(new("warning", where, "Item has an empty name."));

                ValidateAction(config, item.Action, where, issues);
            }
        }

        CheckReachability(config, issues);

        var hasErrors = issues.Any(i => i.Level == "error");
        return new ValidationResult(!hasErrors, issues, MenuTree.Render(config));
    }

    private static void ValidateAction(
        MenuConfig config, ActionDefinition a, string where, List<ValidationIssue> issues)
    {
        if ((a.Format != null || a.Query != null) && a.Type != ActionTypes.Shell)
            issues.Add(new("warning", where, "'format'/'query' only apply to shell actions; ignored here."));

        if (a.Retry is < 0)
            issues.Add(new("error", where, "retry cannot be negative."));

        switch (a.Type)
        {
            case ActionTypes.Shell:
                RequireField(a.Command, "command", where, issues);
                if (a.Format != null && !ActionFormats.All.Contains(a.Format))
                    issues.Add(new("error", where, $"unknown format '{a.Format}' (use json, table or raw)."));
                break;

            case ActionTypes.Submenu:
                if (RequireField(a.Menu, "menu", where, issues) &&
                    !config.Menus.ContainsKey(a.Menu!))
                    issues.Add(new("error", where, $"submenu target '{a.Menu}' does not exist."));
                break;

            case ActionTypes.OpenUrl:
                RequireField(a.Url, "url", where, issues);
                break;

            case ActionTypes.OpenFile:
                RequireField(a.Path, "path", where, issues);
                break;

            case ActionTypes.Script:
                RequireField(a.Path, "path", where, issues);
                break;

            case ActionTypes.OpenConfig:
                RequireField(a.Path, "path", where, issues);
                break;

            case ActionTypes.InputShell:
                ValidateInputShell(a, where, issues);
                break;

            case ActionTypes.Sequence:
                ValidateStepBlock(config, a, where, issues);
                break;

            case ActionTypes.Parallel:
                ValidateStepBlock(config, a, where, issues);
                if (a.MaxParallel is < 1)
                    issues.Add(new("error", where, "max_parallel must be at least 1."));
                break;

            case ActionTypes.Delay:
                if (a.Seconds is null or <= 0)
                    issues.Add(new("error", where, "delay requires a positive 'seconds'."));
                break;

            case ActionTypes.Background:
                RequireField(a.Command, "command", where, issues);
                break;

            case ActionTypes.Jobs:
                break;

            case ActionTypes.Settings:
                break;

            case ActionTypes.Exit:
                break;

            case "":
                issues.Add(new("error", where, "Action has no 'type'."));
                break;

            default:
                issues.Add(new("warning", where,
                    $"Unknown action type '{a.Type}' (fine if a plugin provides it, otherwise it will error at runtime)."));
                break;
        }
    }

    private static void ValidateStepBlock(
        MenuConfig config, ActionDefinition a, string where, List<ValidationIssue> issues)
    {
        if (a.Steps == null || a.Steps.Count == 0)
        {
            issues.Add(new("error", where, $"{a.Type} requires at least one entry in 'steps'."));
            return;
        }

        for (var i = 0; i < a.Steps.Count; i++)
        {
            var step = a.Steps[i];
            var stepWhere = $"{where}.steps[{i}]";

            if (!Engine.StepRunner.IsSequenceStep(step.Type))
            {
                issues.Add(new("error", stepWhere,
                    $"type '{step.Type}' is not allowed as a sequence step " +
                    "(use shell, script, delay, or a nested sequence)."));
                continue;
            }

            // Recurse: validates the step's own required fields (and nested sequences).
            ValidateAction(config, step, stepWhere, issues);
        }

        ValidateHook(config, a.OnSuccess, $"{where}.on_success", issues);
        ValidateHook(config, a.OnFailure, $"{where}.on_failure", issues);
    }

    private static void ValidateHook(
        MenuConfig config, ActionDefinition? hook, string where, List<ValidationIssue> issues)
    {
        if (hook == null)
            return;

        if (!Engine.StepRunner.IsSequenceStep(hook.Type))
            issues.Add(new("error", where,
                $"type '{hook.Type}' is not allowed as a hook (use shell, script, delay, or a nested sequence)."));
        else
            ValidateAction(config, hook, where, issues);
    }

    private static void ValidateInputShell(ActionDefinition a, string where, List<ValidationIssue> issues)
    {
        var hasTemplate = RequireField(a.CommandTemplate, "command_template", where, issues);

        if (a.Inputs == null || a.Inputs.Count == 0)
        {
            issues.Add(new("error", where, "input+shell requires at least one entry in 'inputs'."));
            return;
        }

        foreach (var input in a.Inputs)
            foreach (var problem in Engine.InputValidator.CheckSpec(input))
                issues.Add(new("error", where, problem));

        if (!hasTemplate)
            return;

        var declared = a.Inputs.Select(x => x.Name).ToHashSet();
        var used = TemplateVar.Matches(a.CommandTemplate!).Select(m => m.Groups[1].Value).ToHashSet();

        // A warning, not an error: an unmatched {name} is passed through to the shell
        // literally (menuki only substitutes declared inputs), and brace syntax is often
        // meant for an embedded tool - jq, printf, a python f-string like {k}, etc.
        foreach (var name in used.Where(n => !declared.Contains(n)))
            issues.Add(new("warning", where, $"command_template uses {{{name}}} but no matching input is declared; it will be passed through literally."));

        foreach (var name in declared.Where(n => !used.Contains(n)))
            issues.Add(new("warning", where, $"input '{name}' is declared but never used in command_template."));
    }

    private static void CheckReachability(MenuConfig config, List<ValidationIssue> issues)
    {
        if (string.IsNullOrEmpty(config.StartMenu) || !config.Menus.ContainsKey(config.StartMenu))
            return;

        var reachable = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(config.StartMenu);
        reachable.Add(config.StartMenu);

        while (queue.Count > 0)
        {
            var menu = config.Menus[queue.Dequeue()];
            foreach (var item in menu.Items)
            {
                if (item.Action.Type != ActionTypes.Submenu || item.Action.Menu == null)
                    continue;
                if (config.Menus.ContainsKey(item.Action.Menu) && reachable.Add(item.Action.Menu))
                    queue.Enqueue(item.Action.Menu);
            }
        }

        foreach (var menuId in config.Menus.Keys.Where(id => !reachable.Contains(id)))
            issues.Add(new("warning", menuId, "Menu is unreachable from start_menu (no submenu path leads to it)."));
    }

    private static bool RequireField(string? value, string field, string where, List<ValidationIssue> issues)
    {
        if (string.IsNullOrEmpty(value))
        {
            issues.Add(new("error", where, $"'{field}' is required for this action type."));
            return false;
        }
        return true;
    }
}
