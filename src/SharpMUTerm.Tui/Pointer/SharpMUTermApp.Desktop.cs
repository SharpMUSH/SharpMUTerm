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

    /// <summary>Tells the host which header buttons it may draw. Once, at construction.</summary>
    private void AnnounceHeaderActions() =>
        _pointer?.HeaderActions(
        [
            new HeaderAction(HeaderAction.LayoutsId, "Layouts", "⌥M", show => OnUiThread(() => show(LayoutsMenuItems()))),
        ]);

    /// <summary>The layout the focused character's panes show, or would get.</summary>
    private string LayoutInUse(string key) =>
        _liveLayout is { } live && string.Equals(live.SessionKey, key, StringComparison.Ordinal)
            ? live.LayoutId
            : ResolveLayout(key).Layout.Id;

    /// <summary>
    /// The window's Layouts menu: the built-ins, then the player's, the one in use checked; then the list's
    /// actions on the layout in use; then the list itself. Every entry dispatches a command id.
    /// Internal for tests.
    /// </summary>
    internal IReadOnlyList<PointerMenuItem> LayoutsMenuItems()
    {
        Action Dispatch(string id) => () => OnUiThread(() => DispatchCommand(id));
        var items = new List<PointerMenuItem>();
        if (LayoutsKey() is not { } key)
        {
            items.Add(new PointerMenuItem("No character here — a layout belongs to a character", null, () => { }, Enabled: false));
            items.Add(PointerMenuItem.Separator);
            items.Add(new PointerMenuItem("Manage layouts…", "⌥M", Dispatch("layout:layouts")));
            return items;
        }

        var inUse = LayoutInUse(key);
        var all = Layouts.All;
        foreach (var layout in all.Where(l => l.IsBuiltIn))
        {
            items.Add(new PointerMenuItem(Snippet(layout.Name), null, Dispatch(LayoutCommands.Apply(layout.Id)), Checked: layout.Id == inUse));
        }

        var own = all.Where(l => !l.IsBuiltIn).ToList();
        if (own.Count > 0)
        {
            items.Add(PointerMenuItem.Separator);
            foreach (var layout in own)
            {
                items.Add(new PointerMenuItem(Snippet(layout.Name), null, Dispatch(LayoutCommands.Apply(layout.Id)), Checked: layout.Id == inUse));
            }
        }

        var (world, character) = LayoutOwner(key);
        items.Add(PointerMenuItem.Separator);
        items.Add(new PointerMenuItem("Save changes", null, Dispatch(LayoutCommands.SaveChanges)));
        items.Add(new PointerMenuItem("Save as new", null, Dispatch(LayoutCommands.SaveAsNew)));
        items.Add(new PointerMenuItem(
            world is null ? "Use for this world" : $"Use for {Snippet(world.Name)}", null, Dispatch(LayoutCommands.UseForWorld), Enabled: world is not null));
        items.Add(new PointerMenuItem(
            character is null ? "Use for this character only" : $"Use for {Snippet(character.Name)} only",
            null,
            Dispatch(LayoutCommands.UseForCharacter),
            Enabled: character is not null));
        items.Add(PointerMenuItem.Separator);
        items.Add(new PointerMenuItem("Manage layouts…", "⌥M", Dispatch("layout:layouts")));
        return items;
    }

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
    /// a realised pane.
    /// </summary>
    private void PublishPanels()
    {
        if (_pointer is null)
        {
            return;
        }

        var overlays = new List<PanelOverlay>();
        foreach (var (paneId, rect) in PaneOutputRects())
        {
            if (_workspace.Layout.FindPane(paneId) is not { ActiveTab: { } windowId })
            {
                continue;
            }

            if (IsMapWindow(windowId))
            {
                if (MapViewFor(windowId) is { } view)
                {
                    var walker = _mapWalkers.TryGetValue(windowId, out var known)
                        ? known
                        : _mapWalkers[windowId] = room => OnUiThread(() => WalkFromMap(windowId, room));
                    overlays.Add(new PanelOverlay(
                        PanelIds.Map, windowId, rect, new PanelInk(_ink.Accent, _ink.Draft, _ink.Lift(new Rgb(0x6a, 0x6a, 0x72))), null, view, walker));
                }

                continue;
            }

            if (!TerminalPanels.TryParseWindowId(windowId, out var panelId, out var key))
            {
                continue;
            }

            if (panelId == PanelIds.Vitals)
            {
                var ink = VitalsInk();
                overlays.Add(new PanelOverlay(panelId, windowId, rect, new PanelInk(ink.Bar, ink.Low, ink.Empty), ReadVitals(key)));
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

        var walking = map.Walk.IsWalking ? $"walking, {map.Walk.Remaining} to go" : null;
        var model = MapViewModel.From(map.Map, map.Recorder.CurrentRoomId, map.Walk.RoomsAhead, walking);
        _mapViews[windowId] = (revision, model);
        return model;
    }

    /// <summary>What the host was last told about panels. Internal for tests.</summary>
    internal IReadOnlyList<PanelOverlay> PanelOverlays => _panelOverlays;
}
