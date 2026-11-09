using Menuki.Engine;
using Xunit;

namespace Menuki.Tests;

public class MenuLayoutTests
{
    [Theory]
    [InlineData(8, 2, 2, 4)] // Exactly enough room for the entire frame.
    [InlineData(12, 2, 2, 4)]
    public void A_window_that_fits_the_menu_shows_every_item(int height, int header, int footer, int items)
    {
        Assert.Equal(new MenuLayout.Fit(header, footer, items),
            MenuLayout.Compute(height, header, footer, items));
    }

    [Fact]
    public void One_row_below_the_exact_fit_reduces_visible_items()
    {
        var fit = MenuLayout.Compute(7, 2, 2, 4);

        Assert.Equal(new MenuLayout.Fit(2, 2, 3), fit);
    }

    [Theory]
    [InlineData(12, 6, 0, 6)] // Footer goes first.
    [InlineData(6, 3, 0, 3)]  // Then the top header lines go.
    [InlineData(3, 0, 0, 3)]
    [InlineData(2, 0, 0, 2)]
    [InlineData(1, 0, 0, 1)]
    public void A_short_window_keeps_item_rows_within_its_height(
        int height, int expectedHeader, int expectedFooter, int expectedItems)
    {
        var fit = MenuLayout.Compute(height, 6, 4, 20);

        Assert.Equal(new MenuLayout.Fit(expectedHeader, expectedFooter, expectedItems), fit);
        Assert.InRange(fit.ItemRows, 1, 19);
        Assert.True(fit.HeaderLines + fit.FooterLines + fit.ItemRows <= height);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 1)]
    [InlineData(1, 0, 1)]
    public void An_empty_menu_reserves_one_no_matches_row(int height, int footer, int rows)
    {
        var fit = MenuLayout.Compute(height, 0, footer, 0);

        Assert.Equal(rows, fit.ItemRows);
        Assert.Equal(0, fit.HeaderLines);
        Assert.Equal(footer, fit.FooterLines);
    }
}
