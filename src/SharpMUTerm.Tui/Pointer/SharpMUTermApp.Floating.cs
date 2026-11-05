using SharpMUTerm.Client.Panels;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// The host's half of a floating panel: it draws each panel the client says is popped out in a window of its own, and
/// hands back where the player put it and when they closed it. A member of <see cref="ClientHost"/>, null for a host
/// that cannot float a panel. Called on the client's thread; a host that draws on another thread marshals itself.
/// </summary>
internal interface IFloatingPanels
{
    /// <summary>
    /// Every panel popped out now, whenever one appears, goes, moves or changes what it shows. A panel missing from the
    /// list is docked (or its character is gone): its window closes without docking anything.
    /// </summary>
    void Show(IReadOnlyList<FloatingPanel> panels);
}

/// <summary>One panel in a window of its own, for <see cref="IFloatingPanels"/>.</summary>
/// <param name="Model">The window: which panel, whose, and where it sits (the floating-panel surface's model).</param>
/// <param name="Content">
/// What the panel shows and does, exactly as a docked one is handed it (<see cref="PanelOverlay"/>, with an empty
/// rectangle): the same model its cells draw and the same callbacks, so a walk, a look or a quick key from the floating
/// window runs the same client method on the panel's own character.
/// </param>
/// <param name="Plane">The panel's plane, <c>#rrggbb</c>: its character's pane colour on the client's theme.</param>
/// <param name="Text">The theme's text colour, <c>#rrggbb</c>.</param>
/// <param name="Moved">Where the player moved or sized the window to, in screen pixels. Any thread; one instance.</param>
/// <param name="Dock">Docks the panel back (what closing the window does). Any thread; one instance.</param>
internal sealed record FloatingPanel(
    FloatingPanelModel Model,
    PanelOverlay Content,
    string Plane,
    string Text,
    Action<FloatingBounds> Moved,
    Action Dock);

/// <summary>
/// Panels popped out into windows of their own: the map on a second monitor, vitals floating beside the game.
/// <para>
/// <b>Floating is a fact about the panel's window</b> (<see cref="WorkspaceWindow.Floating"/>), so it is saved with the
/// workspace and means the same to every host: a host that can float (<see cref="ClientHost.FloatingPanels"/>) keeps
/// such a window out of the pane tree and draws it apart; a host that cannot keeps it in a pane and leaves the flag for
/// the next host that can. A layout says the same thing per panel (<see cref="PanelPlacement.Floating"/>).
/// </para>
/// <para>
/// <b>The pane it leaves goes back into the layout, through the workspace's own operations.</b> While the live layout
/// still owns the panes, popping out re-realises it with the panel's windows withheld, so the panes are exactly the
/// layout without that panel — the same rectangles, and so the same NAWS, as a layout that never placed it. Otherwise
/// the window is removed from its pane (<see cref="WorkspaceLayout.RemoveWindow"/>), which prunes a pane it emptied.
/// Docking is the same re-realisation with the windows returned, or the window put back in the pane it left.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>A floating panel named by a layout whose window does not exist yet (a map before its first room).</summary>
    private readonly record struct PendingFloat(string SessionKey, string PanelId, FloatingBounds Bounds);

    private readonly Dictionary<string, PendingFloat> _floatPending = new(StringComparer.Ordinal);

    /// <summary>Where each panel window last floated, so popping it out again puts it back there.</summary>
    private readonly Dictionary<string, FloatingBounds> _floatRemembered = new(StringComparer.Ordinal);

    /// <summary>The pane each floating window left, for a dock with no live layout to put it back by.</summary>
    private readonly Dictionary<string, string> _floatOrigin = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Action> _popOuters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _dockers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<FloatingBounds>> _movers = new(StringComparer.Ordinal);

    private IReadOnlyList<FloatingPanel> _floatingPublished = Array.Empty<FloatingPanel>();
    private bool _publishingFloating;

    /// <summary>Whether this host can put a panel in a window of its own.</summary>
    internal bool CanFloatPanels => Host.FloatingPanels is not null;

    /// <summary>What the floating host was last told. Internal for tests.</summary>
    internal IReadOnlyList<FloatingPanel> FloatingPublished => _floatingPublished;

    /// <summary>The panel a window draws, when it is one that can float; else null.</summary>
    private static string? FloatablePanelOf(string windowId)
    {
        if (IsMapWindow(windowId))
        {
            return PanelIds.Map;
        }

        return TerminalPanels.TryParseWindowId(windowId, out var panel, out _) && PanelFloatCommands.Floatable.Contains(panel)
            ? panel
            : null;
    }

    /// <summary>The window that draws a floatable panel for a character, or null for a panel that cannot float.</summary>
    private static string? FloatWindowId(string panelId, string sessionKey) =>
        panelId == PanelIds.Map ? MapWindowId(sessionKey)
        : PanelFloatCommands.Floatable.Contains(panelId) ? TerminalPanels.WindowId(panelId, sessionKey)
        : null;

    /// <summary>Whether a window is popped out here: floating, and in a host that floats. Internal for tests.</summary>
    internal bool IsFloating(string windowId) => CanFloatPanels && _workspace.FindWindow(windowId)?.Floating is not null;

    /// <summary>A docked panel's pop-out callback, where this host can float it; one instance per window.</summary>
    private Action? PopOuterFor(string windowId)
    {
        if (!CanFloatPanels || FloatablePanelOf(windowId) is null)
        {
            return null;
        }

        return _popOuters.TryGetValue(windowId, out var known)
            ? known
            : _popOuters[windowId] = () => OnUiThread(() => PopOutPanel(windowId));
    }

    /// <summary>Who a character is, for a window title and a ⌃P detail.</summary>
    private string CharacterLabel(string sessionKey) =>
        FindSession(sessionKey)?.Character?.Name ?? FindCharacter(sessionKey)?.Character.Name ?? Snippet(sessionKey);

    /// <summary>Every panel window that can pop out or dock back, for ⌃P; null where this host cannot float.</summary>
    private IReadOnlyList<Core.Commands.PanelFloatRef>? FloatRefs()
    {
        if (!CanFloatPanels)
        {
            return null;
        }

        return _workspace.Windows
            .Where(w => w.SessionKey is not null && FloatablePanelOf(w.Id) is not null
                && (w.Floating is not null || _workspace.Layout.FindWindow(w.Id) is not null))
            .OrderBy(w => w.Sequence)
            .Select(w => new Core.Commands.PanelFloatRef(
                w.Id, PanelTitle(FloatablePanelOf(w.Id)!), CharacterLabel(w.SessionKey!), w.Floating is not null))
            .ToList();
    }

    /// <summary>
    /// Pops the panel drawn in <paramref name="windowId"/> out into a window of its own: the button on its native view,
    /// its pane's right-click menu and ⌃P's <c>panel:popout:</c> all end here. Refused where the host cannot float, for a
    /// window that is no floatable panel, and for the last window the panes hold. Internal so a test drives it as the
    /// window does.
    /// </summary>
    internal bool PopOutPanel(string windowId)
    {
        if (!CanFloatPanels)
        {
            RefuseCommand("this client draws every panel in its pane — pop out is the desktop window's");
            return false;
        }

        if (FloatablePanelOf(windowId) is not { } panelId || _workspace.FindWindow(windowId) is not { SessionKey: { } key } window)
        {
            RefuseCommand($"{Snippet(windowId)} is not a panel that can pop out");
            return false;
        }

        if (window.Floating is not null)
        {
            RefuseCommand($"{PanelTitle(panelId)} is already in a window of its own");
            return false;
        }

        if (_workspace.Layout.Root.Panes().Sum(p => p.Tabs.Count) <= 1)
        {
            RefuseCommand($"{PanelTitle(panelId)} is the only window here — it has nowhere to leave from");
            return false;
        }

        var focus = ActiveWindowId();
        if (_workspace.Layout.FindWindow(windowId) is { } pane)
        {
            _floatOrigin[windowId] = pane.Id;
        }

        window.Floating = _floatRemembered.GetValueOrDefault(windowId)
            ?? LiveLayoutPlacement(key, panelId)?.Floating
            ?? FloatingBounds.Unplaced;
        if (!RearrangeLiveLayout(key, focus == windowId ? null : focus))
        {
            _workspace.Layout.RemoveWindow(windowId);
            RebuildPaneArea();
            Activate(focus != windowId && _workspace.Layout.FindWindow(focus) is not null ? focus : OutputWindowOf(key) ?? ActiveWindowId());
        }

        RefreshRail();
        PublishFloating();
        Notice($"{PanelTitle(panelId)} is in a window of its own — close it to dock it back", MessageSeverity.Info, "⌃P");
        return true;
    }

    /// <summary>
    /// Docks a floating panel back into its region — the floating window's Dock button, closing that window, and ⌃P's
    /// <c>panel:dock:</c> all end here. With the live layout still owning the panes it is re-realised with the panel's
    /// windows returned, so the panel is back exactly where the layout places it; otherwise it goes back into the pane it
    /// left, or the focused one. Internal so a test drives it as the window does.
    /// </summary>
    internal bool DockPanel(string windowId)
    {
        if (_workspace.FindWindow(windowId) is not { Floating: { } bounds, SessionKey: { } key } window)
        {
            RefuseCommand($"{Snippet(windowId)} is not in a window of its own");
            return false;
        }

        if (bounds.IsPlaced)
        {
            _floatRemembered[windowId] = bounds;
        }

        window.Floating = null;
        _floatPending.Remove(windowId);
        if (!RearrangeLiveLayout(key, ActiveWindowId()) || _workspace.Layout.FindWindow(windowId) is null)
        {
            var origin = _floatOrigin.GetValueOrDefault(windowId);
            _workspace.Layout.AddWindow(windowId, origin is not null && _workspace.Layout.FindPane(origin) is not null ? origin : null);
            RebuildPaneArea();
        }

        _floatOrigin.Remove(windowId);
        RefreshRail();
        PublishFloating();
        Notice($"{PanelTitle(FloatablePanelOf(windowId) ?? windowId)} docked back", MessageSeverity.Info, "⌃P");
        return true;
    }

    /// <summary>The player moved or sized a floating panel's window. Remembered on the window, so the workspace and a saved layout keep it.</summary>
    private void MoveFloatingPanel(string windowId, FloatingBounds bounds)
    {
        if (!bounds.IsPlaced || _workspace.FindWindow(windowId) is not { Floating: not null } window || window.Floating == bounds)
        {
            return;
        }

        window.Floating = bounds;
        _floatRemembered[windowId] = bounds;
        PublishFloating();
    }

    /// <summary>A floating panel window moved, as the host reports it. Internal so a test drives it as the window does.</summary>
    internal void SimulateFloatingMoved(string windowId, FloatingBounds bounds) => MoveFloatingPanel(windowId, bounds);

    /// <summary>The live layout's placement of a panel, when <paramref name="key"/>'s layout owns the panes.</summary>
    private PanelPlacement? LiveLayoutPlacement(string key, string panelId) =>
        _liveLayout is { } live && string.Equals(live.SessionKey, key, StringComparison.Ordinal)
            ? Layouts.Find(live.LayoutId)?.PlacementOf(panelId)
            : null;

    /// <summary>
    /// Re-realises the live layout after a panel popped out or docked, when it is <paramref name="key"/>'s and the panes
    /// are still its own; false when there is none to follow (the player has rearranged the panes, or no layout applies).
    /// </summary>
    private bool RearrangeLiveLayout(string key, string? focus)
    {
        if (_liveLayout is not { } live
            || !string.Equals(live.SessionKey, key, StringComparison.Ordinal)
            || _workspace.Layout.ZoomedPaneId is not null
            || !string.Equals(LayoutRealiser.TreeShape(_workspace.Layout.Root), live.TreeShape, StringComparison.Ordinal)
            || Layouts.Find(live.LayoutId) is not { } layout)
        {
            return false;
        }

        return ApplyLayout(key, layout, focus, adoptFloating: false);
    }

    /// <summary>
    /// The windows that draw <paramref name="panel"/> in panes, for the realiser: <see cref="PanelWindows"/>, less a
    /// panel window that floats here — taken out of whatever pane it was put in on the way, so the pane it held is
    /// dropped and its split collapsed.
    /// </summary>
    private IReadOnlyList<string> DockedWindows(string key, string panel)
    {
        var windows = PanelWindows(key, panel);
        if (!CanFloatPanels || FloatWindowId(panel, key) is not { } floatId || !windows.Contains(floatId))
        {
            return windows;
        }

        var window = _workspace.FindWindow(floatId);
        if (window is not null && _floatPending.Remove(floatId, out var pending))
        {
            window.Floating = pending.Bounds;
        }

        if (window?.Floating is null)
        {
            return windows;
        }

        _workspace.Layout.RemoveWindow(floatId);
        return windows.Where(w => w != floatId).ToList();
    }

    /// <summary>Whether a window is kept out of the panes because it floats here.</summary>
    private bool FloatsHere(string windowId) => CanFloatPanels && _workspace.FindWindow(windowId)?.Floating is not null;

    /// <summary>
    /// An explicit apply takes the layout's floating panels as they are saved: each it floats floats (at its saved place
    /// when it has one), each it places docked is docked. Panels the layout does not place keep what they have.
    /// </summary>
    private void AdoptFloating(string key, LayoutDefinition layout)
    {
        foreach (var placement in layout.KnownPlacements)
        {
            if (FloatWindowId(placement.PanelId, key) is not { } id)
            {
                continue;
            }

            var window = _workspace.FindWindow(id);
            if (placement.Floating is { } bounds)
            {
                if (window is null)
                {
                    _floatPending[id] = new PendingFloat(key, placement.PanelId, bounds);
                }
                else
                {
                    window.Floating = bounds.IsPlaced ? bounds : window.Floating ?? _floatRemembered.GetValueOrDefault(id) ?? bounds;
                }
            }
            else
            {
                _floatPending.Remove(id);
                if (window?.Floating is { } was)
                {
                    if (was.IsPlaced)
                    {
                        _floatRemembered[id] = was;
                    }

                    window.Floating = null;
                }
            }
        }
    }

    /// <summary>A character's floating panels, by panel id, with where each sits: what "save changes" captures.</summary>
    private Dictionary<string, FloatingBounds> FloatingPanelsOf(string key)
    {
        var floating = new Dictionary<string, FloatingBounds>(StringComparer.Ordinal);
        foreach (var window in _workspace.Windows)
        {
            if (window.Floating is { } bounds && string.Equals(window.SessionKey, key, StringComparison.Ordinal)
                && FloatablePanelOf(window.Id) is { } panel)
            {
                floating[panel] = bounds;
            }
        }

        foreach (var pending in _floatPending.Values.Where(p => string.Equals(p.SessionKey, key, StringComparison.Ordinal)))
        {
            floating.TryAdd(pending.PanelId, pending.Bounds);
        }

        return floating;
    }

    /// <summary>
    /// A workspace resumed from the last run, by either host: where this host floats, a floating window comes out of the
    /// pane a host that cannot float kept it in; where it cannot, a floating window no pane holds is put back in one, and
    /// keeps its flag for the next host that can.
    /// </summary>
    private void SettleRestoredFloating()
    {
        foreach (var window in _workspace.Windows.Where(w => w.Floating is not null).OrderBy(w => w.Sequence).ToList())
        {
            if (FloatablePanelOf(window.Id) is null)
            {
                window.Floating = null;
                continue;
            }

            var placed = _workspace.Layout.FindWindow(window.Id) is not null;
            if (CanFloatPanels && placed && _workspace.Layout.Root.Panes().Sum(p => p.Tabs.Count) > 1)
            {
                _workspace.Layout.RemoveWindow(window.Id);
            }
            else if (!CanFloatPanels && !placed)
            {
                _workspace.Layout.AddWindow(window.Id, activate: false);
            }
        }
    }

    /// <summary>
    /// Tells the floating host which panels are popped out and what each shows, when any of that changed: on the frame
    /// (<see cref="PublishPanels"/>' reason), when a panel's content is redrawn, and after a pop-out or a dock.
    /// </summary>
    private void PublishFloating()
    {
        if (Host.FloatingPanels is not { } host || _publishingFloating)
        {
            return;
        }

        _publishingFloating = true;
        try
        {
            var panels = new List<FloatingPanel>();
            foreach (var window in _workspace.Windows.Where(w => w.Floating is not null && w.SessionKey is not null).OrderBy(w => w.Sequence).ToList())
            {
                if (FloatablePanelOf(window.Id) is not { } panelId)
                {
                    continue;
                }

                var key = window.SessionKey!;
                EnsureFloatingControl(window.Id, panelId, key);
                if (OverlayFor(window.Id, default, docked: false) is not { } content)
                {
                    continue;
                }

                var id = window.Id;
                var model = Drawing(
                    SurfaceCatalog.FloatingPanel, id, new FloatingPanelModel(id, panelId, PanelTitle(panelId), CharacterLabel(key), window.Floating!));
                panels.Add(new FloatingPanel(
                    model,
                    content,
                    WorkspacePalette.Tint(_theme, TintOf(key)).ToHex(),
                    Hex(_theme.Resolve(Core.Text.TerminalColor.Default, isBackground: false)),
                    _movers.TryGetValue(id, out var mover) ? mover : _movers[id] = bounds => OnUiThread(() => MoveFloatingPanel(id, bounds)),
                    _dockers.TryGetValue(id, out var docker) ? docker : _dockers[id] = () => OnUiThread(() => DockPanel(id))));
            }

            if (panels.SequenceEqual(_floatingPublished))
            {
                return;
            }

            _floatingPublished = panels;
            host.Show(panels);
        }
        finally
        {
            _publishingFloating = false;
        }
    }

    /// <summary>
    /// A floating panel's content is drawn from the same control its pane would hold, which redraws it as its data
    /// changes; one that has never been in a pane (a layout that floats it from the start) has its control made here.
    /// </summary>
    private void EnsureFloatingControl(string windowId, string panelId, string key)
    {
        if (panelId == PanelIds.Map)
        {
            if (!_mapControls.ContainsKey(windowId))
            {
                MapControlFor(windowId);
                RenderMap(windowId);
            }

            return;
        }

        Panels.GetOrCreate(new TerminalPanelContext(panelId, key, FindSession(key), windowId));
    }

    /// <summary>A panel's content changed: a floating one is told, since no frame may be painted for it.</summary>
    private void NoteFloatingContent(string windowId)
    {
        if (CanFloatPanels && _workspace.FindWindow(windowId)?.Floating is not null)
        {
            PublishFloating();
        }
    }
}
