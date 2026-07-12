namespace Menuki.Actions;

public interface IActionExecutor
{
    /// <summary>
    /// Executes the action.
    /// Returns a menu-id to navigate to, or null to stay/go back.
    /// </summary>
    string? Execute();
}
