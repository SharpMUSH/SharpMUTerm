using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// How tall a panel is drawn, in rows of content. Most panels share the window like any pane; a <em>strip</em> is
/// one row however much room there is (the vitals meters, the quick bar), and an editor has a floor below which it
/// stops being one (the docked composer).
/// <para>
/// The vitals panel has a second shape: in a pane narrower than <see cref="CardBelowWidthShare"/> of the window (a
/// side column) it is a <em>card</em>, one meter over another, <see cref="VitalsCardRows"/> tall — the shape the
/// window's native gauges stack in and the cells draw name over bar. The shape follows from where the pane sits, so
/// it is derived on every layout like the rest of this, and a layout of the player's own gets it too.
/// </para>
/// </summary>
public static class PanelSizing
{
    /// <summary>
    /// A vitals pane taking less than this fraction of the window's width (by the split weights above it) is a card
    /// rather than a strip: a strip wants the width a card has no use for.
    /// </summary>
    public const double CardBelowWidthShare = 0.5;

    /// <summary>
    /// The rows of content a vitals card takes for <paramref name="meters"/> meters: its heading and a blank (the card
    /// draws no tab strip, so it titles itself), two a meter (name and numbers, then the bar), a rule and two for the
    /// target, which is reserved whether or not the game names one (a target arriving never reflows the column), and a
    /// row under it. Twenty-pixel rows are what the window's stacked, headed gauges need for the same blocks.
    /// </summary>
    public static int VitalsCardRows(int meters) => (2 * Math.Max(0, meters)) + 6;

    /// <summary>
    /// The rows a quick bar takes where the host draws it as a strip of buttons (<c>HostChrome.Strips</c>, the window):
    /// three twenty-pixel rows hold the mockups' 34-pixel buttons with their padding. Everywhere else it is one row.
    /// </summary>
    public const int QuickBarStripRows = 3;

    /// <summary>The rows of content a docked composer is never squeezed below: its header, four rows of post and its key hints.</summary>
    public const int ComposerMinRows = 6;

    /// <summary>The exact rows of content <paramref name="panelId"/> draws, or null when it shares the window.</summary>
    public static int? FixedRows(string? panelId) => panelId switch
    {
        PanelIds.Vitals => 1,
        PanelIds.QuickBar => 1,
        _ => null,
    };

    /// <summary>The fewest rows of content <paramref name="panelId"/> is given, or null for the ordinary floor.</summary>
    public static int? MinRows(string? panelId) => panelId switch
    {
        PanelIds.Composer => ComposerMinRows,
        _ => null,
    };

    /// <summary>
    /// Whether a pane holding <paramref name="panels"/> (one entry per tab: its panel id, or null for a window no
    /// panel draws) is a strip: every tab a fixed-height panel. A strip with one tab shows no tab strip, because
    /// its content already says what it is.
    /// </summary>
    public static bool IsStrip(IReadOnlyList<string?> panels) =>
        panels.Count > 0 && panels.All(p => FixedRows(p) is not null);

    /// <summary>
    /// Whether a pane holding <paramref name="panels"/> draws its tab strip: not when it holds one panel and nothing
    /// else. Every panel titles itself (COMPOSE, HERE, the vitals heading, the map's room; a strip's row says what it
    /// is), so a strip over it would say the same thing twice and cost a row; a pane holding windows, or several tabs,
    /// keeps it.
    /// </summary>
    public static bool ShowsTabStrip(IReadOnlyList<string?> panels) => !(panels.Count == 1 && panels[0] is not null);

    /// <summary>
    /// Sets <paramref name="pane"/>'s <see cref="PaneNode.FixedRows"/> and <see cref="PaneNode.MinRows"/> from what it
    /// holds. <paramref name="panelOf"/> names the panel each tab draws (null for an ordinary window), and
    /// <paramref name="tabStripRows"/> is the height of the tab strip the host draws over a pane that shows one.
    /// A strip is as tall as its tallest panel (plus the tab strip when it holds several); any other pane shares,
    /// with a floor from the tallest floor among its panels.
    /// </summary>
    /// <param name="pane">The pane.</param>
    /// <param name="panelOf">The panel each tab draws, or null.</param>
    /// <param name="tabStripRows">The height of the tab strip the host draws.</param>
    /// <param name="vitalsCardRows">
    /// Non-null when a vitals tab here is drawn as a card of that many rows (<see cref="ApplyAll"/> decides); null for
    /// the one-row strip.
    /// </param>
    /// <param name="quickBarRows">The rows a quick bar takes: 1, or <see cref="QuickBarStripRows"/> where the host draws buttons.</param>
    public static void Apply(PaneNode pane, Func<string, string?> panelOf, int tabStripRows, int? vitalsCardRows = null, int quickBarRows = 1)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(panelOf);
        var panels = pane.Tabs.Select(panelOf).ToList();
        int? Rows(string? panel) =>
            vitalsCardRows is { } card && string.Equals(panel, PanelIds.Vitals, StringComparison.Ordinal) ? card
            : string.Equals(panel, PanelIds.QuickBar, StringComparison.Ordinal) ? Math.Max(1, quickBarRows)
            : FixedRows(panel);

        var strip = panels.Count > 0 && panels.All(p => Rows(p) is not null);
        var chrome = ShowsTabStrip(panels) ? Math.Max(0, tabStripRows) : 0;
        pane.DrawsHeader = chrome > 0;

        if (strip)
        {
            pane.FixedRows = panels.Max(p => Rows(p)!.Value) + chrome;
            pane.MinRows = null;
            return;
        }

        pane.FixedRows = null;
        var floor = panels.Select(MinRows).Where(m => m is not null).Select(m => m!.Value).DefaultIfEmpty(0).Max();
        pane.MinRows = floor > 0 ? floor + chrome : null;
    }

    /// <summary>
    /// <see cref="Apply"/> for every pane under <paramref name="root"/>, with each vitals pane's shape decided from its
    /// share of the window's width (<see cref="CardBelowWidthShare"/>). <paramref name="vitalsMetersOf"/> is how many
    /// meters the character behind a vitals window has, by window id.
    /// </summary>
    /// <param name="quickBarRows">The rows a quick bar takes (see <see cref="Apply"/>).</param>
    public static void ApplyAll(LayoutNode root, Func<string, string?> panelOf, int tabStripRows, Func<string, int> vitalsMetersOf, int quickBarRows = 1)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(vitalsMetersOf);
        foreach (var (pane, share) in WidthShares(root, 1.0))
        {
            int? card = null;
            if (share < CardBelowWidthShare
                && pane.Tabs.FirstOrDefault(t => string.Equals(panelOf(t), PanelIds.Vitals, StringComparison.Ordinal)) is { } vitals)
            {
                card = VitalsCardRows(vitalsMetersOf(vitals));
            }

            Apply(pane, panelOf, tabStripRows, card, quickBarRows);
        }
    }

    /// <summary>Each pane with the fraction of the window's width its row splits give it.</summary>
    public static IEnumerable<(PaneNode Pane, double Share)> WidthShares(LayoutNode node, double share)
    {
        if (node is PaneNode pane)
        {
            yield return (pane, share);
            yield break;
        }

        var split = (SplitNode)node;
        var total = split.Sizes.Sum(s => Math.Max(0, s));
        for (var i = 0; i < split.Children.Count; i++)
        {
            var part = split.Direction == SplitDirection.Row && total > 0 ? Math.Max(0, split.Sizes[i]) / total : 1.0;
            foreach (var inner in WidthShares(split.Children[i], share * part))
            {
                yield return inner;
            }
        }
    }
}
