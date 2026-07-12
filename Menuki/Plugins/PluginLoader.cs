using System.Reflection;

namespace Menuki.Plugins;

public static class PluginLoader
{
    private static readonly string PluginsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki", "plugins");

    public static List<IActionPlugin> LoadAll()
    {
        var plugins = new List<IActionPlugin>();

        if (!Directory.Exists(PluginsDir))
            return plugins;

        foreach (var dll in Directory.GetFiles(PluginsDir, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IActionPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    try
                    {
                        var instance = Activator.CreateInstance(type) as IActionPlugin;
                        if (instance != null)
                            plugins.Add(instance);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to instantiate plugin '{type.FullName}' from {Path.GetFileName(dll)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load plugin assembly '{Path.GetFileName(dll)}': {ex.Message}");
            }
        }

        return plugins;
    }
}
