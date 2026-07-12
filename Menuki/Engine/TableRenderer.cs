using System.Text;
using System.Text.Json.Nodes;

namespace Menuki.Engine;

/// <summary>
/// Renders JSON as an ASCII table. The sweet spot is an array of flat objects
/// (columns = the union of keys). Falls back with a reason for shapes that don't
/// map cleanly to a table.
/// </summary>
public static class TableRenderer
{
    private const int MaxCellWidth = 40;

    public static string? Render(JsonNode? node, out string? warning)
    {
        warning = null;

        if (node is not JsonArray array)
        {
            warning = "not a JSON array - showing pretty JSON instead.";
            return null;
        }

        if (array.Count == 0)
            return "(empty array)";

        // Array of scalars → single column.
        if (array.All(e => e is JsonValue))
        {
            var sb0 = new StringBuilder();
            foreach (var e in array)
                sb0.AppendLine(Cell(e));
            return sb0.ToString().TrimEnd();
        }

        if (array.Any(e => e is not JsonObject))
        {
            warning = "array mixes objects and non-objects - showing pretty JSON instead.";
            return null;
        }

        // Union of keys, preserving first-seen order.
        var columns = new List<string>();
        foreach (var e in array)
            foreach (var kv in (JsonObject)e!)
                if (!columns.Contains(kv.Key))
                    columns.Add(kv.Key);

        var widths = columns.ToDictionary(c => c, c => c.Length);
        var rows = new List<Dictionary<string, string>>();
        foreach (var e in array)
        {
            var obj = (JsonObject)e!;
            var row = new Dictionary<string, string>();
            foreach (var c in columns)
            {
                var value = obj.TryGetPropertyValue(c, out var v) ? Cell(v) : "";
                row[c] = value;
                widths[c] = Math.Max(widths[c], value.Length);
            }
            rows.Add(row);
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join("  ", columns.Select(c => c.PadRight(widths[c]))));
        sb.AppendLine(string.Join("  ", columns.Select(c => new string('-', widths[c]))));
        foreach (var row in rows)
            sb.AppendLine(string.Join("  ", columns.Select(c => row[c].PadRight(widths[c]))));

        return sb.ToString().TrimEnd();
    }

    private static string Cell(JsonNode? node)
    {
        var text = node switch
        {
            null => "",
            JsonValue => node.ToString(),          // scalar without quotes
            _ => node.ToJsonString()                // nested object/array → compact JSON
        };

        text = text.Replace("\n", " ").Replace("\r", "");
        return text.Length > MaxCellWidth ? text[..(MaxCellWidth - 1)] + "…" : text;
    }
}
