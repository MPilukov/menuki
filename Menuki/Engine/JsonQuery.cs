using System.Text.Json.Nodes;

namespace Menuki.Engine;

/// <summary>
/// A deliberately small, zero-dependency JSONPath subset - enough for the common
/// "pull one field out of a tool's JSON" case, without a JMESPath library.
///
/// Supported: root <c>$</c> (optional), dot access <c>.foo.bar</c>, array index
/// <c>[0]</c>, wildcard <c>[*]</c> (all array elements or all object values), and
/// bracket keys <c>['some key']</c>. A wildcard anywhere yields an array result.
/// </summary>
public static class JsonQuery
{
    private enum Kind { Property, Index, Wildcard }

    private readonly record struct Segment(Kind Kind, string Name, int Index);

    public static JsonNode? Apply(JsonNode? root, string path)
    {
        var segments = Parse(path);
        var current = new List<JsonNode?> { root };
        var sawWildcard = false;

        foreach (var seg in segments)
        {
            var next = new List<JsonNode?>();
            foreach (var node in current)
            {
                switch (seg.Kind)
                {
                    case Kind.Property when node is JsonObject o && o.TryGetPropertyValue(seg.Name, out var v):
                        next.Add(v);
                        break;

                    case Kind.Index when node is JsonArray a && seg.Index >= 0 && seg.Index < a.Count:
                        next.Add(a[seg.Index]);
                        break;

                    case Kind.Wildcard when node is JsonArray arr:
                        sawWildcard = true;
                        foreach (var e in arr) next.Add(e);
                        break;

                    case Kind.Wildcard when node is JsonObject obj:
                        sawWildcard = true;
                        foreach (var kv in obj) next.Add(kv.Value);
                        break;
                }
            }
            current = next;
        }

        // A wildcard (or multiple matches) yields an array; a single path yields the node.
        if (!sawWildcard && current.Count == 1)
            return current[0]?.DeepClone();

        var result = new JsonArray();
        foreach (var n in current)
            result.Add(n?.DeepClone());
        return result;
    }

    private static List<Segment> Parse(string path)
    {
        path = path.Trim();
        if (path.StartsWith('$'))
            path = path[1..];

        var segments = new List<Segment>();
        var i = 0;
        while (i < path.Length)
        {
            var c = path[i];
            if (c == '.')
            {
                i++;
                continue;
            }

            if (c == '[')
            {
                var end = path.IndexOf(']', i);
                if (end < 0)
                    throw new FormatException("unclosed '[' in query");

                var inner = path[(i + 1)..end].Trim();
                if (inner == "*")
                    segments.Add(new Segment(Kind.Wildcard, "", 0));
                else if (int.TryParse(inner, out var idx))
                    segments.Add(new Segment(Kind.Index, "", idx));
                else
                    segments.Add(new Segment(Kind.Property, inner.Trim('\'', '"'), 0));

                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[')
                    i++;
                var name = path[start..i];
                if (name == "*")
                    segments.Add(new Segment(Kind.Wildcard, "", 0));
                else if (name.Length > 0)
                    segments.Add(new Segment(Kind.Property, name, 0));
            }
        }

        return segments;
    }
}
