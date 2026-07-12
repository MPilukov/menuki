using Menuki.Config;

namespace Menuki.Engine;

public static class InfoPanelResolver
{
    public static Dictionary<string, string> Resolve(List<InfoPanelEntry>? entries)
    {
        var result = new Dictionary<string, string>();
        if (entries == null)
            return result;

        foreach (var entry in entries)
        {
            if (entry.Value != null)
            {
                result[entry.Label] = entry.Value;
            }
            else if (entry.Command != null)
            {
                try
                {
                    result[entry.Label] = ShellRunner.Run(entry.Command);
                }
                catch
                {
                    result[entry.Label] = "<error>";
                }
            }
        }

        return result;
    }
}
