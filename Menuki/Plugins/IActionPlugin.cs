using Menuki.Actions;

namespace Menuki.Plugins;

public interface IActionPlugin
{
    string ActionTypeName { get; }
    string DisplayName { get; }
    IReadOnlyList<PluginParameterInfo> Parameters { get; }
    IActionExecutor CreateExecutor(IReadOnlyDictionary<string, string> parameters);
}
