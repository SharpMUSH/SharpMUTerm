using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Tui;

/// <summary>
/// The layout below a comfortable size: the rail folds to its strip on its own, and an empty command line is one row
/// rather than the configured height. Both give their room to the output, which is what a narrow terminal is short of.
/// <para>
/// <b>Why 100 columns.</b> A MU* server wraps at 78 or 80. The open rail with its divider and spacer takes about 20
/// columns (more with long names), so a terminal of 100 still leaves the pane the 80 a server wraps to; below that every
/// other row of a room description ends in one stray word, which was the 80×24 finding. The folded strip costs 8, so
/// at 80 the pane is 72 instead of 60.
/// </para>
/// <para>
/// <b>Why 30 rows, for the command line only.</b> The two extra rows of an empty three-row command line are kept for
/// a line that wraps, and they cost output on every frame. At 30 rows the output keeps 24 or more with them, the
/// height servers page for; below it each one is a line of the game that is not shown. Width alone decides the rail:
/// a short terminal has columns to spare.
/// </para>
/// <para>
/// <b>A player's own fold wins until the width next crosses the line.</b> The automatic fold acts only when the width
/// moves from one side of <see cref="NarrowColumns"/> to the other (and once at start), so ⌃B b, ⌃P and the window
/// sidebar's fold button are never undone by a resize that stays on one side. The fold is the same
/// <c>_railCollapsed</c> they toggle, published as <see cref="SidebarModel.Collapsed"/>, so whichever rail is drawn —
/// the cells' or the window's own sidebar — folds, and both hosts agree.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Below this many columns the rail folds itself and an empty command line is one row.</summary>
    internal const int NarrowColumns = 100;

    /// <summary>Below this many rows an empty command line is one row, whatever the width.</summary>
    internal const int ShortRows = 30;

    /// <summary>Which side of <see cref="NarrowColumns"/> the width was last seen on; null before the first look.</summary>
    private bool? _narrowSeen;

    /// <summary>
    /// The width the narrow layout is decided on: the whole window's in cells. In a terminal that is the cells; in a
    /// window that draws its own sidebar it is the cells plus the sidebar (<see cref="IPointerPresenter.WindowColumns"/>),
    /// which a fold of that sidebar does not change — measured on the cells, folding would widen them past the line
    /// and the next resize would unfold it again.
    /// </summary>
    internal int LayoutColumns() => _pointer?.WindowColumns ?? HeaderWidth();

    /// <summary>Whether the layout is narrow: the rail folds itself.</summary>
    internal bool IsNarrow() => LayoutColumns() < NarrowColumns;

    /// <summary>Whether an empty command line is one row: a narrow or a short window.</summary>
    internal bool CompactInput() => IsNarrow() || HeaderHeight() < ShortRows;

    /// <summary>Whether the rail is folded, by the player or by the narrow layout. Internal for tests.</summary>
    internal bool RailCollapsed => _railCollapsed;

    /// <summary>
    /// Folds or unfolds the rail when the width has crossed <see cref="NarrowColumns"/> since it was last looked at,
    /// and on the first look. Called on every resize (and by tests, which have no resize to raise); returns whether it
    /// changed the rail.
    /// </summary>
    internal bool SyncNarrowLayout()
    {
        var narrow = IsNarrow();
        if (_narrowSeen == narrow)
        {
            return false;
        }

        _narrowSeen = narrow;
        if (_railCollapsed == narrow)
        {
            return false;
        }

        _railCollapsed = narrow;
        if (_workspaceRow is not null)
        {
            RebuildPaneArea();
        }

        PublishHostChrome();
        return true;
    }
}
