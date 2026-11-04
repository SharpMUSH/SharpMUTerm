using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Workspaces;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Layouts in the workspace: resolving a character's layout, realising it as panes, re-folding it as panels get
/// data, the ⌥M list, and the first-connect suggestion.
/// <para>
/// <b>A restored workspace wins at startup.</b> A character whose windows came back from the last run keeps the
/// arrangement it was left in; a character with none gets its resolved layout when its session is bound. ⌥M ⏎
/// always applies. Only the live entry point applies a layout on its own (<c>autoLayout</c>, the
/// <c>save:</c>/<c>logRoot:</c> family): a test or a snapshot binding a session keeps the workspace it built.
/// </para>
/// <para>
/// <b>A live layout is re-folded only while the panes are still its own.</b> The tree shape is recorded when a
/// layout is applied; once the player splits, closes or moves a pane it no longer matches, and from then on the
/// arrangement is theirs and nothing rebuilds it until ⌥M. A re-fold that would draw the same panels is a no-op,
/// so a burst of GMCP rebuilds nothing.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>How long a change in what a session has shown is gathered before the layout is re-folded.</summary>
    internal static readonly TimeSpan LayoutRefoldDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How long after a world's first connect the client waits for MSSP and GMCP before suggesting a layout.</summary>
    internal static readonly TimeSpan LayoutSuggestionDelay = TimeSpan.FromSeconds(5);

    /// <summary>The layout applied last and still owning the panes, or null.</summary>
    private sealed record LiveLayout(
        string SessionKey,
        string LayoutId,
        string FoldShape,
        string TreeShape,
        IReadOnlyDictionary<string, string> PanelPanes);

    /// <summary>
    /// Panels drawn by pane controls of their own, by panel id. See <see cref="TerminalPanels"/>; the panels plug in
    /// from <see cref="RegisterTerminalPanels"/>.
    /// </summary>
    internal TerminalPanels Panels { get; } = new();

    /// <summary>Registers the terminal panels' factories into <see cref="Panels"/>. Called once, at the end of construction.</summary>
    partial void RegisterTerminalPanels();

    private LayoutLibrary? _layoutLibrary;

    /// <summary>Whether a bound session gets its layout without being asked: the live entry point only.</summary>
    private readonly bool _autoLayout;

    /// <summary>The ⌥M list.</summary>
    private readonly ListOverlay _layoutsView;

    /// <summary>Every layout: the built-ins, then the player's (<see cref="AppConfiguration.Layouts"/>).</summary>
    internal LayoutLibrary Layouts => _layoutLibrary ??= new LayoutLibrary(_config.Layouts);

    private LiveLayout? _liveLayout;

    /// <summary>Session keys whose windows the restored workspace holds; the first bind of each keeps the restore.</summary>
    private HashSet<string> _restoredOwners = new(StringComparer.Ordinal);

    private ITimer? _layoutTimer;
    private bool _layoutTimerArmed;
    private readonly Dictionary<string, ITimer> _suggestionTimers = new(StringComparer.Ordinal);

    private int _layoutsSelected;
    private string? _layoutsStatus;

    /// <summary>The name being typed over the selected layout's (r), or null when no rename is under way.</summary>
    private string? _layoutsRenaming;

    /// <summary>Whether the selected layout's delete (d) is waiting for its y.</summary>
    private bool _layoutsConfirmingDelete;

    /// <summary>The longest name a rename takes; the list elides past its column anyway.</summary>
    private const int LayoutNameMaxLength = 40;

    /// <summary>The id of the layout realised in the panes, while it still owns them; for tests.</summary>
    internal string? LiveLayoutId => _liveLayout?.LayoutId;

    /// <summary>Whose layout owns the panes, or null.</summary>
    internal string? LiveLayoutOwner => _liveLayout?.SessionKey;

    /// <summary>Read once the workspace exists: whose windows a resumed workspace brought back.</summary>
    private void NoteRestoredOwners() =>
        _restoredOwners = _workspace.Windows.Select(w => w.SessionKey).OfType<string>().ToHashSet(StringComparer.Ordinal);

    // ── what a panel is drawn by ────────────────────────────────────────────────────────────────────

    private bool IsDemoKey(string key) =>
        _sessions.Sessions.Count == 0 && string.Equals(key, DemoScene.ActiveSessionKey, StringComparison.Ordinal);

    /// <summary>A session's own window: the one it prints into, else the main window recorded as its.</summary>
    private string? OutputWindowOf(string key)
    {
        if (FindSession(key) is { } session && _sessionWindows.TryGetValue(session, out var bound))
        {
            return bound;
        }

        return _workspace.Windows
            .Where(w => w.Kind == WindowKind.Main && string.Equals(w.SessionKey, key, StringComparison.Ordinal))
            .OrderBy(w => w.Sequence)
            .FirstOrDefault()?.Id;
    }

    private IEnumerable<WorkspaceWindow> SpawnWindowsOf(string key) =>
        _workspace.Windows
            .Where(w => w.Kind == WindowKind.Spawn && string.Equals(w.SessionKey, key, StringComparison.Ordinal))
            .OrderBy(w => w.Sequence);

    /// <summary>The name a panel window's tab carries.</summary>
    internal static string PanelTitle(string panelId) => panelId switch
    {
        PanelIds.WhoHere => "Here",
        PanelIds.Vitals => "Vitals",
        PanelIds.QuickBar => "Quick bar",
        PanelIds.Composer => "Composer",
        PanelIds.GameData => "Game data",
        PanelIds.Map => "Map",
        _ => panelId,
    };

    /// <summary>Registers (and places, if it is not) the window a terminal panel is shown in, and returns its id.</summary>
    private string EnsurePanelWindow(string panelId, string key)
    {
        var id = TerminalPanels.WindowId(panelId, key);
        if (_workspace.FindWindow(id) is null)
        {
            _workspace.OpenWindow(id, PanelTitle(panelId), WindowKind.Panel, key);
        }
        else if (_workspace.Layout.FindWindow(id) is null)
        {
            _workspace.Layout.AddWindow(id);
        }

        return id;
    }

    /// <summary>
    /// Opens a registered terminal panel for <paramref name="sessionKey"/> as a tab of the focused pane (or goes on
    /// showing it where it is) and returns its window id; null when no factory is registered for the id.
    /// </summary>
    internal string? OpenPanel(string panelId, string sessionKey)
    {
        if (!Panels.IsRegistered(panelId))
        {
            return null;
        }

        var id = EnsurePanelWindow(panelId, sessionKey);
        RebuildPaneArea();
        return id;
    }

    /// <summary>The windows that draw <paramref name="panelId"/> for <paramref name="key"/>, creating a panel or map window on the way.</summary>
    private IReadOnlyList<string> PanelWindows(string key, string panelId)
    {
        switch (panelId)
        {
            case PanelIds.Output:
                return OutputWindowOf(key) is { } output ? [output] : [];
            case PanelIds.Chat:
                return SpawnWindowsOf(key).Select(w => w.Id).ToList();
            case PanelIds.Map:
                if (MapOf(key) is not { Map.Rooms.Count: > 0 })
                {
                    return [];
                }

                var map = MapWindowId(key);
                if (_workspace.FindWindow(map) is null)
                {
                    _workspace.OpenWindow(map, PanelTitle(PanelIds.Map), WindowKind.Auxiliary, key);
                }

                return [map];
            default:
                return Panels.IsRegistered(panelId) ? [EnsurePanelWindow(panelId, key)] : [];
        }
    }

    /// <summary>The panel a window draws for <paramref name="key"/>, or null when it is not one of that session's.</summary>
    private string? PanelOf(string key, string windowId)
    {
        if (string.Equals(windowId, OutputWindowOf(key), StringComparison.Ordinal))
        {
            return PanelIds.Output;
        }

        if (string.Equals(windowId, MapWindowId(key), StringComparison.Ordinal))
        {
            return PanelIds.Map;
        }

        if (TerminalPanels.TryParseWindowId(windowId, out var panel, out var owner))
        {
            return string.Equals(owner, key, StringComparison.Ordinal) ? panel : null;
        }

        return _workspace.FindWindow(windowId) is { Kind: WindowKind.Spawn } spawn
            && string.Equals(spawn.SessionKey, key, StringComparison.Ordinal)
                ? PanelIds.Chat
                : null;
    }

    /// <summary>What <paramref name="key"/>'s session has shown, as the fold reads it — with every panel nothing draws counted as having no data.</summary>
    internal IPanelAvailability LayoutAvailability(string key)
    {
        var data = FindSession(key)?.GameData ?? (IsDemoKey(key) ? _demoGameData : null);
        var rooms = MapOf(key)?.Map.Rooms.Count ?? 0;
        var facts = PanelAvailability.From(data, rooms, SpawnWindowsOf(key).Count(), _config.Text.StatusMeters);
        return new DrawableAvailability(facts, Panels);
    }

    /// <summary><see cref="PanelAvailability"/>, less the panels this client has nothing to draw with.</summary>
    private sealed class DrawableAvailability(IPanelAvailability facts, TerminalPanels panels) : IPanelAvailability
    {
        public bool HasData(string panelId) =>
            facts.HasData(panelId)
            && (panelId is PanelIds.Output or PanelIds.Chat or PanelIds.Map || panels.IsRegistered(panelId));
    }

    // ── applying ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The world and character a session key names, live or configured.</summary>
    private (WorldDefinition? World, CharacterDefinition? Character) LayoutOwner(string key) =>
        FindSession(key) is { } session
            ? (session.World, session.Character)
            : FindCharacter(key) is { } found ? (found.World, found.Character) : (null, null);

    /// <summary>The layout <paramref name="key"/> uses, by <see cref="LayoutResolver"/>.</summary>
    internal ResolvedLayout ResolveLayout(string key)
    {
        var (world, character) = LayoutOwner(key);
        return LayoutResolver.Resolve(Layouts, world, character);
    }

    /// <summary>
    /// Realises <paramref name="layout"/> for <paramref name="key"/> in the workspace: one pane per drawn panel, every
    /// other window kept, the session's own window focused — or <paramref name="focusWindow"/>, when a re-fold must
    /// not move the reader. False when the session has no window to arrange around.
    /// </summary>
    internal bool ApplyLayout(string key, LayoutDefinition layout, string? focusWindow = null)
    {
        if (OutputWindowOf(key) is not { } output)
        {
            return false;
        }

        var fold = LayoutFold.Fold(layout, LayoutAvailability(key));
        var keep = _workspace.Layout.Root.Panes()
            .SelectMany(p => p.Tabs.Select(t => (t, (string?)p.Id)))
            .ToList();
        if (LayoutRealiser.Realise(layout, fold, panel => PanelWindows(key, panel), output, keep) is not { } realised)
        {
            return false;
        }

        _workspace.Layout.ReplaceTree(realised.Root, realised.FocusedPaneId);
        _liveLayout = new LiveLayout(
            key, layout.Id, realised.Shape, LayoutRealiser.TreeShape(_workspace.Layout.Root), realised.PanelPanes);
        RebuildPaneArea();
        var focus = focusWindow is not null && _workspace.Layout.FindWindow(focusWindow) is not null ? focusWindow : output;
        Activate(focus);
        RefreshRail();
        return true;
    }

    /// <summary>A session has been bound to its window: apply its layout, unless the restored workspace already arranged it.</summary>
    private void ApplyLayoutOnBind(WorldSession session)
    {
        if (!_autoLayout)
        {
            return;
        }

        if (_restoredOwners.Remove(session.SessionKey))
        {
            return; // the player's last arrangement wins over the layout at startup
        }

        ApplyLayout(session.SessionKey, ResolveLayout(session.SessionKey).Layout);
    }

    /// <summary>Something a panel's availability reads has changed for <paramref name="key"/>: re-fold, once the burst settles.</summary>
    private void NoteLayoutInput(string key)
    {
        if (_liveLayout is not { } live || !string.Equals(live.SessionKey, key, StringComparison.Ordinal) || _layoutTimerArmed)
        {
            return;
        }

        _layoutTimerArmed = true;
        _layoutTimer ??= _time.CreateTimer(
            _ => OnUiThread(() =>
            {
                _layoutTimerArmed = false;
                RefoldLayout();
            }),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _layoutTimer.Change(LayoutRefoldDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Re-folds the live layout against what its session has shown now, and rebuilds the panes only if that draws
    /// different panels. Stands down for good once the player has rearranged the panes, and for now while a pane is
    /// zoomed.
    /// </summary>
    internal void RefoldLayout()
    {
        if (_liveLayout is not { } live || _workspace.Layout.ZoomedPaneId is not null)
        {
            return;
        }

        if (!string.Equals(LayoutRealiser.TreeShape(_workspace.Layout.Root), live.TreeShape, StringComparison.Ordinal))
        {
            _liveLayout = null; // the arrangement is the player's now
            return;
        }

        var layout = Layouts.Find(live.LayoutId) ?? ResolveLayout(live.SessionKey).Layout;
        var fold = LayoutFold.Fold(layout, LayoutAvailability(live.SessionKey));
        if (string.Equals(LayoutRealiser.FoldShape(fold), live.FoldShape, StringComparison.Ordinal))
        {
            return;
        }

        ApplyLayout(live.SessionKey, layout, ActiveWindowId());
    }

    /// <summary>
    /// A capture window has just opened for <paramref name="session"/>: put it in the live layout's chat pane when
    /// there is one, rather than the focused pane, and re-fold in case chat had no pane yet.
    /// </summary>
    private void PlaceNewSpawnWindow(WorldSession session, string windowId)
    {
        if (_liveLayout is { } live
            && string.Equals(live.SessionKey, session.SessionKey, StringComparison.Ordinal)
            && live.PanelPanes.TryGetValue(PanelIds.Chat, out var pane)
            && _workspace.Layout.FindPane(pane) is not null)
        {
            _workspace.Layout.AddWindow(windowId, pane, activate: false);
        }

        NoteLayoutInput(session.SessionKey);
    }

    /// <summary>After a settings commit: a live layout whose owner now resolves to another layout is re-applied.</summary>
    private void ReapplyLayoutAfterSettings()
    {
        if (_liveLayout is not { } live)
        {
            return;
        }

        var resolved = ResolveLayout(live.SessionKey).Layout;
        if (!string.Equals(resolved.Id, live.LayoutId, StringComparison.Ordinal))
        {
            ApplyLayout(live.SessionKey, resolved, ActiveWindowId());
        }
    }

    // ── the first-connect suggestion ────────────────────────────────────────────────────────────────

    private static bool NeedsSuggestion(WorldSession session) =>
        session.World.Layout is null && session.World.AcceptedLayoutSuggestion is null && session.Character?.Layout is null;

    /// <summary>A session connected: on a world nobody has picked a layout for, suggest one once MSSP and GMCP have had a moment.</summary>
    private void ScheduleLayoutSuggestion(WorldSession session)
    {
        if (!_autoLayout || !NeedsSuggestion(session) || _suggestionTimers.ContainsKey(session.SessionKey))
        {
            return;
        }

        _suggestionTimers[session.SessionKey] = _time.CreateTimer(
            _ => OnUiThread(() => AcceptLayoutSuggestion(session)),
            null,
            LayoutSuggestionDelay,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>Vitals decide the suggestion outright, so their arrival need not wait for the timer.</summary>
    private void NoteSuggestionSignal(WorldSession session, string package)
    {
        if (_suggestionTimers.ContainsKey(session.SessionKey) && LayoutSuggester.VitalsPackages.Contains(package))
        {
            AcceptLayoutSuggestion(session);
        }
    }

    /// <summary>
    /// Takes the suggestion for <paramref name="session"/>'s world into <see cref="WorldDefinition.AcceptedLayoutSuggestion"/>,
    /// says so once, and applies it when it changes what the live layout is.
    /// </summary>
    internal void AcceptLayoutSuggestion(WorldSession session)
    {
        if (_suggestionTimers.Remove(session.SessionKey, out var timer))
        {
            timer.Dispose();
        }

        if (!NeedsSuggestion(session))
        {
            return;
        }

        var report = _mssp.Find(session.World.Host, session.World.Port)?.Report;
        var suggestion = LayoutSuggester.Suggest(LayoutSignals.From(session.GameData, report));
        session.World.AcceptedLayoutSuggestion = suggestion.LayoutId;
        PersistConfiguration();

        var name = Layouts.Find(suggestion.LayoutId)?.Name ?? suggestion.LayoutId;
        var why = suggestion.Reason switch
        {
            LayoutSuggestionReason.Vitals => "the game sends vitals",
            LayoutSuggestionReason.RoleplayServer => report?.Codebase is { } codebase
                ? $"it runs {Snippet(codebase)}"
                : "it is a roleplay server",
            _ => "nothing it sends points elsewhere",
        };
        Notice(
            $"Using the {name} layout for {Snippet(session.World.Name)}: {why}. ⌥M changes it.",
            MessageSeverity.Info,
            "⌥M",
            new NoticeAction(LayoutCommands.Manage, "Change layout"));

        if (_liveLayout is { } live
            && string.Equals(live.SessionKey, session.SessionKey, StringComparison.Ordinal)
            && !string.Equals(live.LayoutId, suggestion.LayoutId, StringComparison.Ordinal))
        {
            ApplyLayout(session.SessionKey, ResolveLayout(session.SessionKey).Layout, ActiveWindowId());
        }
    }

    // ── ⌥M ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The character the layouts list is about: the focused window's, else the demo's.</summary>
    private string? LayoutsKey() =>
        SendTarget()?.SessionKey
        ?? _workspace.FindWindow(ActiveWindowId())?.SessionKey
        ?? (_sessions.Sessions.Count == 0 ? _demoActiveKey ?? DemoScene.ActiveSessionKey : null);

    /// <summary>Whether the ⌥M list is up.</summary>
    internal bool LayoutsOpen => _layoutsView.IsOpen && _layoutsView.Mode == "layouts";

    /// <summary>What the ⌥M list shows, for a test.</summary>
    internal IReadOnlyList<string> LayoutsLines => _layoutsView.Lines;

    /// <summary>Feeds a key to the ⌥M list.</summary>
    internal void SimulateLayoutsKey(ConsoleKeyInfo key) => _layoutsView.SimulateKey(key);

    /// <summary>⌥M and ⌃P ▸ Layouts: the list of layouts for the focused character, or closes it.</summary>
    private void ShowLayouts()
    {
        if (LayoutsOpen)
        {
            _layoutsView.Close();
            return;
        }

        if (AnyOverlayOpen)
        {
            return;
        }

        if (LayoutsKey() is not { } key)
        {
            RefuseCommand("no character here — a layout belongs to a character (⌃P ▸ Switch to …)");
            return;
        }

        var all = Layouts.All;
        var inUse = _liveLayout is { } live && string.Equals(live.SessionKey, key, StringComparison.Ordinal)
            ? live.LayoutId
            : ResolveLayout(key).Layout.Id;
        _layoutsSelected = Math.Max(0, all.ToList().FindIndex(l => l.Id == inUse));
        _layoutsStatus = null;
        _layoutsRenaming = null;
        _layoutsConfirmingDelete = false;
        _layoutsView.Open(
            "layouts",
            $"Layouts · {Snippet(key)}",
            width => LayoutsListLines(key, width),
            k => LayoutsKeyPressed(key, k),
            width: 64,
            escape: LayoutsEscape);
    }

    /// <summary>Esc in the ⌥M list: leaves a rename or a pending delete first, and closes the list only from rest.</summary>
    private bool LayoutsEscape()
    {
        if (_layoutsRenaming is not null)
        {
            _layoutsRenaming = null;
            _layoutsStatus = "rename cancelled";
            return true;
        }

        if (_layoutsConfirmingDelete)
        {
            _layoutsConfirmingDelete = false;
            _layoutsStatus = "kept";
            return true;
        }

        return false;
    }

    /// <summary>A key while a name is being typed: letters build it, ⏎ keeps it, ⌫ takes a letter back.</summary>
    private bool LayoutsRenameKey(LayoutDefinition selected, ConsoleKeyInfo k)
    {
        var typed = _layoutsRenaming ?? string.Empty;
        switch (k.Key)
        {
            case ConsoleKey.Enter:
                var name = typed.Trim();
                if (name.Length == 0)
                {
                    _layoutsStatus = "a layout needs a name — type one, or Esc keeps the old";
                    return true;
                }

                if (!Layouts.Rename(selected.Id, name))
                {
                    _layoutsStatus = Layouts.IsNameTaken(name, selected.Id)
                        ? $"a layout is already called {name}"
                        : $"{selected.Name} cannot be renamed";
                    return true;
                }

                PersistConfiguration();
                _layoutsRenaming = null;
                _layoutsStatus = $"renamed to {name}";
                return true;
            case ConsoleKey.Backspace:
                _layoutsRenaming = typed.Length == 0 ? typed : typed[..^1];
                return true;
            default:
                if (k.KeyChar >= ' ' && !char.IsControl(k.KeyChar)
                    && (k.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
                    && typed.Length < LayoutNameMaxLength)
                {
                    _layoutsRenaming = typed + k.KeyChar;
                }

                return true;
        }
    }

    /// <summary>The answer to "delete …?": y deletes, anything else keeps it.</summary>
    private bool LayoutsDeleteAnswer(string key, LayoutDefinition selected, ConsoleKeyInfo k)
    {
        _layoutsConfirmingDelete = false;
        if (k.Key != ConsoleKey.Y || (k.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
        {
            _layoutsStatus = "kept";
            return true;
        }

        if (!Layouts.Delete(selected.Id))
        {
            _layoutsStatus = $"{selected.Name} cannot be deleted";
            return true;
        }

        // The panes stay as they are; with the layout they were drawn from gone, they are the player's own now.
        if (_liveLayout is { } live && live.LayoutId == selected.Id)
        {
            _liveLayout = null;
        }

        PersistConfiguration();
        _layoutsSelected = Math.Clamp(_layoutsSelected, 0, Layouts.All.Count - 1);
        var (world, character) = LayoutOwner(key);
        var named = character?.Layout == selected.Id || world?.Layout == selected.Id;
        _layoutsStatus = named
            ? $"deleted {selected.Name} — this character falls back to the next layout"
            : $"deleted {selected.Name}";
        return true;
    }

    private bool LayoutsKeyPressed(string key, ConsoleKeyInfo k)
    {
        var all = Layouts.All;
        _layoutsSelected = Math.Clamp(_layoutsSelected, 0, all.Count - 1);
        var selected = all[_layoutsSelected];
        var (world, character) = LayoutOwner(key);
        if (_layoutsRenaming is not null)
        {
            return LayoutsRenameKey(selected, k);
        }

        if (_layoutsConfirmingDelete)
        {
            return LayoutsDeleteAnswer(key, selected, k);
        }

        _layoutsStatus = null;
        switch (k.Key)
        {
            case ConsoleKey.UpArrow:
                _layoutsSelected = Math.Max(0, _layoutsSelected - 1);
                return true;
            case ConsoleKey.DownArrow:
                _layoutsSelected = Math.Min(all.Count - 1, _layoutsSelected + 1);
                return true;
            case ConsoleKey.Enter:
                if (!ApplyLayout(key, selected))
                {
                    RefuseCommand($"no window to arrange for {Snippet(key)} — switch to the character first");
                    return false;
                }

                Notice($"{selected.Name} layout applied — c keeps it for this character, w for the world", MessageSeverity.Info, "⌥M");
                return false;
            case ConsoleKey.S when k.Modifiers == 0:
                _layoutsStatus = SaveLayoutChanges(key, selected, world, character).Status;
                return true;
            case ConsoleKey.N when k.Modifiers == 0:
                _layoutsStatus = SaveLayoutAsNew(key, selected).Status;
                return true;
            case ConsoleKey.R when k.Modifiers == 0:
                if (selected.IsBuiltIn)
                {
                    _layoutsStatus = $"{selected.Name} is built in — n saves a copy you can rename";
                    return true;
                }

                _layoutsRenaming = selected.Name;
                return true;
            case ConsoleKey.D when k.Modifiers == 0:
                if (selected.IsBuiltIn)
                {
                    _layoutsStatus = $"{selected.Name} is built in and cannot be deleted";
                    return true;
                }

                _layoutsConfirmingDelete = true;
                return true;
            case ConsoleKey.W when k.Modifiers == 0:
                _layoutsStatus = UseLayoutForWorld(key, selected, world).Status;
                return true;
            case ConsoleKey.C when k.Modifiers == 0:
                _layoutsStatus = UseLayoutForCharacter(key, selected, character).Status;
                return true;
            default:
                return true;
        }
    }

    /// <summary>After w or c: applies the layout <paramref name="key"/> now resolves to, if the panes show another.</summary>
    private void ReapplyIfResolvedChanged(string key)
    {
        var resolved = ResolveLayout(key).Layout;
        if (_liveLayout is { } live
            && string.Equals(live.SessionKey, key, StringComparison.Ordinal)
            && string.Equals(live.LayoutId, resolved.Id, StringComparison.Ordinal))
        {
            return;
        }

        ApplyLayout(key, resolved, ActiveWindowId());
    }

    /// <summary>The current arrangement, read back as <paramref name="key"/>'s placements.</summary>
    private CapturedLayout? CaptureArrangement(string key, LayoutDefinition? previous) =>
        LayoutCapture.Capture(_workspace.Layout.Root, window => PanelOf(key, window), previous);

    /// <summary>What one of the list's actions did: whether it did it, and the line that says so.</summary>
    private readonly record struct LayoutActionResult(bool Done, string Status);

    /// <summary>⌥M <c>w</c> and <see cref="LayoutCommands.UseForWorld"/>: <paramref name="layout"/> becomes the world's default.</summary>
    private LayoutActionResult UseLayoutForWorld(string key, LayoutDefinition layout, WorldDefinition? world)
    {
        if (world is null)
        {
            return new(false, "this character's world is not configured");
        }

        world.Layout = layout.Id;
        PersistConfiguration();
        ReapplyIfResolvedChanged(key);
        return new(true, $"{layout.Name} is now {world.Name}'s layout");
    }

    /// <summary>⌥M <c>c</c> and <see cref="LayoutCommands.UseForCharacter"/>: <paramref name="layout"/> becomes the character's own.</summary>
    private LayoutActionResult UseLayoutForCharacter(string key, LayoutDefinition layout, CharacterDefinition? character)
    {
        if (character is null)
        {
            return new(false, "no configured character to keep it for");
        }

        character.Layout = layout.Id;
        PersistConfiguration();
        ReapplyIfResolvedChanged(key);
        return new(true, $"{character.Name} uses {layout.Name}");
    }

    private LayoutActionResult SaveLayoutChanges(string key, LayoutDefinition target, WorldDefinition? world, CharacterDefinition? character)
    {
        if (CaptureArrangement(key, target) is not { } captured)
        {
            return new(false, "nothing to save — no pane holds this character's output");
        }

        var result = Layouts.Update(target.Id, captured.Placements, captured.RegionShares);
        if (result.CreatedNew)
        {
            // A built-in is never changed; whoever was using it is pointed at the copy, and failing that this character.
            if (character?.Layout == target.Id || character is not null && world?.Layout != target.Id)
            {
                character!.Layout = result.Layout.Id;
            }
            else if (world is not null)
            {
                world.Layout = result.Layout.Id;
            }
        }

        PersistConfiguration();
        _layoutsSelected = Layouts.All.ToList().FindIndex(l => l.Id == result.Layout.Id);
        AdoptArrangementAs(key, result.Layout);
        return new(true, result.CreatedNew ? $"{target.Name} is built in — saved as {result.Layout.Name}" : $"saved {result.Layout.Name}");
    }

    private LayoutActionResult SaveLayoutAsNew(string key, LayoutDefinition from)
    {
        if (CaptureArrangement(key, from) is not { } captured)
        {
            return new(false, "nothing to save — no pane holds this character's output");
        }

        var saved = Layouts.SaveAsNew(from.Name, captured.Placements, captured.RegionShares);
        PersistConfiguration();
        _layoutsSelected = Layouts.All.ToList().FindIndex(l => l.Id == saved.Id);
        return new(true, $"saved as {saved.Name} — c or w puts it to use");
    }

    /// <summary>
    /// The panes now are <paramref name="layout"/>, as just saved from them: it becomes the live layout without a
    /// rebuild, so re-folding follows it.
    /// </summary>
    private void AdoptArrangementAs(string key, LayoutDefinition layout)
    {
        var fold = LayoutFold.Fold(layout, LayoutAvailability(key));
        var panels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pane in _workspace.Layout.Root.Panes())
        {
            foreach (var window in pane.Tabs)
            {
                if (PanelOf(key, window) is { } panel)
                {
                    panels.TryAdd(panel, pane.Id);
                }
            }
        }

        _liveLayout = new LiveLayout(
            key, layout.Id, LayoutRealiser.FoldShape(fold), LayoutRealiser.TreeShape(_workspace.Layout.Root), panels);
    }

    /// <summary>One line describing a layout: its own words for a built-in, its panels for a saved one.</summary>
    private static string LayoutDescription(LayoutDefinition layout) => layout.Id switch
    {
        BuiltInLayouts.RoleplayId => "output, composer, who is here",
        BuiltInLayouts.CombatId => "vitals, map, quick bar",
        _ => string.Join(", ", layout.KnownPlacements.Where(p => p.TabOf is null).Select(p => PanelTitle(p.PanelId).ToLowerInvariant())),
    };

    /// <summary>The ⌥M list for <paramref name="key"/> at a width.</summary>
    internal List<string> LayoutsListLines(string key, int width)
    {
        var (world, _) = LayoutOwner(key);
        var inUse = _liveLayout is { } live && string.Equals(live.SessionKey, key, StringComparison.Ordinal)
            ? live.LayoutId
            : ResolveLayout(key).Layout.Id;
        var worldDefault = world?.Layout ?? world?.AcceptedLayoutSuggestion;
        var all = Layouts.All;
        var nameColumn = Math.Clamp(all.Max(l => Snippet(l.Name).Length) + 2, 12, 24);
        var lines = new List<string>();

        void Row(int index, LayoutDefinition layout)
        {
            var selected = index == _layoutsSelected;
            var mark = selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var renaming = selected && _layoutsRenaming is not null;
            var name = renaming ? _layoutsRenaming! + "▏" : Snippet(layout.Name);
            name = renaming
                ? (name.Length > nameColumn - 1 ? "…" + name[^(nameColumn - 2)..] : name)
                : (name.Length > nameColumn - 1 ? name[..(nameColumn - 2)] + "…" : name);
            var tags = new List<string>();
            if (layout.Id == inUse)
            {
                tags.Add("● in use");
            }

            if (layout.Id == worldDefault)
            {
                tags.Add("● world default");
            }

            var tag = tags.Count == 0 ? string.Empty : string.Join("  ", tags);
            var room = Math.Max(0, width - 2 - nameColumn - tag.Length - 1);
            var description = LayoutDescription(layout);
            description = description.Length > room ? (room > 1 ? description[..(room - 1)] + "…" : string.Empty) : description;
            lines.Add(
                $"{mark} [{(selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(name.PadRight(nameColumn))}[/]"
                + $"[{ScreenPalette.Label}]{Escape(description.PadRight(room))}[/]"
                + (tag.Length == 0 ? string.Empty : $" [{ScreenPalette.Accent}]{tag}[/]"));
        }

        lines.Add($"[{ScreenPalette.Label}]built in[/]");
        for (var i = 0; i < all.Count && all[i].IsBuiltIn; i++)
        {
            Row(i, all[i]);
        }

        lines.Add(string.Empty);
        lines.Add($"[{ScreenPalette.Label}]yours[/]");
        var own = 0;
        for (var i = 0; i < all.Count; i++)
        {
            if (!all[i].IsBuiltIn)
            {
                Row(i, all[i]);
                own++;
            }
        }

        if (own == 0)
        {
            lines.Add($"  [{ScreenPalette.Label}]none yet — n saves the panes as they are now[/]");
        }

        string Key(string chord, string what) => $"[{ScreenPalette.Accent}]{chord}[/] [{ScreenPalette.Label}]{what}[/]";
        var current = all[Math.Clamp(_layoutsSelected, 0, all.Count - 1)];
        var statusLine = _layoutsStatus is { } status ? $"[{ScreenPalette.Value}]{Escape(status)}[/]" : string.Empty;

        // Every row below is always present, blank when it has nothing to say, so the modal (sized once, when it
        // opens) never has a line pushed off its bottom by a status or a question.
        lines.Add(string.Empty);
        if (_layoutsRenaming is not null)
        {
            lines.Add($"[{ScreenPalette.Label}]type a new name for[/] [{ScreenPalette.Value}]{Escape(Snippet(current.Name))}[/]");
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add(statusLine);
            lines.Add(string.Empty);
            lines.Add($"[{ScreenPalette.Label}][[⏎]] Keep name  [[⌫]] Erase  [[Esc]] Cancel[/]");
            return lines;
        }

        if (_layoutsConfirmingDelete)
        {
            lines.Add($"[{ScreenPalette.Value}]delete {Escape(Snippet(current.Name))}?[/] [{ScreenPalette.Label}]it cannot be brought back[/]");
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add($"[{ScreenPalette.Label}][[y]] Delete  [[Esc]] or any other key keeps it[/]");
            return lines;
        }

        lines.Add($"{Key("s", "save changes")}     {Key("n", "save as new")}");
        lines.Add($"{Key("w", "use for world")}    {Key("c", "use for this character")}");

        // r and d are named only over a layout of the player's own: on a built-in neither does anything but say so.
        lines.Add(current.IsBuiltIn ? string.Empty : $"{Key("r", "rename")}           {Key("d", "delete")}");
        lines.Add(statusLine);
        lines.Add(string.Empty);
        lines.Add($"[{ScreenPalette.Label}]↑↓ pick  [[Esc]] Close  [[⏎]] Apply[/]");
        return lines;
    }
}
