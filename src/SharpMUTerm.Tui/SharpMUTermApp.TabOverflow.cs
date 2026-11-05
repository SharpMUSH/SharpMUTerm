using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// The tab strip's overflow chip (<see cref="SurfaceCatalog.TabOverflow"/>). Past its floors a strip cannot draw every
/// tab (<see cref="TabStripFit"/>): the framework scrolls it so the tab in front shows, and the rest are simply not
/// there. The chip says how many are missing — <c>+3 ▾</c> at the strip's right edge — and a click on it lists them:
/// a cell list in a terminal, a native menu in the window. Choosing one goes to that window through the registry
/// (<c>win:&lt;id&gt;</c>, the ⌃P "Go to …" entry, which is also the keyboard's way to every hidden tab).
/// <para>
/// <b>No cell is spent.</b> The chip is painted after the frame over the strip's own last cells, which are the clipped
/// end of a tab the framework could not finish drawing anyway; the strip, the pane and NAWS do not move. Which tabs are
/// hidden is read off the framework's own hit test of the strip it just painted (<see cref="FrameworkHitTests.TabAt"/>),
/// so the chip never disagrees with what is on screen.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Each overflowing strip's list and where its chip is, in desktop cells; rebuilt every frame.</summary>
    private readonly Dictionary<string, (MenuModel Menu, PaneRect Chip)> _tabOverflow = new(StringComparer.Ordinal);

    /// <summary>The commands the last overflow list offered, so a choice can be checked against them.</summary>
    private MenuModel? _tabOverflowOffered;

    /// <summary>The cells at a strip's right edge a chip takes, for deciding which clipped tab it covers: a space and <c>+9 ▾</c>.</summary>
    private const int ChipReserve = 5;

    /// <summary>The chip for a list: <c>+3 ▾</c>.</summary>
    internal static string TabOverflowChip(MenuModel menu) => $"+{menu.Entries.Count} ▾";

    /// <summary>Each overflowing pane's list of hidden tabs, by pane id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, MenuModel> TabOverflowMenus =>
        _tabOverflow.ToDictionary(p => p.Key, p => p.Value.Menu, StringComparer.Ordinal);

    /// <summary>Where a pane's chip is, in desktop cells, or null when its strip fits. Internal for tests.</summary>
    internal PaneRect? TabOverflowChipAt(string paneId) => _tabOverflow.TryGetValue(paneId, out var o) ? o.Chip : null;

    /// <summary>
    /// Finds the strips that drew fewer tabs than they hold, records each one's list of hidden tabs as drawn, and paints
    /// its chip over the strip's last cells. On every frame, after the paint, beside the header dividers.
    /// </summary>
    private void PaintTabOverflow(SharpConsoleUI.Layout.CharacterBuffer buffer)
    {
        try
        {
            PaintTabOverflowChips(buffer);
        }
        finally
        {
            // Published whole, for the driver's input thread (TakesTabOverflowPress), which must not read the dictionary
            // this frame is rebuilding.
            _tabOverflowChips = [.. _tabOverflow.Values.Select(o => o.Chip)];
        }
    }

    /// <summary>Where every chip is, in desktop cells, as the last frame painted them; replaced whole, read on the input thread.</summary>
    private volatile PaneRect[] _tabOverflowChips = [];

    /// <summary>
    /// Takes a left-button frame on a chip away from the framework, on the driver's input thread before the framework's
    /// own handler sees the same list (it subscribes inside <c>Run()</c>, after this client). The chip is painted over the
    /// strip's last cells, which can be the tail of a tab the framework <em>did</em> draw — its name or its <c>×</c> — so
    /// left alone the click that opens the list also selected (or closed) that tab under it. The button flags are struck
    /// out of the frame, which leaves the framework a pointer movement; this client's own pointer path has its own copy and
    /// still opens the list. Both hosts deliver mouse frames through this one handler, so both are covered. Internal for tests.
    /// </summary>
    internal bool TakesTabOverflowPress(List<MouseFlags> flags, System.Drawing.Point point)
    {
        var chips = _tabOverflowChips;
        if (chips.Length == 0 || !chips.Any(c => Contains(c, point)))
        {
            return false;
        }

        var removed = flags.RemoveAll(f => f is MouseFlags.Button1Pressed or MouseFlags.Button1Released
            or MouseFlags.Button1Clicked or MouseFlags.Button1DoubleClicked or MouseFlags.Button1TripleClicked
            or MouseFlags.Button1Dragged);
        if (removed > 0 && flags.Count == 0)
        {
            flags.Add(MouseFlags.ReportMousePosition);
        }

        return removed > 0;
    }

    /// <summary>
    /// The frame a press on a pane's chip leaves for the framework, delivered to that pane's tab strip at every cell of the
    /// chip, as the framework would deliver it: true when any of them moved the strip's selection or asked to close a tab.
    /// Internal for tests, for <see cref="SimulatePaneClick"/>'s reason (the framework's mouse path runs only inside
    /// <c>Run()</c>).
    /// </summary>
    internal bool AChipClickReachesTheTabUnderIt(string paneId, bool filtered)
    {
        TabControl? tabs;
        lock (_paneTabsLock)
        {
            tabs = _paneTabs.GetValueOrDefault(paneId);
        }

        if (tabs is null || TabOverflowChipAt(paneId) is not { } chip)
        {
            return false;
        }

        var before = tabs.ActiveTabIndex;
        var closed = false;
        void OnClose(object? sender, TabEventArgs e) => closed = true;
        tabs.TabCloseRequested += OnClose;
        try
        {
            for (var x = chip.X; x < chip.X + chip.Width; x++)
            {
                var point = new System.Drawing.Point(x, chip.Y);
                foreach (var frame in new[] { new List<MouseFlags> { MouseFlags.Button1Pressed }, [MouseFlags.Button1Released, MouseFlags.Button1Clicked] })
                {
                    if (filtered)
                    {
                        TakesTabOverflowPress(frame, point);
                    }

                    var local = new System.Drawing.Point(tabs.ActualWidth - (chip.X + chip.Width - x), 0);
                    tabs.ProcessMouseEvent(new MouseEventArgs(frame, local, point, point, _window));
                }

                if (tabs.ActiveTabIndex != before || closed)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            tabs.TabCloseRequested -= OnClose;
            if (tabs.ActiveTabIndex != before)
            {
                tabs.ActiveTabIndex = before;
            }
        }
    }

    private void PaintTabOverflowChips(SharpConsoleUI.Layout.CharacterBuffer buffer)
    {
        _tabOverflow.Clear();
        foreach (var (paneId, tabs, _) in RealisedPanes())
        {
            if (!tabs.ShowTabHeader || tabs.TabCount < 2 || BoundsOf(tabs) is not { Width: > 0 } strip
                || HiddenTabs(tabs, strip.Width) is not { Count: > 0 } hidden)
            {
                continue;
            }

            var menu = Drawing(SurfaceCatalog.TabOverflow, paneId, new MenuModel(hidden));
            var chip = TabOverflowChip(menu);
            if (chip.Length + 4 > strip.Width)
            {
                continue;
            }

            var at = new PaneRect(strip.X + strip.Width - chip.Length - 1, strip.Y, chip.Length + 1, 1);
            _tabOverflow[paneId] = (menu, at);

            var top = _window.Top + _system.DesktopUpperLeft.Y;
            var by = at.Y - top;
            var bx = at.X - _window.Left;
            if (by < 0 || by >= buffer.Height || bx < 0 || bx + at.Width > buffer.Width)
            {
                continue;
            }

            var plane = buffer.GetCell(bx, by).Background;
            var planeRgb = new Rgb(plane.R, plane.G, plane.B);
            var ink = TryParseRgb(_ink.Accent, out var accent) ? Contrast.Legible(accent, planeRgb) : new Rgb(0xee, 0xee, 0xee);
            buffer.WriteString(bx, by, " " + chip, ToColor(ink), plane);
        }
    }

    /// <summary>
    /// The tabs of a strip that the framework's hit test finds nowhere along it, as menu entries in strip order: each
    /// says the window's name and dispatches <c>win:&lt;id&gt;</c>. Null when every tab was drawn, or the hit test answers
    /// nothing at all (a framework that renamed it), which must not read as every tab hidden.
    /// </summary>
    private List<MenuEntry>? HiddenTabs(TabControl tabs, int width)
    {
        // Cells per tab along the whole strip, and left of where a chip would sit. A tab the framework clipped at the
        // right edge to a letter or two (`R`) names nothing, and the chip is painted over it anyway: it counts as hidden
        // unless the part left of the chip holds its pad and TabStripFit's floor (three letters and an ellipsis).
        var total = new Dictionary<int, int>();
        var shown = new Dictionary<int, int>();
        for (var x = 0; x < width; x++)
        {
            if (FrameworkHitTests.TabAt(tabs, x) is var index and >= 0)
            {
                total[index] = total.GetValueOrDefault(index) + 1;
                if (x < width - ChipReserve)
                {
                    shown[index] = shown.GetValueOrDefault(index) + 1;
                }
            }
        }

        if (total.Count == 0)
        {
            return null;
        }

        // Only the tab reaching the strip's last cell can have been clipped.
        var edge = FrameworkHitTests.TabAt(tabs, width - 1);
        var seen = total.Keys
            .Where(i => i != edge || shown.GetValueOrDefault(i) >= Math.Min(total[i], 1 + TabStripFit.MinimumName))
            .ToHashSet();
        if (seen.Count >= tabs.TabCount)
        {
            return null;
        }

        var hidden = new List<MenuEntry>();
        var pages = tabs.TabPages;
        for (var i = 0; i < pages.Count; i++)
        {
            if (!seen.Contains(i) && pages[i].Tag is string id && _workspace.FindWindow(id) is { } window)
            {
                var unread = window.Unread > 0 ? $" ({UnreadBadge.Format(window.Unread)})" : string.Empty;
                hidden.Add(new MenuEntry(Snippet(TabTitles.Name(window)) + unread, null, CommandIds.Window(id)));
            }
        }

        return hidden;
    }

    /// <summary>The overflowing strip whose chip is under <paramref name="point"/>, or null.</summary>
    private string? TabOverflowAt(System.Drawing.Point point) =>
        _tabOverflow.FirstOrDefault(p => Contains(p.Value.Chip, point)).Key;

    /// <summary>
    /// Opens a pane's list of hidden tabs at <paramref name="point"/>: the host's native menu where it draws menus, else
    /// a cell list. Both hand the chosen entry's command to <see cref="RunTabOverflowEntry"/>. Internal for tests, which
    /// pass <paramref name="cells"/> to open the cell list in a host that draws menus.
    /// </summary>
    internal bool OpenTabOverflow(string paneId, System.Drawing.Point? point = null, bool cells = false)
    {
        if (!_tabOverflow.TryGetValue(paneId, out var overflow) || AnyOverlayOpen)
        {
            return false;
        }

        var menu = overflow.Menu;
        _tabOverflowOffered = menu;
        var at = point ?? new System.Drawing.Point(overflow.Chip.X, overflow.Chip.Y);
        if (!cells && _pointer is { DrawsMenus: true } pointer)
        {
            pointer.ShowMenu(at, menu, command => OnUiThread(() => RunTabOverflowEntry(command)));
            return true;
        }

        _tabOverflowSelected = 0;
        _sendMenu.Open(
            "tabs",
            $"{menu.Entries.Count} more tabs",
            width => TabOverflowLines(menu, _tabOverflowSelected, width),
            key =>
            {
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        _tabOverflowSelected = Math.Max(0, _tabOverflowSelected - 1);
                        return true;
                    case ConsoleKey.DownArrow:
                        _tabOverflowSelected = Math.Min(menu.Entries.Count - 1, _tabOverflowSelected + 1);
                        return true;
                    case ConsoleKey.Enter when menu.Entries[_tabOverflowSelected].Command is { } chosen:
                        _sendMenu.Close();
                        RunTabOverflowEntry(chosen);
                        return false;
                    case >= ConsoleKey.D1 and <= ConsoleKey.D9 when key.Key - ConsoleKey.D1 < menu.Entries.Count
                        && menu.Entries[key.Key - ConsoleKey.D1].Command is { } picked:
                        _sendMenu.Close();
                        RunTabOverflowEntry(picked);
                        return false;
                    default:
                        return true;
                }
            },
            width: TabOverflowLines(menu, 0, int.MaxValue).Max(VisibleLength));
        return true;
    }

    private int _tabOverflowSelected;

    /// <summary>
    /// The cell list of hidden tabs: a digit and a name per tab, the selected one marked, and the keys. The cell renderer
    /// of the overflow surface, drawn from the same <see cref="MenuModel"/> the window's menu is.
    /// </summary>
    internal static List<string> TabOverflowLines(MenuModel menu, int selected, int width)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var lines = new List<string>();
        for (var i = 0; i < menu.Entries.Count; i++)
        {
            var key = i < 9 ? $"{i + 1}" : " ";
            var mark = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var weight = i == selected ? "bold " : string.Empty;
            lines.Add($"{mark} [{ScreenPalette.Label}]{key}[/] [{weight}{ScreenPalette.Value}]{Escape(menu.Entries[i].Label)}[/]");
        }

        lines.Add(string.Empty);
        lines.Add($"[{ScreenPalette.Label}]↑↓ select · ⏎ or a digit goes there · Esc cancels[/]");
        return lines;
    }

    /// <summary>The cell list for a menu with nothing selected; for the surface tests.</summary>
    internal static List<string> TabOverflowLines(MenuModel menu) => TabOverflowLines(menu, 0, int.MaxValue);

    /// <summary>
    /// A hidden tab chosen from the overflow list, in either host: the window it names comes forward through the
    /// registry, as its ⌃P entry would bring it. A command the last list did not offer is refused. Internal for tests.
    /// </summary>
    internal bool RunTabOverflowEntry(string command)
    {
        if (_tabOverflowOffered is not { } offered || !offered.Commands().Contains(command, StringComparer.Ordinal))
        {
            RefuseCommand($"the tab list did not offer {Snippet(command)}");
            return false;
        }

        return DispatchCommand(command);
    }
}
