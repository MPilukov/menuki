namespace Menuki.Actions;

public class SubmenuActionExecutor : IActionExecutor
{
    private readonly string _menuId;

    public SubmenuActionExecutor(string menuId)
    {
        _menuId = menuId;
    }

    public string? Execute()
    {
        return _menuId;
    }
}
