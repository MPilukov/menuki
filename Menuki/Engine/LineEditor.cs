using System.Text;

namespace Menuki.Engine;

/// <summary>
/// A minimal single-line editor to replace <see cref="Console.ReadLine"/> where
/// history recall is wanted. Supports typing, Backspace/Delete, Left/Right,
/// Home/End and Up/Down to walk a history list (most-recent-last). The prompt is
/// expected to be already written at the current cursor position.
/// </summary>
public static class LineEditor
{
    public static string ReadLine(IReadOnlyList<string>? history = null)
    {
        history ??= Array.Empty<string>();

        var buffer = new StringBuilder();
        var pos = 0;
        var histIndex = history.Count;   // == Count means "the new line being typed"
        var stash = "";                  // the in-progress line, saved when walking up
        var prevLen = 0;

        int startLeft = Console.CursorLeft;
        int startTop = Console.CursorTop;

        void Redraw()
        {
            var text = buffer.ToString();
            SafeSetCursor(startLeft, startTop);
            Console.Write(text);
            if (prevLen > text.Length)
                Console.Write(new string(' ', prevLen - text.Length));
            prevLen = text.Length;
            SafeSetCursor(startLeft + pos, startTop);
        }

        void Load(string value)
        {
            buffer.Clear();
            buffer.Append(value);
            pos = buffer.Length;
            Redraw();
        }

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return buffer.ToString();

                case ConsoleKey.Backspace:
                    if (pos > 0) { buffer.Remove(pos - 1, 1); pos--; Redraw(); }
                    break;

                case ConsoleKey.Delete:
                    if (pos < buffer.Length) { buffer.Remove(pos, 1); Redraw(); }
                    break;

                case ConsoleKey.LeftArrow:
                    if (pos > 0) { pos--; Redraw(); }
                    break;

                case ConsoleKey.RightArrow:
                    if (pos < buffer.Length) { pos++; Redraw(); }
                    break;

                case ConsoleKey.Home:
                    pos = 0; Redraw();
                    break;

                case ConsoleKey.End:
                    pos = buffer.Length; Redraw();
                    break;

                case ConsoleKey.UpArrow:
                    if (histIndex > 0)
                    {
                        if (histIndex == history.Count) stash = buffer.ToString();
                        histIndex--;
                        Load(history[histIndex]);
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (histIndex < history.Count)
                    {
                        histIndex++;
                        Load(histIndex == history.Count ? stash : history[histIndex]);
                    }
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(pos, key.KeyChar);
                        pos++;
                        Redraw();
                    }
                    break;
            }
        }
    }

    private static void SafeSetCursor(int left, int top)
    {
        try
        {
            var maxLeft = Math.Max(0, Console.BufferWidth - 1);
            Console.SetCursorPosition(Math.Min(left, maxLeft), top);
        }
        catch
        {
            // out-of-bounds (very long line / tiny window) - leave the cursor be.
        }
    }
}
