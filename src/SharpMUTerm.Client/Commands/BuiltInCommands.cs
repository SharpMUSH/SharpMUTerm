using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Commands;

/// <summary>
/// The client's own commands, registered into a <see cref="ContributionRegistry"/>: every <c>Switch to…</c>
/// and <c>Go to…</c> entry, the stateful commands that read their current value (<c>Pause logging</c> vs
/// <c>Start logging</c>, <c>Unzoom pane</c>, <c>Resume scrollback</c>), and one <c>Open …</c> entry per settings
/// screen the host offers. What each entry is called, where it is listed, which key it names and when it is
/// offered lives here; what it <em>does</em> is the host's, handed in as <c>run</c>, because that work reaches
/// into the shell (panes, sessions, overlays) and this assembly draws nothing.
/// <para>
/// Registration order is the catalog's order, and it is the order the surface lists entries in within a group.
/// </para>
/// </summary>
public static class BuiltInCommands
{
    /// <summary>The command that moves the keyboard into the connection sidebar (⌥C).</summary>
    public const string RailKeysCommand = "layout:rail-keys";

    /// <summary>
    /// Turns F5's <c>join wraps</c> on for the world named after the prefix: the action of the notice that says a pane is
    /// narrower than the server's wrap column. Lists nothing on ⌃P (F5 is the setting's home).
    /// </summary>
    public const string JoinWrapsPrefix = "world:join-wraps:";

    /// <summary>
    /// Registers every built-in command. <paramref name="run"/> does the work for an id and returns false when
    /// it did not do what the label promised; it is called for every id these contributions handle, offered or not.
    /// </summary>
    public static void Register(ContributionRegistry registry, Func<string, bool> run)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(run);
        var commands = registry.Commands;

        void Add(
            string id,
            CommandGroup group,
            string title,
            KeyHint? key = null,
            string? detail = null,
            Func<ICommandContext, bool>? when = null,
            Func<ICommandContext, string>? titleFor = null,
            bool detailFirst = false) =>
            commands.Add(new SingleCommand
            {
                Id = id,
                Category = group,
                Title = title,
                TitleFor = titleFor,
                Key = key,
                Detail = detail,
                DetailFirst = detailFirst,
                IsAvailable = when,
                Run = _ => run(id),
            });

        void Family(string id, string prefix, Func<ICommandContext, IEnumerable<CommandItem>> generate) =>
            commands.Add(new CommandFamily
            {
                Id = id,
                IdPrefix = prefix,
                Generate = generate,
                Run = (commandId, _) => run(commandId),
            });

        // GO TO — switch character, then jump to windows.
        Family("goto:characters", CommandIds.CharacterPrefix, Characters);
        Family("goto:windows", CommandIds.WindowPrefix, Windows);

        // WORLD
        // Both carry their chord. The surface is where this client is discovered from, and a key nobody
        // can find is the same as no feature — ⌃L's newline sat unused until it was reported missing.
        Add("world:reconnect", CommandGroup.World, "Reconnect", "⌥R");
        Add("world:disconnect", CommandGroup.World, "Disconnect", "⌥D");
        Family("world:send-group", CommandIds.GroupPrefix, SendGroup);
        Family("world:join-wraps", JoinWrapsPrefix, _ => []);
        Add("world:reload-scripts", CommandGroup.World, "Reload scripts",
            detail: "re-reads this character's Lua files", when: c => c.State.ScriptsOn);

        // Only where the host has a screen reader to tell: the window's live region, or --plain.
        Add("a11y:toggle-window", CommandGroup.Terminal, "Read this window aloud",
            when: c => c.State.Announced is not null,
            titleFor: c => c.State.Announced == true ? "Stop reading this window aloud" : "Read this window aloud");

        // TERMINAL — stateful labels.
        Add("term:log-off", CommandGroup.Terminal, "Pause logging", when: c => c.State.LoggingOn);
        Add("term:log-on", CommandGroup.Terminal, "Start logging", when: c => !c.State.LoggingOn);
        Add("term:unfreeze", CommandGroup.Terminal, "Resume scrollback", when: c => c.State.Frozen);
        Add("term:freeze", CommandGroup.Terminal, "Freeze pane", when: c => !c.State.Frozen);
        Add("term:clear", CommandGroup.Terminal, "Clear window");

        // Scrollback position. Here as well as on the keyboard because this surface is where the client
        // is discovered from — every other thing you can do to an output window is listed here — and the
        // subtitles are how the keys get taught: a pane that scrolls and says so nowhere is a pane
        // nobody scrolls. The "back" entry appears only when there is somewhere to come back from.
        Add("term:scroll-oldest", CommandGroup.Terminal, "Scroll to oldest", "⌃Home", "PgUp pages back");
        Add("term:scroll-live", CommandGroup.Terminal, "Back to live output", "⌃End", when: c => c.State.ScrolledBack);

        // The restore log's one control that is not a settings field. It is listed unconditionally, and
        // it is listed *here* rather than only on F9, because "delete what this client has written down
        // about my session" is a thing a person wants to do now — after saying something they would
        // rather was not on disk — and a purge you have to find in a settings screen is a purge that
        // happens tomorrow. The per-character switch on F9 is the other half: this one is immediate and
        // total, that one is a standing preference.
        Add("term:restore-purge", CommandGroup.Terminal, "Purge the restore log",
            detail: "deletes every pane's saved content");

        // Copying output. Listed unconditionally and subtitled with the *gesture* as well as the chord,
        // because the gesture is the part nobody can guess: under mouse reporting a plain drag belongs to
        // the application, so a user who has learnt that their terminal needs ⇧-drag has no reason to try
        // dragging here. It refuses out loud with nothing selected, which is what earns it a row at all.
        Add("term:copy", CommandGroup.Terminal, "Copy the selection", "⌃C",
            "drag across a pane or the command line, or ⇧← ⇧→ there");

        // The trigger builder's way in, only with output selected: the entry is "this line, as a rule".
        Add("term:trigger-from-selection", CommandGroup.Terminal, "Make a trigger from the selection",
            detail: "opens F2 with the rule ready", when: c => c.State.OutputSelected);

        // The client's own messages — the status-line notices that dismiss themselves — kept out of the
        // output window (and so out of the session log) and readable here instead.
        Add("term:messages", CommandGroup.Terminal, "Show client messages", detail: "status-line notices");

        // What the server is sending out of band. Listed unconditionally: a meter or a script is written
        // against a GMCP path, and this is where a player finds out which paths their game has.
        Add("term:gamedata", CommandGroup.Terminal, "Show game data", detail: "GMCP and MSDP this connection holds");

        // The map. Listed unconditionally, like the game data above: a map is recorded from the first room a
        // game reports, and nothing else on screen says the client keeps one.
        Add("term:map", CommandGroup.Terminal, "Show map", detail: "this connection's map · click a room, or /walk <room>");
        // The panels a layout places, openable one at a time as tabs: each draws the focused character's own data
        // and says so when there is none yet, so they are listed unconditionally, like the map.
        Add("panel:who-here", CommandGroup.Terminal, "Show who is here", detail: "players and creatures in the room · click a name to look");
        Add("panel:vitals", CommandGroup.Terminal, "Show vitals", detail: "the F7 meters as bars, and your target");
        Add("panel:quick-bar", CommandGroup.Terminal, "Show quick bar", detail: "this character's function-key macros · click one to run it");
        Add("panel:composer", CommandGroup.Terminal, "Dock the composer", detail: "a pane to write posts in · ⌃S sends");

        // A panel in a window of its own, for a second monitor — only where the host can draw one (the desktop window;
        // Floats is null in a terminal, which keeps every panel in its region). One entry per panel window, by id, so the
        // action runs on that panel's own character whichever window is focused.
        Family("panel:popout", Client.Panels.PanelFloatCommands.PopOutPrefix, PopOuts);
        Family("panel:dock", Client.Panels.PanelFloatCommands.DockPrefix, Docks);
        Add("term:walk-stop", CommandGroup.Terminal, "Stop walking", detail: "ends the speedwalk", when: c => c.State.Walking);

        // Game sound, for this run. The levels and the off switch are F7's; this is the quick one.
        Add("term:mute", CommandGroup.Terminal, "Mute sound", detail: "until unmuted or restarted",
            when: c => c.State.Sound == true);
        Add("term:unmute", CommandGroup.Terminal, "Unmute sound", detail: "game sound is muted",
            when: c => c.State.Sound == false);

        // The command line's own history, browsable and searchable. Named here as well as bound to ⌃R
        // because a chord nothing mentions is a chord nobody finds — the same reason every settings screen
        // has a row even though each has an F-key.
        Add("term:history", CommandGroup.Terminal, "Search command history", "⌃R");

        // A macro on a chord the client also takes, or two macros on one chord: only when there is one.
        Add("settings:key-clashes", CommandGroup.Settings, "Key clashes", "F4", "which binding wins, on the keypad screen",
            when: c => c.State.KeyClashes > 0, titleFor: c => $"Key clashes ({c.State.KeyClashes})");

        // The composer. Named here as well as bound to F1 for the reason the row above it is: a surface
        // nobody can find is a surface nobody uses, and this one is not something a reader would guess
        // at from the command line in front of them.
        Add("term:compose", CommandGroup.Terminal, "Compose a post", "F1", "a full editor, sent as one line");

        // Packages: the list, and the install that starts at the review. F10 can be taken by a terminal's
        // own menu key (GNOME Terminal), so this surface is the way in that always works.
        Add("term:packages", CommandGroup.Terminal, "Packages", "F10", "installed packages, on/off, remove");
        Add("term:package-install", CommandGroup.Terminal, "Install package…", detail: "from a file or an https link");
        Add("term:package-browse", CommandGroup.Terminal, "Browse packages", detail: "the package index, this game's first");

        // The first run's two surfaces, both reachable again: the welcome a client with no worlds opens on, and
        // the one-line tour of the keys worth knowing that follows the first connect. The tour entry is one
        // toggle, so it can always be offered and always says what it will do: show it, or hide it.
        Add("term:welcome", CommandGroup.Terminal, "Welcome", detail: "connect to a game, browse games, pick a layout");
        Add("term:tour", CommandGroup.Terminal, "Key tour", detail: "the keys worth knowing · again to hide it");
        Add("term:timestamps-off", CommandGroup.Terminal, "Hide timestamps", when: c => c.State.TimestampsOn);
        Add("term:timestamps-on", CommandGroup.Terminal, "Show timestamps", when: c => !c.State.TimestampsOn);
        Add("term:input2-off", CommandGroup.Terminal, "Hide second input", "⌃B i", "this window",
            when: c => c.State.SecondInputOn, detailFirst: true);
        Add("term:input2-on", CommandGroup.Terminal, "Show second input", "⌃B i", "this window",
            when: c => !c.State.SecondInputOn, detailFirst: true);

        // The newline chord. Here for the same reason the scrollback keys are: a chord that works and is
        // named nowhere is a chord nobody finds, which is precisely how this one was reported as missing.
        // The host says which chord it is: a terminal reports Shift+⏎ and Ctrl+⏎ as a bare ⏎, so there it is
        // Alt+⏎, and a window keeps Alt+⏎ for itself, so there it is Ctrl+⏎.
        Add("term:newline", CommandGroup.Terminal, "Insert a newline in the command line", KeyHint.ForHost(HostKey.Newline));

        // LAYOUT — every entry carries the chord that runs it, because this surface is where the client
        // is discovered from and the ⌃B keymap is otherwise visible only while the prefix is armed.
        Add("layout:split-right", CommandGroup.Layout, "Split right", "⌃B |");
        Add("layout:split-down", CommandGroup.Layout, "Split down", "⌃B -");
        Add("layout:unzoom", CommandGroup.Layout, "Unzoom pane", "⌃B z", when: c => c.State.Zoomed);
        Add("layout:zoom", CommandGroup.Layout, "Zoom pane", "⌃B z", when: c => !c.State.Zoomed);
        Add("layout:close", CommandGroup.Layout, "Close pane", "⌃B x");

        // Directional pane focus. Listed whether or not the workspace has a second pane, deliberately:
        // this is the surface that teaches the keyboard, and the entries are how a reader learns the
        // workspace splits at all. Each refuses out loud when there is nothing that way.
        Add("layout:focus-left", CommandGroup.Layout, "Focus pane left", "⌃←");
        Add("layout:focus-right", CommandGroup.Layout, "Focus pane right", "⌃→");
        Add("layout:focus-up", CommandGroup.Layout, "Focus pane up", "⌃↑");
        Add("layout:focus-down", CommandGroup.Layout, "Focus pane down", "⌃↓");
        Add("layout:cycle", CommandGroup.Layout, "Focus the next pane", "⌃O", "⌃B o");

        // The tab cycle, beside the pane cycle it rhymes with. Listed unconditionally for the same reason
        // the four directional entries above are: this surface is where the keyboard is learnt, and a
        // reader whose panes each hold one window has no other way to find out that a pane holds tabs at
        // all. ⌃N has always done this and was named on F4 and nowhere else.
        //
        // Listing it obliges it to answer, which the directional entries pay for by refusing out loud and
        // this one did not — it returned in silence on a pane with one tab, which is what a dead key looks
        // like. The refusal is the host's (SharpMUTermApp.NextWindow); the entry is only allowed to exist
        // because it is there.
        Add("layout:next-tab", CommandGroup.Layout, "Focus the next tab", "⌃N");

        // The window's sidebar folds from a button on it; this is the same fold from the keyboard, beside ⌃B b.
        Add("layout:toggle-rail", CommandGroup.Layout, "Fold or unfold the connection rail", "⌃B b");

        // The keyboard's way into the sidebar, in both hosts: ↑↓ walk its rows, ⏎ goes there, Esc returns.
        Add(RailKeysCommand, CommandGroup.Layout, "Move into the connection sidebar", "⌥C", "↑↓ walk it · ⏎ goes · Esc returns");

        Family("layout:panes", CommandIds.PanePrefix, Panes);

        // Layouts: the built-in Roleplay and Combat and the player's own. The name of the one in use is shown
        // nowhere on the main screen, so this entry and its chord are how a reader finds that layouts exist.
        Add("layout:layouts", CommandGroup.Layout, "Layouts", "⌥M", "built in and yours · apply, save, set a default");

        // The ⌥M list's actions as commands, for a surface that is not the list (the window's Layouts menu). They
        // generate no entries: ⌥M is the keyboard's way to them, and listing every layout twice would crowd ⌃P.
        Family("layout:library", LayoutCommands.Prefix, _ => []);

        // Pane size, in the plain words the request used ("increase/decrease a pane's horizontal or
        // vertical character size") rather than in the chord's terms. Listed for the same reason the
        // directional entries and the newline chord are: ⌥⇧+arrow is not a chord anybody guesses, and a
        // feature reachable only by a chord nobody knows is the state ⌃L's newline sat in until it was
        // reported as missing. Listed unconditionally too — each refuses out loud when the focused pane
        // has no border to move that way, which is how a reader learns the workspace resizes at all.
        // The arrow names what happens to *this* pane — ↑ taller, ↓ shorter — from either side of the
        // split; it named the direction the divider travelled until that was reported as inverted.
        Add("layout:wider", CommandGroup.Layout, "Make this pane wider", "⌥⇧→");
        Add("layout:narrower", CommandGroup.Layout, "Make this pane narrower", "⌥⇧←");
        Add("layout:taller", CommandGroup.Layout, "Make this pane taller", "⌥⇧↑");
        Add("layout:shorter", CommandGroup.Layout, "Make this pane shorter", "⌥⇧↓");

        // SETTINGS — one entry per configuration screen, in the order the host lists them (its F-key
        // order). Every screen or none: a surface offering one of them and hiding the rest would read
        // as "this is the only thing you can configure", which is the state the surface was in before.
        Family("settings:screens", CommandIds.ScreenPrefix, Screens);
    }

    /// <summary>
    /// The built-in catalog for this state, with no host behind it: what the surface would list. For a test of
    /// the catalog alone, and for any reader that wants the entries without running them.
    /// </summary>
    public static IReadOnlyList<CommandItem> Catalog(
        Workspace workspace,
        IReadOnlyList<CharacterRef> characters,
        string? focusedSessionKey,
        CommandContext context,
        IReadOnlyList<SettingsEntry>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(context);
        var registry = new ContributionRegistry();
        Register(registry, _ => false);
        return registry.Commands.Catalog(new CommandEnvironment(
            context,
            workspace,
            characters,
            focusedSessionKey,
            settings ?? Array.Empty<SettingsEntry>(),
            new HostKeyGlyphs(context.NewlineChord)));
    }

    // The two neighbours in the character cycle carry its chords. Only those two: ⌥J and ⌥K move one
    // step, so they are the honest answer for the rows either side of you and a lie for anybody
    // further away. There is deliberately no direct-selection chord to put on the rest — the digit
    // row is spent on windows and panes, and every remaining digit-bearing modifier (⌥⇧, ⌃⇧) has no
    // legacy encoding at all on this terminal, which was measured rather than assumed.
    private static IEnumerable<CommandItem> Characters(ICommandContext context)
    {
        var characters = context.Characters;
        var focusedSessionKey = context.FocusedSessionKey;
        var cycle = CommandCatalog.CharacterCycle(characters);
        var here = cycle.FindIndex(c => c.SessionKey == focusedSessionKey);
        var next = here >= 0 && cycle.Count > 1 ? cycle[(here + 1) % cycle.Count].SessionKey : null;
        var previous = here >= 0 && cycle.Count > 1
            ? cycle[(here - 1 + cycle.Count) % cycle.Count].SessionKey
            : null;

        foreach (var character in characters)
        {
            if (character.SessionKey == focusedSessionKey)
            {
                continue;
            }

            var state = character.Connected ? "connected" : "offline";
            var chord = character.SessionKey == next ? "⌥J · "
                : character.SessionKey == previous ? "⌥K · "
                : string.Empty;
            yield return new CommandItem(
                CommandGroup.GoTo,
                $"Switch to {character.CharacterName}",
                CommandIds.Character(character.SessionKey),
                $"{chord}{character.WorldName} · {state}")
            {
                Key = chord.Length == 0 ? null : chord[..^3],
                Detail = $"{character.WorldName} · {state}",
            };
        }
    }

    private static IEnumerable<CommandItem> Windows(ICommandContext context)
    {
        var workspace = context.Workspace;
        var activeWindow = workspace.Layout.FocusedPane.ActiveTab;

        // The chord each window's entry names, from the one place windows are numbered — the *focused*
        // character's list, because that is what ⌥N indexes. A window belonging to somebody else has no
        // chord from here and correctly gets none: pressing ⌥2 would reach the focused character's second
        // window, not this entry, and an entry naming a key that goes elsewhere is the defect the
        // numbering exists to prevent. Built as a lookup rather than read per entry because the entries
        // walk the whole registry, including windows no pane holds.
        var windowOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var reachable = workspace.WindowsFor(context.FocusedSessionKey);
        for (var i = 0; i < reachable.Count && i < CommandIds.WindowJumpDigits; i++)
        {
            windowOrdinals[reachable[i].Id] = i + 1;
        }

        foreach (var window in workspace.Windows)
        {
            // A layout panel is furniture, not a destination: each has its own ⌃P entry (Show vitals, Dock the
            // composer…) that goes to it, so a "Go to Vitals" row would be a second spelling of one action.
            if (window.Id == activeWindow || window.Kind == WindowKind.Panel)
            {
                continue;
            }

            var owner = window.SessionKey ?? "unowned";
            var unread = window.Unread > 0 ? $" · {window.Unread} unread" : string.Empty;

            // The chord leads, because it is the part a reader is here to learn — the owner and the count
            // describe the window and this says how to reach it without the surface at all. Windows past
            // the ninth get the same subtitle minus the chord, rather than one naming a key that would do
            // something else.
            var chord = windowOrdinals.TryGetValue(window.Id, out var ordinal) ? $"⌥{ordinal} · " : string.Empty;
            yield return new CommandItem(
                CommandGroup.GoTo,
                $"Go to {window.Title}",
                CommandIds.Window(window.Id),
                $"{chord}{owner}{unread}")
            {
                Key = chord.Length == 0 ? null : chord[..^3],
                Detail = $"{owner}{unread}",
            };
        }
    }

    // The send group: one line to several characters. Offered once two characters are open, since a
    // group of one is the command line.
    private static IEnumerable<CommandItem> SendGroup(ICommandContext context)
    {
        var characters = context.Characters;
        var open = characters.Where(c => c.Open).ToList();
        if (open.Count <= 1 && !characters.Any(c => c.InGroup))
        {
            yield break;
        }

        foreach (var character in open)
        {
            yield return new CommandItem(
                CommandGroup.World,
                character.InGroup
                    ? $"Remove {character.CharacterName} from the send group"
                    : $"Add {character.CharacterName} to the send group",
                CommandIds.GroupToggle(character.SessionKey),
                character.WorldName);
        }

        var members = characters.Count(c => c.InGroup);
        if (members > 0)
        {
            yield return new CommandItem(
                CommandGroup.World,
                "Send the command line to the group",
                CommandIds.GroupSend,
                members == 1 ? "1 character" : $"{members} characters");
            yield return new CommandItem(CommandGroup.World, "Clear the send group", CommandIds.GroupClear);
        }
    }

    // Numbered pane jumps, one entry per pane that exists — the one group here that is *not* listed
    // unconditionally, because "Go to pane 4" on a workspace with two panes names a place there is no
    // way to make. The number is the one the move and drag overlays badge each pane with, so the entry
    // and the digit a user is about to press in move mode are the same number.
    //
    // The chord is ⌃B N and no longer ⌥N: ⌥N names a *window* now, and a pane and a window are
    // different destinations that cannot share one key. ⌃B is where the rest of the pane keymap lives.
    // Only the first nine carry it, so a tenth pane gets an entry with no subtitle rather than one
    // naming a key that does something else — the honest shape for a place only the mouse, ⌃O, the
    // arrows and this entry can reach.
    private static IEnumerable<CommandItem> Panes(ICommandContext context)
    {
        var paneCount = context.Workspace.Layout.Panes.Count;
        if (paneCount <= 1)
        {
            yield break;
        }

        for (var n = 1; n <= paneCount; n++)
        {
            yield return new CommandItem(
                CommandGroup.Layout,
                $"Go to pane {n}",
                CommandIds.Pane(n),
                n <= CommandIds.PaneJumpDigits ? $"⌃B {n}" : null)
            {
                Key = n <= CommandIds.PaneJumpDigits ? $"⌃B {n}" : null,
            };
        }
    }

    private static IEnumerable<CommandItem> PopOuts(ICommandContext context) =>
        (context.State.Floats ?? []).Where(f => !f.Floating).Select(f =>
            new CommandItem(
                CommandGroup.Layout,
                $"Pop out {f.Title}",
                Client.Panels.PanelFloatCommands.PopOut(f.WindowId),
                $"{f.Character} · in a window of its own")
            {
                Detail = $"{f.Character} · in a window of its own",
            });

    private static IEnumerable<CommandItem> Docks(ICommandContext context) =>
        (context.State.Floats ?? []).Where(f => f.Floating).Select(f =>
            new CommandItem(
                CommandGroup.Layout,
                $"Dock {f.Title} back",
                Client.Panels.PanelFloatCommands.Dock(f.WindowId),
                $"{f.Character} · into its place in the layout")
            {
                Detail = $"{f.Character} · into its place in the layout",
            });

    private static IEnumerable<CommandItem> Screens(ICommandContext context) =>
        context.Settings.Select(screen =>
            new CommandItem(CommandGroup.Settings, $"Open {screen.Title}", screen.Id, screen.Shortcut)
            {
                Key = string.IsNullOrEmpty(screen.Shortcut) ? null : screen.Shortcut,
            });
}
