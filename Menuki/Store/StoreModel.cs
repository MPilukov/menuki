using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Menuki.Store;

/// <summary>A git repository of menu configs the user added with <c>menuki store add</c>.</summary>
public class StoreEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>What the user typed: <c>owner/repo</c>, a git URL or a local path.</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "";

    /// <summary>The URL that was cloned (one of the candidates of <see cref="StoreSource"/>).</summary>
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    /// <summary>Folder inside the repo that holds the configs; empty for the repo root.</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

/// <summary>Where an installed config came from and what it looked like when installed.</summary>
public class InstalledEntry
{
    [JsonPropertyName("store")] public string Store { get; set; } = "";
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
}

public class StoreState
{
    [JsonPropertyName("stores")] public List<StoreEntry> Stores { get; set; } = new();
    [JsonPropertyName("installed")] public Dictionary<string, InstalledEntry> Installed { get; set; } = new();
}

/// <summary>One config found in a store clone.</summary>
public record StoreMenu(StoreEntry Store, string Name, string Title, string File, string Hash, IReadOnlyList<string> Opens);

public enum MenuStatus
{
    NotInstalled,
    UpToDate,
    UpdateAvailable,
    ChangedLocally,
    LocalDiffers,
}

/// <summary>
/// The pure parts of the store: parsing a source, reading a clone, comparing it with the
/// configs directory, finding secrets. No git and no console here, so all of it is testable.
/// </summary>
public static class StoreLogic
{
    /// <summary>
    /// Clone candidates and the folder inside the repo for what the user typed.
    /// <c>owner/repo</c> and <c>owner/repo/sub/dir</c> mean GitHub (HTTPS first, then SSH);
    /// a URL or an existing local path is cloned as is.
    /// </summary>
    public static (List<string> Urls, string Path, string DefaultName) ParseSource(string source, string? pathOverride = null)
    {
        var s = source.Trim();
        string sub = "";
        List<string> urls;
        string repoName;

        var looksLocal = s.StartsWith('/') || s.StartsWith("./") || s.StartsWith("../") || s.StartsWith('~') || Directory.Exists(s);
        if (looksLocal)
        {
            var full = System.IO.Path.GetFullPath(ExpandHome(s));
            urls = new List<string> { full };
            repoName = System.IO.Path.GetFileName(full.TrimEnd('/', '\\'));
        }
        else if (s.Contains("://") || s.StartsWith("git@"))
        {
            urls = new List<string> { s };
            repoName = s.TrimEnd('/');
            repoName = repoName[(repoName.LastIndexOfAny(new[] { '/', ':' }) + 1)..];
        }
        else
        {
            var parts = s.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new ArgumentException($"Not a store source: '{source}'. Use owner/repo, owner/repo/folder, a git URL or a local path.");
            var owner = parts[0];
            var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
            urls = new List<string> { $"https://github.com/{owner}/{repo}.git", $"git@github.com:{owner}/{repo}.git" };
            sub = string.Join('/', parts.Skip(2));
            repoName = repo;
        }

        if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repoName = repoName[..^4];
        if (pathOverride != null)
            sub = pathOverride.Trim('/');
        var name = sub.Length > 0 ? sub[(sub.LastIndexOf('/') + 1)..] : repoName;
        return (urls, sub, SafeName(name));
    }

    public static string ExpandHome(string p) =>
        p == "~" || p.StartsWith("~/")
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p.Length > 1 ? p[2..] : "")
            : p;

    public static string SafeName(string s)
    {
        var cleaned = Regex.Replace(s, "[^A-Za-z0-9._-]", "-").Trim('-', '.');
        return cleaned.Length == 0 ? "store" : cleaned;
    }

    public static string Hash(string file) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(file))).ToLowerInvariant();

    /// <summary>The menu configs in a store folder: every top-level *.json with a "menus" object.</summary>
    public static List<StoreMenu> ReadStore(StoreEntry store, string dir)
    {
        var result = new List<StoreMenu>();
        if (!Directory.Exists(dir))
            return result;
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            JsonNode? root;
            try { root = JsonNode.Parse(System.IO.File.ReadAllText(file)); }
            catch { continue; }
            if (root is not JsonObject obj || obj["menus"] is not JsonObject)
                continue;
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            var title = obj["title"]?.GetValueKind() == JsonValueKind.String ? obj["title"]!.GetValue<string>() : name;
            result.Add(new StoreMenu(store, name, title, file, Hash(file), Opens(root)));
        }
        return result;
    }

    /// <summary>
    /// Names of the sibling configs an <c>open-config</c> action opens ("agents.json" -> "agents").
    /// Paths into other folders, home or absolute paths are not part of the store and are left out.
    /// </summary>
    public static List<string> Opens(JsonNode? root)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["type"]?.GetValueKind() == JsonValueKind.String && o["type"]!.GetValue<string>() == "open-config"
                        && o["path"]?.GetValueKind() == JsonValueKind.String)
                    {
                        var p = o["path"]!.GetValue<string>().Trim();
                        if (p.StartsWith("./")) p = p[2..];
                        if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.IndexOfAny(new[] { '/', '\\', '~' }) < 0)
                            names.Add(p[..^5]);
                    }
                    foreach (var kv in o) Walk(kv.Value);
                    break;
                case JsonArray a:
                    foreach (var x in a) Walk(x);
                    break;
            }
        }
        Walk(root);
        return names.ToList();
    }

    public static MenuStatus Status(StoreMenu menu, string configsDir, StoreState state)
    {
        var local = System.IO.Path.Combine(configsDir, menu.Name + ".json");
        if (!System.IO.File.Exists(local))
            return MenuStatus.NotInstalled;
        var localHash = Hash(local);
        if (localHash == menu.Hash)
            return MenuStatus.UpToDate;
        if (state.Installed.TryGetValue(menu.Name, out var rec) && rec.Store == menu.Store.Name)
            return localHash == rec.Hash ? MenuStatus.UpdateAvailable : MenuStatus.ChangedLocally;
        return MenuStatus.LocalDiffers;
    }

    public static string Describe(MenuStatus s) => s switch
    {
        MenuStatus.NotInstalled => "",
        MenuStatus.UpToDate => "installed",
        MenuStatus.UpdateAvailable => "update available",
        MenuStatus.ChangedLocally => "changed locally",
        MenuStatus.LocalDiffers => "a different local copy exists",
        _ => "",
    };

    /// <summary>The names plus every config they open, transitively, that the store has.</summary>
    public static List<StoreMenu> WithDependencies(IEnumerable<StoreMenu> picked, IReadOnlyList<StoreMenu> available)
    {
        var result = new List<StoreMenu>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<StoreMenu>(picked);
        while (queue.Count > 0)
        {
            var m = queue.Dequeue();
            if (!seen.Add(m.Store.Name + "/" + m.Name))
                continue;
            result.Add(m);
            foreach (var dep in m.Opens)
            {
                var d = available.FirstOrDefault(x => x.Store.Name == m.Store.Name && x.Name == dep);
                if (d != null)
                    queue.Enqueue(d);
            }
        }
        return result;
    }

    private static readonly (string Label, Regex Re)[] SecretPatterns =
    {
        ("GitHub token", new Regex(@"\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}|\bgithub_pat_[A-Za-z0-9_]{30,}")),
        ("API key (sk-...)", new Regex(@"\bsk-(ant-|proj-)?[A-Za-z0-9_-]{20,}")),
        ("Slack token", new Regex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}")),
        ("AWS access key", new Regex(@"\bAKIA[0-9A-Z]{16}\b")),
        ("Telegram bot token", new Regex(@"(?<!\d)\d{8,12}:[A-Za-z0-9_-]{35}(?![A-Za-z0-9_-])")),
        ("private key", new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")),
        ("password or token value", new Regex(@"(?i)""(password|passwd|secret|token|api_?key)""\s*:\s*""[^""]{8,}""")),
        ("credential in a URL", new Regex(@"://[^/\s:@""]+:[^/\s@""]{6,}@")),
        ("password or key in a URL", new Regex(@"(?i)[?&](pwd|password|pass|token|access_token|api_?key|key|secret|sig)=[^&\s""]{8,}")),
    };

    /// <summary>What looks like a secret in a config, one line per finding ("line 12: GitHub token").</summary>
    public static List<string> FindSecrets(string text)
    {
        var found = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            foreach (var (label, re) in SecretPatterns)
                if (re.IsMatch(lines[i]))
                    found.Add($"line {i + 1}: {label}");
        return found;
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static StoreState LoadState(string file)
    {
        try
        {
            return System.IO.File.Exists(file)
                ? JsonSerializer.Deserialize<StoreState>(System.IO.File.ReadAllText(file)) ?? new StoreState()
                : new StoreState();
        }
        catch (JsonException e)
        {
            // Never start from an empty state over a broken file: saving it would drop every store.
            throw new InvalidOperationException($"{file} is not valid JSON ({e.Message}). Fix or delete it.");
        }
    }

    public static void SaveState(string file, StoreState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(state, WriteOptions) + "\n", new UTF8Encoding(false));
        System.IO.File.Move(tmp, file, overwrite: true);
    }
}
