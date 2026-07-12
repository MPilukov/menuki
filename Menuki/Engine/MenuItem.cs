using Menuki.Actions;

namespace Menuki.Engine;

public class MenuItem
{
    public string Name { get; }
    public IActionExecutor Action { get; }
    public string? Description { get; }
    public string? Help { get; }

    public MenuItem(string name, IActionExecutor action, string? description = null, string? help = null)
    {
        Name = name;
        Action = action;
        Description = description;
        Help = help;
    }
}
