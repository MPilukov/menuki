using System.Text.Json.Nodes;
using Menuki.Store;
using Xunit;

namespace Menuki.Tests;

public class StoreLogicTests
{
    [Fact]
    public void Parse_github_shorthand_with_folder()
    {
        var (urls, path, name) = StoreLogic.ParseSource("MPilukov/menuki-configs/team/ops");
        Assert.Equal(new[] { "https://github.com/MPilukov/menuki-configs.git", "git@github.com:MPilukov/menuki-configs.git" }, urls);
        Assert.Equal("team/ops", path);
        Assert.Equal("ops", name);
    }

    [Fact]
    public void Parse_url_keeps_it_and_names_store_after_repo()
    {
        var (urls, path, name) = StoreLogic.ParseSource("git@github.com:me/menus.git");
        Assert.Equal(new[] { "git@github.com:me/menus.git" }, urls);
        Assert.Equal("", path);
        Assert.Equal("menus", name);
    }

    [Fact]
    public void Parse_rejects_a_single_word()
    {
        Assert.Throws<ArgumentException>(() => StoreLogic.ParseSource("menus"));
    }

    [Fact]
    public void Opens_finds_sibling_configs_only()
    {
        var root = JsonNode.Parse("""
        { "menus": { "main": { "items": [
          { "name": "a", "action": { "type": "open-config", "path": "agents.json" } },
          { "name": "b", "action": { "type": "open-config", "path": "./git.json" } },
          { "name": "c", "action": { "type": "open-config", "path": "~/other/x.json" } },
          { "name": "d", "action": { "type": "open-config", "path": "sub/y.json" } },
          { "name": "e", "action": { "type": "sequence", "steps": [ { "type": "open-config", "path": "deep.json" } ] } }
        ] } } }
        """);
        Assert.Equal(new[] { "agents", "deep", "git" }, StoreLogic.Opens(root));
    }

    [Theory]
    [InlineData("\"command\": \"curl -H 'Authorization: token ghp_abcdefghijklmnopqrstuvwxyz0123456789'\"")]
    [InlineData("\"command\": \"export KEY=sk-ant-api03-abcdefghijklmnopqrstuv\"")]
    [InlineData("\"url\": \"https://bot123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsawA/\"")]
    [InlineData("\"password\": \"hunter2hunter2\"")]
    [InlineData("\"url\": \"https://user:s3cretpass@example.com/x\"")]
    [InlineData("\"url\": \"https://us06web.zoom.us/j/123?pwd=VoNPNlUz7fp3U9Jyk3rv.1\"")]
    public void Finds_secrets(string line) => Assert.NotEmpty(StoreLogic.FindSecrets("{\n" + line + "\n}"));

    [Fact]
    public void Finds_a_secret_hidden_by_json_escaping_or_a_line_break()
    {
        Assert.NotEmpty(StoreLogic.FindSecrets("{ \"url\": \"https://host/?id=1\\u0026pwd=secret123456\" }"));
        Assert.NotEmpty(StoreLogic.FindSecrets("{ \"password\":\n  \"hunter2hunter2\" }"));
    }

    [Fact]
    public void Plain_config_has_no_secrets()
    {
        Assert.Empty(StoreLogic.FindSecrets("""
        { "title": "my", "menus": { "main": { "items": [
          { "name": "x", "action": { "type": "input+shell", "inputs": [ { "name": "token", "secret": true } ],
            "command_template": "gh auth status && echo {token}" } } ] } } }
        """));
    }
}

/// <summary>
/// The whole loop against a local bare repository: publish from one machine, install on
/// another, edit, update. Two menuki homes stand in for the two machines.
/// </summary>
public class StoreFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "menuki-store-" + Guid.NewGuid().ToString("N"));
    private readonly string _remote;

    public StoreFlowTests()
    {
        Directory.CreateDirectory(_root);
        _remote = Path.Combine(_root, "remote.git");
        Assert.Equal(0, StoreCli.Git(null, "init", "--quiet", "--bare", "--initial-branch=main", _remote).Code);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class Machine
    {
        public string Home = "";
        public string Configs => Path.Combine(Home, "configs");
        public StringWriter Out = new();
        public StringWriter Err = new();
        public Queue<string> Answers = new();
        public bool Interactive;

        public int Run(params string[] args)
        {
            Out = new StringWriter();
            Err = new StringWriter();
            var cli = new StoreCli(Home, Configs, Out, Err, _ => Answers.Count > 0 ? Answers.Dequeue() : "", Interactive);
            return cli.Execute(args);
        }

        public void Save(string name, string json)
        {
            Directory.CreateDirectory(Configs);
            File.WriteAllText(Path.Combine(Configs, name + ".json"), json);
        }

        public string Read(string name) => File.ReadAllText(Path.Combine(Configs, name + ".json"));
    }

    private Machine NewMachine(string name)
    {
        var m = new Machine { Home = Path.Combine(_root, name) };
        GitIdentity(m.Home);
        return m;
    }

    // Commits in the store clone need an identity even on a CI runner without a global one.
    private static void GitIdentity(string home)
    {
        Environment.SetEnvironmentVariable("GIT_AUTHOR_NAME", "menuki test");
        Environment.SetEnvironmentVariable("GIT_AUTHOR_EMAIL", "test@example.com");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_NAME", "menuki test");
        Environment.SetEnvironmentVariable("GIT_COMMITTER_EMAIL", "test@example.com");
    }

    private static string Menu(string title, string? opens = null) => $$"""
    { "title": "{{title}}", "start_menu": "main", "menus": { "main": { "title": "{{title}}", "items": [
      { "name": "hi", "action": { "type": "shell", "command": "echo {{title}}" } }
      {{(opens == null ? "" : $", {{ \"name\": \"more\", \"action\": {{ \"type\": \"open-config\", \"path\": \"{opens}.json\" }} }}")}}
    ] } } }
    """;

    [Fact]
    public void Publish_install_edit_update()
    {
        var a = NewMachine("a");
        a.Save("my", Menu("my", opens: "agents"));
        a.Save("agents", Menu("agents"));
        a.Save("private", Menu("private"));

        Assert.Equal(0, a.Run("store", "add", _remote, "--name", "mine"));
        Assert.Equal(0, a.Run("publish", "my"));
        Assert.Contains("agents", a.Out.ToString());          // what my opens goes along

        var b = NewMachine("b");
        Assert.Equal(0, b.Run("store", "add", _remote, "--name", "mine"));
        Assert.Equal(0, b.Run("install", "my"));
        Assert.Equal(a.Read("my"), b.Read("my"));
        Assert.Equal(a.Read("agents"), b.Read("agents"));
        Assert.False(File.Exists(Path.Combine(b.Configs, "private.json")));

        // A changes my and publishes; B's untouched copy updates.
        a.Save("my", Menu("my v2", opens: "agents"));
        Assert.Equal(0, a.Run("publish", "my", "-m", "v2"));
        Assert.Equal(0, b.Run("update"));
        Assert.Contains("v2", b.Read("my"));

        // B edits agents locally; a store update must not overwrite it without --force.
        b.Save("agents", Menu("agents edited on b"));
        a.Save("agents", Menu("agents v2"));
        Assert.Equal(0, a.Run("publish", "agents"));
        Assert.Equal(0, b.Run("update"));
        Assert.Contains("edited on b", b.Read("agents"));
        Assert.Contains("kept", b.Out.ToString());
        Assert.Equal(0, b.Run("update", "agents", "--force"));
        Assert.Contains("agents v2", b.Read("agents"));
        Assert.NotEmpty(Directory.GetDirectories(Path.Combine(b.Home, "backup")));
    }

    [Fact]
    public void Interactive_install_by_number_asks_before_replacing_a_different_copy()
    {
        var a = NewMachine("a");
        a.Save("alpha", Menu("alpha"));
        a.Save("beta", Menu("beta"));
        Assert.Equal(0, a.Run("store", "add", _remote, "--name", "s"));
        Assert.Equal(0, a.Run("publish", "--all"));

        var b = NewMachine("b");
        b.Save("beta", Menu("beta of b"));
        Assert.Equal(0, b.Run("store", "add", _remote, "--name", "s"));
        b.Interactive = true;
        b.Answers.Enqueue("1 2");   // alpha, beta
        b.Answers.Enqueue("n");     // keep b's own beta
        Assert.Equal(0, b.Run("install"));
        Assert.Contains("a different local copy exists", b.Out.ToString());
        Assert.True(File.Exists(Path.Combine(b.Configs, "alpha.json")));
        Assert.Contains("beta of b", b.Read("beta"));
    }

    [Fact]
    public void Non_interactive_install_never_replaces_without_force()
    {
        var a = NewMachine("a");
        a.Save("beta", Menu("beta"));
        a.Run("store", "add", _remote, "--name", "s");
        a.Run("publish", "beta");

        var b = NewMachine("b");
        b.Save("beta", Menu("beta of b"));
        b.Run("store", "add", _remote, "--name", "s");
        Assert.Equal(0, b.Run("install", "beta"));
        Assert.Contains("beta of b", b.Read("beta"));
        Assert.Equal(0, b.Run("install", "beta", "--force"));
        Assert.Equal(a.Read("beta"), b.Read("beta"));
    }

    [Fact]
    public void Publish_refuses_a_config_with_a_secret()
    {
        var a = NewMachine("a");
        a.Save("ops", """{ "title": "ops", "menus": { "main": { "items": [ { "name": "x", "action": { "type": "shell", "command": "curl -H 'token ghp_abcdefghijklmnopqrstuvwxyz0123456789'" } } ] } } }""");
        a.Run("store", "add", _remote, "--name", "s");
        Assert.Equal(1, a.Run("publish", "ops"));
        Assert.Contains("GitHub token", a.Err.ToString());
        Assert.NotEqual(0, StoreCli.Git(null, "--git-dir", _remote, "rev-parse", "--verify", "--quiet", "HEAD").Code);   // nothing reached the repo
    }

    private string NewRemote(string name)
    {
        var path = Path.Combine(_root, name + ".git");
        Assert.Equal(0, StoreCli.Git(null, "init", "--quiet", "--bare", "--initial-branch=main", path).Code);
        return path;
    }

    [Fact]
    public void A_store_link_is_never_read_and_publish_does_not_write_through_it()
    {
        var a = NewMachine("a");
        a.Save("my", Menu("my"));
        a.Save("private", Menu("private"));
        a.Run("store", "add", _remote, "--name", "s");
        a.Run("publish", "my");

        // Someone with push access replaces my.json in the repo with a link to a local file.
        var evil = Path.Combine(_root, "evil");
        StoreCli.Git(null, "clone", "--quiet", _remote, evil);
        File.Delete(Path.Combine(evil, "my.json"));
        File.CreateSymbolicLink(Path.Combine(evil, "my.json"), Path.Combine(a.Configs, "private.json"));
        StoreCli.Git(evil, "add", "-A");
        StoreCli.Git(evil, "commit", "--quiet", "-m", "link");
        StoreCli.Git(evil, "push", "--quiet", "origin", "HEAD");

        var b = NewMachine("b");
        b.Run("store", "add", _remote, "--name", "s");
        b.Run("install", "my");
        Assert.False(File.Exists(Path.Combine(b.Configs, "my.json")));   // not offered: the link is skipped

        a.Save("my", Menu("my v2"));
        Assert.Equal(0, a.Run("publish", "my", "--force"));
        Assert.Contains("\"private\"", a.Read("private"));          // untouched
    }

    [Fact]
    public void Replacing_twice_keeps_the_original_in_a_backup()
    {
        var r2 = NewRemote("second");
        var a = NewMachine("a");
        a.Save("my", Menu("from one"));
        a.Run("store", "add", _remote, "--name", "one");
        a.Run("publish", "my", "--store", "one");
        a.Save("my", Menu("from two"));
        a.Run("store", "add", r2, "--name", "two");
        a.Run("publish", "my", "--store=two");

        var b = NewMachine("b");
        b.Save("my", Menu("original of b"));
        b.Run("store", "add", _remote, "--name", "one");
        b.Run("store", "add", r2, "--name", "two");
        Assert.Equal(0, b.Run("install", "one/my", "--force"));
        Assert.Equal(0, b.Run("install", "two/my", "--force"));
        var backups = Directory.GetFiles(Path.Combine(b.Home, "backup"), "my.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText).ToList();
        Assert.Contains(backups, t => t.Contains("original of b"));
    }

    [Fact]
    public void A_failed_first_push_does_not_leave_the_store_stuck()
    {
        var hook = Path.Combine(_remote, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\nexit 1\n");
        File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var a = NewMachine("a");
        a.Save("alpha", Menu("alpha"));
        a.Save("beta", Menu("beta"));
        a.Run("store", "add", _remote, "--name", "s");
        Assert.Equal(1, a.Run("publish", "alpha"));
        Assert.Contains("failed", a.Err.ToString());

        File.Delete(hook);
        Assert.Equal(0, a.Run("publish", "beta"));
        var files = StoreCli.Git(null, "--git-dir", _remote, "ls-tree", "--name-only", "HEAD").Out;
        Assert.Contains("beta.json", files);
        Assert.DoesNotContain("alpha.json", files);                 // the failed attempt was not sent later
    }

    [Fact]
    public void A_store_added_while_empty_sees_later_publishes()
    {
        var b = NewMachine("b");
        Assert.Equal(0, b.Run("store", "add", _remote, "--name", "s"));
        var a = NewMachine("a");
        a.Save("alpha", Menu("alpha"));
        a.Run("store", "add", _remote, "--name", "s");
        a.Run("publish", "alpha");

        Assert.Equal(0, b.Run("install", "alpha"));
        Assert.True(File.Exists(Path.Combine(b.Configs, "alpha.json")));
        b.Save("beta", Menu("beta"));
        Assert.Equal(0, b.Run("publish", "beta"));
    }

    [Fact]
    public void Update_installs_a_config_a_new_version_opens()
    {
        var a = NewMachine("a");
        a.Save("my", Menu("my"));
        a.Run("store", "add", _remote, "--name", "s");
        a.Run("publish", "my");
        var b = NewMachine("b");
        b.Run("store", "add", _remote, "--name", "s");
        b.Run("install", "my");

        a.Save("agents", Menu("agents"));
        a.Save("my", Menu("my v2", opens: "agents"));
        a.Run("publish", "my");
        Assert.Equal(0, b.Run("update"));
        Assert.True(File.Exists(Path.Combine(b.Configs, "agents.json")));
    }

    [Fact]
    public void Publish_does_not_replace_a_version_published_elsewhere()
    {
        var a = NewMachine("a");
        a.Save("my", Menu("my"));
        a.Run("store", "add", _remote, "--name", "s");
        a.Run("publish", "my");
        var b = NewMachine("b");
        b.Run("store", "add", _remote, "--name", "s");
        b.Run("install", "my");

        b.Save("my", Menu("my from b"));
        Assert.Equal(0, b.Run("publish", "my"));
        a.Save("my", Menu("my from a, not synced"));
        Assert.Equal(1, a.Run("publish", "my"));
        Assert.Contains("has not seen", a.Err.ToString());
        Assert.Equal(0, a.Run("publish", "my", "--force"));
    }

    [Fact]
    public void A_path_as_the_name_is_refused_and_deletes_nothing()
    {
        var a = NewMachine("a");
        a.Save("my", Menu("my"));
        a.Run("store", "add", _remote, "--name", "s");
        Assert.Equal(1, a.Run("publish", Path.Combine(a.Configs, "my")));
        Assert.Equal(1, a.Run("publish", "./my"));
        Assert.Contains("not a config name", a.Err.ToString());
        Assert.True(File.Exists(Path.Combine(a.Configs, "my.json")));
    }

    [Fact]
    public void A_store_folder_that_is_a_link_is_refused()
    {
        var a = NewMachine("a");
        a.Save("foo", Menu("foo"));
        var evil = Path.Combine(_root, "evil");
        StoreCli.Git(null, "clone", "--quiet", _remote, evil);
        Directory.CreateSymbolicLink(Path.Combine(evil, "sub"), "../../configs");   // from <home>/stores/s: the user's configs
        StoreCli.Git(evil, "add", "-A");
        StoreCli.Git(evil, "commit", "--quiet", "-m", "link");
        StoreCli.Git(evil, "push", "--quiet", "origin", "HEAD");

        Assert.Equal(1, a.Run("store", "add", _remote, "--name", "s", "--path", "sub"));
        Assert.Contains("is a link", a.Err.ToString());
        Assert.True(File.Exists(Path.Combine(a.Configs, "foo.json")));
    }

    [Fact]
    public void Store_names_differing_only_by_case_are_one_name()
    {
        var a = NewMachine("a");
        Assert.Equal(0, a.Run("store", "add", _remote, "--name", "work"));
        Assert.Equal(1, a.Run("store", "add", NewRemote("other"), "--name", "Work"));
    }

    [Fact]
    public void Force_does_not_skip_the_secret_check_but_allow_secrets_does()
    {
        var a = NewMachine("a");
        a.Save("ops", """{ "title": "ops", "menus": { "main": { "items": [ { "name": "x", "action": { "type": "open-url", "url": "https://zoom.us/j/1?pwd=abcdefghijkl" } } ] } } }""");
        a.Run("store", "add", _remote, "--name", "s");
        Assert.Equal(1, a.Run("publish", "ops", "--force"));
        Assert.Equal(0, a.Run("publish", "ops", "--allow-secrets"));
    }

    [Fact]
    public void Unknown_options_are_errors()
    {
        var a = NewMachine("a");
        Assert.Equal(1, a.Run("install", "--stor", "x"));
        Assert.Contains("Unknown option", a.Err.ToString());
    }

    [Fact]
    public void Store_list_and_remove()
    {
        var a = NewMachine("a");
        Assert.Equal(0, a.Run("store", "add", _remote, "--name", "s"));
        Assert.Equal(1, a.Run("store", "add", _remote, "--name", "s"));   // the name is taken
        Assert.Equal(0, a.Run("store", "list"));
        Assert.Contains("s  ", a.Out.ToString());
        Assert.Equal(0, a.Run("store", "remove", "s"));
        Assert.False(Directory.Exists(Path.Combine(a.Home, "stores", "s")));
        Assert.Equal(1, a.Run("install", "x"));
        Assert.Contains("No stores yet", a.Err.ToString());
    }
}
