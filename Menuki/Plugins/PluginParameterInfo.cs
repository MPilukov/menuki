namespace Menuki.Plugins;

public class PluginParameterInfo
{
    public required string Name { get; init; }
    public required string Prompt { get; init; }
    public bool Required { get; init; } = true;
    public string? DefaultValue { get; init; }
}
