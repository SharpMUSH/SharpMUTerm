using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Text;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// What a host that draws native views over the cells is told: the header actions it may offer as buttons, and
/// the panels on screen with their content. Both go out through <see cref="IPointerPresenter"/>, the channel the
/// scrollbars already ride, and both leave every cell as it was — so a host that draws nothing native (the
/// terminal, the snapshot) is unaffected, and the window's native views are a presentation over the same frame.
/// <para>
/// <b>Nothing here is a second implementation.</b> The Layouts menu is built from the layout library's commands
/// (<see cref="LayoutCommands"/>), dispatched through the registry, which run the same methods as the ⌥M list's
/// keys; the vitals a native gauge draws are <see cref="ReadVitals"/>, the model the cell strip draws.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private IReadOnlyList<PanelOverlay> _panelOverlays = Array.Empty<PanelOverlay>();

    /// <summary>How many times each map window has been drawn; a native map's model is rebuilt only when this moves.</summary>
    private readonly Dictionary<string, long> _mapRevisions = new(StringComparer.Ordinal);

    /// <summary>The native map model last built for each map window, with the revision it was built at.</summary>
    private readonly Dictionary<string, (long Revision, MapViewModel Model)> _mapViews = new(StringComparer.Ordinal);

    /// <summary>One walk callback per map window, kept so an overlay compares equal from frame to frame.</summary>
    private readonly Dictionary<string, Action<long>> _mapWalkers = new(StringComparer.Ordinal);

    /// <summary>One pick callback per who-here or quick-bar window, for the same reason.</summary>
    private readonly Dictionary<string, Action<int>> _panelPickers = new(StringComparer.Ordinal);

    /// <summary>The one command callback every native panel is handed.</summary>
    private Action<string>? _panelDispatch;

    /// <summary>The Layouts menu the client last built, so a command handed back can be checked against it.</summary>
    private MenuModel _layoutsMenuOffered = MenuModel.Empty;

    /// <summary>Tells the host which header buttons it may draw. Once, at construction.</summary>
    private void AnnounceHeaderActions() =>
        _pointer?.HeaderActions(
        [
            new HeaderAction(
                HeaderAction.LayoutsId,
                "Layouts",
                "⌥M",
                show => OnUiThread(() => show(_layoutsMenuOffered = LayoutsMenu())),
                id => OnUiThread(() => RunLayoutsMenuEntry(id))),
        ]);

    /// <summary>The layout the focused character's panes show, or would get.</summary>
    private string LayoutInUse(string key) =>
        _liveLayout is { } live && string.Equals(live.SessionKey, key, StringComparison.Ordinal)
            ? live.LayoutId
            : ResolveLayout(key).Layout.Id;

    /// <summary>
    /// The window's Layouts menu: the built-ins, then the player's, the one in use checked; then the list's
    /// actions on the layout in use; then the list itself. Every entry names a command id the registry runs.
    /// Internal for tests.
    /// </summary>
    internal MenuModel LayoutsMenu()
    {
        var entries = new List<MenuEntry>();
        if (LayoutsKey() is not { } key)
        {
            entries.Add(new MenuEntry("No character here — a layout belongs to a character", null, null, Enabled: false));
            entries.Add(MenuEntry.Separator);
            entries.Add(new MenuEntry("Manage layouts…", "⌥M", LayoutCommands.Manage));
            return new MenuModel(entries);
        }

        var inUse = LayoutInUse(key);
        var all = Layouts.All;
        foreach (var layout in all.Where(l => l.IsBuiltIn))
        {
            entries.Add(new MenuEntry(Snippet(layout.Name), null, LayoutCommands.Apply(layout.Id), Checked: layout.Id == inUse));
        }

        var own = all.Where(l => !l.IsBuiltIn).ToList();
        if (own.Count > 0)
        {
            entries.Add(MenuEntry.Separator);
            foreach (var layout in own)
            {
                entries.Add(new MenuEntry(Snippet(layout.Name), null, LayoutCommands.Apply(layout.Id), Checked: layout.Id == inUse));
            }
        }

        var (world, character) = LayoutOwner(key);
        entries.Add(MenuEntry.Separator);
        entries.Add(new MenuEntry("Save changes", null, LayoutCommands.SaveChanges));
        entries.Add(new MenuEntry("Save as new", null, LayoutCommands.SaveAsNew));
        entries.Add(new MenuEntry(
            world is null ? "Use for this world" : $"Use for {Snippet(world.Name)}", null, LayoutCommands.UseForWorld, Enabled: world is not null));
        entries.Add(new MenuEntry(
            character is null ? "Use for this character only" : $"Use for {Snippet(character.Name)} only",
            null,
            LayoutCommands.UseForCharacter,
            Enabled: character is not null));
        entries.Add(MenuEntry.Separator);
        entries.Add(new MenuEntry("Manage layouts…", "⌥M", LayoutCommands.Manage));
        return new MenuModel(entries);
    }

    /// <summary>
    /// An entry chosen from the window's Layouts menu, by the command id the client put in it. A command the menu last
    /// built did not offer is refused, the chrome's rule: the host hands back a choice, never a command of its own.
    /// Internal so a test can drive it as the window does.
    /// </summary>
    internal bool RunLayoutsMenuEntry(string id)
    {
        if (!_layoutsMenuOffered.Commands().Contains(id, StringComparer.Ordinal))
        {
            RefuseCommand($"the Layouts menu never offered {Snippet(id)}");
            return false;
        }

        return DispatchCommand(id);
    }

    /// <summary>Builds the Layouts menu as the header button does, and remembers it as offered. Internal for tests.</summary>
    internal MenuModel OfferLayoutsMenu() => _layoutsMenuOffered = LayoutsMenu();

    /// <summary>
    /// A layout-library command (<see cref="LayoutCommands"/>), for the focused character: the ⌥M list's own
    /// methods, with what they did said in a notice rather than in the list's status line.
    /// </summary>
    private bool RunLayoutCommand(string id)
    {
        if (LayoutsKey() is not { } key)
        {
            RefuseCommand("no character here — a layout belongs to a character (⌃P ▸ Switch to …)");
            return false;
        }

        if (id.StartsWith(LayoutCommands.ApplyPrefix, StringComparison.Ordinal))
        {
            if (Layouts.Find(id[LayoutCommands.ApplyPrefix.Length..]) is not { } chosen)
            {
                RefuseCommand($"there is no layout {Snippet(id[LayoutCommands.ApplyPrefix.Length..])}");
                return false;
            }

            if (!ApplyLayout(key, chosen))
            {
                RefuseCommand($"no window to arrange for {Snippet(key)} — switch to the character first");
                return false;
            }

            Notice($"{chosen.Name} layout applied", MessageSeverity.Info, "⌥M");
            return true;
        }

        var current = Layouts.Find(LayoutInUse(key)) ?? ResolveLayout(key).Layout;
        var (world, character) = LayoutOwner(key);
        LayoutActionResult? result = id switch
        {
            LayoutCommands.SaveChanges => SaveLayoutChanges(key, current, world, character),
            LayoutCommands.SaveAsNew => SaveLayoutAsNew(key, current),
            LayoutCommands.UseForWorld => UseLayoutForWorld(key, current, world),
            LayoutCommands.UseForCharacter => UseLayoutForCharacter(key, current, character),
            _ => null,
        };
        if (result is not { } done)
        {
            RefuseCommand($"{id} isn't a layout command");
            return false;
        }

        if (done.Done)
        {
            Notice(Capitalise(done.Status), MessageSeverity.Info, "⌥M");
        }
        else
        {
            RefuseCommand(done.Status);
        }

        return done.Done;
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Tells the host which panels are on screen, where, and what they show, when any of that changed. Rides the
    /// frame, for <see cref="PublishScrollbars"/>' reason. A panel is on screen when its window is the front tab of
    /// a realised pane and no modal window covers the main one.
    /// </summary>
    private void PublishPanels()
    {
        if (_pointer is null)
        {
            return;
        }

        // A modal over the main window (a settings screen, ⌃P, F1) is drawn in cells over the panes; a native view
        // would sit on top of it, so while one is up no panel is on screen as far as the host is concerned.
        var overlays = new List<PanelOverlay>();
        // F1 drawn by the host covers the panes the same way, though it is no framework modal.
        var covered = _system.ModalStateService.HasModals || _nativeCompose is not null;
        var whole = Host.Chrome.Strips && !covered ? PaneRectsForResize() : null;
        foreach (var (paneId, rect) in !covered ? PaneOutputRects() : new Dictionary<string, Core.Workspaces.PaneRect>())
        {
            // A native panel lies over its pane's gutter too (HostChrome.PaneGutter): those columns hold nothing but the
            // pane's plane, and the panel pads itself.
            var over = Host.Chrome.PaneGutter
                ? new Core.Workspaces.PaneRect(rect.X - PaneGutterColumns, rect.Y, rect.Width + (2 * PaneGutterColumns), rect.Height)
                : rect;
            if (CollapsedBarOverlay(paneId, over) is { } bar)
            {
                overlays.Add(Seamed(bar, paneId, whole));
            }
            else if (_workspace.Layout.FindPane(paneId) is { ActiveTab: { } windowId } && OverlayFor(windowId, over, docked: true) is { } overlay)
            {
                overlays.Add(Seamed(overlay, paneId, whole));
            }
        }

        if (overlays.SequenceEqual(_panelOverlays))
        {
            return;
        }

        _panelOverlays = overlays;
        _pointer.Panels(overlays);
    }

    /// <summary>
    /// Marks the edges of a strip panel that another pane meets (<see cref="PanelOverlay.RuleAbove"/>/<c>RuleBelow</c>), where
    /// the host draws strips and so the strip is not recessed: its native view draws the hairline the cells' plane step stood
    /// for. Whole pane rectangles, so a neighbour's tab strip counts as the neighbour.
    /// </summary>
    private PanelOverlay Seamed(PanelOverlay overlay, string paneId, Dictionary<string, Core.Workspaces.PaneRect>? whole)
    {
        if (whole is null || !IsStripPane(paneId) || !whole.TryGetValue(paneId, out var me))
        {
            return overlay;
        }

        bool Meets(Func<Core.Workspaces.PaneRect, bool> edge) => whole.Any(p =>
            p.Key != paneId && edge(p.Value) && p.Value.X < me.X + me.Width && me.X < p.Value.X + p.Value.Width);
        var above = Meets(o => o.Y + o.Height == me.Y);
        var below = Meets(o => o.Y == me.Y + me.Height);
        return above || below ? overlay with { RuleAbove = above, RuleBelow = below } : overlay;
    }

    /// <summary>
    /// What a host is told about the panel drawn in <paramref name="windowId"/>: its model, its colours and its actions,
    /// over <paramref name="rect"/> (a pane's output rectangle, or empty for a panel floating in a window of its own).
    /// Null for a window that is no native panel, or one with nothing to draw yet. One function for a docked panel and a
    /// floating one, so the two are handed the same model and the same callbacks. A docked panel that can float also
    /// carries its pop-out callback, where the host can float one.
    /// </summary>
    private PanelOverlay? OverlayFor(string windowId, Core.Workspaces.PaneRect rect, bool docked)
    {
        var popOut = docked ? PopOuterFor(windowId) : null;
        if (IsMapWindow(windowId))
        {
            if (MapViewFor(windowId) is not { } view)
            {
                return null;
            }

            var walker = _mapWalkers.TryGetValue(windowId, out var known)
                ? known
                : _mapWalkers[windowId] = room => OnUiThread(() => WalkFromMap(windowId, room));
            return new PanelOverlay(
                PanelIds.Map, windowId, rect, new PanelInk(_ink.Accent, _ink.Draft, _ink.Lift(new Rgb(0x6a, 0x6a, 0x72))), null, view, walker,
                PopOut: popOut, SelectRoom: MapSelectorFor(windowId), EditMap: MapEditorFor(windowId),
                LookMap: MapLookerFor(windowId));
        }

        if (!TerminalPanels.TryParseWindowId(windowId, out var panelId, out var key))
        {
            return null;
        }

        if (panelId == PanelIds.Vitals)
        {
            var ink = VitalsInk();
            return new PanelOverlay(panelId, windowId, rect, new PanelInk(ink.Bar, ink.Low, ink.Empty), ReadVitals(key), PopOut: popOut);
        }

        if (panelId == PanelIds.WhoHere)
        {
            return new PanelOverlay(
                panelId, windowId, rect, PanelListInk(), null, WhoHere: ReadWhoHere(key), Pick: PickerFor(windowId, key), PopOut: popOut);
        }

        if (panelId == PanelIds.QuickBar)
        {
            return new PanelOverlay(
                panelId, windowId, rect, PanelListInk() with { Low = DangerInk() }, null, QuickBar: QuickBarEntries(key), Pick: PickerFor(windowId, key),
                Dispatch: _panelDispatch ??= id => OnUiThread(() => DispatchCommand(id)), PopOut: popOut);
        }

        if (docked && panelId == PanelIds.Composer && NativeComposerHost is not null
            && _dockedComposers.TryGetValue(windowId, out var docked2) && docked2.Model is { } composer)
        {
            // Only to a host with its own editor: the cells stay underneath it, and the editor is kept to them.
            return new PanelOverlay(
                panelId, windowId, rect, ComposerInk(), null, Composer: composer, ComposerSession: DockedSessionFor(docked2));
        }

        return null;
    }

    /// <summary>The list panels' colours: the accent their cells draw an active dot and a key in, and the warning.</summary>
    private PanelInk PanelListInk() => new(_ink.Accent, _ink.Notice, _ink.Lift(new Rgb(0x6a, 0x6a, 0x72)));

    /// <summary>
    /// A who-here or quick-bar window's pick callback: the row or item at an index of the model last published for
    /// that window, acted on exactly as a click on its cells is, on the window's own character.
    /// </summary>
    private Action<int> PickerFor(string windowId, string sessionKey) =>
        _panelPickers.TryGetValue(windowId, out var known)
            ? known
            : _panelPickers[windowId] = index => OnUiThread(() => PickFromPanel(windowId, sessionKey, index));

    private void PickFromPanel(string windowId, string sessionKey, int index)
    {
        var published = _panelOverlays.FirstOrDefault(p => p.WindowId == windowId)
            ?? _floatingPublished.FirstOrDefault(f => f.Content.WindowId == windowId)?.Content;
        if (published?.WhoHere is { } who && index >= 0 && index < who.Rows.Count)
        {
            OfferLook(windowId, sessionKey, who.Rows[index].Name);
        }
        else if (published?.QuickBar is { } bar && index >= 0 && index < bar.Items.Count)
        {
            RunQuickMacro(sessionKey, bar.Items[index].Descriptor);
        }
    }

    /// <summary>
    /// A map window's native model, rebuilt only when the cell map has been redrawn since (<see cref="RenderMap"/>
    /// counts), so a frame that changed nothing on the map costs a dictionary lookup.
    /// </summary>
    private MapViewModel? MapViewFor(string windowId)
    {
        if (MapOf(_workspace.FindWindow(windowId)?.SessionKey) is not { } map)
        {
            return null;
        }

        var revision = _mapRevisions.GetValueOrDefault(windowId);
        if (_mapViews.TryGetValue(windowId, out var cached) && cached.Revision == revision)
        {
            return cached.Model;
        }

        var model = BuildMapView(windowId, map);
        _mapViews[windowId] = (revision, model);
        return model;
    }

    /// <summary>The map's area colours for the theme in use: the set that clears the floor on its panes.</summary>
    private MapAreaPalette MapPalette() => MapAreaPalette.For(WorkspacePalette.DarkPanes(_theme));

    /// <summary>What the host was last told about panels. Internal for tests.</summary>
    internal IReadOnlyList<PanelOverlay> PanelOverlays => _panelOverlays;
}
