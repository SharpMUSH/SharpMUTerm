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
/// <param name="Checked">Whether the entry is the one in force (the layout in use), drawn with a check by a host that can.</param>
internal sealed record PointerMenuItem(string Label, string? Hint, Action Run, bool Enabled = true, bool Checked = false)
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

    /// <summary>
    /// The panels on screen that a host may draw natively over their cells, whenever one moves or its content
    /// changes. The cells stay what they are; a host that draws no native panels ignores this (the default).
    /// </summary>
    void Panels(IReadOnlyList<PanelOverlay> panels)
    {
    }

    /// <summary>
    /// The actions a host may offer as native buttons along the header, said once when the app starts. A host
    /// with no buttons ignores this (the default); every action is also a key and a ⌃P entry.
    /// </summary>
    void HeaderActions(IReadOnlyList<HeaderAction> actions)
    {
    }

    /// <summary>
    /// The header band and the connection sidebar, as data, whenever either moves or what it says changes. The cells
    /// stay what they are; a host that draws no native chrome ignores this (the default).
    /// </summary>
    void Chrome(ChromeOverlay chrome)
    {
    }

    /// <summary>
    /// How many columns a terminal the size of the host's whole window would have: its cells plus the chrome it draws
    /// beside them, in cells. Null when the cells are the whole window (the terminal, and the default). The client's
    /// narrow layout measures this rather than the cells (<c>SharpMUTermApp.LayoutColumns</c>), because folding a
    /// sidebar the host draws gives the cells its width: measured on the cells, the fold would undo itself.
    /// </summary>
    int? WindowColumns => null;

    /// <summary>
    /// A notice was said, as it is drawn on the status row. The status row's cells carry it in every host; a host that
    /// draws notices natively (the window's toasts) shows this over the cells and ignores nothing else. It must never
    /// take focus. A host that draws none ignores this (the default).
    /// </summary>
    void Notice(NoticeOverlay notice)
    {
    }
}

/// <summary>
/// One panel on screen, for a host that draws it natively over its pane rather than in cells: where it is and what
/// it shows. The cells under it are drawn as in any host, so a host that draws nothing loses nothing, and the
/// terminal-and-window parity, which compares cells, is unaffected.
/// </summary>
/// <param name="PanelId">The panel (<c>vitals</c>, <c>map</c>, …; <c>PanelIds</c>).</param>
/// <param name="WindowId">The window the panel is drawn in, which is the pane's front tab.</param>
/// <param name="Rect">The pane's output rectangle, in desktop cells — the area the cells of the panel cover.</param>
/// <param name="Ink">The colours the cells are drawn in, so a native view says the same things in the same hues.</param>
/// <param name="Vitals">For the vitals panel, its content: the same model its cell strip is drawn from.</param>
/// <param name="Map">For the map, the rooms around the character, drawn from the recorder the cell map is drawn from.</param>
/// <param name="WalkTo">
/// For the map, walks the panel's own character to a room by id, through the speedwalk a click on a cell map uses.
/// Safe to call from any thread. The same instance for as long as the window lives, so an overlay compares equal.
/// </param>
/// <param name="WhoHere">For the who-here panel, its content: the same model its cell rows are drawn from.</param>
/// <param name="QuickBar">For the quick bar, its content: the same model its cell row is drawn from.</param>
/// <param name="Pick">
/// For who-here and the quick bar, does what a click on row or item N of the cells does, on the panel's own
/// character: who-here puts <c>look</c> and the name on that character's command line, the quick bar runs the
/// binding through the key's own path. The index is into the model this overlay carries; the client reads the row
/// back from what it published, so the host never names a person or a command. Safe to call from any thread, and
/// the same instance for as long as the window lives.
/// </param>
/// <param name="Dispatch">
/// Runs a command id through the client's registry (the quick bar's <c>F4 edits keys</c> is
/// <see cref="SharpMUTerm.Client.Panels.QuickBarModel.EditCommand"/>). Safe to call from any thread; one instance.
/// </param>
/// <param name="Composer">
/// For the docked composer, its content: the model its cell header, editor and key row are drawn from. Only for a
/// host with a native composer (<see cref="ClientHost.NativeComposer"/>).
/// </param>
/// <param name="ComposerSession">
/// For the docked composer, the way a native editor's text and keys go back to the client: one instance for as long as
/// the panel lives, so an overlay compares equal from frame to frame.
/// </param>
/// <param name="PopOut">
/// Pops the panel out into a window of its own (<c>PopOutPanel</c>, what ⌃P's <c>panel:popout:</c> entry runs), for a
/// host that can float a panel (<see cref="ClientHost.FloatingPanels"/>); null elsewhere, and for a panel that cannot
/// float. Safe to call from any thread; one instance per window.
/// </param>
internal sealed record PanelOverlay(
    string PanelId,
    string WindowId,
    PaneRect Rect,
    PanelInk Ink,
    SharpMUTerm.Client.Panels.VitalsModel? Vitals,
    SharpMUTerm.Client.Panels.MapViewModel? Map = null,
    Action<long>? WalkTo = null,
    SharpMUTerm.Client.Panels.WhoHereModel? WhoHere = null,
    SharpMUTerm.Client.Panels.QuickBarModel? QuickBar = null,
    Action<int>? Pick = null,
    Action<string>? Dispatch = null,
    SharpMUTerm.Client.Composer.ComposerModel? Composer = null,
    SharpMUTerm.Client.Composer.ComposerSession? ComposerSession = null,
    Action? PopOut = null);

/// <summary>A panel's colours as <c>#rrggbb</c>: a bar or fill, the warning, and the empty part of a bar.</summary>
internal sealed record PanelInk(string Bar, string Low, string Track);

/// <summary>
/// An action a host may draw as a native button in the header, over blank header cells. Its menu is built by the
/// client, on the client's thread, from the same commands its key runs — a host never builds one of its own.
/// </summary>
/// <param name="Id">Which action (<see cref="LayoutsId"/>); a host picks its icon by this.</param>
/// <param name="Label">What it is, for a tooltip and a screen reader.</param>
/// <param name="Hint">The key that does the same, for the tooltip.</param>
/// <param name="Menu">
/// Builds the menu; safe to call from any thread. The client builds it on its own thread and hands it to the
/// callback there, so a host marshals in the callback itself.
/// </param>
/// <param name="Dispatch">
/// Runs an entry by the command id the menu carried; safe to call from any thread. An id the last menu built did
/// not offer is refused, so a host can choose an entry and never name a command of its own.
/// </param>
internal sealed record HeaderAction(
    string Id,
    string Label,
    string? Hint,
    Action<Action<SharpMUTerm.Client.Commands.MenuModel>> Menu,
    Action<string> Dispatch)
{
    /// <summary>The layouts button: built-in and saved layouts, save, and a default for a world or a character.</summary>
    public const string LayoutsId = "layouts";
}
