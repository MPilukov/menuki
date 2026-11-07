using System.Diagnostics;
using Menuki.Config;

namespace Menuki.Store;

/// <summary>
/// Menu configs shared through git repositories ("stores"):
///
///   menuki store add &lt;source&gt; [--name N] [--path DIR]   remember a repo and clone it
///   menuki store [list]                                  the stores
///   menuki store remove &lt;name&gt;                          forget one (installed configs stay)
///   menuki install [name...] [--store S] [--force]       pick from the stores and install
///   menuki update [name...] [--force]                    take newer store versions of installed configs
///   menuki publish &lt;name...&gt; [--store S] [--all] [-m MSG] [--force]   send local configs to a store
///
/// A store is cloned with the user's own git, so a private repo works with whatever access
/// git already has. Configs land in the configs directory under their file name; what came
/// from where is kept in &lt;menuki home&gt;/store.json, which tells an update from a local edit.
/// </summary>
public class StoreCli
{
    public static readonly string[] Commands = { "store", "install", "update", "publish" };

    public static bool IsStoreCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0]);

    private readonly string _home;
    private readonly string _configsDir;
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly Func<string, string?> _ask;
    private readonly bool _interactive;

    public StoreCli(string home, string configsDir, TextWriter output, TextWriter error, Func<string, string?> ask, bool interactive)
    {
        _home = home;
        _configsDir = configsDir;
        _out = output;
        _err = error;
        _ask = ask;
        _interactive = interactive;
    }

    private string StateFile => Path.Combine(_home, "store.json");
    private string StoreDir(StoreEntry s) => Path.Combine(_home, "stores", s.Name);
    private string MenusDir(StoreEntry s) => s.Path.Length > 0 ? Path.Combine(StoreDir(s), s.Path) : StoreDir(s);

    public static int Run(string[] args)
    {
        var home = Environment.GetEnvironmentVariable("MENUKI_HOME") is { Length: > 0 } h
            ? h
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".menuki");
        var cli = new StoreCli(home, ConfigResolver.ConfigsDir, Console.Out, Console.Error,
            prompt => { Console.Write(prompt); return Console.ReadLine(); },
            !Console.IsInputRedirected);
        return cli.Execute(args);
    }

    public int Execute(string[] args)
    {
        try
        {
            var opts = Options.Parse(args.Skip(1).ToArray());
            return args[0] switch
            {
                "store" => StoreCommand(opts),
                "install" => Install(opts),
                "update" => Update(opts),
                "publish" => Publish(opts),
                _ => 2,
            };
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
        {
            _err.WriteLine(e.Message);
            return 1;
        }
    }

    // ---------- store add / list / remove ----------

    private int StoreCommand(Options o)
    {
        var sub = o.Positional.FirstOrDefault() ?? "list";
        var rest = o.Positional.Skip(1).ToList();
        var state = StoreLogic.LoadState(StateFile);
        switch (sub)
        {
            case "list":
                if (state.Stores.Count == 0)
                {
                    _out.WriteLine("No stores yet. Add one: menuki store add owner/repo");
                    return 0;
                }
                foreach (var s in state.Stores)
                {
                    var count = StoreLogic.ReadStore(s, MenusDir(s)).Count;
                    _out.WriteLine($"{s.Name}  {s.Url}{(s.Path.Length > 0 ? ", folder " + s.Path : "")}  {count} configs");
                }
                return 0;
            case "add":
                if (rest.Count == 0)
                    throw new ArgumentException("Usage: menuki store add <owner/repo[/folder] | git URL | path> [--name N] [--path DIR]");
                return AddStore(state, rest[0], o.Value("--name"), o.Value("--path"));
            case "remove":
            case "rm":
                if (rest.Count == 0)
                    throw new ArgumentException("Usage: menuki store remove <name>");
                var gone = state.Stores.FirstOrDefault(s => s.Name == rest[0])
                           ?? throw new ArgumentException($"No store named '{rest[0]}'. See: menuki store list");
                state.Stores.Remove(gone);
                StoreLogic.SaveState(StateFile, state);
                if (Directory.Exists(StoreDir(gone)))
                    Directory.Delete(StoreDir(gone), recursive: true);
                _out.WriteLine($"Removed store {gone.Name}. Configs installed from it stay in {_configsDir}.");
                return 0;
            default:
                throw new ArgumentException($"Unknown: menuki store {sub}. Use: add, list, remove.");
        }
    }

    private int AddStore(StoreState state, string source, string? name, string? pathOverride)
    {
        var (urls, sub, defaultName) = StoreLogic.ParseSource(source, pathOverride);
        var entry = new StoreEntry { Name = StoreLogic.SafeName(name ?? defaultName), Source = source, Path = sub };
        if (state.Stores.Any(s => s.Name == entry.Name))
            throw new ArgumentException($"A store named '{entry.Name}' already exists. Pick another with --name.");

        var dir = StoreDir(entry);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
        string? lastError = null;
        foreach (var url in urls)
        {
            var (code, _, err) = Git(null, "clone", "--quiet", url, dir);
            if (code == 0)
            {
                entry.Url = url;
                break;
            }
            lastError = err.Trim();
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        if (entry.Url.Length == 0)
            throw new InvalidOperationException($"Could not clone {source}: {lastError}\nCheck that git on this machine can reach it (gh auth login && gh auth setup-git, or an SSH key).");
        if (sub.Length > 0 && !Directory.Exists(MenusDir(entry)))
        {
            Directory.Delete(dir, recursive: true);
            throw new ArgumentException($"The repository has no folder '{sub}'.");
        }

        state.Stores.Add(entry);
        StoreLogic.SaveState(StateFile, state);
        var menus = StoreLogic.ReadStore(entry, MenusDir(entry));
        _out.WriteLine($"Added store {entry.Name} ({entry.Url}{(sub.Length > 0 ? ", folder " + sub : "")}): {menus.Count} configs.");
        _out.WriteLine(menus.Count > 0 ? "Install from it: menuki install" : "It has no configs yet. Send one: menuki publish <name>");
        return 0;
    }

    // ---------- install ----------

    private int Install(Options o)
    {
        var state = StoreLogic.LoadState(StateFile);
        var stores = PickStores(state, o.Value("--store"));
        Refresh(stores);
        var available = stores.SelectMany(s => StoreLogic.ReadStore(s, MenusDir(s))).ToList();
        if (available.Count == 0)
        {
            _out.WriteLine("The stores have no configs.");
            return 0;
        }

        List<StoreMenu> picked;
        if (o.Positional.Count > 0 || o.Has("--all"))
            picked = o.Has("--all") ? available : o.Positional.Select(n => Find(available, n)).ToList();
        else
        {
            if (!_interactive)
                throw new ArgumentException("Name the configs to install: menuki install <name...> (or --all).");
            ShowCatalog(available, state);
            var answer = _ask("\nInstall which? Numbers or names, space separated, 'all' (empty to cancel): ")?.Trim() ?? "";
            if (answer.Length == 0)
            {
                _out.WriteLine("Nothing installed.");
                return 0;
            }
            picked = answer == "all"
                ? available
                : answer.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => int.TryParse(t, out var i) && i >= 1 && i <= available.Count ? available[i - 1] : Find(available, t))
                    .ToList();
        }

        var all = StoreLogic.WithDependencies(picked, available);
        var extra = all.Where(m => !picked.Contains(m)).Select(m => m.Name).ToList();
        if (extra.Count > 0)
            _out.WriteLine($"Also installing what they open: {string.Join(", ", extra)}");

        int installed = 0, skipped = 0;
        foreach (var m in all)
        {
            if (InstallOne(m, state, o.Has("--force")))
                installed++;
            else
                skipped++;
        }
        StoreLogic.SaveState(StateFile, state);
        _out.WriteLine($"Installed {installed}{(skipped > 0 ? $", skipped {skipped}" : "")}. Run one: menuki <name>");
        return 0;
    }

    private bool InstallOne(StoreMenu m, StoreState state, bool force)
    {
        var target = Path.Combine(_configsDir, m.Name + ".json");
        var status = StoreLogic.Status(m, _configsDir, state);
        if (status == MenuStatus.UpToDate)
        {
            Record(state, m);
            _out.WriteLine($"  {m.Name}: already up to date");
            return true;
        }
        if ((status == MenuStatus.ChangedLocally || status == MenuStatus.LocalDiffers) && !force)
        {
            if (!ConfirmOverwrite(m, target))
            {
                _out.WriteLine($"  {m.Name}: kept the local copy");
                return false;
            }
        }
        Write(m, target, backup: status != MenuStatus.NotInstalled);
        Record(state, m);
        _out.WriteLine($"  {m.Name}: {(status == MenuStatus.NotInstalled ? "installed" : "updated")}");
        ReportMissingTools(target);
        return true;
    }

    private bool ConfirmOverwrite(StoreMenu m, string target)
    {
        if (!_interactive)
        {
            _out.WriteLine($"  {m.Name}: the local copy differs from the store; not replaced (add --force).");
            return false;
        }
        while (true)
        {
            var a = (_ask($"  {m.Name}.json here differs from the store copy. Replace it? [y/N/d=diff] ") ?? "").Trim().ToLowerInvariant();
            if (a == "d")
            {
                var (_, diff, _) = Git(null, "diff", "--no-index", "--", target, m.File);
                _out.WriteLine(diff);
                continue;
            }
            return a is "y" or "yes";
        }
    }

    private void Write(StoreMenu m, string target, bool backup)
    {
        Directory.CreateDirectory(_configsDir);
        if (backup && File.Exists(target))
        {
            var dir = Path.Combine(_home, "backup", "store-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            File.Copy(target, Path.Combine(dir, m.Name + ".json"), overwrite: true);
        }
        var tmp = target + ".tmp";
        File.Copy(m.File, tmp, overwrite: true);
        File.Move(tmp, target, overwrite: true);
    }

    private static void Record(StoreState state, StoreMenu m) =>
        state.Installed[m.Name] = new InstalledEntry { Store = m.Store.Name, Hash = m.Hash, At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };

    private void ReportMissingTools(string file)
    {
        try
        {
            var cfg = System.Text.Json.JsonSerializer.Deserialize<MenuConfig>(File.ReadAllText(file));
            if (cfg == null) return;
            var missing = Engine.Requirements.Missing(cfg);
            if (missing.Count > 0)
                _out.WriteLine("    " + Engine.Requirements.Describe(missing).Replace("\n", "\n    "));
        }
        catch
        {
            // a config that does not parse is reported when it is opened
        }
    }

    private void ShowCatalog(List<StoreMenu> available, StoreState state)
    {
        var width = available.Max(m => m.Name.Length);
        var i = 0;
        foreach (var group in available.GroupBy(m => m.Store.Name))
        {
            _out.WriteLine($"{group.Key}:");
            foreach (var m in group)
            {
                i++;
                var status = StoreLogic.Describe(StoreLogic.Status(m, _configsDir, state));
                var title = m.Title != m.Name ? m.Title : "";
                var opens = m.Opens.Count > 0 ? $"  opens: {string.Join(", ", m.Opens)}" : "";
                _out.WriteLine($"  {i,2}) {m.Name.PadRight(width)}  {title}{(status.Length > 0 ? $"  [{status}]" : "")}{opens}");
            }
        }
    }

    // ---------- update ----------

    private int Update(Options o)
    {
        var state = StoreLogic.LoadState(StateFile);
        var stores = PickStores(state, o.Value("--store"));
        Refresh(stores);
        var available = stores.SelectMany(s => StoreLogic.ReadStore(s, MenusDir(s))).ToList();
        var names = o.Positional.Count > 0 ? o.Positional : state.Installed.Keys.OrderBy(k => k).ToList();
        if (names.Count == 0)
        {
            _out.WriteLine("Nothing installed from a store yet. See: menuki install");
            return 0;
        }

        int updated = 0, current = 0, kept = 0;
        foreach (var name in names)
        {
            if (!state.Installed.TryGetValue(name, out var rec))
            {
                _out.WriteLine($"  {name}: not installed from a store");
                continue;
            }
            var m = available.FirstOrDefault(x => x.Name == name && x.Store.Name == rec.Store);
            if (m == null)
            {
                _out.WriteLine($"  {name}: no longer in store {rec.Store}");
                continue;
            }
            var status = StoreLogic.Status(m, _configsDir, state);
            var target = Path.Combine(_configsDir, name + ".json");
            switch (status)
            {
                case MenuStatus.UpToDate:
                    Record(state, m);
                    current++;
                    break;
                case MenuStatus.UpdateAvailable:
                case MenuStatus.NotInstalled:
                    Write(m, target, backup: status != MenuStatus.NotInstalled);
                    Record(state, m);
                    _out.WriteLine($"  {name}: updated");
                    updated++;
                    break;
                default:
                    if (o.Has("--force"))
                    {
                        Write(m, target, backup: true);
                        Record(state, m);
                        _out.WriteLine($"  {name}: replaced the local edit with the store copy (old one in backup/)");
                        updated++;
                    }
                    else
                    {
                        _out.WriteLine($"  {name}: changed here, kept. Send it: menuki publish {name}; or take the store copy: menuki update {name} --force");
                        kept++;
                    }
                    break;
            }
        }
        StoreLogic.SaveState(StateFile, state);
        _out.WriteLine($"Updated {updated}, up to date {current}{(kept > 0 ? $", kept local edits {kept}" : "")}.");
        return 0;
    }

    // ---------- publish ----------

    private int Publish(Options o)
    {
        var state = StoreLogic.LoadState(StateFile);
        var names = o.Has("--all") ? ConfigResolver.ListNames(_configsDir) : o.Positional.ToList();
        if (names.Count == 0)
            throw new ArgumentException("Usage: menuki publish <name...> [--store S] [-m message] (or --all)");

        StoreEntry store;
        var storeName = o.Value("--store");
        if (storeName != null)
            store = state.Stores.FirstOrDefault(s => s.Name == storeName) ?? throw new ArgumentException($"No store named '{storeName}'. See: menuki store list");
        else
        {
            var recorded = names.Select(n => state.Installed.GetValueOrDefault(n)?.Store).Where(s => s != null).Distinct().ToList();
            store = recorded.Count == 1 && state.Stores.Any(s => s.Name == recorded[0])
                ? state.Stores.First(s => s.Name == recorded[0])
                : state.Stores.Count == 1
                    ? state.Stores[0]
                    : throw new ArgumentException(state.Stores.Count == 0
                        ? "No stores yet. Add one: menuki store add owner/repo"
                        : "Which store? Add --store <name>. See: menuki store list");
        }

        // The configs they open go along, so an install on another machine is complete.
        var queue = new Queue<string>(names);
        var all = new List<string>();
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (all.Contains(n))
                continue;
            var file = Path.Combine(_configsDir, n + ".json");
            if (!File.Exists(file))
                throw new ArgumentException($"No saved config named '{n}' in {_configsDir}.");
            all.Add(n);
            try
            {
                foreach (var dep in StoreLogic.Opens(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))))
                    if (File.Exists(Path.Combine(_configsDir, dep + ".json")))
                        queue.Enqueue(dep);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new ArgumentException($"{n}.json is not valid JSON; fix it before publishing.");
            }
        }
        if (all.Count > names.Count)
            _out.WriteLine($"Also publishing what they open: {string.Join(", ", all.Except(names))}");

        if (!o.Has("--force"))
        {
            var findings = all.SelectMany(n => StoreLogic.FindSecrets(File.ReadAllText(Path.Combine(_configsDir, n + ".json")))
                .Select(f => $"  {n}.json {f}")).ToList();
            if (findings.Count > 0)
            {
                _err.WriteLine("Not published: these look like secrets.\n" + string.Join("\n", findings) +
                               "\nMove them out of the config (an environment variable, a keychain lookup) or add --force if they are not secrets.");
                return 1;
            }
        }

        var repo = StoreDir(store);
        Refresh(new List<StoreEntry> { store }, strict: true);
        var dir = MenusDir(store);
        Directory.CreateDirectory(dir);
        foreach (var n in all)
            File.Copy(Path.Combine(_configsDir, n + ".json"), Path.Combine(dir, n + ".json"), overwrite: true);

        var rel = all.Select(n => Path.GetRelativePath(repo, Path.Combine(dir, n + ".json"))).ToArray();
        GitOrThrow(repo, new[] { "add", "--" }.Concat(rel).ToArray());
        var (changed, _, _) = Git(repo, "diff", "--cached", "--quiet");
        if (changed == 0)
            _out.WriteLine($"Store {store.Name} already has these versions.");
        else
        {
            var message = o.Value("-m") ?? o.Value("--message") ?? $"Publish {string.Join(", ", all)} from {Environment.MachineName}";
            GitOrThrow(repo, "commit", "--quiet", "-m", message);
            var (code, _, err) = Git(repo, "push", "--quiet", "origin", "HEAD");
            if (code != 0)
            {
                Git(repo, "reset", "--quiet", "--hard", "HEAD~1");   // the clone is only a cache; local configs are untouched
                throw new InvalidOperationException($"Push to {store.Url} failed: {err.Trim()}");
            }
            _out.WriteLine($"Published to {store.Name}: {string.Join(", ", all)}");
        }

        foreach (var m in StoreLogic.ReadStore(store, dir).Where(m => all.Contains(m.Name)))
            Record(state, m);
        StoreLogic.SaveState(StateFile, state);
        return 0;
    }

    // ---------- helpers ----------

    private List<StoreEntry> PickStores(StoreState state, string? name)
    {
        if (state.Stores.Count == 0)
            throw new ArgumentException("No stores yet. Add one: menuki store add owner/repo");
        if (name == null)
            return state.Stores;
        var s = state.Stores.FirstOrDefault(x => x.Name == name) ?? throw new ArgumentException($"No store named '{name}'. See: menuki store list");
        return new List<StoreEntry> { s };
    }

    private void Refresh(List<StoreEntry> stores, bool strict = false)
    {
        foreach (var s in stores)
        {
            var dir = StoreDir(s);
            if (!Directory.Exists(Path.Combine(dir, ".git")))
            {
                var (c, _, e) = Git(null, "clone", "--quiet", s.Url, dir);
                if (c != 0)
                {
                    if (strict) throw new InvalidOperationException($"Could not clone {s.Url}: {e.Trim()}");
                    _err.WriteLine($"Store {s.Name}: could not clone ({e.Trim()}).");
                }
                continue;
            }
            if (Git(dir, "rev-parse", "--verify", "--quiet", "HEAD").Code != 0)
                continue;   // an empty repository: nothing to pull yet, the first publish creates the branch
            var (code, _, err) = Git(dir, "pull", "--quiet", "--ff-only");
            if (code != 0)
            {
                if (strict) throw new InvalidOperationException($"Could not update store {s.Name}: {err.Trim()}");
                _err.WriteLine($"Store {s.Name}: could not update, using the last copy ({err.Trim()}).");
            }
        }
    }

    private static StoreMenu Find(List<StoreMenu> available, string query)
    {
        var parts = query.Split('/', 2);
        var matches = parts.Length == 2
            ? available.Where(m => m.Store.Name == parts[0] && m.Name == parts[1]).ToList()
            : available.Where(m => m.Name == StripJson(query)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"No config '{query}' in the stores. See: menuki install"),
            _ => throw new ArgumentException($"'{query}' is in several stores: {string.Join(", ", matches.Select(m => m.Store.Name + "/" + m.Name))}. Name one of them."),
        };
    }

    private static string StripJson(string s) => s.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? s[..^5] : s;

    private static void GitOrThrow(string? dir, params string[] args)
    {
        var (code, _, err) = Git(dir, args);
        if (code != 0)
            throw new InvalidOperationException($"git {args[0]} failed: {err.Trim()}");
    }

    public static (int Code, string Out, string Err) Git(string? dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (dir != null)
        {
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(dir);
        }
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";   // never hang on a password prompt
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            return (p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("git is not installed or not on PATH; the store needs it.");
        }
    }

    /// <summary>Positional arguments plus --flags (with or without a value).</summary>
    private sealed class Options
    {
        private static readonly HashSet<string> WithValue = new() { "--store", "--name", "--path", "-m", "--message" };
        public List<string> Positional { get; } = new();
        private readonly Dictionary<string, string?> _flags = new();

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith('-') && a.Length > 1)
                {
                    if (WithValue.Contains(a))
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentException($"{a} needs a value.");
                        o._flags[a] = args[++i];
                    }
                    else
                        o._flags[a] = null;
                }
                else
                    o.Positional.Add(a);
            }
            return o;
        }

        public bool Has(string flag) => _flags.ContainsKey(flag);
        public string? Value(string flag) => _flags.TryGetValue(flag, out var v) ? v : null;
    }
}
