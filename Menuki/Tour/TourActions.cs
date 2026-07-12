using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Menuki.Actions;
using Menuki.Config;
using Menuki.Engine;
using Menuki.Headless;
using Menuki.Onboarding;

namespace Menuki.Tour;

/// <summary>
/// Private runtime actions available only inside the tour. They are NOT part of the
/// public config schema - they exist to let the tour show its own JSON, run itself
/// headlessly, and hand off to the menu-creation wizard.
/// </summary>
public static class TourActions
{
    public const string ShowJson = "tour:show-json";
    public const string Headless = "tour:headless";
    public const string CreateMenu = "tour:create-menu";

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void Register(ActionExecutorRegistry registry, string workspacePath, ThemeManager theme)
    {
        registry.Register(ShowJson, a =>
            new ShowJsonExecutor(workspacePath, a.Parameters?.GetValueOrDefault("menu") ?? ""));

        registry.Register(Headless, a =>
            new HeadlessDemoExecutor(workspacePath, a.Parameters?.GetValueOrDefault("action")));

        registry.Register(CreateMenu, _ => new CreateMenuExecutor(theme));
    }

    private static void PressKey()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\nPress any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);
    }

    /// <summary>Shows the JSON for the menu the user is currently exploring.</summary>
    private sealed class ShowJsonExecutor : IActionExecutor
    {
        private readonly string _workspacePath;
        private readonly string _menuId;

        public ShowJsonExecutor(string workspacePath, string menuId)
        {
            _workspacePath = workspacePath;
            _menuId = menuId;
        }

        public string? Execute()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"This screen is generated from this JSON (menu \"{_menuId}\"):\n");
            Console.ResetColor();

            try
            {
                var node = JsonNode.Parse(File.ReadAllText(_workspacePath));
                var menu = node?["menus"]?[_menuId];
                Console.WriteLine(menu?.ToJsonString(Pretty) ?? "(menu not found)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(could not read source: {ex.Message})");
            }

            PressKey();
            return null;
        }
    }

    /// <summary>Runs the tour config through the same headless path an agent/script uses.</summary>
    private sealed class HeadlessDemoExecutor : IActionExecutor
    {
        private readonly string _workspacePath;
        private readonly string? _actionId;

        public HeadlessDemoExecutor(string workspacePath, string? actionId)
        {
            _workspacePath = workspacePath;
            _actionId = actionId;
        }

        public string? Execute()
        {
            Console.Clear();
            var registry = ActionExecutorFactory.BuildDefault();

            MenuConfig config;
            try
            {
                config = JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(_workspacePath))!;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(could not load config: {ex.Message})");
                PressKey();
                return null;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            if (string.IsNullOrEmpty(_actionId))
            {
                Console.WriteLine("$ menuki list --config <this-tour>\n");
                Console.ResetColor();
                Console.WriteLine(JsonSerializer.Serialize(HeadlessRunner.BuildCatalog(config, registry), Pretty));
            }
            else
            {
                Console.WriteLine($"$ menuki exec --config <this-tour> --action {_actionId}\n");
                Console.ResetColor();
                var action = HeadlessRunner.Find(config, _actionId);
                if (action == null)
                {
                    Console.WriteLine($"(action '{_actionId}' not found)");
                }
                else
                {
                    var outcome = HeadlessRunner.Execute(action, new Dictionary<string, string>());
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        id = action.Id,
                        type = action.Type,
                        ok = outcome.Ok,
                        exit_code = outcome.ExitCode,
                        stdout = outcome.StdOut,
                        stderr = outcome.StdErr
                    }, Pretty));
                }
            }

            PressKey();
            return null;
        }
    }

    /// <summary>Hands off to the real menu-creation wizard, then runs the result.</summary>
    private sealed class CreateMenuExecutor : IActionExecutor
    {
        private readonly ThemeManager _theme;

        public CreateMenuExecutor(ThemeManager theme)
        {
            _theme = theme;
        }

        public string? Execute()
        {
            var result = OnboardingWizard.Run(_theme);
            if (result != null)
                Navigator.Run(result.Value.config, _theme, result.Value.configPath, ActionExecutorFactory.BuildDefault());
            return null;
        }
    }
}
