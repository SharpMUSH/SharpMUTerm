using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>What the pointer is over, as far as anything on screen reacts to it.</summary>
internal enum PointerKind
{
    /// <summary>Nothing that reacts: the header, the status row, blank backdrop.</summary>
    None,

    /// <summary>A pane's output, where a drag selects text.</summary>
    Output,

    /// <summary>A link in a pane's output: a URL, an MXP <c>&lt;SEND&gt;</c>, a <c>PROMPT</c>.</summary>
    Link,

    /// <summary>A tab in a pane's tab strip.</summary>
    Tab,

    /// <summary>A row of the connection sidebar that goes somewhere.</summary>
    RailRow,

    /// <summary>The line between two split panes, which a drag moves.</summary>
    Divider,

    /// <summary>A command line.</summary>
    CommandLine,
}

/// <summary>
/// The pointer a host shows over a target. Named for what it means, not for any platform's cursor, so a
/// window can map it to its own cursors and a terminal to the names OSC 22 takes.
/// </summary>
internal enum PointerShape
{
    Default,

    /// <summary>Text that a drag selects.</summary>
    Text,

    /// <summary>Something a click acts on.</summary>
    Hand,

    /// <summary>A vertical line a drag moves sideways.</summary>
    ResizeColumns,

    /// <summary>A horizontal line a drag moves up and down.</summary>
    ResizeRows,
}

/// <summary>
/// What is under the pointer, worked out by the client, which is the only thing that knows: a host sees
/// cells, and the client knows which of those cells are a link, a tab or a divider. A host shows it in its
/// own way — the window with a cursor and a tooltip, a terminal that understands OSC 22 with a pointer
/// shape — and a host that can show neither shows nothing.
/// </summary>
/// <param name="Kind">What kind of thing it is.</param>
/// <param name="Area">The cells it covers, in desktop cells, so a host can place a tooltip beside it.</param>
/// <param name="Shape">The pointer to show over it.</param>
/// <param name="Tooltip">One or two lines saying what a click does, or null.</param>
/// <param name="Key">Identifies the target, so a host can tell a move within it from a move to another.</param>
internal sealed record PointerTarget(PointerKind Kind, PaneRect Area, PointerShape Shape, string? Tooltip, string Key)
{
    /// <summary>Nothing under the pointer reacts to it.</summary>
    public static PointerTarget Nothing { get; } = new(PointerKind.None, default, PointerShape.Default, null, string.Empty);
}

/// <summary>
/// One entry of a right-click menu. The client builds the menu, because it knows what the place clicked
/// can do; a host only draws it. <see cref="Run"/> is already marshalled onto the client's own thread, so
/// a host may call it from wherever its menu reports the choice.
/// </summary>
/// <param name="Label">What the entry says. <see cref="Separator"/> is a rule, not an entry.</param>
/// <param name="Hint">The key that does the same, when there is one, drawn after the label.</param>
/// <param name="Run">What choosing it does.</param>
/// <param name="Enabled">False draws it dimmed and unclickable, which says the action exists but not here.</param>
internal sealed record PointerMenuItem(string Label, string? Hint, Action Run, bool Enabled = true)
{
    /// <summary>A rule between groups of entries.</summary>
    public static PointerMenuItem Separator { get; } = new("-", null, () => { }, false);

    public bool IsSeparator => ReferenceEquals(this, Separator);
}

/// <summary>
/// A pane's scroll position, for a host that draws a scrollbar over the pane rather than in a cell of it
/// (a cell would narrow the pane, and every connected world is told the pane's width).
/// </summary>
/// <param name="WindowId">The window the pane is showing.</param>
/// <param name="Track">The pane's output rectangle, in desktop cells; the bar runs down its right edge.</param>
/// <param name="ContentRows">Rows of output the pane holds.</param>
/// <param name="ViewportRows">Rows it shows at once.</param>
/// <param name="Offset">Rows hidden above what it shows.</param>
/// <param name="ScrollTo">Scrolls the pane so that many rows are above it; safe to call from any thread.</param>
internal sealed record PaneScrollbar(
    string WindowId, PaneRect Track, int ContentRows, int ViewportRows, int Offset, Action<int> ScrollTo)
{
    /// <summary>Same position, ignoring the callback, which is a fresh closure each time.</summary>
    public bool SameAs(PaneScrollbar other) =>
        WindowId == other.WindowId && Track == other.Track && ContentRows == other.ContentRows
        && ViewportRows == other.ViewportRows && Offset == other.Offset;
}

/// <summary>
/// The host's half of the pointer: what it does with what the client says is under the pointer. Every call
/// comes on the client's own thread; a host that draws on another thread marshals there itself. A member
/// of <see cref="ClientHost"/>, null for a host that shows none of it (the snapshot, the tests).
/// </summary>
internal interface IPointerPresenter
{
    /// <summary>The pointer moved onto something else. <see cref="PointerTarget.Nothing"/> when it left everything.</summary>
    void Hover(PointerTarget target);

    /// <summary>A right-click asked for a menu at a cell. An empty list is never passed.</summary>
    void ShowMenu(System.Drawing.Point cell, IReadOnlyList<PointerMenuItem> items);

    /// <summary>
    /// Whether <see cref="ShowMenu"/> draws anything. Where it does not, the client opens its own chooser for
    /// the one menu a server can ask for, an MXP <c>&lt;SEND&gt;</c> with several commands.
    /// </summary>
    bool DrawsMenus { get; }

    /// <summary>The panes' scroll positions, whenever one of them changes.</summary>
    void Scrollbars(IReadOnlyList<PaneScrollbar> bars);
}
