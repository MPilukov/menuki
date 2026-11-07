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
    /// <summary>
    /// The store's config folder. Every step from the clone down to it must be a real folder:
    /// a link (say <c>sub -&gt; ../../configs</c>) would make the store the user's own configs.
    /// </summary>
    private string MenusDir(StoreEntry s)
    {
        var root = Path.GetFullPath(StoreDir(s));
        if (s.Path.Length == 0)
            return root;
        var dir = Path.GetFullPath(Path.Combine(root, s.Path));
        if (!dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Store {s.Name}: folder '{s.Path}' is outside the repository.");
        for (var d = dir; d.Length > root.Length; d = Path.GetDirectoryName(d)!)
            if (Directory.Exists(d) && new DirectoryInfo(d).LinkTarget != null)
                throw new InvalidOperationException($"Store {s.Name}: '{Path.GetRelativePath(root, d)}' in the repository is a link, not a folder; refusing to use it.");
        return dir;
    }

    /// <summary>A config name is a file name in the configs folder: no paths, no "..".</summary>
    private static string CheckName(string name)
    {
        var n = StripJson(name.Trim());
        if (!System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z0-9._-]+$") || n.Trim('.').Length == 0)
            throw new ArgumentException($"'{name}' is not a config name (a name like 'my', without a folder).");
        return n;
    }

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
            using var _ = AcquireLock();
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

    /// <summary>One store command at a time per menuki home, so two runs never lose each other's state.</summary>
    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(_home);
        var path = Path.Combine(_home, "store.lock");
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(200);
            }
            catch (IOException)
            {
                throw new InvalidOperationException("Another menuki store command is running (store.lock is busy). Try again when it is done.");
            }
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
        if (state.Stores.Any(s => string.Equals(s.Name, entry.Name, StringComparison.OrdinalIgnoreCase)))
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
        try
        {
            if (sub.Length > 0 && !Directory.Exists(MenusDir(entry)))
                throw new ArgumentException($"The repository has no folder '{sub}'.");
        }
        catch
        {
            Directory.Delete(dir, recursive: true);
            throw;
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
            picked = o.Has("--all") ? FirstPerName(available) : o.Positional.Select(n => Find(available, n.Contains('/') ? n : CheckName(n))).ToList();
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
                ? FirstPerName(available)
                : answer.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => int.TryParse(t, out var i) && i >= 1 && i <= available.Count ? available[i - 1] : Find(available, t))
                    .ToList();
        }

        var all = FirstPerName(StoreLogic.WithDependencies(picked, available));
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

    private string? _backupDir;

    private void Write(StoreMenu m, string target, bool backup)
    {
        Directory.CreateDirectory(_configsDir);
        if (backup && File.Exists(target))
        {
            if (_backupDir == null)
            {
                var stamp = "store-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var dir = Path.Combine(_home, "backup", stamp);
                for (var i = 2; Directory.Exists(dir); i++)
                    dir = Path.Combine(_home, "backup", $"{stamp}-{i}");
                Directory.CreateDirectory(dir);
                _backupDir = dir;
            }
            var copy = Path.Combine(_backupDir, m.Name + ".json");
            if (!File.Exists(copy))   // the first copy in a run is the user's original; never replace it
                File.Copy(target, copy);
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
        var names = o.Positional.Count > 0 ? o.Positional.Select(CheckName).ToList() : state.Installed.Keys.OrderBy(k => k).ToList();
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
        // A newer version may open configs that are not here yet; without them its open-config fails.
        var tracked = state.Installed.Where(kv => names.Contains(kv.Key))
            .Select(kv => available.FirstOrDefault(x => x.Name == kv.Key && x.Store.Name == kv.Value.Store))
            .OfType<StoreMenu>().ToList();
        foreach (var dep in StoreLogic.WithDependencies(tracked, available).Where(d => !tracked.Contains(d)))
            if (StoreLogic.Status(dep, _configsDir, state) == MenuStatus.NotInstalled)
            {
                Write(dep, Path.Combine(_configsDir, dep.Name + ".json"), backup: false);
                Record(state, dep);
                _out.WriteLine($"  {dep.Name}: installed (now opened by an updated config)");
                updated++;
            }

        StoreLogic.SaveState(StateFile, state);
        _out.WriteLine($"Updated {updated}, up to date {current}{(kept > 0 ? $", kept local edits {kept}" : "")}.");
        return 0;
    }

    // ---------- publish ----------

    private int Publish(Options o)
    {
        var state = StoreLogic.LoadState(StateFile);
        var names = (o.Has("--all") ? ConfigResolver.ListNames(_configsDir) : o.Positional).Select(CheckName).Distinct().ToList();
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

        var repo = StoreDir(store);
        Refresh(new List<StoreEntry> { store }, strict: true);
        var dir = MenusDir(store);

        // Read each config once: what is scanned is exactly what is pushed, even if it is saved again meanwhile.
        var content = all.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(_configsDir, n + ".json")));
        if (!o.Has("--allow-secrets"))
        {
            var findings = all.SelectMany(n => StoreLogic.FindSecrets(StoreLogic.Decode(content[n]))
                .Select(f => $"  {n}.json {f}")).ToList();
            if (findings.Count > 0)
            {
                _err.WriteLine("Not published: these look like secrets.\n" + string.Join("\n", findings) +
                               "\nMove them out of the config (an environment variable, a keychain lookup), or add --allow-secrets if they are not secrets or the repository is private and you accept that.");
                return 1;
            }
        }

        // Do not replace a version another machine published since this one last synced.
        if (!o.Has("--force"))
        {
            var current = StoreLogic.ReadStore(store, dir).ToDictionary(m => m.Name);
            var newer = all.Where(n => current.TryGetValue(n, out var sm)
                                       && sm.Hash != StoreLogic.Hash(content[n])
                                       && !(state.Installed.TryGetValue(n, out var rec) && rec.Store == store.Name && rec.Hash == sm.Hash))
                .ToList();
            if (newer.Count > 0)
            {
                _err.WriteLine($"Not published: store {store.Name} has a version of {string.Join(", ", newer)} that this machine has not seen " +
                               "(published from elsewhere). Compare first (menuki install <name> shows the diff with d), or add --force to replace it.");
                return 1;
            }
        }

        try
        {
            Directory.CreateDirectory(dir);
            foreach (var n in all)
            {
                var dest = Path.Combine(dir, n + ".json");
                // A link in the repo must not make the write go through it to another file.
                if (new FileInfo(dest).LinkTarget != null)
                    File.Delete(dest);
                File.WriteAllBytes(dest, content[n]);
            }

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
                    throw new InvalidOperationException($"Push to {store.Url} failed: {err.Trim()}");
                _out.WriteLine($"Published to {store.Name}: {string.Join(", ", all)}");
            }
        }
        catch
        {
            // The clone is only a cache: drop it so nothing half-done is sent later; the next command clones it again.
            DeleteClone(store);
            throw;
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
            // The configured URL, not `remote get-url`: that one applies insteadOf rewrites and would never match.
            var origin = Git(dir, "config", "--get", "remote.origin.url");
            var (code, err) = origin.Code == 0 && origin.Out.Trim() == s.Url ? Sync(dir) : (1, "");
            if (code == 1)
            {
                // Another repo's clone in this folder, one made while the repo was empty, or one with
                // leftovers of an interrupted publish: clone it again.
                DeleteClone(s);
                var (c, _, e) = Git(null, "clone", "--quiet", s.Url, dir);
                (code, err) = c == 0 ? (0, "") : (2, e);
            }
            if (code != 0)
            {
                if (strict) throw new InvalidOperationException($"Could not update store {s.Name}: {err.Trim()}");
                _err.WriteLine($"Store {s.Name}: could not update, using the last copy ({err.Trim()}).");
            }
        }
    }

    /// <summary>
    /// Fetch and make the clone exactly the remote branch: the clone is a cache, so a local
    /// commit or file left by a failed run must not survive. 1 = nothing to follow yet.
    /// </summary>
    private static (int Code, string Err) Sync(string dir)
    {
        var fetch = Git(dir, "fetch", "--quiet", "--prune", "origin");
        if (fetch.Code != 0)
            return (2, fetch.Err);
        var remoteHasBranch = Git(dir, "for-each-ref", "--count=1", "refs/remotes/origin").Out.Trim().Length > 0;
        var hasLocalCommit = Git(dir, "rev-parse", "--verify", "--quiet", "HEAD").Code == 0;
        if (!remoteHasBranch)
            return hasLocalCommit || Git(dir, "status", "--porcelain").Out.Trim().Length > 0
                ? (1, "")   // empty remote: drop an unpushed commit or files left staged by an interrupted run
                : (0, "");
        var upstream = Git(dir, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
        if (upstream.Code != 0)
            return (1, "");   // cloned while the repository was empty: no branch to follow yet
        var reset = Git(dir, "reset", "--quiet", "--hard", upstream.Out.Trim());
        if (reset.Code != 0)
            return (2, reset.Err);
        Git(dir, "clean", "--quiet", "-fdx");
        return (0, "");
    }

    private void DeleteClone(StoreEntry s)
    {
        var dir = StoreDir(s);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static List<StoreMenu> FirstPerName(IEnumerable<StoreMenu> menus) =>
        menus.GroupBy(m => m.Name).Select(g => g.First()).ToList();

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
            // One stream after the other on this thread, like ShellRunner. Reading both at once (async or on a
            // second thread) crashes the self-contained single-file build on macOS: the first reads of two
            // pipes race in Socket.LoadSocketTypeFromHandle (AccessViolationException, about every other clone).
            // git's stderr here is a few lines, far below the pipe buffer, so this cannot block.
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stdout, stderr);
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
        private static readonly HashSet<string> Switches = new() { "--force", "--all", "--allow-secrets" };
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
                    var eq = a.IndexOf('=');
                    if (eq > 0)
                    {
                        var key = a[..eq];
                        if (!WithValue.Contains(key))
                            throw new ArgumentException($"Unknown option {key}.");
                        o._flags[key] = a[(eq + 1)..];
                    }
                    else if (WithValue.Contains(a))
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentException($"{a} needs a value.");
                        o._flags[a] = args[++i];
                    }
                    else if (Switches.Contains(a))
                        o._flags[a] = null;
                    else
                        throw new ArgumentException($"Unknown option {a}.");
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
