using SharpConsoleUI;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The connection sidebar from the keyboard, and the menu a right click on one of its rows opens — in both hosts, from
/// one model. ⌥C puts a cursor on the sidebar (<see cref="SidebarModel.Cursor"/>); ↑↓, Home and End walk its rows, ⏎ goes
/// where a click on that row goes (<see cref="DispatchRailTarget"/>), and Esc or ⌥C hands the keys back to the command
/// line. Any other key does too, and then does what it does there, so a reader who starts typing is never stuck.
/// <para>
/// <b>Focus does not move.</b> The framework's focus stays pinned to the armed command line
/// (<see cref="PinFocusToArmedBar"/>); the cursor is client state the main window's key chain reads first, the way an
/// armed ⌃B is. That is what makes it the same feature in the terminal and the window: the cell rail draws the cursor as
/// a band across its row, the native sidebar as a ring, and both read it off the same model.
/// </para>
/// <para>
/// <b>The row menu is the ⌃P catalog's entries</b> for that row (<see cref="SidebarRowMenu"/>): switch, reconnect or
/// disconnect, edit in F5 or F9, go to, close, clear. Each names a registry command and the key that does the same, so a
/// menu cannot offer what the keyboard lacks; choosing one first brings the row forward (a right click on a tab does the
/// same), then runs the command, and a command the menu did not offer is refused.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The key (<see cref="SidebarModel.KeyOf(RailRow)"/>) of the row the keyboard is on, or null.</summary>
    private string? _railCursor;

    /// <summary>The menu the sidebar was last offered, and the row it was for.</summary>
    private (string Row, MenuModel Menu) _sidebarMenuOffered = (string.Empty, MenuModel.Empty);

    /// <summary>The two delegates the chrome overlay carries for the row menu; one instance each, so overlays compare equal.</summary>
    private Action<string, Action<MenuModel>>? _sidebarMenuRequest;

    private Action<string>? _sidebarMenuRun;

    /// <summary>Whether the sidebar holds the keys. Internal for tests.</summary>
    internal bool RailKeysActive => _railCursor is not null;

    /// <summary>The row the keyboard is on, for a test.</summary>
    internal string? RailCursor => _railCursor;

    /// <summary>⌥C: into the sidebar, or back out of it.</summary>
    private void ToggleRailKeys()
    {
        if (_railCursor is not null)
        {
            LeaveRailKeys();
            return;
        }

        var rows = BuildSidebar().NavigableRows();
        if (rows.Count == 0)
        {
            RefuseCommand("the sidebar has nothing to walk yet — ⌃P ▸ Welcome adds a game");
            return;
        }

        // Start where the reader is: the window in front, else the character in front, else the top.
        var selected = CommandIds.Window(ActiveWindowId());
        var start = rows.FirstOrDefault(r => r.Kind == RailRowKind.Window && r.Target == selected)
            ?? rows.FirstOrDefault(r => r.Kind == RailRowKind.Character && r.Active)
            ?? rows[0];
        _railCursor = SidebarModel.KeyOf(start);
        RefreshRail();
        Notice("sidebar: ↑↓ walk · ⏎ goes · Esc returns", MessageSeverity.Info, "⌥C");
    }

    /// <summary>Hands the keys back to the command line. Does nothing when the sidebar does not hold them.</summary>
    private void LeaveRailKeys()
    {
        if (_railCursor is null)
        {
            return;
        }

        _railCursor = null;
        RefreshRail();
    }

    /// <summary>
    /// A key while the sidebar holds the keyboard. Returns true for the keys it answers; anything else gives the keys
    /// back and returns false, so the key goes on down the chain to the command line.
    /// </summary>
    private bool TryRailKey(KeyPressedEventArgs e)
    {
        if (_railCursor is null)
        {
            return false;
        }

        var key = e.KeyInfo;
        var bare = key.Modifiers == 0;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow when bare:
                MoveRailCursor(-1);
                break;
            case ConsoleKey.DownArrow when bare:
                MoveRailCursor(1);
                break;
            case ConsoleKey.Home when bare:
                MoveRailCursor(int.MinValue);
                break;
            case ConsoleKey.End when bare:
                MoveRailCursor(int.MaxValue);
                break;
            case ConsoleKey.Escape:
                LeaveRailKeys();
                break;
            case ConsoleKey.Enter when bare:
                var row = BuildSidebar().NavigableRows().FirstOrDefault(r => SidebarModel.KeyOf(r) == _railCursor);
                LeaveRailKeys();
                if (row?.Target is { } target)
                {
                    DispatchRailTarget(target);
                }

                break;
            default:
                LeaveRailKeys();
                return false;
        }

        e.Handled = true;
        return true;
    }

    /// <summary>Moves the cursor by <paramref name="step"/> rows, clamped; the extremes of int go to the ends.</summary>
    private void MoveRailCursor(int step)
    {
        var rows = BuildSidebar().NavigableRows();
        if (rows.Count == 0)
        {
            LeaveRailKeys();
            return;
        }

        var at = rows.ToList().FindIndex(r => SidebarModel.KeyOf(r) == _railCursor);
        var next = step switch
        {
            int.MinValue => 0,
            int.MaxValue => rows.Count - 1,
            _ => Math.Clamp((at < 0 ? 0 : at) + step, 0, rows.Count - 1),
        };
        _railCursor = SidebarModel.KeyOf(rows[next]);
        RefreshRail();
    }

    /// <summary>
    /// The band the cell rail draws the keyboard cursor's row on: the armed command line's, because it says the same
    /// thing there — this is where the keys go.
    /// </summary>
    private string RailCursorBand()
    {
        var band = WorkspacePalette.ArmedBand(_theme);
        return $"{Contrast.Legible(_theme.Foreground, band).ToHex()} on {band.ToHex()}";
    }

    /// <summary>Drives a key into the sidebar's key handling as the main window would. Internal for tests.</summary>
    internal bool SimulateRailKey(ConsoleKeyInfo key) => TryRailKey(new KeyPressedEventArgs(key, false));

    /// <summary>
    /// The menu for one row of the sidebar, by its key (<see cref="SidebarModel.KeyOf(RailRow)"/>): the ⌃P entries that
    /// act on what the row names, each with its key. Remembered as offered, so the answer can be checked. Internal for
    /// tests and for the cell rail's own pointer menu.
    /// </summary>
    internal MenuModel SidebarRowMenu(string rowKey)
    {
        var model = BuildSidebar();
        var row = model.Rows().FirstOrDefault(r => r.Target is not null && SidebarModel.KeyOf(r) == rowKey);
        var entries = new List<MenuEntry>();
        if (row?.Target is { } target)
        {
            var world = model.Worlds.FirstOrDefault(w => RowsOf(w).Contains(rowKey));
            var character = world?.Characters.FirstOrDefault(c => c.Windows.Any(w => CommandIds.Window(w.Id) == target)
                || CommandIds.Character(c.SessionKey) == target);
            var title = Snippet(row.Label);
            switch (row.Kind)
            {
                case RailRowKind.World:
                    var pick = world?.Characters.FirstOrDefault(c => CommandIds.Character(c.SessionKey) == target);
                    Offer(entries, $"Switch to {Snippet(pick?.Name ?? title)}", pick?.Chord, target);
                    entries.Add(MenuEntry.Separator);
                    Offer(entries, $"Edit {title} in F5", "F5", ScreenCommandPrefix + "worlds");
                    break;

                case RailRowKind.Empty:
                    Offer(entries, $"Add a character to {Snippet(world?.Name ?? string.Empty)} in F5", "F5", ScreenCommandPrefix + "worlds");
                    break;

                case RailRowKind.Character:
                    Offer(entries, row.Active ? $"{title} is in front" : $"Switch to {title}", row.Chord, target, enabled: !row.Active);
                    Offer(entries, row.Connected ? "Disconnect" : "Connect", row.Connected ? "⌥D" : "⌥R",
                        row.Connected ? "world:disconnect" : "world:reconnect");
                    entries.Add(MenuEntry.Separator);
                    Offer(entries, $"Edit {Snippet(world?.Name ?? string.Empty)} in F5", "F5", ScreenCommandPrefix + "worlds");
                    Offer(entries, $"Edit {title} in F9", "F9", ScreenCommandPrefix + "logging");
                    break;

                case RailRowKind.Window:
                    var window = character?.Windows.FirstOrDefault(w => CommandIds.Window(w.Id) == target);
                    var front = model.IsSelected(window ?? new RailWindow(string.Empty, string.Empty, null, 0, false, false));
                    Offer(entries, front ? $"{title} is in front" : $"Go to {title}", row.Chord, target, enabled: !front && !row.Closed);
                    if (!row.Closed)
                    {
                        // ⌃W closes the window in front; the session's main window is the session and never closes.
                        Offer(entries, "Close this window", "⌃W", "layout:close", enabled: window?.Id != MainWindowId);
                        Offer(entries, "Clear this window", null, "term:clear");
                    }

                    break;
            }
        }

        var menu = new MenuModel(entries);
        _sidebarMenuOffered = (rowKey, menu);
        return menu;

        IEnumerable<string> RowsOf(RailWorld w) => RailModel.Build([w]).Where(r => r.Target is not null).Select(SidebarModel.KeyOf);
    }

    /// <summary>An entry, only when the registry runs its command.</summary>
    private void Offer(List<MenuEntry> entries, string label, string? key, string command, bool enabled = true)
    {
        if (_contributions.Commands.Resolve(command) is not null)
        {
            entries.Add(new MenuEntry(label, key, command, Enabled: enabled));
        }
    }

    /// <summary>
    /// An entry chosen from a sidebar row's menu: the row is brought forward first (unless the entry is that very
    /// step), then the command runs, as a right click on a tab focuses it before its menu acts. A command the menu last
    /// offered did not carry is refused. Internal so a test can drive it as the window does.
    /// </summary>
    internal bool RunSidebarMenuEntry(string id)
    {
        var (rowKey, menu) = _sidebarMenuOffered;
        if (!menu.Commands().Contains(id, StringComparer.Ordinal))
        {
            RefuseCommand($"the sidebar's menu never offered {Snippet(id)}");
            return false;
        }

        var target = rowKey[(rowKey.IndexOf('|', StringComparison.Ordinal) + 1)..];
        if (!string.Equals(id, target, StringComparison.Ordinal)
            && (target.StartsWith(CommandIds.CharacterPrefix, StringComparison.Ordinal) || target.StartsWith(CommandIds.WindowPrefix, StringComparison.Ordinal)))
        {
            DispatchRailTarget(target);
        }

        return string.Equals(id, target, StringComparison.Ordinal) ? DispatchRailTarget(id) : DispatchCommand(id);
    }

    /// <summary>The row key a cell rail target is drawn as: its character or window row, else its world row.</summary>
    private string RailRowKeyFor(string target)
    {
        var rows = BuildSidebar().Rows().Where(r => r.Target == target).ToList();
        var row = rows.FirstOrDefault(r => r.Kind is RailRowKind.Character or RailRowKind.Window) ?? rows.FirstOrDefault();
        return row is null ? SidebarModel.KeyOf(RailRowKind.Character, target) : SidebarModel.KeyOf(row);
    }
}
