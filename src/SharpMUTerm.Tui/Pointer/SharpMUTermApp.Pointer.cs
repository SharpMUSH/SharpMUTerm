using System.Runtime.CompilerServices;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The pointer, beyond clicks: what is under it (<see cref="TargetAt"/>), the pane dividers lighting up
/// under it and moving with a drag, the right-click menu, and the scroll positions a host draws scrollbars
/// from. Everything here is decided by the client and shown by the host (<see cref="IPointerPresenter"/>),
/// so the terminal and the window cannot disagree about what a cell is — only about how much of it they can
/// show.
/// <para>
/// <b>The divider cues are cells, and so are in every host.</b> A divider lights up under the pointer and
/// stays lit while it is dragged, in <see cref="WorkspacePalette.RuleHover"/>; a terminal with motion
/// reporting sees that as well as the window does. It is a recolour of a cell already drawn, so it costs
/// no cell and moves no pane rectangle — the NAWS rule every focus cue here keeps.
/// </para>
/// <para>
/// <b>Everything runs on the client's thread.</b> Mouse frames arrive on the driver's input thread and are
/// marshalled here (<see cref="OnPointerFrame"/>) because the answers read arranged layout, which only that
/// thread may touch.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Where a split pane's dividers are, rebuilt with the pane area.</summary>
    private readonly List<DividerSlot> _dividers = new();

    private PointerTarget _hovered = PointerTarget.Nothing;
    private DividerKey? _litDivider;
    private DividerDrag? _dividerDrag;
    private IReadOnlyList<PaneScrollbar> _scrollbars = Array.Empty<PaneScrollbar>();

    /// <summary>One divider between two children of a split, and the control that draws it.</summary>
    private sealed record DividerSlot(HorizontalGridControl Control, SplitNode Split, int Index);

    /// <summary>A divider by what it divides, which survives the rebuild that replaces its control.</summary>
    private readonly record struct DividerKey(SplitNode Split, int Index)
    {
        public bool Equals(DividerKey other) => ReferenceEquals(Split, other.Split) && Index == other.Index;

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Split), Index);
    }

    /// <summary>A divider being dragged.</summary>
    private sealed record DividerDrag(DividerKey Key);

    /// <summary>What the pointer was last over. Internal for tests.</summary>
    internal PointerTarget Hovered => _hovered;

    /// <summary>Whether a divider is lit (hovered or dragged). Internal for tests.</summary>
    internal bool DividerLit => _litDivider is not null;

    /// <summary>A pane divider: the hairline, registered so the pointer can find it, and lit if it is the one under the pointer.</summary>
    private HorizontalGridControl PaneDivider(SplitNode split, int index)
    {
        var key = new DividerKey(split, index);
        var rule = Divider(_litDivider is { } lit && lit.Equals(key));
        _dividers.Add(new DividerSlot(rule, split, index));
        return rule;
    }

    /// <summary>
    /// One mouse frame, on the client's thread: a divider drag, a right-click menu, or a hover. Nothing
    /// here consumes the frame — the framework and the pane-drag tracker see it as they always did.
    /// </summary>
    private void OnPointerFrame(List<MouseFlags> flags, System.Drawing.Point point)
    {
        if (AnyOverlayOpen || _moveMode || _dragActive)
        {
            _dividerDrag = null;
            SetHover(PointerTarget.Nothing);
            return;
        }

        var pressed = flags.Contains(MouseFlags.Button1Pressed);
        var dragged = flags.Contains(MouseFlags.Button1Dragged);

        if (_dividerDrag is { } drag)
        {
            if (flags.Contains(MouseFlags.Button1Released))
            {
                _dividerDrag = null;
                UpdateHover(point);
            }
            else if (pressed)
            {
                // A drag frame, or the bare press a held button repeats every 100 ms: either way, the pointer is here.
                MoveDivider(drag.Key, point);
            }

            return;
        }

        if (flags.Contains(MouseFlags.Button1DoubleClicked) && DividerAt(point) is { } evened)
        {
            if (PaneResize.Even(evened.Split))
            {
                RebuildPaneArea();
            }

            return;
        }

        if (pressed && !dragged && DividerAt(point) is { } grabbed)
        {
            _dividerDrag = new DividerDrag(new DividerKey(grabbed.Split, grabbed.Index));
            return;
        }

        if (flags.Contains(MouseFlags.Button3Pressed))
        {
            ShowPointerMenu(point);
            return;
        }

        UpdateHover(point);
    }

    /// <summary>
    /// One mouse frame through the pointer path, as the driver would deliver it. The test seam, for
    /// <see cref="SimulatePaneClick"/>'s reason: the driver's mouse handler is registered inside <c>Run()</c>.
    /// </summary>
    internal void SimulatePointer(System.Drawing.Point point, params MouseFlags[] flags) => OnPointerFrame([.. flags], point);

    /// <summary>Works out what is at <paramref name="point"/> and tells the host if it changed.</summary>
    private void UpdateHover(System.Drawing.Point point) => SetHover(TargetAt(point));

    private void SetHover(PointerTarget target)
    {
        var divider = _dividerDrag?.Key
            ?? (target.Kind == PointerKind.Divider && DividerAt(target.Area) is { } slot ? new DividerKey(slot.Split, slot.Index) : (DividerKey?)null);
        if (!Nullable.Equals(divider, _litDivider))
        {
            _litDivider = divider;
            RelightDividers();
        }

        if (target.Key == _hovered.Key && target.Tooltip == _hovered.Tooltip)
        {
            return;
        }

        _hovered = target;
        _pointer?.Hover(target);
    }

    /// <summary>Recolours the dividers in place for the one now lit, without rebuilding anything.</summary>
    private void RelightDividers()
    {
        foreach (var slot in _dividers)
        {
            var lit = _litDivider is { } key && key.Equals(new DividerKey(slot.Split, slot.Index));
            slot.Control.BackgroundColor = ToColor(lit ? WorkspacePalette.RuleHover(_theme) : WorkspacePalette.Rule(_theme));
        }

        _window.Invalidate(true);
    }

    /// <summary>Moves a dragged divider's border to the pointer, as far as the panes either side allow.</summary>
    private void MoveDivider(DividerKey key, System.Drawing.Point point)
    {
        var boundary = key.Split.Direction == SplitDirection.Row ? point.X : point.Y;
        if (PaneResize.DragBorder(key.Split, key.Index, boundary, PaneRectsForResize()).Changed)
        {
            RebuildPaneArea();
        }
    }

    /// <summary>Every realised pane's whole rectangle, in desktop cells — what the resize arithmetic measures.</summary>
    private Dictionary<string, PaneRect> PaneRectsForResize() =>
        RealisedPanes().ToDictionary(p => p.PaneId, p => p.Rect, StringComparer.Ordinal);

    /// <summary>The divider whose cells include <paramref name="point"/>, or null.</summary>
    private DividerSlot? DividerAt(System.Drawing.Point point)
    {
        foreach (var slot in _dividers)
        {
            if (BoundsOf(slot.Control) is { } rect && Contains(rect, point))
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>The divider occupying exactly <paramref name="area"/>, or null.</summary>
    private DividerSlot? DividerAt(PaneRect area) => _dividers.FirstOrDefault(slot => BoundsOf(slot.Control) == area);

    /// <summary>
    /// What is at a desktop cell, as the pointer sees it. Internal so a test can ask without a mouse.
    /// </summary>
    internal PointerTarget TargetAt(System.Drawing.Point point)
    {
        if (DividerAt(point) is { } divider && BoundsOf(divider.Control) is { } line)
        {
            var columns = divider.Split.Direction == SplitDirection.Row;
            return new PointerTarget(
                PointerKind.Divider,
                line,
                columns ? PointerShape.ResizeColumns : PointerShape.ResizeRows,
                columns
                    ? "Drag to resize the panes either side · double-click to even them out\n⌥⇧← ⌥⇧→ resize the focused pane"
                    : "Drag to resize the panes above and below · double-click to even them out\n⌥⇧↑ ⌥⇧↓ resize the focused pane",
                $"divider:{RuntimeHelpers.GetHashCode(divider.Split)}:{divider.Index}");
        }

        if (BoundsOf(_rail) is { } rail && Contains(rail, point))
        {
            return RailTargetAt(rail, point);
        }

        foreach (var (paneId, tabs, rect) in RealisedPanes())
        {
            if (Contains(rect, point))
            {
                return PaneTargetAt(paneId, tabs, rect, point);
            }
        }

        foreach (var bar in new[] { _input, _second })
        {
            if (bar.Visible && BoundsOf(bar) is { } box && Contains(box, point))
            {
                return new PointerTarget(PointerKind.CommandLine, box, PointerShape.Text, null, bar == _input ? "input" : "input2");
            }
        }

        return PointerTarget.Nothing;
    }

    private PointerTarget RailTargetAt(PaneRect rail, System.Drawing.Point point)
    {
        if (FrameworkHitTests.LinkAt(_rail, point.X - rail.X, point.Y - rail.Y) is not { } hit)
        {
            return PointerTarget.Nothing;
        }

        var row = new PaneRect(rail.X, point.Y, rail.Width, 1);
        var target = hit.Span.Url;
        string tooltip;
        if (target.StartsWith(RailModel.NoCharactersPrefix, StringComparison.Ordinal))
        {
            tooltip = $"{target[RailModel.NoCharactersPrefix.Length..]} has no characters yet — F5 adds one";
        }
        else if (BuildCatalog().FirstOrDefault(c => c.Id == target) is { } command)
        {
            tooltip = command.Subtitle is { Length: > 0 } subtitle ? $"{command.Title}\n{subtitle}" : command.Title;
        }
        else
        {
            tooltip = hit.Span.Text.Trim();
        }

        return new PointerTarget(PointerKind.RailRow, row, PointerShape.Hand, tooltip, "rail:" + target);
    }

    private PointerTarget PaneTargetAt(string paneId, TabControl tabs, PaneRect rect, System.Drawing.Point point)
    {
        if (point.Y < rect.Y + tabs.TabHeaderHeight)
        {
            var index = FrameworkHitTests.TabAt(tabs, point.X - rect.X);
            var pages = tabs.TabPages;
            if (index < 0 || index >= pages.Count || pages[index].Tag is not string tabWindow)
            {
                return PointerTarget.Nothing;
            }

            // The tab's own columns: the run either side of the pointer the framework says is the same tab.
            var from = point.X - rect.X;
            var to = from;
            while (from > 0 && FrameworkHitTests.TabAt(tabs, from - 1) == index)
            {
                from--;
            }

            while (to + 1 < rect.Width && FrameworkHitTests.TabAt(tabs, to + 1) == index)
            {
                to++;
            }

            return new PointerTarget(
                PointerKind.Tab,
                new PaneRect(rect.X + from, point.Y, to - from + 1, 1),
                PointerShape.Hand,
                TabTooltip(tabWindow, pages[index].IsClosable),
                $"tab:{paneId}:{tabWindow}");
        }

        if (_workspace.Layout.FindPane(paneId)?.ActiveTab is not { } windowId)
        {
            return PointerTarget.Nothing;
        }

        foreach (var control in new[] { _frozenPanes.GetValueOrDefault(windowId), _panes.GetValueOrDefault(windowId) })
        {
            if (control is null || BoundsOf(control) is not { } box || !Contains(box, point))
            {
                continue;
            }

            if (FrameworkHitTests.LinkAt(control, point.X - box.X, point.Y - box.Y) is { } hit)
            {
                var span = new PaneRect(box.X + hit.PaintX + hit.Span.StartCol, point.Y, hit.Span.EndCol - hit.Span.StartCol, 1);
                return new PointerTarget(
                    PointerKind.Link, span, PointerShape.Hand, LinkTooltip(windowId, hit.Span.Url),
                    $"link:{windowId}:{hit.Span.LinkIndex}:{hit.Span.Url}");
            }
        }

        return new PointerTarget(PointerKind.Output, rect, PointerShape.Text, null, "out:" + windowId);
    }

    /// <summary>What clicking a tab does, and the window's whole title, which the strip may have elided.</summary>
    private string TabTooltip(string windowId, bool closable)
    {
        var window = _workspace.FindWindow(windowId);
        var title = Snippet(WindowTitle(windowId));
        var unread = window is { Unread: > 0 } ? $" · {window.Unread} unread" : string.Empty;
        var actions = closable ? "click to show it · drag it onto another pane · × closes it" : "click to show it · drag it onto another pane";
        return $"{title}{unread}\n{actions}";
    }

    /// <summary>
    /// What clicking a pane's link does, said before it is done. For a link a server wrote, that is the
    /// whole point: a URL is shown in full before anything opens it, and a <c>&lt;SEND&gt;</c> says what it
    /// sends and to whom.
    /// </summary>
    private string? LinkTooltip(string windowId, string payload)
    {
        switch (LinkPayload.Parse(payload))
        {
            case (LinkAction.Send, var command):
                return WindowSession(windowId) is { } session
                    ? $"Sends to {SessionTitle(session)}:\n{Snippet(command)}"
                    : $"Would send, but this window has no connection:\n{Snippet(command)}";
            case (LinkAction.Prompt, var text):
                return $"Puts this on the command line:\n{Snippet(text)}";
            case (LinkAction.Web, var url) when windowId == WebWindowId:
                return $"Opens here:\n{Snippet(url)}";
            case (LinkAction.Web, var url):
                return _openUrl is null ? $"No browser is set up to open:\n{Snippet(url)}" : $"Opens in your browser:\n{Snippet(url)}";
            default:
                return null;
        }
    }

    /// <summary>
    /// The right-click menu for whatever is at <paramref name="point"/>, handed to the host to draw. The
    /// entries are the ⌃P surface's own commands wherever one exists, so a menu cannot offer an action the
    /// keyboard does not have, and what it says beside each is the key that does the same.
    /// </summary>
    private void ShowPointerMenu(System.Drawing.Point point)
    {
        if (_pointer is null)
        {
            return;
        }

        var items = PointerMenu(point);
        if (items.Count > 0)
        {
            _pointer.ShowMenu(point, items);
        }
    }

    /// <summary>The menu <see cref="ShowPointerMenu"/> would show. Internal so a test can read it.</summary>
    internal IReadOnlyList<PointerMenuItem> PointerMenu(System.Drawing.Point point)
    {
        var target = TargetAt(point);
        var items = new List<PointerMenuItem>();
        switch (target.Kind)
        {
            case PointerKind.Divider when DividerAt(point) is { } divider:
                items.Add(new PointerMenuItem("Even out these panes", "double-click", () => OnUiThread(() =>
                {
                    if (PaneResize.Even(divider.Split))
                    {
                        RebuildPaneArea();
                    }
                })));
                break;

            case PointerKind.RailRow:
                var railTarget = target.Key["rail:".Length..];
                if (railTarget.StartsWith(CommandIds.CharacterPrefix, StringComparison.Ordinal) ||
                    railTarget.StartsWith(CommandIds.WindowPrefix, StringComparison.Ordinal))
                {
                    var go = BuildCatalog().FirstOrDefault(c => c.Id == railTarget);
                    items.Add(new PointerMenuItem(go?.Title ?? "Go to it", null, () => OnUiThread(() => DispatchRailTarget(railTarget))));
                }

                if (railTarget.StartsWith(CommandIds.CharacterPrefix, StringComparison.Ordinal))
                {
                    items.Add(new PointerMenuItem("Connect", "⌥R", () => OnUiThread(() =>
                    {
                        DispatchRailTarget(railTarget);
                        DispatchCommand("world:reconnect");
                    })));
                    items.Add(new PointerMenuItem("Disconnect", "⌥D", () => OnUiThread(() =>
                    {
                        DispatchRailTarget(railTarget);
                        DispatchCommand("world:disconnect");
                    })));
                }

                break;

            case PointerKind.Tab:
                var tabWindow = target.Key.Split(':', 3)[2];
                Activate(tabWindow);
                if (tabWindow != MainWindowId)
                {
                    items.Add(new PointerMenuItem("Close tab", "⌃W", () => OnUiThread(() => CloseWindow(tabWindow))));
                }

                AddCommands(items, "layout:next-tab", null, "layout:split-right", "layout:split-down", "layout:zoom", "layout:unzoom", "layout:close");
                break;

            case PointerKind.Link:
            case PointerKind.Output:
                var paneId = PaneAt(point);
                if (paneId is not null && paneId != _workspace.Layout.FocusedPaneId)
                {
                    FocusPane(paneId);
                }

                if (target.Kind == PointerKind.Link)
                {
                    AddLinkItems(items, target);
                    items.Add(PointerMenuItem.Separator);
                }

                AddCommands(
                    items,
                    "term:copy", null,
                    "layout:split-right", "layout:split-down", "layout:zoom", "layout:unzoom", "layout:close", null,
                    "term:freeze", "term:unfreeze", "term:scroll-live", "term:clear", "term:timestamps-on", "term:timestamps-off");
                break;

            case PointerKind.CommandLine:
                AddCommands(items, "term:copy", "term:compose", "term:history", "term:newline");
                break;
        }

        return Tidy(items);
    }

    private void AddLinkItems(List<PointerMenuItem> items, PointerTarget target)
    {
        var parts = target.Key.Split(':', 4);
        var (windowId, payload) = (parts[1], parts[3]);
        var (action, value) = LinkPayload.Parse(payload);
        var label = action switch
        {
            LinkAction.Send => "Send it",
            LinkAction.Prompt => "Put it on the command line",
            LinkAction.Web when windowId == WebWindowId => "Open it here",
            LinkAction.Web => "Open in browser",
            _ => null,
        };
        if (label is not null)
        {
            items.Add(new PointerMenuItem(label, "click", () => OnUiThread(() => OnLinkClicked(windowId, payload))));
        }

        if (value.Length > 0)
        {
            items.Add(new PointerMenuItem(
                action == LinkAction.Web ? "Copy link address" : "Copy the command",
                null,
                () => OnUiThread(() =>
                {
                    if (_clipboard is null)
                    {
                        RefuseCommand("this client has no clipboard to copy to");
                        return;
                    }

                    _clipboard(value);
                    Notice($"copied {Snippet(value)}", MessageSeverity.Info);
                }),
                _clipboard is not null));
        }
    }

    /// <summary>Adds the ⌃P entries with these ids that the surface lists right now, in this order; null is a rule.</summary>
    private void AddCommands(List<PointerMenuItem> items, params string?[] ids)
    {
        var catalog = BuildCatalog();
        foreach (var id in ids)
        {
            if (id is null)
            {
                items.Add(PointerMenuItem.Separator);
                continue;
            }

            if (catalog.FirstOrDefault(c => c.Id == id) is { } command)
            {
                items.Add(new PointerMenuItem(command.Title, ShortHint(command.Subtitle), () => OnUiThread(() => DispatchCommand(id))));
            }
        }
    }

    /// <summary>
    /// The key a catalog subtitle names, when its first part is one (<c>⌃B |</c>, <c>⌥R</c>, <c>F1</c>); null
    /// when it describes rather than names (<c>Aetherfall · connected</c>), since a menu has room for a key only.
    /// </summary>
    internal static string? ShortHint(string? subtitle)
    {
        var first = subtitle?.Split(" · ", 2)[0].Trim();
        if (first is not { Length: > 0 and <= 12 })
        {
            return null;
        }

        var key = first[0] is '⌃' or '⌥' or '⇧' or '^'
            || first.StartsWith("Alt+", StringComparison.Ordinal)
            || first.StartsWith("Ctrl+", StringComparison.Ordinal)
            || (first.Length is 2 or 3 && first[0] == 'F' && char.IsDigit(first[1]));
        return key ? first : null;
    }

    /// <summary>No rule at either end and never two together.</summary>
    private static List<PointerMenuItem> Tidy(List<PointerMenuItem> items)
    {
        var tidy = new List<PointerMenuItem>(items.Count);
        foreach (var item in items)
        {
            if (item.IsSeparator && (tidy.Count == 0 || tidy[^1].IsSeparator))
            {
                continue;
            }

            tidy.Add(item);
        }

        if (tidy.Count > 0 && tidy[^1].IsSeparator)
        {
            tidy.RemoveAt(tidy.Count - 1);
        }

        return tidy;
    }

    /// <summary>The pane whose rectangle holds <paramref name="point"/>, or null.</summary>
    private string? PaneAt(System.Drawing.Point point) =>
        RealisedPanes().FirstOrDefault(p => Contains(p.Rect, point)).PaneId;

    /// <summary>
    /// Tells the host where each pane is scrolled to, when any of that changed. Rides the frame, for
    /// <see cref="ReportPaneSizes"/>' reason: the numbers exist only once a layout is arranged.
    /// </summary>
    private void PublishScrollbars()
    {
        if (_pointer is null)
        {
            return;
        }

        var outputs = PaneOutputRects();
        var bars = new List<PaneScrollbar>(outputs.Count);
        foreach (var (paneId, rect) in outputs)
        {
            if (_workspace.Layout.FindPane(paneId) is not { ActiveTab: { } windowId } pane
                || pane.Frozen
                || _paneScrolls.GetValueOrDefault(windowId) is not { } panel)
            {
                continue;
            }

            bars.Add(new PaneScrollbar(
                windowId, rect, panel.TotalContentHeight, panel.ViewportHeight, panel.VerticalScrollOffset,
                offset => OnUiThread(() =>
                {
                    if (_paneScrolls.GetValueOrDefault(windowId) is { } live && live.VerticalScrollOffset != offset)
                    {
                        live.ScrollVerticalBy(offset - live.VerticalScrollOffset);
                        SyncScrollbackState(windowId);
                    }
                })));
        }

        if (bars.Count == _scrollbars.Count && bars.Zip(_scrollbars).All(pair => pair.First.SameAs(pair.Second)))
        {
            return;
        }

        _scrollbars = bars;
        _pointer.Scrollbars(bars);
    }

    /// <summary>A control's arranged rectangle in desktop cells, or null before it has been arranged.</summary>
    private PaneRect? BoundsOf(IWindowControl control)
    {
        if (_window.GetLayoutNode(control) is not { } node || node.AbsoluteBounds.Width <= 0)
        {
            return null;
        }

        var origin = ContentOrigin();
        var bounds = node.AbsoluteBounds;
        return new PaneRect(origin.X + bounds.X, origin.Y + bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool Contains(PaneRect rect, System.Drawing.Point point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
}
