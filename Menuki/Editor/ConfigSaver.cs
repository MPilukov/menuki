using System.Text.Json;
using System.Text.Json.Serialization;
using Menuki.Config;

namespace Menuki.Editor;

public static class ConfigSaver
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Save(MenuConfig config, string configPath)
    {
        // Backup
        var backupPath = configPath + ".bak";
        if (File.Exists(configPath))
            File.Copy(configPath, backupPath, overwrite: true);

        // Serialize
        var json = JsonSerializer.Serialize(config, Options);

        // Atomic write
        var tempPath = configPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, configPath, overwrite: true);
    }
}
