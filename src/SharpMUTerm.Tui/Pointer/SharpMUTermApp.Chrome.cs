using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The header band and the connection sidebar as data, for a host that draws them natively over their cells (the
/// window). Both are the view-models the cell header and the cell rail are drawn from (<see cref="BuildHeaderModel"/>,
/// <see cref="BuildSidebar"/>), so a native view cannot say anything the cells do not.
/// <para>
/// <b>A click comes back as a target the client itself put in the model</b>, and goes where the same click on the
/// cells goes: a rail target to <see cref="DispatchRailTarget"/>, the rail's own handler, and the header's palette
/// target to the command surface the ribbon's <c>☰</c> opens. Never through <see cref="OnLinkClicked"/>, the output
/// panes' handler, and never a command the host spelt: a target that is not in the overlay the client last published
/// is refused.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private ChromeOverlay? _chromeOverlay;

    private Action<string>? _chromeActivate;

    /// <summary>
    /// Tells the host where the header and the rail are and what they say, when any of it changed. Rides the frame,
    /// for <see cref="PublishPanels"/>' reason, and the main window's activation, because another window opening over
    /// this one (⌃P, a settings screen, the quit prompt) repaints that window and not this one.
    /// </summary>
    private void PublishChrome()
    {
        if (_pointer is null)
        {
            return;
        }

        var overlay = BuildChromeOverlay();
        if (overlay == _chromeOverlay)
        {
            return;
        }

        _chromeOverlay = overlay;
        _pointer.Chrome(overlay);
    }

    /// <summary>The chrome as it stands. Internal for tests.</summary>
    internal ChromeOverlay BuildChromeOverlay()
    {
        _chromeActivate ??= target => OnUiThread(() => ActivateChromeTarget(target));
        var header = BuildHeaderModel();
        var brand = FillHex(AccentPalette[2]);
        var palette = new ChromePalette(
            brand,
            header.World is { } world ? FillHex(world.Accent) : null,
            _ink.Accent,
            _ink.Draft,
            UnreadBadge.TintFor(_ink));

        // Shown only while the main window is the one in front. Every other window here — the command surface, the
        // settings screens, the composer, the quit prompt — is drawn in cells over this one, and a native view over
        // the header or the rail would paint over it.
        return new ChromeOverlay(
            MainWindowInFront(),
            CellRectOf(_header),
            header,
            RailCellRect(),
            BuildSidebar(),
            palette,
            _chromeActivate);
    }

    /// <summary>
    /// Whether no other window of the client's is in front of the main one. Asked of the window system rather than of
    /// the window's own active flag, which a window that has never been activated (a headless harness, the first
    /// frame) reports false with nothing over it.
    /// </summary>
    private bool MainWindowInFront() =>
        _system.ActiveWindow is not { } front || ReferenceEquals(front, _window);

    /// <summary>
    /// The sidebar's rectangle: its whole column, top to bottom, which is what the rail's cells (and the plane under
    /// them) occupy — the rail control itself is only as tall as its rows. The control's own when there is no column yet.
    /// </summary>
    private PaneRect RailCellRect()
    {
        var rows = CellRectOf(_rail);
        var column = _railColumn is null ? default : CellRectOf(_railColumn);
        return column.Width > 0 && column.Height >= rows.Height ? column : rows;
    }

    /// <summary>A control's arranged rectangle in desktop cells, or empty while it has none.</summary>
    private PaneRect CellRectOf(SharpConsoleUI.Controls.IWindowControl control)
    {
        if (_window.GetLayoutNode(control) is not { } node)
        {
            return default;
        }

        var origin = ContentOrigin();
        var bounds = node.AbsoluteBounds;
        return new PaneRect(origin.X + bounds.X, origin.Y + bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>
    /// A click on a native header or sidebar, by the target the client put in the model. Internal so a test can drive
    /// it the way a host does; returns whether the target was one the client offered and was acted on.
    /// </summary>
    internal bool ActivateChromeTarget(string target)
    {
        var offered = _chromeOverlay ?? BuildChromeOverlay();
        if (!offered.Offers(target))
        {
            RefuseCommand($"the header and sidebar never offered {Snippet(target)}");
            return false;
        }

        if (target == HeaderModel.PaletteTarget)
        {
            // Opens, rather than the ☰'s toggle: a search box is clicked to start typing in it, never to close it.
            if (!_palette.IsOpen)
            {
                ToggleMenu();
            }

            return true;
        }

        if (target == SidebarModel.AddGameTarget)
        {
            return DispatchCommand(target);
        }

        return DispatchRailTarget(target);
    }

    /// <summary>What the host was last told about the header and the rail. Internal for tests.</summary>
    internal ChromeOverlay? ChromeOverlayPublished => _chromeOverlay;
}

/// <summary>
/// The header band and the connection sidebar, for a host that draws them natively over their cells. The cells are
/// drawn as in any host, so a host that draws nothing loses nothing, and the parity test, which compares cells, is
/// unaffected.
/// </summary>
/// <param name="Shown">Whether the main window is in front; while another window is, nothing native may be drawn.</param>
/// <param name="Header">The header row's rectangle, in desktop cells.</param>
/// <param name="HeaderModel">What the header says.</param>
/// <param name="Rail">The rail's rectangle, in desktop cells: the sidebar column, top to bottom.</param>
/// <param name="Sidebar">What the rail says.</param>
/// <param name="Palette">The client's own colours for the active theme.</param>
/// <param name="Activate">
/// Acts on a target from <see cref="HeaderModel"/> or <see cref="Sidebar"/>; safe to call from any thread. The same
/// instance for the life of the app, so an overlay compares equal from frame to frame.
/// </param>
internal sealed record ChromeOverlay(
    bool Shown,
    PaneRect Header,
    HeaderModel HeaderModel,
    PaneRect Rail,
    SidebarModel Sidebar,
    ChromePalette Palette,
    Action<string> Activate)
{
    /// <summary>Whether <paramref name="target"/> is one this overlay offered a click on.</summary>
    public bool Offers(string target) =>
        target == HeaderModel.PaletteTarget
        || target == HeaderModel.Character?.Target
        || Sidebar.Targets().Contains(target, StringComparer.Ordinal);
}

/// <summary>
/// The chrome's own colours as <c>#rrggbb</c>: the brand fill of the ribbon's first segment, the world's fill (or
/// null with no world), the client's accent, the draft pen, and the unread badge. Planes are not here: a host reads
/// those off the frame's cells, so a native view sits on what the cells under it wear.
/// </summary>
internal sealed record ChromePalette(string Brand, string? WorldFill, string Accent, string Draft, string Unread);
