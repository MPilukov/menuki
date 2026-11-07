using Menuki.Engine;
using Xunit;

namespace Menuki.Tests;

public class KeyboardLayoutTests
{
    [Theory]
    [InlineData("кузщ", "repo")]
    [InlineData("КУЗЩ", "REPO")]
    [InlineData("ёхъжэбю", "`[];',.")]
    [InlineData("rузщ 1", "repo 1")]   // mixed: only the Russian letters move
    public void Russian_letters_become_the_english_keys(string typed, string expected) =>
        Assert.Equal(expected, KeyboardLayout.AsLatin(typed));

    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("GHBDTN", "ПРИВЕТ")]
    [InlineData("`[];',.", "ёхъжэбю")]
    public void English_keys_become_the_russian_letters(string typed, string expected) =>
        Assert.Equal(expected, KeyboardLayout.AsCyrillic(typed));

    [Theory]
    [InlineData("Repos sync", "repo", true)]
    [InlineData("Repos sync", "кузщ", true)]
    [InlineData("Repos sync", "КУЗЩ", true)]
    [InlineData("Привет мир", "ghbdtn", true)]
    [InlineData("Привет мир", "привет", true)]
    [InlineData("Deploy", "кузщ", false)]
    [InlineData("Deploy", "", true)]
    public void Matches_the_query_as_typed_or_in_the_other_layout(string name, string query, bool expected) =>
        Assert.Equal(expected, KeyboardLayout.Matches(name, query));

    [Fact]
    public void Every_key_maps_back()
    {
        const string all = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>";
        Assert.Equal(all, KeyboardLayout.AsLatin(KeyboardLayout.AsCyrillic(all)));
    }
}
