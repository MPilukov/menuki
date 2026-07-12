using System.Text;
using Menuki.Config;

namespace Menuki.Authoring;

/// <summary>
/// Renders a menu config as an ASCII tree so an author (human or agent) can see
/// the nesting at a glance. Submenu links are expanded once; a link back to an
/// already-shown menu is marked rather than recursed, so arbitrary nesting and
/// cycles are safe to preview.
/// </summary>
public static class MenuTree
{
    public static string Render(MenuConfig config)
    {
        var sb = new StringBuilder();
        var startId = config.StartMenu;

        if (!config.Menus.TryGetValue(startId, out var start))
        {
            sb.AppendLine($"(start_menu '{startId}' not found)");
            return sb.ToString();
        }

        var visited = new HashSet<string> { startId };
        sb.AppendLine($"{startId}  \"{start.Title}\"");
        RenderItems(config, start, "", visited, sb);
        return sb.ToString().TrimEnd();
    }

    private static void RenderItems(
        MenuConfig config, MenuDefinition menu, string prefix, HashSet<string> visited, StringBuilder sb)
    {
        for (var i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];
            var last = i == menu.Items.Count - 1;
            var branch = last ? "└─ " : "├─ ";
            var childPrefix = prefix + (last ? "   " : "│  ");
            var action = item.Action;

            if (action.Type == ActionTypes.Submenu)
            {
                var target = action.Menu ?? "?";
                if (config.Menus.TryGetValue(target, out var sub))
                {
                    if (visited.Add(target))
                    {
                        sb.AppendLine($"{prefix}{branch}{item.Name}  → {target}");
                        RenderItems(config, sub, childPrefix, visited, sb);
                    }
                    else
                    {
                        sb.AppendLine($"{prefix}{branch}{item.Name}  → {target} (shown above)");
                    }
                }
                else
                {
                    sb.AppendLine($"{prefix}{branch}{item.Name}  → {target} (MISSING)");
                }
            }
            else if (action.Type is ActionTypes.Sequence or ActionTypes.Parallel && action.Steps is { Count: > 0 })
            {
                sb.AppendLine($"{prefix}{branch}{item.Name}  [{action.Type}]");
                var steps = action.Steps;
                for (var j = 0; j < steps.Count; j++)
                {
                    var slast = j == steps.Count - 1;
                    sb.AppendLine($"{childPrefix}{(slast ? "└─ " : "├─ ")}{Engine.StepRunner.Label(steps[j])}");
                }
            }
            else
            {
                sb.AppendLine($"{prefix}{branch}{item.Name}  [{action.Type}]");
            }
        }
    }
}
