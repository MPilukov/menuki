using System.Reflection;
using System.Text.Json;

namespace Menuki.Examples;

/// <summary>One embedded example pack: its short name and human-readable title.</summary>
public record ExampleInfo(string Name, string Title);

/// <summary>
/// The curated example packs, embedded into the binary at build time (see the
/// EmbeddedResource entry in Menuki.csproj). This lets <c>menuki examples</c> list, run,
/// print and save them offline - no repo clone, works the same for every install method.
/// </summary>
public static class ExampleCatalog
{
    private const string Prefix = "examples/";
    private const string Suffix = ".json";

    // plugin-demo depends on external plugin DLLs and repo-relative script paths, so it
    // cannot run standalone - keep it out of the offline catalog.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "plugin-demo" };

    private static Assembly Asm => typeof(ExampleCatalog).Assembly;

    private static IEnumerable<string> ResourceNames() =>
        Asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) &&
                        n.EndsWith(Suffix, StringComparison.Ordinal));

    private static string NameOf(string resource) =>
        resource.Substring(Prefix.Length, resource.Length - Prefix.Length - Suffix.Length);

    /// <summary>All runnable example packs, sorted by name.</summary>
    public static IReadOnlyList<ExampleInfo> List()
    {
        var result = new List<ExampleInfo>();
        foreach (var res in ResourceNames())
        {
            var name = NameOf(res);
            if (Excluded.Contains(name))
                continue;
            result.Add(new ExampleInfo(name, TitleOf(res) ?? name));
        }
        return result.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    public static bool Exists(string name) =>
        !Excluded.Contains(name) && ResourceNames().Any(r => NameOf(r).Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The raw JSON of an example, or null if there is no such pack.</summary>
    public static string? ReadJson(string name)
    {
        if (Excluded.Contains(name))
            return null;
        var res = ResourceNames().FirstOrDefault(r => NameOf(r).Equals(name, StringComparison.OrdinalIgnoreCase));
        if (res == null)
            return null;
        using var stream = Asm.GetManifestResourceStream(res);
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? TitleOf(string resource)
    {
        try
        {
            using var stream = Asm.GetManifestResourceStream(resource);
            if (stream == null) return null;
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
