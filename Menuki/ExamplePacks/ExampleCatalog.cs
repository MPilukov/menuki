using System.Reflection;
using System.Text.Json;

namespace Menuki.Examples;

/// <summary>One embedded example pack: its short name, human-readable title, and category.</summary>
/// <remarks>
/// <paramref name="Category"/> is the folder the pack lives in under <c>examples/</c>
/// (e.g. "dev", "devops", "agents"); empty for packs that sit at the root.
/// </remarks>
public record ExampleInfo(string Name, string Title, string Category);

/// <summary>
/// The curated example packs, embedded into the binary at build time (see the
/// EmbeddedResource entry in Menuki.csproj). This lets <c>menuki examples</c> list, run,
/// print and save them offline - no repo clone, works the same for every install method.
///
/// Packs are organised into category folders under <c>examples/</c>; the folder name becomes
/// the pack's <see cref="ExampleInfo.Category"/>. Pack names (the file leaf) stay unique
/// across all categories, so <c>menuki examples &lt;name&gt;</c> works without a folder prefix.
/// </summary>
public static class ExampleCatalog
{
    private const string Prefix = "examples/";
    private const string Suffix = ".json";

    /// <summary>Display order for category groupings; anything else sorts after, alphabetically.</summary>
    public static readonly IReadOnlyList<string> CategoryOrder =
        new[] { "dev", "devops", "agents", "appearance", "demos" };

    // plugin-demo depends on external plugin DLLs and repo-relative script paths, so it
    // cannot run standalone - keep it out of the offline catalog.
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "plugin-demo" };

    private static Assembly Asm => typeof(ExampleCatalog).Assembly;

    // Resource names are the LogicalName set in the csproj, e.g. "examples/dev/git.json".
    // Normalize any backslash a Windows build may introduce so parsing is separator-agnostic.
    private static IEnumerable<string> ResourceNames() =>
        Asm.GetManifestResourceNames()
            .Select(n => n.Replace('\\', '/'))
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) &&
                        n.EndsWith(Suffix, StringComparison.Ordinal));

    // The path between the prefix and suffix, e.g. "dev/git" for "examples/dev/git.json".
    private static string RelPath(string resource) =>
        resource.Substring(Prefix.Length, resource.Length - Prefix.Length - Suffix.Length);

    // The pack name (file leaf), e.g. "git" for "examples/dev/git.json".
    private static string NameOf(string resource)
    {
        var rel = RelPath(resource);
        var slash = rel.LastIndexOf('/');
        return slash < 0 ? rel : rel[(slash + 1)..];
    }

    // The category (folder), e.g. "dev"; empty for packs at the examples root.
    private static string CategoryOf(string resource)
    {
        var rel = RelPath(resource);
        var slash = rel.LastIndexOf('/');
        return slash < 0 ? "" : rel[..slash];
    }

    /// <summary>All runnable example packs, sorted by name.</summary>
    public static IReadOnlyList<ExampleInfo> List()
    {
        var result = new List<ExampleInfo>();
        foreach (var res in ResourceNames())
        {
            var name = NameOf(res);
            if (Excluded.Contains(name))
                continue;
            result.Add(new ExampleInfo(name, TitleOf(res) ?? name, CategoryOf(res)));
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
        var res = ResourceRawFor(name);
        if (res == null)
            return null;
        using var stream = Asm.GetManifestResourceStream(res);
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Map a pack name back to its real (non-normalized) manifest resource name, which is what
    // GetManifestResourceStream expects.
    private static string? ResourceRawFor(string name) =>
        Asm.GetManifestResourceNames()
            .FirstOrDefault(raw => NameOf(raw.Replace('\\', '/')).Equals(name, StringComparison.OrdinalIgnoreCase)
                                   && raw.Replace('\\', '/').StartsWith(Prefix, StringComparison.Ordinal)
                                   && raw.EndsWith(Suffix, StringComparison.Ordinal));

    private static string? TitleOf(string resource)
    {
        try
        {
            var raw = ResourceRawFor(NameOf(resource));
            if (raw == null) return null;
            using var stream = Asm.GetManifestResourceStream(raw);
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
