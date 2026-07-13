using Menuki.Config;
using Menuki.Engine;
using Xunit;

namespace Menuki.Tests;

public class ThemeTests
{
    static ThemeTests()
    {
        // Redirect persisted settings to a scratch dir so tests never touch the real
        // ~/.menuki/settings.json (SetTheme/Toggle persist).
        var tmp = Path.Combine(Path.GetTempPath(), "menuki-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MENUKI_HOME", tmp);
    }

    [Fact]
    public void Catalog_has_the_expected_builtins_in_order()
    {
        Assert.Equal(
            new[] { "auto", "dark", "light", "ocean", "forest", "matrix", "high-contrast", "synthwave" },
            ThemeCatalog.Names.ToArray());
        Assert.Equal("auto", ThemeCatalog.Names[0]); // default must be first
        Assert.True(ThemeCatalog.IsKnown("ocean"));
        Assert.False(ThemeCatalog.IsKnown("custom")); // custom is not a built-in palette
    }

    [Fact]
    public void Auto_theme_uses_terminal_default_text_for_any_background()
    {
        Assert.Equal(ThemeCatalog.DefaultColor, ThemeCatalog.Get("auto").Text);
    }

    [Fact]
    public void Next_cycles_and_wraps_around()
    {
        var names = ThemeCatalog.Names;
        Assert.Equal("dark", ThemeCatalog.Next("auto", names));
        Assert.Equal("auto", ThemeCatalog.Next("synthwave", names)); // wraps to the first
    }

    [Fact]
    public void Next_from_unknown_starts_at_first()
    {
        Assert.Equal("auto", ThemeCatalog.Next("does-not-exist", ThemeCatalog.Names));
    }

    [Fact]
    public void Get_falls_back_to_auto_for_unknown_name()
    {
        Assert.Equal(ThemeCatalog.Get("auto").Selected, ThemeCatalog.Get("nope").Selected);
    }

    [Fact]
    public void Custom_theme_available_only_when_config_supplies_colors()
    {
        var withColors = new ThemeManager("dark", new ColorScheme { Selected = "Green" });
        Assert.Contains("custom", withColors.AvailableThemes);

        var withoutColors = new ThemeManager("dark", null);
        Assert.DoesNotContain("custom", withoutColors.AvailableThemes);
    }

    [Fact]
    public void Custom_theme_merges_config_colors_over_defaults()
    {
        var mgr = new ThemeManager("dark", new ColorScheme { Selected = "Green" });
        mgr.SetTheme("custom");
        Assert.Equal("Green", mgr.Current.Selected);
        // A field the config did not set falls back to the auto (default) base.
        Assert.Equal(ThemeCatalog.Get("auto").Title, mgr.Current.Title);
    }
}
