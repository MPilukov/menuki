namespace Menuki.Actions;

/// <summary>
/// Opens the appearance/behavior settings screen. Like the editor hotkeys, it hands a
/// sentinel back to the navigation loop, which owns the ThemeManager and runs the screen.
/// </summary>
public class SettingsActionExecutor : IActionExecutor
{
    public const string Sentinel = "__settings__";

    public string? Execute() => Sentinel;
}
