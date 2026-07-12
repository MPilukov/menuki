using Menuki.Engine;

namespace Menuki.Actions;

/// <summary>
/// Loads another config file as a nested navigation session. Returns a sentinel
/// that <see cref="Navigator"/> resolves (relative to the current config) and runs.
/// </summary>
public class OpenConfigActionExecutor : IActionExecutor
{
    private readonly string _path;

    public OpenConfigActionExecutor(string path)
    {
        _path = path;
    }

    public string? Execute() => Navigator.OpenConfigPrefix + _path;
}
