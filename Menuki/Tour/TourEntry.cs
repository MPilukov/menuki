using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Menuki.Actions;
using Menuki.Config;
using Menuki.Engine;

namespace Menuki.Tour;

/// <summary>
/// Entry point for `menuki tour`. Generates the tour config into an editable
/// workspace, registers the private tour actions, and runs it through the normal
/// navigator with a progress-tracking hook.
/// </summary>
public static class TourEntry
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string WorkspaceDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki", "tour", "workspace");

    public static void Run(ThemeManager? theme = null)
    {
        theme ??= new ThemeManager(null, null);

        var env = EnvDetect.Detect();
        var config = TourMenuBuilder.Build(env);

        Directory.CreateDirectory(WorkspaceDir);
        var configPath = Path.Combine(WorkspaceDir, "menu.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, Pretty));
        File.WriteAllText(Path.Combine(WorkspaceDir, "scenario.json"), ScenarioPack);

        var registry = ActionExecutorFactory.BuildDefault();
        TourActions.Register(registry, configPath, theme);

        var visited = TourProgress.Load();
        TourMenuBuilder.Decorate(config, visited);

        Console.Title = config.Title;
        Navigator.Run(config, theme, configPath, registry, onEnterMenu: menuId =>
        {
            if (TourMenuBuilder.IsSection(menuId) && visited.Add(menuId))
                TourProgress.Save(visited);
            TourMenuBuilder.Decorate(config, visited);
        });
    }

    /// <summary>A tiny self-contained pack loaded via open-config - all echoes, nothing real.</summary>
    private const string ScenarioPack = """
    {
      "title": "Demo Pack (git-style)",
      "start_menu": "main",
      "menus": {
        "main": {
          "title": "Git-style demo (simulated)",
          "info": [
            { "label": "Note", "value": "Loaded via open-config from the tour. Everything here only echoes." },
            { "label": "Return", "value": "Press Esc to go back to the tour." }
          ],
          "items": [
            { "name": "Status", "description": "Pretend to show repo status.",
              "action": { "type": "shell", "command": "echo 'On branch main - nothing to commit'" } },
            { "name": "Recent log", "description": "Pretend to list commits.",
              "action": { "type": "shell", "command": "echo 'abc123 first commit'" } },
            { "name": "Would deploy", "description": "Shows a command without running it.",
              "action": { "type": "shell", "command": "echo 'Would run: git push origin main (skipped - demo)'" } }
          ]
        }
      }
    }
    """;
}
