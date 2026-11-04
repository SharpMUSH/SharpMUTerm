using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Chrome;

/// <summary>
/// One tab of a pane's strip, as data: what the cell strip writes into the tab's title (<c>TabTitles</c>) and what the
/// window's native strip draws, so the two say the same thing about the same window.
/// </summary>
/// <param name="WindowId">The window the tab brings forward.</param>
/// <param name="Name">The window's name as the strip spells it: the owner ahead of a capture's title (<c>Corvid · Chat</c>).</param>
/// <param name="Title">The window's own title, without the owner — what an elided cell tab falls back to.</param>
/// <param name="Selected">Whether it is the strip's front tab.</param>
/// <param name="Unread">Lines that arrived while it was not caught up; zero for none.</param>
/// <param name="Draft">Whether its command line holds an unsent line.</param>
/// <param name="Foreign">Whether it belongs to a character other than the focused one (the cells' <c>⌁</c>).</param>
/// <param name="Closable">Whether it offers a close (the cells' <c>×</c>): the front tab, never the main window.</param>
/// <param name="Owner">The session key of the character it belongs to, or null for a window nobody owns.</param>
public sealed record TabChipModel(
    string WindowId,
    string Name,
    string Title,
    bool Selected,
    int Unread,
    bool Draft,
    bool Foreign,
    bool Closable,
    string? Owner)
{
    /// <summary>The command a click on the tab is the same as: the ⌃P "Go to …" entry for its window.</summary>
    public string Target => Core.Commands.CommandIds.Window(WindowId);

    /// <summary>The tab for <paramref name="window"/>, as the strip of a pane whose focused character is <paramref name="focusedCharacterKey"/>.</summary>
    public static TabChipModel From(WorkspaceWindow window, string? focusedCharacterKey, bool selected, bool closable)
    {
        ArgumentNullException.ThrowIfNull(window);
        var name = window.Kind != WindowKind.Main ? CharacterNames.Window(window.OwnerLabel, window.Title) : window.Title;
        var foreign = focusedCharacterKey is not null
            && window.SessionKey is not null
            && !string.Equals(window.SessionKey, focusedCharacterKey, StringComparison.Ordinal);
        return new TabChipModel(
            window.Id, name, window.Title, selected, Math.Max(0, window.Unread), window.HasUnsentInput, foreign, closable, window.SessionKey);
    }
}

/// <summary>
/// A pane's tab strip, as data (<c>SurfaceCatalog.TabStrip</c>): the tabs in strip order and whether the pane holds the
/// keyboard. The cells write each tab's title from it; the window draws it natively over the strip's row, which the
/// client leaves blank when the host says it draws the strips (<c>HostChrome.Tabs</c>). Value equality, lists included,
/// so an unchanged strip is not published again.
/// </summary>
public sealed record TabStripModel(string PaneId, bool Focused, IReadOnlyList<TabChipModel> Tabs)
{
    /// <summary>
    /// A strip's tabs with the owner prefix dropped when every tab belongs to the same character: <c>Corvid</c> beside
    /// <c>Chat</c>, as the mockup's strip reads, rather than <c>Corvid · Chat</c>, which says the owner the strip's other
    /// tabs, its chip and its pane already say. A strip holding two characters' windows keeps the prefix on each, and so
    /// does a capture alone in a pane (one tab), which is what keeps it traceable once dragged away from its character.
    /// </summary>
    public static IReadOnlyList<TabChipModel> OneOwner(IReadOnlyList<TabChipModel> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (tabs.Count < 2 || tabs[0].Owner is not { } owner || tabs.Any(t => !string.Equals(t.Owner, owner, StringComparison.Ordinal)))
        {
            return tabs;
        }

        return [.. tabs.Select(t => t with { Name = t.Title })];
    }

    /// <summary>The tab a click on <paramref name="windowId"/> names, or null when the strip does not hold it.</summary>
    public TabChipModel? Find(string windowId) => Tabs.FirstOrDefault(t => string.Equals(t.WindowId, windowId, StringComparison.Ordinal));

    public bool Equals(TabStripModel? other) =>
        other is not null
        && string.Equals(PaneId, other.PaneId, StringComparison.Ordinal)
        && Focused == other.Focused
        && Tabs.SequenceEqual(other.Tabs);

    public override int GetHashCode() => HashCode.Combine(PaneId, Focused, Tabs.Count);
}

/// <summary>
/// The band across a pane split by scrolling back (<c>SurfaceCatalog.ScrollBand</c>): how far back the history above it
/// is, in display rows, and the way back to the live tail. The cells draw it as a rule
/// (<c>ScrollSplitBarRenderer</c>); the window draws it natively over that row, its button ending where End does.
/// </summary>
/// <param name="WindowId">The split window.</param>
/// <param name="RowsBack">Display rows below the history viewport: the status row's figure.</param>
public sealed record ScrollBandModel(string WindowId, int RowsBack)
{
    /// <summary>The command the band's button is the same as: ⌃P ▸ Back to live output.</summary>
    public const string LiveCommand = "term:scroll-live";

    /// <summary>The key that returns, named on the button.</summary>
    public const string ReturnKey = "End";

    /// <summary>The key that freezes the split instead.</summary>
    public const string FreezeKey = "⌥F";

    /// <summary>The native band's lead: <c>Scrolled back 120 rows</c>, the count capped as the cells cap it.</summary>
    public string Lead(string count) => $"Scrolled back {count} rows";

    /// <summary>What the native band says beside its lead.</summary>
    public const string Detail = "New lines keep arriving below";

    /// <summary>The native button's words, before its key.</summary>
    public const string Action = "Jump to latest";
}

/// <summary>
/// A tab being dragged off a native strip, as the client resolved it (<c>SurfaceCatalog.TabStrip</c>'s drag): where it
/// would land, in the words the cells' drop preview and status prompt use. The window draws its hint from this — the
/// zone over the target pane, or an insertion mark on the tab's own strip — and never decides a drop itself.
/// </summary>
/// <param name="WindowId">The window the dragged tab brings forward.</param>
/// <param name="SourcePaneId">The pane whose strip the tab was taken from.</param>
/// <param name="TargetPaneId">The pane it would land in, or null when it is over no pane (a release there drops nothing).</param>
/// <param name="Edge">The edge of the target pane it would split toward, or null for a tab in it (or a reorder).</param>
/// <param name="ReorderIndex">Where in its own strip it would go, while it is over that strip; else null.</param>
/// <param name="Zone">The cells the drop would take, in desktop cells: the target pane, or the half of it a split gives; null for none.</param>
/// <param name="Label">What the drop does, as the cells' prompt says it: <c>split pane 2 left</c>, <c>tab in pane 1</c>.</param>
public sealed record TabDragModel(
    string WindowId,
    string SourcePaneId,
    string? TargetPaneId,
    Edge? Edge,
    int? ReorderIndex,
    PaneRect? Zone,
    string Label)
{
    /// <summary>The cells a drop on <paramref name="pane"/> toward <paramref name="edge"/> takes: the half a split gives the new pane, or the whole pane.</summary>
    public static PaneRect ZoneOf(PaneRect pane, Edge? edge) => edge switch
    {
        Core.Workspaces.Edge.Left => new PaneRect(pane.X, pane.Y, Math.Max(1, pane.Width / 2), pane.Height),
        Core.Workspaces.Edge.Right => new PaneRect(pane.X + (pane.Width / 2), pane.Y, pane.Width - (pane.Width / 2), pane.Height),
        Core.Workspaces.Edge.Top => new PaneRect(pane.X, pane.Y, pane.Width, Math.Max(1, pane.Height / 2)),
        Core.Workspaces.Edge.Bottom => new PaneRect(pane.X, pane.Y + (pane.Height / 2), pane.Width, pane.Height - (pane.Height / 2)),
        _ => pane,
    };
}

/// <summary>The steps of a native tab drag, as the window hands them back.</summary>
public enum TabDragPhase
{
    /// <summary>The pointer left the pressed tab with the button held: the drag starts.</summary>
    Begin,

    /// <summary>The pointer moved.</summary>
    Move,

    /// <summary>The button came up: the drop is made where the client resolves it.</summary>
    Drop,

    /// <summary>The drag was abandoned (a right press, the pointer lost).</summary>
    Cancel,
}

/// <summary>
/// One step of a native tab drag: the pane and window it started from, and where the pointer is in desktop cells. The
/// window adds <paramref name="ReorderIndex"/> — the slot among the strip's tabs the pointer is over — only while the
/// pointer is over the tab's own strip, because only the window knows where it drew its tabs; the client checks the cell
/// is on that strip and the slot exists before it uses it.
/// </summary>
public readonly record struct TabDragStep(TabDragPhase Phase, string PaneId, string WindowId, int X, int Y, int? ReorderIndex);
