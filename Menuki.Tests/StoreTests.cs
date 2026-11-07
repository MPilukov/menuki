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
