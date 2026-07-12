using Menuki.Actions;

namespace Menuki.Engine;

/// <summary>
/// An interactive appearance/behavior settings screen. Choices apply live (the screen
/// re-renders with the new theme / marker) and persist globally via <see cref="AppSettings"/>.
/// Reached from the welcome screen and from a <c>settings</c> action in a config.
/// </summary>
public static class SettingsScreen
{
    public static void Run(ThemeManager theme)
    {
        var selected = 0;

        while (true)
        {
            var markerState = AppSettings.ShowSelectionMarker ? "On" : "Off";

            var items = new List<MenuItem>
            {
                new($"Theme: {theme.ActiveThemeName}", new TagActionExecutor("theme"),
                    $"Enter cycles through {theme.AvailableThemes.Count} themes (or press T anywhere)."),
                new($"Selection marker: {markerState}", new TagActionExecutor("marker"),
                    "Show the ▶ marker on the highlighted row. Off = highlight by color only."),
                new("Back", new TagActionExecutor("back"), null),
            };

            var info = new Dictionary<string, string>
            {
                ["Settings"] = "Changes apply now and are saved to ~/.menuki/settings.json.",
                ["Themes"] = string.Join(", ", theme.AvailableThemes),
            };

            var engine = new MenuEngine("Settings", items, theme, info, isRoot: true, editable: false);
            engine.WriteMenu(selectedItemIndex: selected);
            var result = engine.RunMenu(selected);
            selected = engine.SelectedIndex;

            switch (result)
            {
                case "theme":
                    theme.Toggle();
                    break;
                case "marker":
                    AppSettings.SetShowSelectionMarker(!AppSettings.ShowSelectionMarker);
                    break;
                default: // "back" or Esc
                    return;
            }
        }
    }
}
