using System.Text.Json;
using Xunit;
using static Menuki.Tests.PtyTerminal;

namespace Menuki.Tests;

/// <summary>Esc on any input step of an input+shell item cancels the item without running it.</summary>
public class InputShellCancelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"menuki-input-cancel-{Guid.NewGuid():N}");
    private string Marker => Path.Combine(_dir, "ran.txt");
    private string History => Path.Combine(_dir, ".menuki", "input_history");
    private string HistoryText => File.Exists(History) ? File.ReadAllText(History) : "";

    public InputShellCancelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A menu with one input+shell item whose command writes the entered values to <see cref="Marker"/>.</summary>
    private string Config(params object[] inputs)
    {
        var names = string.Join(" ", inputs.Select(i => "{" + JsonSerializer.SerializeToElement(i).GetProperty("name").GetString() + "}"));
        var config = new
        {
            title = "Input cancel test",
            start_menu = "main",
            menus = new
            {
                main = new
                {
                    title = "Main",
                    items = new object[]
                    {
                        new
                        {
                            name = "Ask and run",
                            action = new { type = "input+shell", inputs, command_template = $"echo {names} > '{Marker}'" }
                        }
                    }
                }
            }
        };
        var path = Path.Combine(_dir, "main.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        return path;
    }

    private static object Choice(string name) =>
        new { name, type = "choice", prompt = "Pick env", options = new[] { "dev", "staging", "prod" } };

    private static object Text(string name, bool secret = false) =>
        new { name, type = "string", prompt = "Type name", secret };

    [Fact]
    public async Task Esc_in_a_choice_returns_to_the_menu_without_running()
    {
        using var terminal = new PtyTerminal(Config(Choice("env")), _dir);
        await terminal.ExpectMenu("Main");
        terminal.Send(Enter);
        await terminal.ExpectText("Esc = cancel");
        terminal.Send(Down + Escape);
        await terminal.ExpectMenu("Main");
        terminal.Send(Escape);
        await terminal.ExpectExit();
        Assert.False(File.Exists(Marker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Esc_in_a_text_field_returns_to_the_menu_without_running(bool secret)
    {
        using var terminal = new PtyTerminal(Config(Text("name", secret)), _dir);
        await terminal.ExpectMenu("Main");
        terminal.Send(Enter);
        await terminal.ExpectText("Type name");
        terminal.Send("half-typed" + Escape);
        await terminal.ExpectMenu("Main");
        terminal.Send(Escape);
        await terminal.ExpectExit();
        Assert.False(File.Exists(Marker));
        Assert.DoesNotContain("half-typed", HistoryText);
    }

    [Fact]
    public async Task Esc_on_a_later_step_cancels_the_whole_item()
    {
        using var terminal = new PtyTerminal(Config(Choice("env"), Text("name")), _dir);
        await terminal.ExpectMenu("Main");
        terminal.Send(Enter);
        await terminal.ExpectText("Esc = cancel");
        terminal.Send(Down + Enter);
        await terminal.ExpectText("Type name");
        terminal.Send(Escape);
        await terminal.ExpectMenu("Main");
        terminal.Send(Escape);
        await terminal.ExpectExit();
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public async Task Enter_on_every_step_still_runs_the_command()
    {
        using var terminal = new PtyTerminal(Config(Choice("env"), Text("name")), _dir);
        await terminal.ExpectMenu("Main");
        terminal.Send(Enter);
        await terminal.ExpectText("Esc = cancel");
        terminal.Send(Down + Enter);
        await terminal.ExpectText("Type name");
        terminal.Send("abc" + Enter);
        await terminal.ExpectText("Press any key to continue");
        terminal.Send("x");
        await terminal.ExpectMenu("Main");
        terminal.Send(Escape);
        await terminal.ExpectExit();
        Assert.Equal("staging abc", File.ReadAllText(Marker).Trim());
        Assert.Contains("abc", HistoryText);   // proves the history lands in the test's HOME
    }
}
