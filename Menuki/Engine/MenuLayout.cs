namespace Menuki.Engine;

/// <summary>
/// Decides how many menu items fit on screen between the header (info panel, message, title)
/// and the footer (key hints). The whole frame must fit the window: if it is taller, the
/// terminal scrolls and every row the menu repaints lands on the wrong line.
/// </summary>
public static class MenuLayout
{
    public sealed record Fit(int HeaderLines, int FooterLines, int ItemRows);

    /// <param name="height">Window rows, or 0 when unknown (everything is shown).</param>
    /// <param name="headerLines">Lines above the items; the last one is the title, kept as long as possible.</param>
    /// <param name="footerLines">Lines below the items (a blank spacer, then the hints).</param>
    /// <param name="itemCount">Rows the items need (at least 1, for the "No matches" line).</param>
    public static Fit Compute(int height, int headerLines, int footerLines, int itemCount)
    {
        itemCount = Math.Max(1, itemCount);
        if (height <= 0)
            return new Fit(headerLines, footerLines, itemCount);

        // Items get at least half the window (three rows in a tiny one): give up footer lines
        // first, then header lines from the top (info panel, message), the title last.
        var minItems = Math.Min(itemCount, Math.Max(3, height / 2));
        while (headerLines + footerLines + minItems > height && footerLines > 0)
            footerLines--;
        while (headerLines + footerLines + minItems > height && headerLines > 0)
            headerLines--;

        var rows = Math.Max(1, Math.Min(itemCount, height - headerLines - footerLines));
        return new Fit(headerLines, footerLines, rows);
    }
}
