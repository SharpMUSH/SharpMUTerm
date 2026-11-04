using SharpConsoleUI.Controls;
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
        var seen = new HashSet<int>();
        for (var x = 0; x < width; x++)
        {
            if (FrameworkHitTests.TabAt(tabs, x) is var index and >= 0)
            {
                seen.Add(index);
            }
        }

        if (seen.Count == 0 || seen.Count >= tabs.TabCount)
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
