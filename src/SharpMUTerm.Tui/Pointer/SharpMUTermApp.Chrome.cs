using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The header band and the connection sidebar as data, for a host that draws them itself, outside the cells (the
/// window; <see cref="ClientHost.Chrome"/>). Both are the view-models the cell header and the cell rail are drawn
/// from (<see cref="BuildHeaderModel"/>, <see cref="BuildSidebar"/>), so a native view cannot say anything the cells
/// would not.
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
    /// Tells the host what the header and the rail say, when any of it changed — only a host that draws them
    /// (<see cref="ClientHost.Chrome"/>). Rides the frame, for <see cref="PublishPanels"/>' reason; the main window's
    /// activation, because another window opening over this one (⌃P, a settings screen) repaints that window and not
    /// this one; and every refresh of the header or the rail (<see cref="PublishHostChrome"/>), because with neither in
    /// the cells a change to them may move no cell and paint no frame.
    /// </summary>
    private void PublishChrome()
    {
        if (_pointer is null || !Host.Chrome.Any)
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

    /// <summary>
    /// <see cref="PublishChrome"/> from a refresh of the header or the rail, which can run while the app is still
    /// being built (the window and the overlays not there yet), when there is nothing to publish.
    /// </summary>
    private void PublishHostChrome()
    {
        if (_window is not null && _palette is not null)
        {
            PublishChrome();
        }
    }

    /// <summary>The chrome as it stands. Internal for tests.</summary>
    internal ChromeOverlay BuildChromeOverlay()
    {
        _chromeActivate ??= target => OnUiThread(() => ActivateChromeTarget(target));
        _sidebarMenuRequest ??= (row, show) => OnUiThread(() =>
        {
            if (MainWindowInFront())
            {
                show(SidebarRowMenu(row));
            }
        });
        _sidebarMenuRun ??= id => OnUiThread(() => RunSidebarMenuEntry(id));
        var header = BuildHeaderModel();
        var brand = FillHex(AccentPalette[2]);
        var palette = new ChromePalette(
            brand,
            header.World is { } world ? FillHex(world.Accent) : null,
            _ink.Accent,
            _ink.Draft,
            UnreadBadge.TintFor(_ink),
            _ink.Notice,
            Hex(_theme.StatusBackground),
            Hex(WorkspacePalette.Backdrop(_theme)),
            Hex(_theme.Resolve(Core.Text.TerminalColor.Default, isBackground: false)))
        {
            BrandInk = WorkspacePalette.HeaderInk(_theme, ParseHex(brand)).ToHex(),
            HeaderMuted = WorkspacePalette.HeaderMuted(_theme).ToHex(),
        };

        return new ChromeOverlay(
            Host.Chrome,
            MainWindowInFront(),
            header,
            BuildSidebar(),
            palette,
            _chromeActivate,
            _sidebarMenuRequest,
            _sidebarMenuRun);
    }

    /// <summary>
    /// Whether no other window of the client's is in front of the main one. Asked of the window system rather than of
    /// the window's own active flag, which a window that has never been activated (a headless harness, the first
    /// frame) reports false with nothing over it.
    /// </summary>
    private bool MainWindowInFront() =>
        _nativeCompose is null && (_system.ActiveWindow is not { } front || ReferenceEquals(front, _window));

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
            // Opens, rather than the ☰'s toggle: a search box is clicked to start typing in it, never to close it. A
            // second click while the palette is up is that same answer, not a refusal.
            if (_palette.IsOpen || NativePaletteIsOpen)
            {
                return true;
            }

            if (!MainWindowInFront())
            {
                return false;
            }

            OpenMenu();

            return true;
        }

        // A modal in front (⌃P, a settings screen, the composer, the quit prompt) owns the keys and the screen; the
        // chrome stays drawn beside it, inert, as a desktop app's does under its own dialog.
        if (!MainWindowInFront())
        {
            return false;
        }

        if (target == SidebarModel.CollapseTarget)
        {
            ToggleRail();
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
/// The header band and the connection sidebar, for a host that draws them itself beside the cells
/// (<see cref="ClientHost.Chrome"/>). There are no cells under them: the client leaves no row or column for a part the
/// host draws, so the host places them in its own units and the cell grid is what is left.
/// </summary>
/// <param name="Drawn">Which parts the host draws; a part it does not draw is in the cells and is not its to show.</param>
/// <param name="Interactive">
/// Whether the main window is in front. While another of the client's windows is (⌃P, a settings screen, the
/// composer, the quit prompt), the chrome stays drawn and takes no click: the modal owns the screen.
/// </param>
/// <param name="HeaderModel">What the header says.</param>
/// <param name="Sidebar">What the rail says.</param>
/// <param name="Palette">The client's own colours for the active theme, planes included.</param>
/// <param name="Activate">
/// Acts on a target from <see cref="HeaderModel"/> or <see cref="Sidebar"/>; safe to call from any thread. The same
/// instance for the life of the app, so an overlay compares equal from frame to frame.
/// </param>
/// <param name="RowMenu">
/// Asks the client for a sidebar row's menu, by the row's key (<see cref="SidebarModel.KeyOf(RailRow)"/>); the client
/// builds it on its thread and hands it to the callback, which runs there (the host marshals). Nothing is handed back
/// while a modal of the client's is in front. Same instance for the life of the app.
/// </param>
/// <param name="RunRowMenu">Runs an entry of the menu <see cref="RowMenu"/> last built, by its command id; safe from any thread.</param>
internal sealed record ChromeOverlay(
    HostChrome Drawn,
    bool Interactive,
    HeaderModel HeaderModel,
    SidebarModel Sidebar,
    ChromePalette Palette,
    Action<string> Activate,
    Action<string, Action<SharpMUTerm.Client.Commands.MenuModel>> RowMenu,
    Action<string> RunRowMenu)
{
    /// <summary>Whether <paramref name="target"/> is one this overlay offered a click on.</summary>
    public bool Offers(string target) =>
        (Drawn.Header && (target == HeaderModel.PaletteTarget || target == HeaderModel.Character?.Target))
        || (Drawn.Rail && (target == SidebarModel.CollapseTarget || Sidebar.Targets().Contains(target, StringComparer.Ordinal)));
}

/// <summary>
/// The chrome's own colours as <c>#rrggbb</c>: the brand fill of the ribbon's first segment, the world's fill (or
/// null with no world), the client's accent, the draft pen, the unread badge, the client's notice voice (the armed
/// ⌃B), and the three a host needs to sit its views on the client's palette with no cell to read them off — the header
/// band's plane, the backdrop the rail sits on, and the default ink.
/// </summary>
internal sealed record ChromePalette(
    string Brand,
    string? WorldFill,
    string Accent,
    string Draft,
    string Unread,
    string Notice,
    string HeaderPlane,
    string RailPlane,
    string Ink)
{
    /// <summary>The ink on <see cref="Brand"/>, held to the header's floor — light on a light theme's deep fill.</summary>
    public string BrandInk { get; init; } = "#000000";

    /// <summary>The header's quiet text (counts, a pill's detail, the search hint), held to the header's floor on its band.</summary>
    public string HeaderMuted { get; init; } = "#808080";
}
