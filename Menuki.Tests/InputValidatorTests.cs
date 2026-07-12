using Menuki.Config;
using Menuki.Engine;
using Xunit;

namespace Menuki.Tests;

public class InputValidatorTests
{
    private static InputDefinition Input(string type, Action<InputDefinition>? cfg = null)
    {
        var i = new InputDefinition { Name = "x", Prompt = "X", Type = type };
        cfg?.Invoke(i);
        return i;
    }

    [Theory]
    [InlineData("yes", "true")]
    [InlineData("no", "false")]
    [InlineData("1", "true")]
    [InlineData("0", "false")]
    [InlineData("TRUE", "true")]
    public void Boolean_normalizes(string raw, string expected)
    {
        var r = InputValidator.Resolve(Input(InputTypes.Boolean), raw);
        Assert.True(r.Ok);
        Assert.Equal(expected, r.Value);
    }

    [Fact]
    public void Boolean_rejects_garbage()
    {
        var r = InputValidator.Resolve(Input(InputTypes.Boolean), "maybe");
        Assert.False(r.Ok);
    }

    [Fact]
    public void Choice_accepts_option_and_reports_allowed_on_miss()
    {
        var input = Input(InputTypes.Choice, i => i.Options = new() { "staging", "production" });

        Assert.True(InputValidator.Resolve(input, "production").Ok);

        var bad = InputValidator.Resolve(input, "prod");
        Assert.False(bad.Ok);
        Assert.Equal(new[] { "staging", "production" }, bad.Allowed);
    }

    [Theory]
    [InlineData("50", true)]
    [InlineData("1", true)]
    [InlineData("1000", true)]
    [InlineData("0", false)]
    [InlineData("1001", false)]
    [InlineData("abc", false)]
    public void Number_respects_range(string raw, bool ok)
    {
        var input = Input(InputTypes.Number, i => { i.Min = 1; i.Max = 1000; });
        Assert.Equal(ok, InputValidator.Resolve(input, raw).Ok);
    }

    [Theory]
    [InlineData("2026-07-12", true)]
    [InlineData("2026-13-99", false)]
    [InlineData("not-a-date", false)]
    public void Date_validates_format(string raw, bool ok)
    {
        var input = Input(InputTypes.Date, i => i.Format = "yyyy-MM-dd");
        Assert.Equal(ok, InputValidator.Resolve(input, raw).Ok);
    }

    [Fact]
    public void String_pattern_is_enforced()
    {
        var input = Input(InputTypes.String, i => { i.Pattern = "^OPS-[0-9]+$"; i.Example = "OPS-1234"; });
        Assert.True(InputValidator.Resolve(input, "OPS-42").Ok);
        Assert.False(InputValidator.Resolve(input, "bug-42").Ok);
    }

    [Fact]
    public void Missing_required_value_fails_but_default_is_used()
    {
        var noDefault = new InputDefinition { Name = "e", Prompt = "E", Type = InputTypes.String };
        Assert.False(InputValidator.Resolve(noDefault, null).Ok);

        var withDefault = new InputDefinition { Name = "e", Prompt = "E", Default = "dev" };
        var r = InputValidator.Resolve(withDefault, null);
        Assert.True(r.Ok);
        Assert.Equal("dev", r.Value);
    }

    [Fact]
    public void CheckSpec_flags_broken_definitions()
    {
        var choiceNoOptions = Input(InputTypes.Choice);
        Assert.NotEmpty(InputValidator.CheckSpec(choiceNoOptions));

        var badRange = Input(InputTypes.Number, i => { i.Min = 10; i.Max = 1; });
        Assert.NotEmpty(InputValidator.CheckSpec(badRange));

        var badDefault = Input(InputTypes.Number, i => i.Default = "abc");
        Assert.NotEmpty(InputValidator.CheckSpec(badDefault));

        var unknownType = Input("frobnicate");
        Assert.NotEmpty(InputValidator.CheckSpec(unknownType));

        var good = Input(InputTypes.Choice, i => { i.Options = new() { "a", "b" }; i.Default = "a"; });
        Assert.Empty(InputValidator.CheckSpec(good));
    }
}
