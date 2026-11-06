using System.Globalization;
using System.Text;

namespace Menuki.Engine;

/// <summary>
/// Measures and fits text to terminal columns. The menu keeps every line within one screen row,
/// so it can address rows by number: a line that wrapped would push everything below it down
/// and the marker would be painted over the wrong item.
/// </summary>
public static class TerminalText
{
    private const string Ellipsis = "…";

    /// <summary>Columns a code point takes: 0 for combining marks, 2 for wide (CJK, emoji), else 1.</summary>
    public static int RuneWidth(Rune rune)
    {
        var cp = rune.Value;
        if (cp == 0 || cp == 0x200D || (cp >= 0xFE00 && cp <= 0xFE0F)) return 0; // ZWJ, variation selectors
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            or UnicodeCategory.Control)
            return 0;
        return IsWide(cp) ? 2 : 1;
    }

    private static bool IsWide(int cp) =>
        (cp >= 0x1100 && cp <= 0x115F) ||
        cp is 0x231A or 0x231B or 0x23F0 or 0x23F3 or 0x25FD or 0x25FE or 0x2614 or 0x2615 or 0x267F or 0x2693
            or 0x26A1 or 0x26AA or 0x26AB or 0x26BD or 0x26BE or 0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA
            or 0x26F2 or 0x26F3 or 0x26F5 or 0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 or 0x274C
            or 0x274E or 0x2757 or 0x27B0 or 0x27BF or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 ||
        (cp >= 0x23E9 && cp <= 0x23EC) ||
        (cp >= 0x2648 && cp <= 0x2653) ||
        (cp >= 0x2753 && cp <= 0x2755) ||
        (cp >= 0x2795 && cp <= 0x2797) ||
        (cp >= 0x2E80 && cp <= 0x303E) ||
        (cp >= 0x3041 && cp <= 0xA4CF) ||
        (cp >= 0xAC00 && cp <= 0xD7A3) ||
        (cp >= 0xF900 && cp <= 0xFAFF) ||
        (cp >= 0xFE30 && cp <= 0xFE4F) ||
        (cp >= 0xFF00 && cp <= 0xFF60) ||
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||
        (cp >= 0x1F000 && cp <= 0x1FAFF) ||
        (cp >= 0x20000 && cp <= 0x3FFFD);

    /// <summary>
    /// Columns a grapheme cluster (what the user sees as one character) takes. An emoji
    /// presentation selector (VS16, as in "❤️") makes it wide; joined sequences ("👩‍💻") and
    /// flags take the width of their widest part, not the sum.
    /// </summary>
    public static int ClusterWidth(string cluster)
    {
        var max = 0;
        foreach (var rune in cluster.EnumerateRunes())
        {
            if (rune.Value == 0xFE0F) return 2;
            max = Math.Max(max, RuneWidth(rune));
        }
        return max;
    }

    /// <summary>The grapheme clusters of <paramref name="text"/>, in order.</summary>
    private static IEnumerable<string> Clusters(string text)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
            yield return e.GetTextElement();
    }

    /// <summary>
    /// Replaces control characters (newlines, tabs, escapes) with spaces: one of them in a line
    /// would move the cursor and break the row count. Shell output in the info panel has them.
    /// </summary>
    public static string Sanitize(string text)
    {
        if (!text.Any(char.IsControl)) return text;
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
            result.Append(char.IsControl(c) ? ' ' : c);
        return result.ToString();
    }

    public static int Width(string text)
    {
        var width = 0;
        foreach (var cluster in Clusters(text))
            width += ClusterWidth(cluster);
        return width;
    }

    /// <summary>Cuts <paramref name="text"/> to at most <paramref name="maxWidth"/> columns, ending in "…" when cut.</summary>
    public static string Truncate(string text, int maxWidth)
    {
        if (maxWidth <= 0) return "";
        text = Sanitize(text);
        if (Width(text) <= maxWidth) return text;

        var budget = maxWidth - 1; // room for the ellipsis
        var result = new StringBuilder();
        var used = 0;
        foreach (var cluster in Clusters(text))
        {
            var w = ClusterWidth(cluster);
            if (used + w > budget) break;
            result.Append(cluster);
            used += w;
        }
        return result.Append(Ellipsis).ToString();
    }

    /// <summary>
    /// Splits <paramref name="text"/> into lines of at most <paramref name="maxWidth"/> columns,
    /// breaking at spaces where possible. Continuation lines start with <paramref name="indent"/>.
    /// </summary>
    public static List<string> Wrap(string text, int maxWidth, string indent = "")
    {
        text = Sanitize(text);
        var lines = new List<string>();
        if (maxWidth <= Width(indent) + 1)
        {
            lines.Add(Truncate(text, maxWidth));
            return lines;
        }

        var current = new StringBuilder();
        var currentWidth = 0;
        var lastSpace = -1; // index in `current` of the last space, to break there
        foreach (var cluster in Clusters(text))
        {
            var w = ClusterWidth(cluster);
            if (currentWidth + w > maxWidth)
            {
                string carry;
                if (lastSpace > Width(indent))
                {
                    carry = current.ToString(lastSpace + 1, current.Length - lastSpace - 1);
                    current.Length = lastSpace;
                }
                else
                {
                    carry = "";
                }
                lines.Add(current.ToString().TrimEnd());
                current.Clear().Append(indent).Append(carry);
                currentWidth = Width(current.ToString());
                lastSpace = -1;
                if (cluster == " ") continue;
            }
            if (cluster == " ") lastSpace = current.Length;
            current.Append(cluster);
            currentWidth += w;
        }
        if (current.ToString().Trim().Length > 0 || lines.Count == 0)
            lines.Add(current.ToString().TrimEnd());
        return lines;
    }

    /// <summary>
    /// Joins <paramref name="parts"/> with <paramref name="separator"/> into as few lines of at most
    /// <paramref name="maxWidth"/> columns as possible, never splitting a part across lines.
    /// Every line starts with <paramref name="indent"/>.
    /// </summary>
    public static List<string> JoinWrapped(IEnumerable<string> parts, string separator, string indent, int maxWidth)
    {
        var lines = new List<string>();
        var current = new StringBuilder(indent);
        var empty = true;
        foreach (var part in parts)
        {
            var candidate = empty ? current + part : current + separator + part;
            if (!empty && Width(candidate) > maxWidth)
            {
                lines.Add(current.ToString());
                current.Clear().Append(indent).Append(part);
            }
            else
            {
                current.Clear().Append(candidate);
            }
            empty = false;
        }
        if (!empty) lines.Add(current.ToString());
        return lines.Select(l => Truncate(l, maxWidth)).ToList();
    }
}
