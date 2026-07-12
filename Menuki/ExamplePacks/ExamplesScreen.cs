using Menuki.Actions;
using Menuki.Engine;

namespace Menuki.Examples;

/// <summary>
/// Interactive browser for the built-in example packs, shown from the welcome screen.
/// Pick a pack and press Enter to run it; Esc returns to the list.
/// </summary>
public static class ExamplesScreen
{
    private const string Back = "__back__";

    public static void Run(ThemeManager theme)
    {
        var registry = ActionExecutorFactory.BuildDefault();
        var examples = ExampleCatalog.List();
        var width = examples.Count == 0 ? 0 : examples.Max(e => e.Name.Length);
        var selected = 0;

        while (true)
        {
            var items = examples
                .Select(e => new MenuItem($"{e.Name.PadRight(width)}   {e.Title}",
                    new TagActionExecutor(e.Name), e.Title))
                .ToList();
            items.Add(new MenuItem("Back", new TagActionExecutor(Back), null));

            var info = new Dictionary<string, string>
            {
                ["Examples"] = "Curated packs bundled in the binary - Enter to run one.",
                ["Save"] = "From the shell: menuki examples <name> --save to copy one and edit it.",
            };

            var engine = new MenuEngine("Example packs", items, theme, info, isRoot: true, editable: false);
            engine.WriteMenu(selectedItemIndex: selected);
            var result = engine.RunMenu(selected);
            selected = engine.SelectedIndex;

            if (result == null || result == Back)
                return;

            ExamplesCli.Launch(result, theme, registry);
        }
    }
}
