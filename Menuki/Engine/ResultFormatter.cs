using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Menuki.Config;

namespace Menuki.Engine;

/// <summary>Formatted command output plus an optional non-fatal warning.</summary>
public record FormattedResult(string Text, string? Warning = null);

/// <summary>
/// Post-processes captured command output: an optional JSONPath query extracts a
/// value, then a format ("json" pretty-print / "table" / "raw") renders it.
/// Errors degrade gracefully - the raw text is returned with a warning.
/// </summary>
public static class ResultFormatter
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool NeedsFormatting(string? format, string? query) =>
        !string.IsNullOrEmpty(query) ||
        (!string.IsNullOrEmpty(format) && format != ActionFormats.Raw);

    public static FormattedResult Format(string raw, string? format, string? query)
    {
        JsonNode? node = null;

        // Query first (needs valid JSON input).
        if (!string.IsNullOrEmpty(query))
        {
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(raw);
            }
            catch (JsonException ex)
            {
                return new FormattedResult(raw, $"query skipped - input is not JSON: {ex.Message}");
            }

            try
            {
                node = JsonQuery.Apply(parsed, query);
            }
            catch (Exception ex)
            {
                return new FormattedResult(raw, $"query failed: {ex.Message}");
            }

            if (node == null)
                return new FormattedResult("(no match for query)");
        }

        return format switch
        {
            ActionFormats.Json => AsPrettyJson(node, raw),
            ActionFormats.Table => AsTable(node, raw),
            _ => new FormattedResult(node?.ToJsonString(Pretty) ?? raw) // raw, or the queried value
        };
    }

    private static FormattedResult AsPrettyJson(JsonNode? node, string raw)
    {
        try
        {
            node ??= JsonNode.Parse(raw);
            return new FormattedResult(node?.ToJsonString(Pretty) ?? raw);
        }
        catch (JsonException ex)
        {
            return new FormattedResult(raw, $"not valid JSON: {ex.Message}");
        }
    }

    private static FormattedResult AsTable(JsonNode? node, string raw)
    {
        try
        {
            node ??= JsonNode.Parse(raw);
        }
        catch (JsonException ex)
        {
            return new FormattedResult(raw, $"not valid JSON: {ex.Message}");
        }

        var table = TableRenderer.Render(node, out var warning);
        if (table != null)
            return new FormattedResult(table);

        // Not table-shaped - fall back to pretty JSON, keeping the reason.
        return new FormattedResult(node?.ToJsonString(Pretty) ?? raw, warning);
    }
}
