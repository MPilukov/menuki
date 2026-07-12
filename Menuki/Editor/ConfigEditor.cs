using System.Text.Json;
using Menuki.Actions;
using Menuki.Config;
using Menuki.Engine;

namespace Menuki.Editor;

public class ConfigEditor
{
    private readonly MenuConfig _config;
    private readonly string _configPath;
    private readonly ThemeManager _theme;
    private readonly ActionExecutorRegistry? _registry;

    public ConfigEditor(MenuConfig config, string configPath, ThemeManager theme, ActionExecutorRegistry? registry = null)
    {
        _config = config;
        _configPath = configPath;
        _theme = theme;
        _registry = registry;
    }

    // ── E key → main editor menu ─────────────────────────────────

    public void EditMenu(string menuId)
    {
        if (!_config.Menus.TryGetValue(menuId, out var menuDef))
            return;

        while (true)
        {
            var info = new Dictionary<string, string>
            {
                { "Menu ID", menuId },
                { "Title", menuDef.Title },
                { "Items", menuDef.Items.Count.ToString() },
                { "Info entries", (menuDef.Info?.Count ?? 0).ToString() },
                { "App Title", _config.Title },
                { "Start Menu", _config.StartMenu },
                { "Total Menus", _config.Menus.Count.ToString() }
            };

            var items = new List<MenuItem>
            {
                new("Edit menu title", new TagActionExecutor("title")),
                new("Edit info panel", new TagActionExecutor("info")),
                new("Edit menu items", new TagActionExecutor("items")),
                new("--- App Settings ---", new TagActionExecutor("__sep__")),
                new("Edit app title", new TagActionExecutor("app_title")),
                new("Edit start menu", new TagActionExecutor("start")),
                new("Create new submenu here", new TagActionExecutor("create_sub")),
                new("Delete a menu", new TagActionExecutor("delete_menu")),
                new("Import from file", new TagActionExecutor("import")),
            };

            var engine = new MenuEngine($"Edit: {menuDef.Title}", items, _theme, info, editable: false);
            engine.WriteMenu(0);
            var result = engine.RunMenu();

            switch (result)
            {
                case "title":
                    menuDef.Title = PromptHelper.PromptString("Menu Title", menuDef.Title, _theme);
                    Save();
                    break;
                case "info":
                    EditInfoPanel(menuDef);
                    break;
                case "items":
                    EditMenuItems(menuDef);
                    break;
                case "app_title":
                    _config.Title = PromptHelper.PromptString("App Title", _config.Title, _theme);
                    Save();
                    break;
                case "start":
                    EditStartMenu();
                    break;
                case "create_sub":
                    CreateSubmenuHere(menuDef);
                    break;
                case "delete_menu":
                    DeleteMenu(menuId);
                    break;
                case "import":
                    ImportConfig();
                    break;
                case "__sep__":
                    break;
                default:
                    return;
            }
        }
    }

    // ── Info Panel ────────────────────────────────────────────────

    private void EditInfoPanel(MenuDefinition menuDef)
    {
        while (true)
        {
            menuDef.Info ??= new List<InfoPanelEntry>();
            var items = new List<MenuItem>
            {
                new("[+] Add entry", new TagActionExecutor("add"))
            };

            for (var i = 0; i < menuDef.Info.Count; i++)
            {
                var entry = menuDef.Info[i];
                var desc = entry.Value != null
                    ? $"{entry.Label} = {entry.Value}"
                    : $"{entry.Label} (cmd: {entry.Command})";
                items.Add(new MenuItem(desc, new TagActionExecutor($"edit_{i}")));
            }

            if (menuDef.Info.Count > 0)
            {
                items.Add(new MenuItem("--- Delete ---", new TagActionExecutor("__sep__")));
                for (var i = 0; i < menuDef.Info.Count; i++)
                    items.Add(new MenuItem($"[-] {menuDef.Info[i].Label}", new TagActionExecutor($"del_{i}")));
            }

            var engine = new MenuEngine("Edit Info Panel", items, _theme, editable: false);
            engine.WriteMenu(0);
            var result = engine.RunMenu();

            if (result == null || result == "__sep__") return;

            if (result == "add")
            {
                AddInfoEntry(menuDef);
            }
            else if (result.StartsWith("edit_") && int.TryParse(result[5..], out var editIdx) && editIdx < menuDef.Info.Count)
            {
                EditInfoEntry(menuDef.Info[editIdx]);
                Save();
            }
            else if (result.StartsWith("del_") && int.TryParse(result[4..], out var delIdx) && delIdx < menuDef.Info.Count)
            {
                if (PromptHelper.PromptConfirm($"Delete '{menuDef.Info[delIdx].Label}'?", _theme))
                {
                    menuDef.Info.RemoveAt(delIdx);
                    Save();
                }
            }
        }
    }

    private void AddInfoEntry(MenuDefinition menuDef)
    {
        var label = PromptHelper.PromptString("Label", "", _theme);
        if (string.IsNullOrWhiteSpace(label)) return;

        var typeIdx = PromptHelper.PromptChoice("Entry type", new[] { "Static value", "Shell command" }, 0, _theme);
        if (typeIdx == null) return;

        var entry = new InfoPanelEntry { Label = label };

        if (typeIdx == 0)
            entry.Value = PromptHelper.PromptString("Value", "", _theme);
        else
            entry.Command = PromptHelper.PromptString("Command", "", _theme);

        menuDef.Info!.Add(entry);
        Save();
    }

    private void EditInfoEntry(InfoPanelEntry entry)
    {
        entry.Label = PromptHelper.PromptString("Label", entry.Label, _theme);

        var currentType = entry.Value != null ? 0 : 1;
        var typeIdx = PromptHelper.PromptChoice("Entry type", new[] { "Static value", "Shell command" }, currentType, _theme);
        if (typeIdx == null) return;

        if (typeIdx == 0)
        {
            entry.Value = PromptHelper.PromptString("Value", entry.Value ?? "", _theme);
            entry.Command = null;
        }
        else
        {
            entry.Command = PromptHelper.PromptString("Command", entry.Command ?? "", _theme);
            entry.Value = null;
        }
    }

    // ── Menu Items ────────────────────────────────────────────────

    private void EditMenuItems(MenuDefinition menuDef)
    {
        while (true)
        {
            var items = new List<MenuItem>
            {
                new("[+] Add new item", new TagActionExecutor("add"))
            };

            for (var i = 0; i < menuDef.Items.Count; i++)
            {
                var item = menuDef.Items[i];
                var typeLabel = FormatActionType(item.Action);
                items.Add(new MenuItem($"[{i + 1}] {item.Name}  [{typeLabel}]", new TagActionExecutor($"item_{i}")));
            }

            var engine = new MenuEngine($"Items: {menuDef.Title}", items, _theme, editable: false);
            engine.WriteMenu(0);
            var result = engine.RunMenu();

            if (result == null) return;

            if (result == "add")
                AddMenuItem(menuDef);
            else if (result.StartsWith("item_") && int.TryParse(result[5..], out var idx) && idx < menuDef.Items.Count)
                EditSingleItem(menuDef, idx);
        }
    }

    private void EditSingleItem(MenuDefinition menuDef, int index)
    {
        while (true)
        {
            var item = menuDef.Items[index];
            var info = new Dictionary<string, string>
            {
                { "Name", item.Name },
                { "Action", FormatActionType(item.Action) },
                { "Position", $"{index + 1} of {menuDef.Items.Count}" }
            };

            var items = new List<MenuItem>
            {
                new("Edit name", new TagActionExecutor("name")),
                new($"Edit action type  [{FormatActionType(item.Action)}]", new TagActionExecutor("type")),
                new("Edit action parameters", new TagActionExecutor("params")),
                new("Move up", new TagActionExecutor("up")),
                new("Move down", new TagActionExecutor("down")),
                new("Delete this item", new TagActionExecutor("delete")),
            };

            var engine = new MenuEngine($"Edit: {item.Name}", items, _theme, info, editable: false);
            engine.WriteMenu(0);
            var result = engine.RunMenu();

            switch (result)
            {
                case "name":
                    item.Name = PromptHelper.PromptString("Item name", item.Name, _theme);
                    Save();
                    break;
                case "type":
                    EditActionType(item);
                    break;
                case "params":
                    EditActionParams(item.Action);
                    break;
                case "up":
                    if (index > 0)
                    {
                        (menuDef.Items[index], menuDef.Items[index - 1]) = (menuDef.Items[index - 1], menuDef.Items[index]);
                        index--;
                        Save();
                    }
                    break;
                case "down":
                    if (index < menuDef.Items.Count - 1)
                    {
                        (menuDef.Items[index], menuDef.Items[index + 1]) = (menuDef.Items[index + 1], menuDef.Items[index]);
                        index++;
                        Save();
                    }
                    break;
                case "delete":
                    if (PromptHelper.PromptConfirm($"Delete '{item.Name}'?", _theme))
                    {
                        menuDef.Items.RemoveAt(index);
                        Save();
                        return;
                    }
                    break;
                default:
                    return;
            }
        }
    }

    private void AddMenuItem(MenuDefinition menuDef)
    {
        var name = PromptHelper.PromptString("Item name", "", _theme);
        if (string.IsNullOrWhiteSpace(name)) return;

        var availableTypes = GetAvailableActionTypes();
        var typeIdx = PromptHelper.PromptChoice("Action type", availableTypes, 0, _theme);
        if (typeIdx == null) return;

        var action = new ActionDefinition { Type = availableTypes[typeIdx.Value] };

        if (action.Type == ActionTypes.Submenu)
        {
            var menuId = PromptSubmenuTarget();
            if (menuId == null) return;
            action.Menu = menuId;
        }
        else
        {
            PromptActionParams(action);
        }

        InsertBeforeExit(menuDef, new MenuItemDefinition { Name = name, Action = action });
        Save();
    }

    private string? PromptSubmenuTarget()
    {
        var existingIds = _config.Menus.Keys.ToArray();
        var choices = existingIds.Append(">> Create new menu").ToArray();
        var idx = PromptHelper.PromptChoice("Target menu", choices, 0, _theme);
        if (idx == null) return null;

        if (idx.Value < existingIds.Length)
            return existingIds[idx.Value];

        // Create new menu inline
        var newId = PromptHelper.PromptString("New menu ID", "", _theme);
        if (string.IsNullOrWhiteSpace(newId) || _config.Menus.ContainsKey(newId))
        {
            if (_config.Menus.ContainsKey(newId ?? ""))
                PromptHelper.ShowMessage($"Menu '{newId}' already exists.", _theme);
            return null;
        }

        var title = PromptHelper.PromptString("Menu title", newId, _theme);
        _config.Menus[newId] = new MenuDefinition { Title = title, Items = new List<MenuItemDefinition>() };
        Save();
        return newId;
    }

    private void EditActionType(MenuItemDefinition item)
    {
        var availableTypes = GetAvailableActionTypes();
        var currentIdx = Array.IndexOf(availableTypes, item.Action.Type);
        if (currentIdx < 0) currentIdx = 0;
        var newIdx = PromptHelper.PromptChoice("Action type", availableTypes, currentIdx, _theme);
        if (newIdx == null) return;

        if (newIdx.Value != currentIdx)
        {
            item.Action = new ActionDefinition { Type = availableTypes[newIdx.Value] };

            if (item.Action.Type == ActionTypes.Submenu)
            {
                var menuId = PromptSubmenuTarget();
                if (menuId != null) item.Action.Menu = menuId;
            }
            else
            {
                PromptActionParams(item.Action);
            }
        }

        Save();
    }

    private void PromptActionParams(ActionDefinition action)
    {
        switch (action.Type)
        {
            case ActionTypes.Shell:
                action.Command = PromptHelper.PromptString("Shell command", action.Command ?? "", _theme);
                break;
            case ActionTypes.Submenu:
                var menuId = PromptSubmenuTarget();
                if (menuId != null) action.Menu = menuId;
                break;
            case ActionTypes.OpenUrl:
                action.Url = PromptHelper.PromptString("URL", action.Url ?? "", _theme);
                break;
            case ActionTypes.InputShell:
                EditInputs(action);
                action.CommandTemplate = PromptHelper.PromptString("Command template (use {name})", action.CommandTemplate ?? "", _theme);
                break;
            case ActionTypes.OpenFile:
                action.Path = PromptHelper.PromptString("File path", action.Path ?? "", _theme);
                action.Editor = PromptHelper.PromptOptionalString("Editor (empty = OS default)", action.Editor, _theme);
                break;
            case ActionTypes.Script:
                action.Path = PromptHelper.PromptString("Script path", action.Path ?? "", _theme);
                action.Args = PromptHelper.PromptOptionalString("Arguments (optional)", action.Args, _theme);
                break;
            case ActionTypes.Exit:
                break;
            default:
                PromptPluginParams(action);
                break;
        }
    }

    private void EditActionParams(ActionDefinition action)
    {
        PromptActionParams(action);
        Save();
    }

    // ── Input+Shell inputs editing ───────────────────────────────

    private void EditInputs(ActionDefinition action)
    {
        action.Inputs ??= new List<InputDefinition>();

        while (true)
        {
            var items = new List<MenuItem>
            {
                new("[+] Add input variable", new TagActionExecutor("add"))
            };

            for (var i = 0; i < action.Inputs.Count; i++)
            {
                var inp = action.Inputs[i];
                var defaultStr = inp.Default != null ? $", default: \"{inp.Default}\"" : "";
                items.Add(new MenuItem($"{inp.Name} (prompt: \"{inp.Prompt}\"{defaultStr})", new TagActionExecutor($"edit_{i}")));
            }

            if (action.Inputs.Count > 0)
            {
                items.Add(new MenuItem("--- Delete ---", new TagActionExecutor("__sep__")));
                for (var i = 0; i < action.Inputs.Count; i++)
                    items.Add(new MenuItem($"[-] {action.Inputs[i].Name}", new TagActionExecutor($"del_{i}")));
            }

            var engine = new MenuEngine("Edit Inputs", items, _theme, editable: false);
            engine.WriteMenu(0);
            var result = engine.RunMenu();

            if (result == null || result == "__sep__") return;

            if (result == "add")
            {
                var name = PromptHelper.PromptString("Variable name (used in template as {name})", "", _theme);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var prompt = PromptHelper.PromptString("Prompt text shown to user", name, _theme);
                var def = PromptHelper.PromptOptionalString("Default value", null, _theme);
                action.Inputs.Add(new InputDefinition { Name = name, Prompt = prompt, Default = def });
                Save();
            }
            else if (result.StartsWith("edit_") && int.TryParse(result[5..], out var editIdx) && editIdx < action.Inputs.Count)
            {
                var inp = action.Inputs[editIdx];
                inp.Name = PromptHelper.PromptString("Variable name", inp.Name, _theme);
                inp.Prompt = PromptHelper.PromptString("Prompt text", inp.Prompt, _theme);
                inp.Default = PromptHelper.PromptOptionalString("Default value", inp.Default, _theme);
                Save();
            }
            else if (result.StartsWith("del_") && int.TryParse(result[4..], out var delIdx) && delIdx < action.Inputs.Count)
            {
                if (PromptHelper.PromptConfirm($"Delete input '{action.Inputs[delIdx].Name}'?", _theme))
                {
                    action.Inputs.RemoveAt(delIdx);
                    Save();
                }
            }
        }
    }

    // ── App Settings (inside E menu) ─────────────────────────────

    private void EditStartMenu()
    {
        var menuIds = _config.Menus.Keys
            .Where(id => _config.Menus[id].Items.Count > 0)
            .ToArray();

        if (menuIds.Length == 0)
        {
            PromptHelper.ShowMessage("No menus with items available.", _theme);
            return;
        }

        var currentIdx = Array.IndexOf(menuIds, _config.StartMenu);
        if (currentIdx < 0) currentIdx = 0;

        var idx = PromptHelper.PromptChoice("Start menu (only menus with items shown)", menuIds, currentIdx, _theme);
        if (idx == null) return;

        _config.StartMenu = menuIds[idx.Value];
        Save();
    }

    private void CreateSubmenuHere(MenuDefinition parentMenu)
    {
        var id = PromptHelper.PromptString("New menu ID (e.g. my-tools)", "", _theme);
        if (string.IsNullOrWhiteSpace(id)) return;

        if (_config.Menus.ContainsKey(id))
        {
            PromptHelper.ShowMessage($"Menu '{id}' already exists.", _theme);
            return;
        }

        var title = PromptHelper.PromptString("Menu title", id, _theme);

        _config.Menus[id] = new MenuDefinition
        {
            Title = title,
            Items = new List<MenuItemDefinition>()
        };

        InsertBeforeExit(parentMenu, new MenuItemDefinition
        {
            Name = title,
            Action = new ActionDefinition { Type = ActionTypes.Submenu, Menu = id }
        });

        Save();
        PromptHelper.ShowMessage($"Menu '{id}' created and linked in current menu.", _theme);
    }

    private void DeleteMenu(string currentMenuId)
    {
        var menuIds = _config.Menus.Keys
            .Where(k => k != _config.StartMenu && k != currentMenuId)
            .ToArray();

        if (menuIds.Length == 0)
        {
            PromptHelper.ShowMessage("No menus available to delete (can't delete start menu or current menu).", _theme);
            return;
        }

        var idx = PromptHelper.PromptChoice("Select menu to delete", menuIds, 0, _theme);
        if (idx == null) return;

        var targetId = menuIds[idx.Value];

        // Check references
        var referencedBy = _config.Menus
            .Where(m => m.Value.Items.Any(i => i.Action.Type == ActionTypes.Submenu && i.Action.Menu == targetId))
            .Select(m => m.Key)
            .ToList();

        if (referencedBy.Count > 0)
        {
            var refs = string.Join(", ", referencedBy);
            if (!PromptHelper.PromptConfirm($"'{targetId}' is linked from: {refs}. Links will break. Delete?", _theme))
                return;
        }

        if (!PromptHelper.PromptConfirm($"Delete menu '{targetId}'?", _theme))
            return;

        _config.Menus.Remove(targetId);

        // Clean up broken submenu links
        foreach (var menu in _config.Menus.Values)
            menu.Items.RemoveAll(i => i.Action.Type == ActionTypes.Submenu && i.Action.Menu == targetId);

        Save();
        PromptHelper.ShowMessage($"Menu '{targetId}' deleted. Broken links removed.", _theme);
    }

    // ── Import ───────────────────────────────────────────────────

    private void ImportConfig()
    {
        var path = PromptHelper.PromptFilePath("Import JSON config", _theme);
        if (path == null) return;

        MenuConfig? imported;
        try
        {
            var json = File.ReadAllText(path);
            imported = JsonSerializer.Deserialize<MenuConfig>(json);
        }
        catch (Exception e)
        {
            PromptHelper.ShowMessage($"Failed to parse: {e.Message}", _theme);
            return;
        }

        if (imported?.Menus == null || imported.Menus.Count == 0)
        {
            PromptHelper.ShowMessage("No menus found in the imported file.", _theme);
            return;
        }

        var importedCount = 0;

        foreach (var (id, menuDef) in imported.Menus)
        {
            if (_config.Menus.ContainsKey(id))
            {
                var conflictItems = new List<MenuItem>
                {
                    new("Skip (keep current)", new TagActionExecutor("skip")),
                    new("Overwrite (replace with imported)", new TagActionExecutor("overwrite")),
                    new("Rename imported (enter new ID)", new TagActionExecutor("rename")),
                };

                var conflictInfo = new Dictionary<string, string>
                {
                    { "Conflicting ID", id },
                    { "Current title", _config.Menus[id].Title },
                    { "Imported title", menuDef.Title }
                };

                var engine = new MenuEngine($"Conflict: {id}", conflictItems, _theme, conflictInfo, editable: false);
                engine.WriteMenu(0);
                var action = engine.RunMenu();

                switch (action)
                {
                    case "overwrite":
                        _config.Menus[id] = menuDef;
                        importedCount++;
                        break;
                    case "rename":
                        var newId = PromptHelper.PromptString("New menu ID", id + "-imported", _theme);
                        if (!string.IsNullOrWhiteSpace(newId) && !_config.Menus.ContainsKey(newId))
                        {
                            _config.Menus[newId] = menuDef;
                            importedCount++;
                        }
                        break;
                }
            }
            else
            {
                _config.Menus[id] = menuDef;
                importedCount++;
            }
        }

        if (importedCount > 0)
            Save();

        PromptHelper.ShowMessage($"Imported {importedCount} menu(s) from {Path.GetFileName(path)}.", _theme);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static void InsertBeforeExit(MenuDefinition menu, MenuItemDefinition newItem)
    {
        var exitIndex = menu.Items.FindLastIndex(i => i.Action.Type == ActionTypes.Exit);
        if (exitIndex >= 0)
            menu.Items.Insert(exitIndex, newItem);
        else
            menu.Items.Add(newItem);
    }

    private void Save()
    {
        ConfigSaver.Save(_config, _configPath);
    }

    private string FormatActionType(ActionDefinition action)
    {
        return action.Type switch
        {
            ActionTypes.Shell => "shell",
            ActionTypes.Submenu => $"submenu -> {action.Menu}",
            ActionTypes.Exit => "exit",
            ActionTypes.OpenUrl => "open-url",
            ActionTypes.InputShell => "input+shell",
            ActionTypes.OpenFile => $"open-file: {action.Path}",
            ActionTypes.Script => $"script: {action.Path}",
            _ => FormatPluginType(action.Type)
        };
    }

    private string FormatPluginType(string type)
    {
        if (_registry != null)
        {
            var plugin = _registry.GetPlugin(type);
            if (plugin != null)
                return plugin.DisplayName;
        }
        return $"{type} (plugin)";
    }

    private string[] GetAvailableActionTypes()
    {
        if (_registry != null)
            return _registry.GetRegisteredTypes();
        return ActionTypes.BuiltIn;
    }

    private void PromptPluginParams(ActionDefinition action)
    {
        if (_registry == null) return;
        var plugin = _registry.GetPlugin(action.Type);
        if (plugin == null) return;

        action.Parameters ??= new Dictionary<string, string>();
        foreach (var param in plugin.Parameters)
        {
            var current = action.Parameters.TryGetValue(param.Name, out var val) ? val : param.DefaultValue ?? "";
            var value = PromptHelper.PromptString(param.Prompt, current ?? "", _theme);
            if (!string.IsNullOrEmpty(value))
                action.Parameters[param.Name] = value;
        }
    }
}
