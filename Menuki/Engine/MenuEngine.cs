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

    private string _searchQuery = "";
    private bool _searching; // '/' opened the search: printable keys go to the query, not to commands
    private readonly List<MenuItem> _allItems;

    // Geometry of the frame on screen, set by every full render. The menu tracks rows itself
    // (from the top after Console.Clear) instead of asking the terminal where the cursor is,
    // and keeps the frame within the window, so a row number always points at the same line.
    private int _width;
    private int _height;
    private int _titleRow = -1;
    private int _itemsTop;
    private int _itemRows;
    private int _offset; // index of the first item on screen when the list is scrolled
    private string? _message;
    private int _selected; // selected index in _currentItems (filtered); SelectedIndex is in the full list

    /// <summary>A screen line: text segments, each in its own colour.</summary>
    private sealed record Line(params (Action Style, string Text)[] Segments);

    /// <summary>Stands for the title line, which WriteTitle paints (its counter changes while scrolling).</summary>
    private static readonly Line TitleLine = new();

    /// <summary>Index of the item that was selected when the loop last returned - used to restore selection.</summary>
    public int SelectedIndex { get; private set; }

    public MenuEngine(string menuTitle, List<MenuItem> items, ThemeManager theme, Dictionary<string, string>? addInfo = null, bool isRoot = false, bool editable = true)
    {
        _addInfo = addInfo;
        _menuTitle = menuTitle;
        _theme = theme;
        _isRoot = isRoot;
        _editable = editable;

        _currentItems = new List<MenuItem>(items);
        _allItems = new List<MenuItem>(items);
    }

    private void ApplySearch()
    {
        _currentItems = _searchQuery.Length == 0
            ? new List<MenuItem>(_allItems)
            : _allItems
                .Where(i => i.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        _offset = 0;
    }

    /// <summary>
    /// Handles a key while the search is open. Every printable character, letters of the
    /// commands included, goes to the query; Esc closes the search, and so does Backspace
    /// on an empty query. Returns false for keys the search leaves to the menu (arrows, Enter).
    /// </summary>
    private bool HandleSearchKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            _searching = false;
            _searchQuery = "";
        }
        else if (key.Key == ConsoleKey.Backspace)
        {
            if (_searchQuery.Length > 0)
                _searchQuery = _searchQuery[..^1];
            else
                _searching = false;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            _searchQuery += key.KeyChar;
        }
        else
        {
            return false;
        }

        ApplySearch();
        return true;
    }

    /// <summary>Columns a line may use. The last column stays free: writing into it makes some terminals wrap.</summary>
    private int LineWidth => _width > 1 ? _width - 1 : int.MaxValue;

    private bool Scrolled => _currentItems.Count > _itemRows;

    public void WriteMenu(int selectedItemIndex, string? pushTextToUser = null)
    {
        _message = pushTextToUser;
        Render(selectedItemIndex);
    }

    private void Render(int selectedIndex)
    {
        _width = SafeWindowWidth();
        _height = SafeWindowHeight();

        var header = BuildHeader();
        var footer = BuildFooter(withPaging: false);
        var fit = MenuLayout.Compute(_height, header.Count, footer.Count, _currentItems.Count);
        if (fit.ItemRows < _currentItems.Count)
        {
            footer = BuildFooter(withPaging: true);
            fit = MenuLayout.Compute(_height, header.Count, footer.Count, _currentItems.Count);
        }

        // Lines that did not fit go from the top of the header and the end of the footer.
        header = header.Skip(header.Count - fit.HeaderLines).ToList();
        if (fit.FooterLines < footer.Count && fit.FooterLines > 0)
            footer.RemoveAt(0); // the blank spacer goes before any hint
        footer = footer.Take(fit.FooterLines).ToList();
        _itemRows = fit.ItemRows;
        _titleRow = fit.HeaderLines > 0 ? fit.HeaderLines - 1 : -1;
        _itemsTop = fit.HeaderLines;
        if (_currentItems.Count > 0)
            EnsureVisible(Math.Clamp(selectedIndex, 0, _currentItems.Count - 1));

        _theme.UseText();
        Console.Clear();
        // With the marker on, the cursor is hidden so ▶ is the only cue; with it off, the
        // cursor stays visible and marks the selected row itself.
        SetCursorVisible(!AppSettings.ShowSelectionMarker);

        // Lines are separated, not terminated, by newlines: a newline after the bottom row
        // would scroll the whole frame up by one.
        var first = true;
        void NewLine()
        {
            if (!first) Console.WriteLine();
            first = false;
        }

        foreach (var line in header)
        {
            NewLine();
            WriteLine(line);
        }

        if (_currentItems.Count == 0)
        {
            NewLine();
            WriteLine(new Line((_theme.UseMessage, $"{Prefix}No matches")));
        }
        else
        {
            for (var row = 0; row < _itemRows; row++)
            {
                NewLine();
                var idx = _offset + row;
                WriteItem(idx, idx == selectedIndex);
            }
        }

        foreach (var line in footer)
        {
            NewLine();
            WriteLine(line);
        }

        _theme.UseText();
    }

    private List<Line> BuildHeader()
    {
        var lines = new List<Line>();

        if (_addInfo != null && _addInfo.Count > 0)
        {
            lines.Add(new Line((_theme.UseInfoBorder, "-------- Info --------")));

            var maxLength = _addInfo.Select(x => x.Key).Max(x => x.Length);
            foreach (var data in _addInfo)
            {
                var label = Prefix + data.Key + new string(' ', maxLength - data.Key.Length) + " : ";
                lines.Add(new Line((_theme.UseInfoLabel, label), (_theme.UseInfoValue, data.Value)));
            }

            lines.Add(new Line((_theme.UseInfoBorder, "----------------------")));
        }

        var message = _searching ? $"Search: {_searchQuery}_" : _message; // "_" marks where typing goes
        if (!string.IsNullOrWhiteSpace(message))
        {
            // A long description wraps onto several lines, each of them a single screen row.
            foreach (var part in message.Replace("\r", "").Split('\n'))
                foreach (var wrapped in TerminalText.Wrap(Prefix + part, LineWidth, Prefix))
                    lines.Add(new Line((_theme.UseMessage, wrapped)));
        }
        else
        {
            lines.Add(new Line((_theme.UseText, "")));
        }

        lines.Add(TitleLine);
        return lines;
    }

    private List<Line> BuildFooter(bool withPaging)
    {
        var hints = new List<string> { "Up/Down: Navigate", "Enter: Select" };

        if (_editable)
        {
            hints.Add("E: Edit JSON");
            hints.Add("Q: Quick Edit");
            hints.Add("V: Validate");
            hints.Add("R: Reload");
        }

        hints.Add("/: Search");
        hints.Add("?: Info");
        hints.Add("T: Theme");
        hints.Add(_searching ? "Esc: Close search" : _isRoot ? "Esc: Exit" : "Esc: Back");

        if (withPaging)
            hints.Add("Left/Right: Page");

        var lines = new List<Line> { new((_theme.UseText, "")) };
        foreach (var hintLine in TerminalText.JoinWrapped(hints, "  |  ", Prefix, LineWidth))
            lines.Add(new Line((_theme.UseInfoBorder, hintLine)));
        return lines;
    }

    /// <summary>The title cut to the line width; while scrolling the N/M counter stays and the name gives way.</summary>
    private string TitleText()
    {
        var counter = Scrolled && _currentItems.Count > 0 ? $"  {_selected + 1}/{_currentItems.Count}" : "";
        var room = LineWidth == int.MaxValue ? int.MaxValue : LineWidth - TerminalText.Width(counter);
        if (room < 8) // too narrow for both: the counter alone
            return TerminalText.Truncate(counter.TrimStart(), LineWidth);
        return TerminalText.Truncate($"-------- {_menuTitle} --------", room) + counter;
    }

    /// <summary>Writes a line's segments in their colours, cut to the line width.</summary>
    private void WriteLine(Line line)
    {
        if (ReferenceEquals(line, TitleLine))
        {
            WriteTitle();
            return;
        }

        var budget = LineWidth;
        foreach (var (style, text) in line.Segments)
        {
            if (budget <= 0) break;
            var fitted = TerminalText.Truncate(text, budget);
            style();
            Console.Write(fitted);
            budget -= TerminalText.Width(fitted);
        }
        _theme.UseText();
    }

    private void WriteTitle()
    {
        _theme.UseTitle();
        Console.Write(TitleText());
        _theme.UseText();
    }

    /// <summary>Writes the row for item <paramref name="index"/> at the cursor.</summary>
    private void WriteItem(int index, bool selected)
    {
        var budget = LineWidth;
        var name = TerminalText.Truncate(_currentItems[index].Name, budget == int.MaxValue ? budget : budget - Prefix.Length);
        if (selected)
        {
            _theme.UseSelected();
            Console.Write($"{SelectedPrefix}{name}");
        }
        else
        {
            _theme.UseText();
            Console.Write($"{Prefix}{name}");
        }
        _theme.UseText();
    }

    /// <summary>Repaints one item row in place, clearing what an earlier, longer text left there.</summary>
    private void RepaintItem(int index, bool selected)
    {
        var row = index - _offset;
        if (index < 0 || index >= _currentItems.Count || row < 0 || row >= _itemRows) return;

        Console.SetCursorPosition(0, _itemsTop + row);
        WriteItem(index, selected);
        if (_width > 1)
        {
            var used = TerminalText.Width(Prefix) + TerminalText.Width(
                TerminalText.Truncate(_currentItems[index].Name, LineWidth - Prefix.Length));
            if (used < LineWidth)
                Console.Write(new string(' ', LineWidth - used));
        }
    }

    private void RepaintTitle()
    {
        if (_titleRow < 0) return;
        Console.SetCursorPosition(0, _titleRow);
        WriteTitle();
        if (_width > 1)
        {
            var used = TerminalText.Width(TitleText());
            if (used < LineWidth)
                Console.Write(new string(' ', LineWidth - used));
        }
    }

    /// <summary>Scrolls so <paramref name="index"/> is on screen; returns whether the view moved.</summary>
    private bool EnsureVisible(int index)
    {
        var before = _offset;
        if (index < _offset)
            _offset = index;
        else if (index >= _offset + _itemRows)
            _offset = index - _itemRows + 1;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _currentItems.Count - _itemRows));
        _selected = index;
        return _offset != before;
    }

    /// <summary>Moves the selection, repainting only the rows that changed unless the view scrolled.</summary>
    private void Select(int from, int to)
    {
        if (EnsureVisible(to))
        {
            for (var i = _offset; i < _offset + _itemRows && i < _currentItems.Count; i++)
                RepaintItem(i, i == to);
        }
        else
        {
            RepaintItem(from, false);
            RepaintItem(to, true);
        }

        if (Scrolled)
            RepaintTitle();
        ParkCursor(to);
    }

    /// <summary>Leaves the cursor at the start of the selected row (it is the cue when the marker is off).</summary>
    private void ParkCursor(int index)
    {
        var row = index - _offset;
        if (row >= 0 && row < _itemRows)
            Console.SetCursorPosition(0, _itemsTop + row);
    }

    /// <summary>
    /// Waits for a key, or returns null when the window is resized so the caller can redraw
    /// the menu for the new size (a pane split or a font change otherwise leaves it garbled).
    /// </summary>
    private ConsoleKeyInfo? ReadKeyOrResize()
    {
        while (true)
        {
            bool available;
            try { available = Console.KeyAvailable; }
            catch (InvalidOperationException) { return Console.ReadKey(intercept: true); }

            if (available)
                return Console.ReadKey(intercept: true);
            if (SafeWindowWidth() != _width || SafeWindowHeight() != _height)
                return null;
            Thread.Sleep(50);
        }
    }

    /// <summary>The index of <paramref name="item"/> in the unfiltered list, which callers use to restore selection.</summary>
    private int FullIndex(int index)
    {
        if (index < 0 || index >= _currentItems.Count) return 0;
        var full = _allItems.IndexOf(_currentItems[index]);
        return full >= 0 ? full : index;
    }

    /// <summary>
    /// Runs the interactive menu loop.
    /// Returns a menu-id string to navigate to, or null to go back.
    /// </summary>
    public string? RunMenu(int startIndex = 0)
    {
        if (_currentItems.Count == 0)
        {
            do
            {
                if (ReadKeyOrResize() is not { } ki)
                {
                    Render(0);
                    continue;
                }
                if (_searching ? HandleSearchKey(ki) : OpenSearch(ki))
                {
                    WriteMenu(selectedItemIndex: 0);
                    return RunMenu();
                }
                if (_editable && EditCommand(ki.Key) is { } editCmd)
                {
                    SelectedIndex = 0;
                    SetCursorVisible(true);
                    return editCmd;
                }
                if (ki.Key == ConsoleKey.T)
                {
                    _theme.Toggle();
                    WriteMenu(selectedItemIndex: 0);
                    return RunMenu();
                }
                if (ki.Key == ConsoleKey.Escape)
                    return null;
            }
            while (true);
        }

        var index = Math.Clamp(startIndex, 0, _currentItems.Count - 1);
        if (index != _selected)
            Render(index); // the caller rendered another selection
        SelectedIndex = FullIndex(index);
        ParkCursor(index);

        do
        {
            if (ReadKeyOrResize() is not { } keyinfo)
            {
                Render(index);
                ParkCursor(index);
                continue;
            }

            var last = _currentItems.Count - 1;
            int? target = keyinfo.Key switch
            {
                ConsoleKey.DownArrow => index < last ? index + 1 : 0,
                ConsoleKey.UpArrow => index > 0 ? index - 1 : last,
                ConsoleKey.PageDown => Math.Min(last, index + _itemRows),
                ConsoleKey.PageUp => Math.Max(0, index - _itemRows),
                ConsoleKey.RightArrow when Scrolled => Math.Min(last, index + _itemRows),
                ConsoleKey.LeftArrow when Scrolled => Math.Max(0, index - _itemRows),
                ConsoleKey.Home => 0,
                ConsoleKey.End => last,
                _ => null
            };

            if (target is { } to)
            {
                if (to != index)
                    Select(index, to);
                index = to;
                continue;
            }

            if (keyinfo.Key == ConsoleKey.Enter)
            {
                try
                {
                    SetCursorVisible(true); // the action may prompt for input
                    var result = _currentItems[index].Action.Execute();
                    if (result != null)
                    {
                        SelectedIndex = FullIndex(index);
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

            if (_searching)
            {
                var current = _currentItems[index];
                if (HandleSearchKey(keyinfo))
                {
                    // While typing the first match is selected; closing the search keeps the item the cursor was on.
                    var keep = _searching ? 0 : Math.Max(0, _currentItems.IndexOf(current));
                    WriteMenu(selectedItemIndex: keep);
                    return RunMenu(keep);
                }
            }

            if (OpenSearch(keyinfo))
            {
                WriteMenu(selectedItemIndex: index);
                return RunMenu(index);
            }

            if (keyinfo.Key == ConsoleKey.T)
            {
                _theme.Toggle();
                WriteMenu(selectedItemIndex: index);
                return RunMenu(index);
            }

            if (_editable && EditCommand(keyinfo.Key) is { } editCmd)
            {
                SelectedIndex = FullIndex(index);
                SetCursorVisible(true); // handing off to the editor, which reads input
                return editCmd;
            }

            if (keyinfo.Key == ConsoleKey.Escape)
                break;

            if (keyinfo.KeyChar == '?')
            {
                WriteMenu(selectedItemIndex: index, pushTextToUser: Describe(_currentItems[index]));
                ParkCursor(index);
                continue;
            }

        }
        while (true);

        SelectedIndex = FullIndex(index);
        return null;
    }

    /// <summary>'/' opens the search (outside of it); returns whether it did.</summary>
    private bool OpenSearch(ConsoleKeyInfo key)
    {
        if (_searching || key.KeyChar != '/')
            return false;
        _searching = true;
        return true;
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
