using System.Globalization;

namespace SharpMUTerm.Core.Workspaces;

/// <summary>
/// The full workspace state the TUI shell drives: the <see cref="WorkspaceLayout"/> pane tree plus
/// the registry of <see cref="WorkspaceWindow"/>s the panes host. It keeps the two consistent —
/// opening a window places it in a pane, closing one removes its tab, spawn routing finds-or-creates
/// the destination window — and tracks activity badges (unread, unsent-input) against visibility.
/// UI-agnostic and fully testable; the SharpConsoleUI layer renders from it and calls its operations.
/// </summary>
public sealed class Workspace
{
    private readonly Dictionary<string, WorkspaceWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>
    /// The last <see cref="WorkspaceWindow.Sequence"/> handed out. Never reused, so a number is a
    /// window's for as long as it is open — see <see cref="WindowsFor"/> for why the ordinal is then
    /// taken from the sorted position rather than from this.
    /// </summary>
    private int _sequenceCounter;

    /// <summary>Creates a workspace with a single main window in one pane.</summary>
    public Workspace(string mainWindowId = "main", string mainTitle = "Main", string? sessionKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(mainWindowId);
        Layout = new WorkspaceLayout(new[] { mainWindowId });
        Register(new WorkspaceWindow(mainWindowId, mainTitle, WindowKind.Main, sessionKey));
    }

    /// <summary>
    /// Rebuilds a workspace from a restored set of windows and a pre-built layout (session resume).
    /// The two are assumed consistent — every window id referenced by a pane tab should have a window.
    /// <para>
    /// A window restored without a <see cref="WorkspaceWindow.Sequence"/> is seeded from the order it
    /// arrived in — the numbering it was saved under. Sequenced windows keep theirs and unsequenced ones
    /// are numbered after the highest taken, so a half-migrated set cannot produce two windows with one
    /// number. Same shape as <see cref="WorkspaceLayout"/>'s restoring constructor.
    /// </para>
    /// </summary>
    public Workspace(IEnumerable<WorkspaceWindow> windows, WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(windows);
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        var restored = windows.ToList();
        foreach (var window in restored)
        {
            _windows[window.Id] = window;
        }

        _sequenceCounter = restored.Select(w => w.Sequence).DefaultIfEmpty(WorkspaceWindow.Unsequenced).Max();
        foreach (var window in restored.Where(w => w.Sequence <= WorkspaceWindow.Unsequenced))
        {
            window.Sequence = ++_sequenceCounter;
        }
    }

    /// <summary>The pane tree.</summary>
    public WorkspaceLayout Layout { get; }

    /// <summary>
    /// Every known window, whether or not a pane still holds it. The order is the registry's, which is a
    /// dictionary's — fine for "what exists", and <b>not</b> what anything numbers windows by; see
    /// <see cref="WindowsFor"/>.
    /// </summary>
    public IReadOnlyCollection<WorkspaceWindow> Windows => _windows.Values;

    /// <summary>
    /// <b>The windows <paramref name="sessionKey"/> can reach, in creation order — the one order this
    /// client numbers windows in.</b> The Nth entry is the window ⌥N goes to while that character is
    /// active, the <c>⌥N</c> the connection rail prints on that window's row, and the chord the ⌃P
    /// <c>Go to …</c> entry for it names. Those are three spellings of this index and there is
    /// deliberately no second ordering for any of them to drift onto.
    /// <para>
    /// <b>Scoped to the active character, re-based from 1.</b> Global numbering gives every character
    /// sharing a pane the same <c>⌥1</c>, and nine digits do not stretch across everybody's windows.
    /// Scoped, ⌥1 is <em>this</em> character's main window whoever you are.
    /// </para>
    /// <para>
    /// <b>An unowned window is in everybody's list</b> — the web view is reachable from anywhere, so it
    /// takes a digit under each character. Not a second numbering: this is exactly the set the rail draws
    /// window rows for, so screen and chord are one list read twice.
    /// </para>
    /// <para>
    /// <b>Creation order</b>, because any ordering that is a function of <em>where</em> a window sits
    /// moves when something is inserted before it, and a digit must not stop meaning what it meant.
    /// <b>The number is the index, not the sequence</b> — sequences are never reused, so reading them
    /// directly would leave a digit that does nothing with windows still on the screen. <b>Placed only</b>,
    /// since a window no pane holds is drawn <c>closed</c> and a digit for it names nowhere to go.
    /// </para>
    /// </summary>
    /// <param name="sessionKey">
    /// The active character, or null when nothing is active — which leaves the unowned windows, the only
    /// ones there is anywhere to go to.
    /// </param>
    public IReadOnlyList<WorkspaceWindow> WindowsFor(string? sessionKey) =>
        _windows.Values
            .Where(w => Layout.FindWindow(w.Id) is not null)
            .Where(w => w.SessionKey is null || string.Equals(w.SessionKey, sessionKey, StringComparison.Ordinal))
            .OrderBy(w => w.Sequence)
            .ToList();

    /// <summary>Files a window in the registry, giving it the next creation sequence.</summary>
    private WorkspaceWindow Register(WorkspaceWindow window)
    {
        window.Sequence = ++_sequenceCounter;
        _windows[window.Id] = window;
        return window;
    }

    /// <summary>Looks up a window by id, or null.</summary>
    public WorkspaceWindow? FindWindow(string id) => _windows.GetValueOrDefault(id);

    /// <summary>
    /// Opens a window: registers it (if new) and places it as a tab. An existing id updates nothing
    /// but is returned so callers can treat open as idempotent. Placement defaults to the focused
    /// pane. Returns the window.
    /// </summary>
    public WorkspaceWindow OpenWindow(
        string id,
        string title,
        WindowKind kind = WindowKind.Auxiliary,
        string? sessionKey = null,
        string? paneId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (_windows.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var window = Register(new WorkspaceWindow(id, title, kind, sessionKey));
        Layout.AddWindow(id, paneId);
        return window;
    }

    /// <summary>
    /// Routes a matched trigger's line to the window <paramref name="target"/> names, on behalf of
    /// <paramref name="sessionKey"/>: <b>a window that already exists wins, and a spawn window is what
    /// happens when nothing answers</b>. Counts the line as unread unless the destination is currently
    /// being read, and returns it.
    /// <para>
    /// <b>What a target may reach is narrower than "any window with that title":</b> this session's own
    /// windows, the unowned ones, and another character's <em>main</em> window — one alt's channel
    /// collected into the pane you read. Never another session's spawn window, or two characters running
    /// one capture rule would collapse into a single pane filed under whoever matched first.
    /// </para>
    /// <para>
    /// <b>Only a placed window is a destination</b> — appending to a window no pane holds is
    /// indistinguishable from the rule not firing. <b>Finding never creates:</b> a target may be a
    /// template filled from the server's own text, and what bounds that is this arm only ever landing in
    /// a window the user already has.
    /// </para>
    /// </summary>
    public WorkspaceWindow RouteLine(string target, string? sessionKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        if (FindRouteTarget(target, sessionKey) is not { } existing)
        {
            return RouteSpawn(target, sessionKey);
        }

        NoteActivity(existing.Id);
        return existing;
    }

    /// <summary>
    /// The window <paramref name="target"/> already names for <paramref name="sessionKey"/>, or null when
    /// nothing does — the finding half of <see cref="RouteLine"/>, with no side effects, so a caller can
    /// tell "this line opened a pane" from "this line went to one that was already there" without
    /// routing twice. See <see cref="RouteLine"/> for what a target may and may not reach.
    /// </summary>
    public WorkspaceWindow? FindRouteTarget(string target, string? sessionKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);

        // Preference order, and it has to be total: several windows may carry one title, and a route that
        // resolved differently from one line to the next would scatter a channel across panes. This
        // session's own first, then the unowned, then another character's main; ties inside a group go to
        // the older window, which is the same creation order everything else here numbers windows in.
        var best = _windows.Values
            .Where(w => string.Equals(w.Title, target, StringComparison.Ordinal))
            .Where(w => Layout.FindWindow(w.Id) is not null)
            .Select(w => (Window: w, Rank: RouteRank(w, sessionKey)))
            .Where(candidate => candidate.Rank >= 0)
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Window.Sequence)
            .Select(candidate => candidate.Window)
            .FirstOrDefault();

        // A renamed spawn window answers to no title, and its rule must go on feeding it rather than
        // opening a second pane under the old name. Placed only, like the lookup above: the registry
        // outlives the layout, and routing to a closed window writes into a buffer nobody can see.
        // Falling through costs nothing — RouteSpawn re-places this same id, history and all.
        var renamed = _windows.GetValueOrDefault(SpawnWindowId(sessionKey, target));
        return best ?? (renamed is not null && Layout.FindWindow(renamed.Id) is not null ? renamed : null);
    }

    /// <summary>
    /// How willingly <paramref name="window"/> takes a line routed by <paramref name="sessionKey"/> —
    /// lower is better, and negative means never.
    /// </summary>
    private static int RouteRank(WorkspaceWindow window, string? sessionKey) => window switch
    {
        _ when window.SessionKey is not null && string.Equals(window.SessionKey, sessionKey, StringComparison.Ordinal) => 0,
        _ when window.SessionKey is null => 1,
        _ when window.Kind == WindowKind.Main => 2,
        _ => -1,
    };

    /// <summary>
    /// Routes trigger-spawned output to <paramref name="sessionKey"/>'s spawn window named
    /// <paramref name="target"/>, creating and placing the window on first use, and counts the line as
    /// unread unless the window is currently visible. Returns the destination window.
    /// <para>
    /// <b>This is the creating half only</b>; <see cref="RouteLine"/> is what a routed line goes through,
    /// and it reaches here when no window the target names already exists.
    /// </para>
    /// <para>
    /// <b>The destination is per session, not per workspace.</b> The id carries the owner, so two
    /// characters running one capture rule get a window each and the second cannot land in the first's —
    /// where it would also be invisible, since the rail draws window rows for the active character only.
    /// </para>
    /// </summary>
    public WorkspaceWindow RouteSpawn(string target, string? sessionKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        var id = SpawnWindowId(sessionKey, target);
        var window = _windows.TryGetValue(id, out var existing)
            ? existing
            : Register(new WorkspaceWindow(id, target, WindowKind.Spawn, sessionKey));

        // Placed on the way past, not only when the window is new: the registry outlives the layout, so a
        // window can be known and closed at once. Making this total is what lets FindRouteTarget decline
        // a closed window and fall through here.
        if (Layout.FindWindow(id) is null)
        {
            Layout.AddWindow(id, activate: false); // spawns open in the background and accrue unread
        }

        NoteActivity(id);
        return window;
    }

    /// <summary>Every spawn window id starts with this.</summary>
    public const string SpawnPrefix = "spawn:";

    /// <summary>
    /// The owner field of a spawn window that belongs to nobody. A single <c>-</c> rather than the empty
    /// string, because it is not a decimal length and so cannot be mistaken for one.
    /// </summary>
    private const string Unowned = "-";

    /// <summary>
    /// The window id the spawn <paramref name="target"/> of the session <paramref name="sessionKey"/>
    /// routes to. Unique per <c>(owner, target)</c> and stable for ever, so a reconnect or a restart
    /// comes back to the pane it left.
    /// <para>
    /// <b>Why the length prefix.</b> Both halves are user-controlled and may contain colons, so joining
    /// them with a separator is not injective — <c>(a, b:c)</c> and <c>(a:b, c)</c> would collapse two
    /// characters' panes into one. The owner's length in front makes the encoding total and reversible.
    /// </para>
    /// <para>
    /// Legible rather than hashed: this id is a dictionary key, a <c>config.json</c> value and the stem
    /// of a <c>RestoreLog</c> file name, and a digest would buy no property a reversible encoding lacks
    /// while making all three unreadable.
    /// </para>
    /// </summary>
    /// <param name="sessionKey">The owning <c>world.character</c> session, or null for a window nobody owns.</param>
    /// <param name="target">The capture target, which is also the window's title.</param>
    public static string SpawnWindowId(string? sessionKey, string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        return sessionKey is null
            ? $"{SpawnPrefix}{Unowned}:{target}"
            : $"{SpawnPrefix}{sessionKey.Length}:{sessionKey}:{target}";
    }

    /// <summary>
    /// Reads a spawn window id back into the pair that made it. False when <paramref name="id"/> is not
    /// one this build writes — including a spawn id from before the owner was in it, which is what
    /// <c>ConfigurationMigrator</c>'s v4→v5 step upgrades.
    /// </summary>
    public static bool TryReadSpawnWindowId(string id, out string? sessionKey, out string target)
    {
        sessionKey = null;
        target = string.Empty;
        if (id is null || !id.StartsWith(SpawnPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = id.AsSpan(SpawnPrefix.Length);
        if (rest.StartsWith(Unowned + ":", StringComparison.Ordinal))
        {
            target = rest[(Unowned.Length + 1)..].ToString();
            return target.Length > 0;
        }

        // NumberStyles.None: bare digits only, so a sign or surrounding space is not silently accepted
        // into a field whose whole job is to say how many characters to take.
        var separator = rest.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(rest[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            return false;
        }

        // The owner's exact length, then the colon that closes it: anything shorter is not this
        // encoding. Written as `<=` rather than `< length + 1` so a declared length of int.MaxValue
        // cannot overflow the comparison into passing and then index off the end.
        var owner = rest[(separator + 1)..];
        if (owner.Length <= length || owner[length] != ':')
        {
            return false;
        }

        sessionKey = owner[..length].ToString();
        target = owner[(length + 1)..].ToString();
        return target.Length > 0;
    }

    /// <summary>
    /// Records a line arriving in a window: increments its unread badge unless the window is
    /// <see cref="IsCaughtUp"/> — visible <em>and</em> showing its live tail. Unknown ids are ignored.
    /// </summary>
    public void NoteActivity(string windowId)
    {
        if (_windows.TryGetValue(windowId, out var window) && !IsCaughtUp(windowId))
        {
            window.Unread++;
        }
    }

    /// <summary>
    /// Makes a window the active tab of its pane, focuses that pane, and clears its unread badge — but
    /// only if the window is showing its live tail. A window the viewer had scrolled back keeps its
    /// badge when its tab is picked, because the unread lines are still below the viewport; returning to
    /// the bottom (<see cref="SetScrolledBack"/> with <c>false</c>) is what clears it.
    /// Returns false if the window is not placed in any pane.
    /// </summary>
    public bool ActivateWindow(string windowId)
    {
        var pane = Layout.FindWindow(windowId);
        if (pane is null || !_windows.ContainsKey(windowId))
        {
            return false;
        }

        Layout.SetActiveTab(pane.Id, windowId);
        Layout.Focus(pane.Id);
        if (!_windows[windowId].ScrolledBack)
        {
            _windows[windowId].Unread = 0;
        }

        return true;
    }

    /// <summary>
    /// Records whether the shell has this window's output scrolled back off its live tail. Returning to
    /// the bottom of a <em>visible</em> window is the reader catching up, so it clears the unread badge
    /// the same way picking the tab does. Unknown ids are ignored.
    /// </summary>
    /// <returns>True when the flag changed, so a caller can skip repainting badges that cannot have moved.</returns>
    public bool SetScrolledBack(string windowId, bool scrolledBack)
    {
        if (!_windows.TryGetValue(windowId, out var window) || window.ScrolledBack == scrolledBack)
        {
            return false;
        }

        window.ScrolledBack = scrolledBack;
        if (!scrolledBack && IsVisible(windowId))
        {
            window.Unread = 0;
        }

        return true;
    }

    /// <summary>Sets the unsent-input marker for a window. Unknown ids are ignored.</summary>
    public void SetUnsentInput(string windowId, bool hasUnsent)
    {
        if (_windows.TryGetValue(windowId, out var window))
        {
            window.HasUnsentInput = hasUnsent;
        }
    }

    /// <summary>
    /// Records who owns a window, for the case where a session takes over one that already exists.
    /// Unknown ids are ignored.
    /// <para>
    /// Ownership is otherwise fixed at creation, and the main window is opened before any session
    /// exists — so without this the rail and the command surface would keep naming whoever held it
    /// before the adopting session arrived.
    /// </para>
    /// </summary>
    public void SetWindowOwner(string windowId, string? sessionKey)
    {
        if (_windows.TryGetValue(windowId, out var window))
        {
            window.SessionKey = sessionKey;
        }
    }

    /// <summary>Closes a window: removes its tab (pruning empty panes) and forgets its state.</summary>
    public bool CloseWindow(string windowId)
    {
        if (!_windows.Remove(windowId))
        {
            return false;
        }

        Layout.RemoveWindow(windowId);
        return true;
    }

    /// <summary>True when the window is the active tab of the pane that hosts it.</summary>
    public bool IsVisible(string windowId) =>
        Layout.FindWindow(windowId) is { } pane && pane.ActiveTab == windowId;

    /// <summary>
    /// True when a line arriving here would land somewhere the reader can see it: the visible tab of its
    /// pane <em>and</em> not scrolled back. This is the condition unread badging turns on;
    /// <see cref="IsVisible"/> alone would badge nothing while the reader sits in their scrollback.
    /// </summary>
    public bool IsCaughtUp(string windowId) =>
        IsVisible(windowId) && _windows.TryGetValue(windowId, out var window) && !window.ScrolledBack;
}
