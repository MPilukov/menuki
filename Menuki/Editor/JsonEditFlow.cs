using System.Text.Json;
using Menuki.Authoring;
using Menuki.Config;

namespace Menuki.Editor;

/// <summary>
/// The JSON-first editing flow: open the config in an external editor, then
/// re-read, validate and reload it - with error recovery. The in-memory config
/// is never replaced until a new version both deserializes and passes validation.
/// </summary>
public static class JsonEditFlow
{
    /// <summary>Back up, open the file in the chosen editor, then reload with validation/recovery.</summary>
    public static MenuConfig? EditJson(string path, string? editor = null)
    {
        TryBackup(path);

        if (!ExternalEditor.Launch(path, editor))
        {
            Pause();
            return null;
        }

        return LoadValidateRecover(path, editor);
    }

    /// <summary>Re-read the file from disk with validation/recovery.</summary>
    public static MenuConfig? Reload(string path, string? editor = null) => LoadValidateRecover(path, editor);

    /// <summary>Validate the on-disk file and print a report (read-only).</summary>
    public static void Validate(string path)
    {
        Console.Clear();
        var (config, error) = TryLoad(path);
        if (config == null)
            PrintInvalidJson(error!);
        else
            PrintReport(MenuValidator.Validate(config));
        Pause();
    }

    private static MenuConfig? LoadValidateRecover(string path, string? editor)
    {
        while (true)
        {
            Console.Clear();
            var (config, error) = TryLoad(path);

            if (config != null)
            {
                var result = MenuValidator.Validate(config);
                if (result.Ok)
                {
                    PrintSuccess(config);
                    Pause();
                    return config;
                }
                PrintErrors(result);
            }
            else
            {
                PrintInvalidJson(error!);
            }

            switch (PromptRecovery(File.Exists(BakPath(path))))
            {
                case RecoveryChoice.Edit:
                    ExternalEditor.Launch(path, editor);
                    continue;
                case RecoveryChoice.Restore:
                    TryRestore(path);
                    continue;
                default:
                    return null; // keep the current in-memory menu
            }
        }
    }

    // --- load -----------------------------------------------------------

    private static (MenuConfig? Config, string? Error) TryLoad(string path)
    {
        try
        {
            var config = JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(path));
            return config == null ? (null, "File is empty or parsed to null.") : (config, null);
        }
        catch (JsonException ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex)
        {
            return (null, $"Cannot read file: {ex.Message}");
        }
    }

    // --- output ---------------------------------------------------------

    private static void PrintSuccess(MenuConfig config)
    {
        var items = config.Menus.Values.Sum(m => m.Items.Count);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Configuration reloaded");
        Console.ResetColor();
        Console.WriteLine($"  {config.Menus.Count} menus, {items} items");
    }

    private static void PrintErrors(ValidationResult result)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Configuration is invalid");
        Console.ResetColor();
        Console.WriteLine();

        foreach (var issue in result.Issues.Where(i => i.Level == "error"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("ERROR  ");
            Console.ResetColor();
            Console.WriteLine(issue.Where);
            Console.WriteLine($"       {issue.Message}");
            Console.WriteLine();
        }
    }

    private static void PrintInvalidJson(string error)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Configuration is not valid JSON");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"       {error}");
        Console.WriteLine();
    }

    private static void PrintReport(ValidationResult result)
    {
        if (result.Ok)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Configuration is valid");
            Console.ResetColor();
        }
        else
        {
            PrintErrors(result);
        }

        foreach (var issue in result.Issues.Where(i => i.Level == "warning"))
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("WARN   ");
            Console.ResetColor();
            Console.WriteLine($"{issue.Where}: {issue.Message}");
        }

        Console.WriteLine();
        Console.WriteLine(result.Tree);
    }

    private enum RecoveryChoice { Edit, Restore, Keep }

    private static RecoveryChoice PromptRecovery(bool hasBackup)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(hasBackup
            ? "[E] Edit again   [B] Restore backup   [Esc] Keep current in-memory menu"
            : "[E] Edit again   [Esc] Keep current in-memory menu");
        Console.ResetColor();

        while (true)
        {
            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.E:
                    return RecoveryChoice.Edit;
                case ConsoleKey.B when hasBackup:
                    return RecoveryChoice.Restore;
                case ConsoleKey.Escape:
                    return RecoveryChoice.Keep;
            }
        }
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);
    }

    // --- backup ---------------------------------------------------------

    private static string BakPath(string path) => path + ".bak";

    private static void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Copy(path, BakPath(path), overwrite: true);
        }
        catch
        {
            // Backup is best-effort; proceed without it.
        }
    }

    private static void TryRestore(string path)
    {
        try
        {
            var bak = BakPath(path);
            if (File.Exists(bak))
                File.Copy(bak, path, overwrite: true);
        }
        catch
        {
            // Nothing to restore or not writable - the next load will report it.
        }
    }
}
