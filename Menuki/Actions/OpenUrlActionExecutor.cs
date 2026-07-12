using Menuki.Engine;

namespace Menuki.Actions;

public class OpenUrlActionExecutor : IActionExecutor
{
    private readonly string _url;

    public OpenUrlActionExecutor(string url)
    {
        _url = url;
    }

    public string? Execute()
    {
        ShellRunner.OpenUrl(_url);
        return null;
    }
}
