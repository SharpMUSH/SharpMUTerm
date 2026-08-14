namespace SharpMUTerm.Tui;

/// <summary>
/// What one tab costs its strip: the cells its name wants, and the cells it spends on everything else.
/// </summary>
/// <param name="NameLength">
/// The visible width of the tab's whole name — the owner prefix a child window carries, and its own
/// title.
/// </param>
/// <param name="TitleLength">
/// The same name without that prefix. It is a separate number because dropping the prefix is the first
/// thing a crowded strip does and the last thing it can do without losing a letter of anybody's name.
/// </param>
/// <param name="Fixed">
/// Everything else the tab occupies: the framework's one-space pad either side, the focus marker, the
/// unread badge, the draft pen, the other-character mark, its <c>×</c>, and the <c>│</c> that follows it.
/// Computed by measuring a rendered title rather than re-deriving it, so the two cannot drift.
/// </param>
/// <param name="Selected">Whether this is the tab its pane is showing.</param>
internal readonly record struct TabCost(int NameLength, int TitleLength, int Fixed, bool Selected);

/// <summary>
/// How much of each tab's name a strip can afford to draw. Pure, and separate from
/// <see cref="TabTitles"/> for the reason every other renderer here is split that way: the rule is the
/// part worth pinning, and it needs no terminal to state.
/// <para>
/// <b>It exists because the framework clips and does not scroll.</b> <c>TabControl</c> draws its tabs
/// left to right from index 0 and writes each one clipped to the header rectangle — there is no
/// first-visible-tab offset anywhere in the control — so a strip narrower than its tabs simply stops
/// drawing, with nothing said. The tabs past the edge are gone, and so is the <c>×</c>, the separator,
/// and the framework's own <c>← →</c> hint, which is only drawn when there is slack left to draw it in.
/// None of that is reachable from here; what <em>is</em> ours is the titles, so the titles shrink.
/// </para>
/// <para>
/// <b>The owner prefix goes first, and goes for everybody.</b> A pane full of one character's captures
/// repeats <c>Corvid - </c> on every tab, which is the cheapest thing in the strip to lose and the most
/// there is of it — dropping it is what turns eight tabs that do not fit into eight tabs that nearly do.
/// It is dropped across the whole strip rather than per tab, so the tabs stay comparable, and what it
/// said is still said twice over: by the chip the tab is painted on, and by the <c>⌁</c> a window
/// belonging to another character wears.
/// </para>
/// <para>
/// <b>Then names share by water-filling, not by proportion.</b> A cap is lowered until the strip fits,
/// and every name longer than the cap is cut to it — so a strip holding <c>Chat</c> beside
/// <c>O-Gatecrashers</c> takes the cells from the long one and leaves the short one whole. Proportional
/// shrinking would take a slice off <c>Chat</c> too, for no gain.
/// </para>
/// <para>
/// <b>The selected tab is spared, and keeps a floor of its own when it cannot be.</b> It is the one the
/// reader is looking at and the one the strip exists to name, so it holds its full title while any other
/// tab still has cells to give — and even in a strip that cannot fit its tabs at all, it keeps enough to
/// be read. A strip where every label including the selected one is three letters and an ellipsis
/// answers none of the questions a strip is for.
/// </para>
/// <para>
/// <b>Only the name shrinks.</b> The badge, the pen, the <c>⌁</c> and the focus <c>▌</c> are facts about
/// the window rather than decoration, and each is one or two cells against a name that is routinely
/// twenty — taking them would save little and cost the strip its meaning.
/// </para>
/// </summary>
internal static class TabStripFit
{
    /// <summary>
    /// The fewest cells of name a background tab is cut to. Below three a name is an initial and an
    /// ellipsis, which identifies nothing; at three there is still a syllable to recognise. It is a floor
    /// rather than a guarantee — a strip narrower than its tabs at the floor is one the framework will
    /// clip, and this cannot prevent that, only postpone it.
    /// </summary>
    internal const int MinimumName = 3;

    /// <summary>
    /// The fewest for the tab the pane is showing. Higher than <see cref="MinimumName"/> on purpose: it
    /// costs at most a few cells — one background tab's worth across the whole strip — and it buys the
    /// one thing the strip must never stop saying.
    /// </summary>
    internal const int MinimumSelectedName = 8;

    /// <summary>
    /// How many cells of name each tab may draw, in the order it was given them. A tab whose budget
    /// equals its <see cref="TabCost.NameLength"/> is not being elided at all, which is the answer for
    /// every strip that already fits; one whose budget is its <see cref="TabCost.TitleLength"/> is losing
    /// its owner prefix and nothing else.
    /// </summary>
    /// <param name="tabs">The strip's tabs, in the order they are drawn.</param>
    /// <param name="stripWidth">The cells the strip has, or zero when it is not yet known.</param>
    internal static IReadOnlyList<int> Budgets(IReadOnlyList<TabCost> tabs, int stripWidth)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var full = tabs.Select(t => t.NameLength).ToArray();

        // A width of zero is a strip that has not been arranged yet, not a strip with no room: eliding
        // against it would cut every title on the first frame and undo it on the second.
        if (tabs.Count == 0 || stripWidth <= 0 || Width(tabs, full) <= stripWidth)
        {
            return full;
        }

        var titles = tabs.Select(t => t.TitleLength).ToArray();
        if (Width(tabs, titles) <= stripWidth)
        {
            return titles;
        }

        return Fill(tabs, stripWidth, spareSelected: true)
               ?? Fill(tabs, stripWidth, spareSelected: false)
               ?? tabs.Select(Floor).ToArray();
    }

    /// <summary>
    /// The largest cap that fits, or null when even the floors do not. Walked down from the longest title
    /// rather than solved for: both the tab count and a name's length are small, and the loop is the
    /// version a reader can check against the rule it implements.
    /// </summary>
    private static int[]? Fill(IReadOnlyList<TabCost> tabs, int stripWidth, bool spareSelected)
    {
        for (var cap = tabs.Max(t => t.TitleLength); cap >= MinimumName; cap--)
        {
            var budgets = tabs
                .Select(t => t.Selected
                    ? (spareSelected ? t.TitleLength : Math.Max(Floor(t), Math.Min(t.TitleLength, cap)))
                    : Math.Min(t.TitleLength, cap))
                .ToArray();

            if (Width(tabs, budgets) <= stripWidth)
            {
                return budgets;
            }
        }

        return null;
    }

    private static int Floor(TabCost tab) =>
        Math.Min(tab.TitleLength, tab.Selected ? MinimumSelectedName : MinimumName);

    private static int Width(IReadOnlyList<TabCost> tabs, IReadOnlyList<int> budgets)
    {
        var width = 0;
        for (var i = 0; i < tabs.Count; i++)
        {
            width += tabs[i].Fixed + budgets[i];
        }

        return width;
    }
}
