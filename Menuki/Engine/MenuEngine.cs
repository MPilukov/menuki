namespace Menuki.Engine;

public class MenuEngine
{
    private List<MenuItem> _currentItems;
    private readonly Dictionary<string, string>? _addInfo;
    private readonly string _menuTitle;
    private readonly ThemeManager _theme;

    // Both prefixes are the same visual width so the item text stays aligned whether or
    // not a row is selected, and a repaint cleanly overwrites the marker with spaces.
    private static readonly string Prefix = "    ";
    private static readonly string MarkerPrefix = "  ▶ "; // "  ▶ "

    // The selected-row prefix depends on the user's preference: a "▶" marker (cursor
    // hidden, marker is the cue) or plain indentation (cursor stays visible as the cue).
    private static string SelectedPrefix => AppSettings.ShowSelectionMarker ? MarkerPrefix : Prefix;

    /// <summary>
    /// Show or hide the terminal cursor, ignoring platforms/streams that don't support it.
    /// The menu hides the cursor so the ▶ marker is the only selection indicator (no stray
    /// block parked at the line start); text-input paths turn it back on.
    /// </summary>
    internal static void SetCursorVisible(bool visible)
    {
        try { Console.CursorVisible = visible; }
        catch { /* redirected output or unsupported platform */ }
    }

    private static int SafeWindowWidth()
    {
        try { return Console.WindowWidth; } catch { return 0; }
    }

    private static int SafeWindowHeight()
    {
        try { return Console.WindowHeight; } catch { return 0; }
    }

    private readonly bool _isRoot;
    private readonly bool _editable;
    private readonly bool _needShortVersion;
    private int _shortVersionPage;
    private readonly int _shortVersionSize;
    private readonly List<MenuItem>? _savedAllCurrentItems;
    private int _itemsStartLine;

    private string _searchQuery = "";
    private readonly List<MenuItem> _allItems;

    /// <summary>Index of the item that was selected when the loop last returned - used to restore selection.</summary>
    public int SelectedIndex { get; private set; }

    public MenuEngine(string menuTitle, List<MenuItem> items, ThemeManager theme, Dictionary<string, string>? addInfo = null, bool isRoot = false, bool editable = true)
    {
        _addInfo = addInfo;
        _menuTitle = menuTitle;
        _theme = theme;
        _isRoot = isRoot;
        _editable = editable;

        // Console.WindowWidth can be 0 when the size is undetermined (some ptys / redirected
        // consoles); guard the truncation so a negative budget never throws.
        var width = SafeWindowWidth();
        _currentItems = new List<MenuItem>();
        foreach (var item in items)
        {
            var budget = width - Prefix.Length - 5;
            var name = (width > 0 && item.Name.Length + Prefix.Length > width && budget > 0)
                ? item.Name.Substring(0, budget)
                : item.Name;

            _currentItems.Add(new MenuItem(name, item.Action, item.Description, item.Help));
        }

        _allItems = new List<MenuItem>(_currentItems);

        var height = SafeWindowHeight();
        if (height > 0 && height <= _currentItems.Count)
        {
            _needShortVersion = true;
            _shortVersionSize = Math.Max(1, height - 2);
            _savedAllCurrentItems = _currentItems;
        }
    }

    private List<MenuItem> GetShortItems(int pageAdd)
    {
        _shortVersionPage += pageAdd;
        var maxPage = _savedAllCurrentItems!.Count / _shortVersionSize;

        if (_shortVersionPage < 0)
            _shortVersionPage = maxPage;
        else if (_shortVersionPage > maxPage)
            _shortVersionPage = 0;

        return _savedAllCurrentItems
            .Skip(_shortVersionPage * _shortVersionSize)
            .Take(_shortVersionSize)
            .ToList();
    }

    private void ApplySearch()
    {
        if (_searchQuery.Length == 0)
        {
            _currentItems = new List<MenuItem>(_allItems);
            _shortVersionPage = 0;
        }
        else
        {
            _currentItems = _allItems
                .Where(i => i.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public void WriteMenu(int selectedItemIndex, string? pushTextToUser = null, int? pageAdd = null)
    {
        if (_needShortVersion && _searchQuery.Length == 0)
        {
            var items = GetShortItems(pageAdd: pageAdd ?? 0);
            _currentItems = items;
            Console.SetCursorPosition(0, 0);
        }

        Console.ForegroundColor = _theme.Text;
        Console.Clear();
        // With the marker on, the cursor is hidden so ▶ is the only cue; with it off, the
        // cursor stays visible and marks the selected row itself.
        SetCursorVisible(!AppSettings.ShowSelectionMarker);

        if (_addInfo != null && _addInfo.Count > 0)
        {
            Console.ForegroundColor = _theme.InfoBorder;
            Console.WriteLine("-------- Info --------");

            var maxLength = _addInfo.Select(x => x.Key).Max(x => x.Length);

            foreach (var data in _addInfo)
            {
                Console.ForegroundColor = _theme.InfoLabel;
                Console.Write(Prefix);
                Console.Write(data.Key);

                if (maxLength != data.Key.Length)
                {
                    var tabs = new string(' ', maxLength - data.Key.Length);
                    Console.Write(tabs);
                }

                Console.Write(" : ");

                Console.ForegroundColor = _theme.InfoValue;
                Console.WriteLine(data.Value);
            }

            Console.ForegroundColor = _theme.InfoBorder;
            Console.WriteLine("----------------------");
        }

        if (_searchQuery.Length > 0)
        {
            Console.ForegroundColor = _theme.Message;
            Console.Write(Prefix);
            Console.WriteLine($"Search: {_searchQuery}");
            Console.ForegroundColor = _theme.Text;
        }
        else if (!string.IsNullOrWhiteSpace(pushTextToUser))
        {
            Console.ForegroundColor = _theme.Message;
            Console.Write(Prefix);
            Console.WriteLine(pushTextToUser);
            Console.ForegroundColor = _theme.Text;
        }
        else
        {
            Console.WriteLine();
        }

        Console.ForegroundColor = _theme.Title;
        Console.WriteLine($"-------- {_menuTitle} --------");
        Console.ForegroundColor = _theme.Text;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (Console.BufferHeight < _currentItems.Count + Console.CursorTop)
                    Console.SetBufferSize(Console.BufferWidth, Console.BufferHeight + _currentItems.Count + Console.CursorTop + 1);
            }
            catch
            {
                // ignore
            }
        }

        _itemsStartLine = Console.CursorTop;

        if (_currentItems.Count == 0 && _searchQuery.Length > 0)
        {
            Console.ForegroundColor = _theme.Message;
            Console.WriteLine($"{Prefix}No matches");
            Console.ForegroundColor = _theme.Text;
        }
        else
        {
            for (var idx = 0; idx < _currentItems.Count; idx++)
            {
                var item = _currentItems[idx];

                if (idx == selectedItemIndex)
                {
                    Console.ForegroundColor = _theme.Selected;
                    Console.WriteLine($"{SelectedPrefix}{item.Name}");
                    Console.ForegroundColor = _theme.Text;
                }
                else
                {
                    Console.WriteLine($"{Prefix}{item.Name}");
                }
            }
        }

        WriteHints();
    }

    private void WriteHints()
    {
        Console.WriteLine();
        Console.ForegroundColor = _theme.InfoBorder;

        var hints = new List<string> { "Up/Down: Navigate", "Enter: Select" };

        if (_editable)
        {
            hints.Add("E: Edit JSON");
            hints.Add("Q: Quick Edit");
            hints.Add("V: Validate");
            hints.Add("R: Reload");
        }

        hints.Add("?: Info");
        hints.Add("T: Theme");
        hints.Add(_isRoot ? "Esc: Exit" : "Esc: Back");

        if (_needShortVersion)
            hints.Add("Left/Right: Page");

        Console.WriteLine($"{Prefix}{string.Join("  |  ", hints)}");
        Console.ForegroundColor = _theme.Text;
    }

    /// <summary>
    /// Runs the interactive menu loop.
    /// Returns a menu-id string to navigate to, or null to go back.
    /// </summary>
    public string? RunMenu(int startIndex = 0)
    {
        if (_currentItems.Count == 0)
        {
            ConsoleKeyInfo ki;
            do
            {
                ki = Console.ReadKey(intercept: true);
                if (_editable && EditCommand(ki.Key) is { } editCmd) { SetCursorVisible(true); return editCmd; }
                if (ki.Key == ConsoleKey.T)
                {
                    _theme.Toggle();
                    WriteMenu(selectedItemIndex: 0);
                    return RunMenu();
                }
                if (ki.Key == ConsoleKey.Escape)
                {
                    if (_searchQuery.Length > 0)
                    {
                        _searchQuery = "";
                        ApplySearch();
                        WriteMenu(selectedItemIndex: 0);
                        return RunMenu();
                    }
                    return null;
                }
                if (ki.Key == ConsoleKey.Backspace && _searchQuery.Length > 0)
                {
                    _searchQuery = _searchQuery[..^1];
                    ApplySearch();
                    WriteMenu(selectedItemIndex: 0);
                    return RunMenu();
                }
                if (!char.IsControl(ki.KeyChar) && ki.KeyChar != ' ')
                {
                    _searchQuery += ki.KeyChar;
                    ApplySearch();
                    WriteMenu(selectedItemIndex: 0);
                    return RunMenu();
                }
            }
            while (true);
        }

        ConsoleKeyInfo keyinfo;
        var index = Math.Clamp(startIndex, 0, _currentItems.Count - 1);
        SelectedIndex = index;

        Console.SetCursorPosition(0, _itemsStartLine + index);

        do
        {
            keyinfo = Console.ReadKey(intercept: true);

            var preSelectedStr = _currentItems[index].Name;
            Console.ForegroundColor = _theme.Text;

            Console.SetCursorPosition(0, _itemsStartLine + index);
            Console.Write($"{Prefix}{preSelectedStr}");

            if (keyinfo.Key == ConsoleKey.DownArrow)
            {
                index = (index + 1 < _currentItems.Count) ? index + 1 : 0;

                var str = _currentItems[index].Name;
                Console.ForegroundColor = _theme.Selected;
                Console.SetCursorPosition(0, _itemsStartLine + index);
                Console.Write($"{SelectedPrefix}{str}");
            }

            if (keyinfo.Key == ConsoleKey.UpArrow)
            {
                index = (index - 1 >= 0) ? index - 1 : _currentItems.Count - 1;

                var str = _currentItems[index].Name;
                Console.ForegroundColor = _theme.Selected;
                Console.SetCursorPosition(0, _itemsStartLine + index);
                Console.Write($"{SelectedPrefix}{str}");
            }

            if (keyinfo.Key == ConsoleKey.Enter)
            {
                try
                {
                    SetCursorVisible(true); // the action may prompt for input
                    var result = _currentItems[index].Action.Execute();
                    if (result != null)
                    {
                        SelectedIndex = index;
                        return result;
                    }

                    WriteMenu(selectedItemIndex: index);
                    return RunMenu(index);
                }
                catch (Exception e)
                {
                    ShowErrorToUser("Action.Execute Error: " + e.Message);
                    WriteMenu(selectedItemIndex: index);
                    return RunMenu(index);
                }
            }

            if (keyinfo.Key == ConsoleKey.T)
            {
                _theme.Toggle();
                WriteMenu(selectedItemIndex: index);
                return RunMenu(index);
            }

            if (_editable && EditCommand(keyinfo.Key) is { } editCmd)
            {
                SetCursorVisible(true); // handing off to the editor, which reads input
                return editCmd;
            }

            if (_needShortVersion && _searchQuery.Length == 0)
            {
                if (keyinfo.Key == ConsoleKey.RightArrow)
                {
                    WriteMenu(selectedItemIndex: 0, pageAdd: 1);
                    return RunMenu();
                }

                if (keyinfo.Key == ConsoleKey.LeftArrow)
                {
                    WriteMenu(selectedItemIndex: 0, pageAdd: -1);
                    return RunMenu();
                }
            }

            if (keyinfo.Key == ConsoleKey.Escape)
            {
                if (_searchQuery.Length > 0)
                {
                    _searchQuery = "";
                    ApplySearch();
                    WriteMenu(selectedItemIndex: 0);
                    return RunMenu();
                }
                break;
            }

            if (keyinfo.Key == ConsoleKey.Backspace)
            {
                if (_searchQuery.Length > 0)
                {
                    _searchQuery = _searchQuery[..^1];
                    ApplySearch();
                    WriteMenu(selectedItemIndex: 0);
                    return RunMenu();
                }
            }

            if (keyinfo.KeyChar == '?')
            {
                WriteMenu(selectedItemIndex: index, pushTextToUser: Describe(_currentItems[index]));
                Console.SetCursorPosition(0, _itemsStartLine + index);
                continue;
            }

            if (!char.IsControl(keyinfo.KeyChar) && keyinfo.KeyChar != ' ')
            {
                _searchQuery += keyinfo.KeyChar;
                ApplySearch();
                WriteMenu(selectedItemIndex: 0);
                return RunMenu();
            }
        }
        while (true);

        return null;
    }

    /// <summary>Info line shown for the '?' key: description plus optional help.</summary>
    private static string Describe(MenuItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Description))
            parts.Add(item.Description!.Trim());
        if (!string.IsNullOrWhiteSpace(item.Help))
            parts.Add(item.Help!.Trim());

        return parts.Count == 0
            ? $"{item.Name}: no description."
            : string.Join(" - ", parts);
    }

    private void ShowErrorToUser(string message)
    {
        WriteMenu(selectedItemIndex: 0, pushTextToUser: message);
        RunMenu();
    }

    /// <summary>Maps an editor hotkey to the sentinel the navigation loop understands.</summary>
    private static string? EditCommand(ConsoleKey key) => key switch
    {
        ConsoleKey.E => "__edit_json__",
        ConsoleKey.Q => "__quick_edit__",
        ConsoleKey.V => "__validate__",
        ConsoleKey.R => "__reload__",
        _ => null
    };
}
