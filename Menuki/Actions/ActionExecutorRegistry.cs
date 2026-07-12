using Menuki.Config;
using Menuki.Plugins;

namespace Menuki.Actions;

public class ActionExecutorRegistry
{
    private readonly Dictionary<string, Func<ActionDefinition, IActionExecutor>> _factories = new();
    private readonly Dictionary<string, IActionPlugin> _plugins = new();

    public void Register(string actionType, Func<ActionDefinition, IActionExecutor> factory)
    {
        _factories[actionType] = factory;
    }

    public void RegisterPlugin(IActionPlugin plugin)
    {
        if (_factories.ContainsKey(plugin.ActionTypeName))
        {
            Console.Error.WriteLine($"Plugin conflict: action type '{plugin.ActionTypeName}' is already registered. Skipping '{plugin.DisplayName}'.");
            return;
        }

        _plugins[plugin.ActionTypeName] = plugin;
        _factories[plugin.ActionTypeName] = action =>
        {
            var parameters = action.Parameters ?? new Dictionary<string, string>();
            return plugin.CreateExecutor(parameters);
        };
    }

    public IActionExecutor Create(ActionDefinition action)
    {
        if (_factories.TryGetValue(action.Type, out var factory))
            return factory(action);

        return new ErrorActionExecutor($"Unknown action type: '{action.Type}'. Plugin may not be loaded.");
    }

    public string[] GetRegisteredTypes()
    {
        var types = new List<string>(ActionTypes.BuiltIn);
        foreach (var key in _plugins.Keys)
        {
            if (!types.Contains(key))
                types.Add(key);
        }
        return types.ToArray();
    }

    public IActionPlugin? GetPlugin(string actionType)
    {
        return _plugins.TryGetValue(actionType, out var plugin) ? plugin : null;
    }
}
