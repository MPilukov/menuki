namespace Menuki.Actions;

public class TagActionExecutor : IActionExecutor
{
    private readonly string _tag;

    public TagActionExecutor(string tag)
    {
        _tag = tag;
    }

    public string? Execute()
    {
        return _tag;
    }
}
