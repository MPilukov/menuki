namespace Menuki.Actions;

public class ExitActionExecutor : IActionExecutor
{
    public string? Execute()
    {
        Environment.Exit(0);
        return null;
    }
}
