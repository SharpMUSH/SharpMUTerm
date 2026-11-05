using SharpConsoleUI.Controls;
using SharpConsoleUI;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Tui.Panels;

namespace SharpMUTerm.Tui;

/// <summary>
/// The four panels a layout can place that the workspace did not already have: who is here, the vitals strip,
/// the quick bar, and the docked composer. Each is a control made once per (panel, character) by the factory
/// registered here, drawn from that character's own data — never <c>_active</c>'s — and sized to its pane on the
/// frame, the way a map window is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Links are per surface.</b> A who-here row and a quick-bar entry are links, answered by that panel's own
/// <c>LinkClicked</c> and nothing else; the payload is an index into what the panel last drew
/// (<c>mux:who:3</c>, <c>mux:quick:1</c>), so no text from the game is ever read back as a command, and the output
/// panes' handler never sees either scheme.
/// </para>
/// <para>
/// <b>The docked composer can hold the keyboard</b>, and that is decided in one place (<see cref="ArmedComposer"/>):
/// it is armed while its window is the focused pane's tab and the writer has not handed the keyboard back with Esc.
/// While it is armed the focus pin (<see cref="PinFocusToArmedBar"/>) holds the <em>editor</em>, so typing, paste
/// and the caret go there together, and both command lines wear the idle band. Moving to its pane (⌃arrows, ⌃O, a
/// tab click) or clicking in it arms it; Esc, ⌃↓ onto a command line, or moving to another pane disarms it.
/// </para>
/// </remarks>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The markup panels, by window id.</summary>
    private readonly Dictionary<string, PanelView> _panelViews = new(StringComparer.Ordinal);

    /// <summary>The docked composers, by window id.</summary>
    private readonly Dictionary<string, DockedComposer> _dockedComposers = new(StringComparer.Ordinal);

    /// <summary>Who is in the demo's room: written by <see cref="RoomOccupants.Apply"/>, as the wire writes a session's.</summary>
    private readonly RoomOccupants _demoOccupants = new();

    /// <summary>The window <see cref="ArmedComposer"/> last saw in front, so arriving at a composer re-arms it.</summary>
    private string? _composerArmWindow;

    /// <summary>Whether Esc (or ⌃↓ onto a bar) handed the keyboard back from the composer in front.</summary>
    private bool _composerReleased;

    /// <summary>When an Esc released a composer, so the Enter of a ⌥⏎ that follows it is not sent from the bar.</summary>
    private DateTimeOffset? _composerEscapeAt;

    partial void RegisterTerminalPanels()
    {
        Panels.Register(PanelIds.WhoHere, NewWhoHerePanel);
        Panels.Register(PanelIds.Vitals, NewVitalsPanel);
        Panels.Register(PanelIds.QuickBar, NewQuickBarPanel);
        Panels.Register(PanelIds.Composer, NewDockedComposer);
    }

    /// <summary>The ⌃P ids that open a panel: <c>panel:</c> and the panel's id.</summary>
    private const string PanelCommandPrefix = "panel:";

    /// <summary>
    /// ⌃P ▸ <c>Show who is here</c> and its siblings: opens the focused character's panel as a tab of the focused
    /// pane, or goes to it. A window that belongs to no character refuses, for <see cref="ShowMap"/>'s reason.
    /// </summary>
    private void ShowPanel(string panelId)
    {
        var key = SendTarget()?.SessionKey ?? _workspace.FindWindow(ActiveWindowId())?.SessionKey;
        if (key is not { Length: > 0 })
        {
            RefuseCommand("no character here — pick one first (⌃P ▸ Switch to …)");
            return;
        }

        if (OpenPanel(panelId, key) is not { } windowId)
        {
            RefuseCommand($"this client cannot draw a {Snippet(panelId)} panel");
            return;
        }

        Activate(windowId);
    }

    // --- Who is here ------------------------------------------------------------------------------------------

    private IWindowControl NewWhoHerePanel(TerminalPanelContext context)
    {
        IReadOnlyList<WhoHereRow> drawn = [];
        var view = NewPanelView(context, (width, height) =>
        {
            var model = Drawing(PanelIds.WhoHere, context.WindowId, ReadWhoHere(context.SessionKey));
            drawn = model.Rows;
            return WhoHereRenderer.Render(model, width, height, new WhoHereRenderer.Ink(_ink.Accent, PanelHeadingInk()));
        });
        view.Click = url =>
        {
            if (WhoHereRenderer.ParseLink(url) is { } index && index < drawn.Count)
            {
                OfferLook(context.WindowId, context.SessionKey, drawn[index].Name);
            }
        };

        if (context.Session is { } session)
        {
            session.Occupants.Changed += (_, _) => OnUiThread(view.Refresh);
            session.GameData.Changed += (_, package) =>
            {
                if (package.StartsWith("Room.", StringComparison.OrdinalIgnoreCase)
                    || package.StartsWith("MSDP.ROOM", StringComparison.OrdinalIgnoreCase))
                {
                    OnUiThread(view.Refresh);
                }
            };
        }
        else if (IsDemoKey(context.SessionKey))
        {
            _demoOccupants.Changed += (_, _) => OnUiThread(view.Refresh);
        }

        return view.Control;
    }

    /// <summary>
    /// A character's who-here panel as the one model every view draws (<see cref="WhoHereModel"/>): the cell rows
    /// here, and the window's native list through <see cref="PublishPanels"/>.
    /// </summary>
    internal WhoHereModel ReadWhoHere(string sessionKey)
    {
        if (PanelOccupants(sessionKey) is not { Known: true } roster)
        {
            return WhoHereModel.Unknown;
        }

        var data = PanelGameData(sessionKey);
        var room = data is null ? null : StatusMeter.Read(data, "Room.Info.name|MSDP.ROOM_NAME");
        return WhoHereModel.From(room, roster.Snapshot);
    }

    /// <summary>
    /// A click on a name puts <c>look Name</c> on the character's command line and sends nothing: the name is the
    /// game's text, and the player sees the line before ⏎. It lands on the bar of the panel's own character — the
    /// panel's window is brought forward when the one in front belongs to someone else.
    /// </summary>
    private void OfferLook(string windowId, string sessionKey, string name)
    {
        var owner = WindowSession(ActiveWindowId())?.SessionKey ?? _workspace.FindWindow(ActiveWindowId())?.SessionKey;
        if (!string.Equals(owner, sessionKey, StringComparison.Ordinal))
        {
            Activate(windowId);
        }

        if (ArmedComposer() is not null)
        {
            ReleaseComposer();
        }

        PlaceGrab(ActiveBar(), $"look {name}", sessionKey);
        Notice($"look {Snippet(name)} is on the command line — ⏎ sends it", MessageSeverity.Info);
    }

    // --- Vitals -----------------------------------------------------------------------------------------------

    private IWindowControl NewVitalsPanel(TerminalPanelContext context)
    {
        var view = NewPanelView(context, (width, height) =>
        {
            if (ReadVitals(context.SessionKey) is not { } vitals)
            {
                return ["[dim]No vitals: this character is not connected.[/]"];
            }

            var drawn = Drawing(PanelIds.Vitals, context.WindowId, vitals);
            if (drawn.IsEmpty)
            {
                return ["[dim]No vitals yet — the game has sent none of the meters F7 names.[/]"];
            }

            // A pane in a side column is a card (PanelSizing.ApplyAll made it tall); a strip is one row.
            return height >= 3
                ? [.. VitalsStripRenderer.RenderCard(drawn, width, height, VitalsInk())]
                : [VitalsStripRenderer.Render(drawn, width, VitalsInk())];
        });

        if (context.Session is { } session)
        {
            session.GameData.Changed += (_, _) => OnUiThread(() =>
            {
                view.Refresh();
                ResizeVitalsCard(context.WindowId);
            });
            session.MxpMetersChanged += (_, _) => OnUiThread(() =>
            {
                view.Refresh();
                ResizeVitalsCard(context.WindowId);
            });
        }

        return view.Control;
    }

    /// <summary>
    /// A character's vitals as the one model every view draws (<see cref="VitalsModel"/>): the cell strip here, and
    /// the window's native gauges through <see cref="PublishPanels"/>. Null when the character has no game data.
    /// </summary>
    internal VitalsModel? ReadVitals(string sessionKey)
    {
        if (PanelGameData(sessionKey) is not { } data)
        {
            return null;
        }

        var configured = PanelMeters(sessionKey);
        var meters = StatusMeterRenderer.Read(configured, data)
            .Select(m => new VitalsMeter(m.Label, m.Value, m.Max, m.HasMax, m.Share, m.Low, m.Colour))
            .ToList();

        // Where each value was found: the first of a meter's alternatives the game has sent, as the reading took it.
        var answered = configured
            .Select(m => StatusMeter.Alternatives(m.Value).FirstOrDefault(path => data.GetText(path) is not null))
            .OfType<string>();
        return new VitalsModel(meters, VitalsModel.TargetFrom(CombatTarget.Read(data)))
        {
            Source = VitalsModel.SourceOf(answered),
        };
    }

    /// <summary>
    /// The strip's colours, each held to the floor against the plane it lands on: the bars are fills but carry
    /// meaning, so they are lifted like text; the empty part of a bar is a track of solid blocks in the rule colour, a fill rather than text.
    /// </summary>
    private VitalsStripRenderer.Ink VitalsInk() =>
        new(_ink.Accent, LowInk(_ink.Plane), WorkspacePalette.Rule(_theme).ToHex(), PanelHeadingInk(),
            colour => MeterInk(colour, _ink.Plane));

    /// <summary>The low cue's ink on <paramref name="plane"/> (<see cref="WorkspacePalette.Low"/>), as markup.</summary>
    private string LowInk(Rgb plane) => WorkspacePalette.Low(_theme, plane).ToHex();

    /// <summary>The plane the panels' inks are held against (the panes' reading plane). Internal for tests.</summary>
    internal Rgb PanelPlane => _ink.Plane;

    /// <summary>A meter's colour (<see cref="MeterColours"/>) held to the floor on <paramref name="plane"/>, as markup.</summary>
    private static string MeterInk(string colour, Rgb plane) => Contrast.Legible(ParseHex(colour), plane).ToHex();

    /// <summary>A meter's label on the status row: its colour held to the row's small-text floor on the row's plane.</summary>
    private string StatusMeterInk(string colour) =>
        Contrast.Legible(ParseHex(colour), _theme.StatusBackground, WorkspacePalette.HeaderTextFloor).ToHex();

    /// <summary>
    /// The ink a dangerous binding (<see cref="Macro.Danger"/>) is drawn in: the settings screens' warning red, held to the
    /// floor on the panes' reading plane.
    /// </summary>
    private string DangerInk() => Contrast.Legible(ParseHex(ScreenPalette.Warn), _ink.Plane).ToHex();

    // --- Quick bar --------------------------------------------------------------------------------------------

    private IWindowControl NewQuickBarPanel(TerminalPanelContext context)
    {
        IReadOnlyList<QuickBarItem> drawn = [];
        var view = NewPanelView(context, (width, _) =>
        {
            var model = Drawing(PanelIds.QuickBar, context.WindowId, QuickBarEntries(context.SessionKey));
            drawn = model.Items;
            return [QuickBarRenderer.Render(model, width, WorkspacePalette.HeaderMuted(_theme, _ink.Plane).ToHex(), DangerInk())];
        });
        view.Click = url =>
        {
            if (url == QuickBarRenderer.EditLink)
            {
                DispatchCommand(QuickBarModel.EditCommand);
            }
            else if (QuickBarRenderer.ParseLink(url) is { } index && index < drawn.Count)
            {
                RunQuickMacro(context.SessionKey, drawn[index].Descriptor);
            }
        };
        return view.Control;
    }

    /// <summary>
    /// The function-key bindings a character's quick bar lists: its session's engine when it has one (so a script's
    /// runtime binding shows), else the bindings its trigger sets configure.
    /// </summary>
    internal QuickBarModel QuickBarEntries(string sessionKey)
    {
        if (FindSession(sessionKey) is { } session)
        {
            return QuickBarRenderer.Entries(Host.Keys, session.Macros.Macros, session.Macros.Resolve);
        }

        if (FindCharacter(sessionKey) is not { } found)
        {
            return QuickBarModel.Empty;
        }

        var macros = _config.ResolveTriggerSets(found.Character, found.World).SelectMany(s => s.Macros).ToList();
        return QuickBarRenderer.Entries(Host.Keys, macros, key => macros.FirstOrDefault(
            m => string.Equals(MacroKey.Canonicalise(m.Key), key, StringComparison.OrdinalIgnoreCase)) is { Enabled: true } m ? m : null);
    }

    /// <summary>
    /// A click on an entry runs that binding through <see cref="WorldSession.HandleKeyAsync"/> — the path the key
    /// itself takes — on the panel's own character, never the one in front.
    /// </summary>
    private void RunQuickMacro(string sessionKey, string descriptor)
    {
        if (FindSession(sessionKey) is not { } session)
        {
            RefuseCommand($"{Snippet(DisplayName(sessionKey))} has no open session — ⌃P ▸ Switch to it opens one");
            return;
        }

        _ = session.HandleKeyAsync(descriptor);
        LastQuickMacro = descriptor;
    }

    /// <summary>The descriptor the quick bar last ran, for the tests.</summary>
    internal string? LastQuickMacro { get; private set; }

    /// <summary>
    /// The band a docked panel's heading is drawn on in the cells (<see cref="HeadingBand"/>), or null for the bare accent
    /// label. Null where the host draws the panels natively (<see cref="HostChrome.Strips"/>, the window): a native view lies
    /// over the heading row, so the band is never seen there except where the window reads the cells beside a divider for
    /// the planes either side of its hairline, and a band there drew a filled bar across the seam between two panels.
    /// </summary>
    private HeadingBand.Ink? PanelHeadingInk() => Host.Chrome.Strips ? null : HeadingBand.For(_theme, _ink.Accent);

    // --- The docked composer ----------------------------------------------------------------------------------

    private IWindowControl NewDockedComposer(TerminalPanelContext context)
    {
        var composer = new DockedComposer(context.WindowId, context.SessionKey, _screens, _ink.Accent, DescribeDocked, PanelHeadingInk());
        if (_composeDrafts.TryGetValue(context.SessionKey, out var draft))
        {
            composer.Load(draft.Body, draft.Escaping, draft.Verb);
        }

        composer.Changed += (_, _) => KeepDockedDraft(composer);
        _dockedComposers[context.WindowId] = composer;
        var box = PanelBox(context.WindowId);
        composer.Fit(box.Width, box.Height);
        return composer.Control;
    }

    /// <summary>
    /// Writes the docked buffer into the draft store F1 opens from, so the two are one draft. An empty buffer
    /// forgets the draft, as an F1 window closed empty does.
    /// </summary>
    private void KeepDockedDraft(DockedComposer composer)
    {
        if (composer.Body.Trim().Length == 0 && composer.Verb.Length == 0)
        {
            _composeDrafts.Remove(composer.SessionKey);
            return;
        }

        _composeDrafts[composer.SessionKey] = new ComposeResult(composer.Body, composer.Escaping, Verb: composer.Verb);
    }

    /// <summary>Puts a character's kept draft back into its docked composers, after F1 changed it.</summary>
    private void SyncDockedComposers(string? sessionKey)
    {
        foreach (var composer in _dockedComposers.Values)
        {
            if (sessionKey is null || string.Equals(composer.SessionKey, sessionKey, StringComparison.Ordinal))
            {
                var draft = _composeDrafts.GetValueOrDefault(composer.SessionKey);
                composer.Load(draft.Body ?? string.Empty, draft.Body is null ? composer.Escaping : draft.Escaping, draft.Verb ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// The composer that holds the keyboard, or null: the docked composer whose window is the focused pane's tab,
    /// unless the writer handed the keyboard back. Arriving at a composer's window clears the hand-back, so moving
    /// to its pane is moving into it.
    /// </summary>
    private DockedComposer? ArmedComposer()
    {
        var windowId = ActiveWindowId();
        if (!string.Equals(windowId, _composerArmWindow, StringComparison.Ordinal))
        {
            _composerArmWindow = windowId;
            _composerReleased = false;
        }

        return !_composerReleased && _dockedComposers.TryGetValue(windowId, out var composer) ? composer : null;
    }

    /// <summary>The band the primary command line is painting itself in now.</summary>
    internal SharpConsoleUI.Color? PrimaryBandColor => _input.BackgroundColor;

    /// <summary>Whether a docked composer holds the keyboard.</summary>
    internal bool ComposerArmed => ArmedComposer() is not null;

    /// <summary>The docked composer drawn in a panel window, for the tests and the snapshot views.</summary>
    internal DockedComposer? DockedComposerIn(string windowId) => _dockedComposers.GetValueOrDefault(windowId);

    /// <summary>Whether the framework's focus is on a docked composer's editor.</summary>
    internal bool ComposerHasFocus =>
        _window.FocusManager.FocusedControl is { } focused && _dockedComposers.Values.Any(c => c.Owns(focused));

    /// <summary>
    /// Paints who holds the keyboard: the armed composer's header marker, and both command lines idle while a
    /// composer has it — ⏎ there inserts a line, it does not send — or while ⌥C has put the keys on the sidebar, where ⏎
    /// goes to a row. Cells in both hosts, so the window's command line follows with nothing of its own.
    /// </summary>
    private void SyncComposerBands()
    {
        var armed = ArmedComposer();

        // The dictionary is opened the first time a docked composer takes the keyboard, as F1 opens it on its first
        // opening: a client that never writes a post reads none.
        if (armed is { Spelling: null } && ComposerSpelling() is { } checker)
        {
            armed.UseSpelling(checker, _screens.Colour(ScreenPalette.Warn));
        }

        foreach (var composer in _dockedComposers.Values)
        {
            composer.Armed = ReferenceEquals(composer, armed);
        }

        var barHolds = armed is null && _railCursor is null;
        _input.Armed = barHolds && ReferenceEquals(_armed, _input);
        _second.Armed = barHolds && ReferenceEquals(_armed, _second);
        SyncStatusMode(); // the composer's keys on the status row, or the pane's again
    }

    /// <summary>Re-derives who holds the keyboard after the focused pane or window changed.</summary>
    private void SyncComposerArm()
    {
        SyncComposerBands();
        PinFocusToArmedBar();
    }

    /// <summary>Esc in the composer: the keyboard goes back to the command line; the pane stays where it is.</summary>
    private void ReleaseComposer()
    {
        _composerReleased = true;
        SyncComposerBands();
        PinFocusToArmedBar();
    }

    /// <summary>
    /// A click put the framework's focus on a docked composer's editor: arm that composer, bringing its window to
    /// the front when it is not. Called on the framework's <c>FocusChanged</c>, ahead of the focus pin, which would
    /// otherwise take the focus straight back; never from the pin itself, which runs while the editor still has the
    /// focus it is about to lose.
    /// </summary>
    private void ArmClickedComposer()
    {
        if (_pinningFocus || !_window.GetIsActive() || _window.FocusManager.FocusedControl is not { } focused
            || _dockedComposers.Values.FirstOrDefault(c => c.Owns(focused)) is not { } clicked)
        {
            return;
        }

        // A click in either field puts the keyboard in that field, armed or not.
        clicked.InCommand = ReferenceEquals(focused, clicked.CommandRow.Field);
        if (ReferenceEquals(clicked, ArmedComposer()))
        {
            return;
        }

        if (!string.Equals(ActiveWindowId(), clicked.WindowId, StringComparison.Ordinal))
        {
            _pinningFocus = true;
            try
            {
                Activate(clicked.WindowId);
            }
            finally
            {
                _pinningFocus = false;
            }
        }

        _composerArmWindow = clicked.WindowId;
        _composerReleased = false;
        SyncComposerBands();
    }

    /// <summary>
    /// A key while a docked composer holds the keyboard. The workspace gestures keep their meaning — a bound macro,
    /// ⌃arrows, ⌥⇧arrows, ⌃C — and the composer's own three (⌃S, ⌥L, Esc) are answered here; everything else is the
    /// editor's, handed to it explicitly as the command line's keys are, ⏎ included.
    /// </summary>
    private string? HandleComposerKey(DockedComposer composer, KeyPressedEventArgs e)
    {
        var key = e.KeyInfo;
        _composerEscapeAt = null;
        if (DispatchMacro(key) is { } sent)
        {
            e.Handled = true;
            return sent;
        }

        if (TryResizeKey(e))
        {
            return null;
        }

        var ctrl = key.Modifiers == ConsoleModifiers.Control;
        var alt = key.Modifiers == ConsoleModifiers.Alt;
        if (ctrl && key.Key is ConsoleKey.C or ConsoleKey.X)
        {
            // Never the editor's own: its ⌃C/⌃X write to the system clipboard through a helper nothing can
            // substitute. Through the app's clipboard, as the command line's are.
            e.Handled = true;
            var field = composer.Focused;
            if (!field.HasSelection)
            {
                CopySelection();
            }
            else if (ToClipboard(field.GetSelectedText(), key.Key == ConsoleKey.X ? "cut" : "copied")
                && key.Key == ConsoleKey.X)
            {
                field.ProcessKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
            }

            return null;
        }

        if (ctrl && key.Key == ConsoleKey.S)
        {
            e.Handled = true;
            SendDocked(composer);
            return null;
        }

        if (alt && key.Key == ConsoleKey.L)
        {
            e.Handled = true;
            composer.ToggleEscaping();
            return null;
        }

        if (alt && key.Key == ConsoleKey.W && composer.Spelling is not null)
        {
            e.Handled = true;
            AddDockedWordAtCaret(composer);
            return null;
        }

        if (key.Key == ConsoleKey.Escape && key.Modifiers == 0)
        {
            e.Handled = true;
            _composerEscapeAt = _time.GetUtcNow();
            ReleaseComposer();
            return null;
        }

        if (TryFocusKey(e))
        {
            return null;
        }

        // ⇥ / ⇧⇥ move between the command field and the text; ⏎ in the command field moves on to the text, a command
        // being one line. F1's keys, handed here as every composer key is.
        if ((key.Key == ConsoleKey.Tab && (key.Modifiers & ~ConsoleModifiers.Shift) == 0)
            || (key.Key == ConsoleKey.Enter && key.Modifiers == 0 && composer.InCommand))
        {
            e.Handled = true;
            composer.InCommand = key.Key == ConsoleKey.Tab && !composer.InCommand && key.Modifiers == 0;
            _window.FocusControl(composer.Focused);
            composer.Redraw();
            return null;
        }

        e.Handled = true;
        var target = composer.Focused;
        if (!target.HasFocus)
        {
            _window.FocusControl(target);
        }

        target.ProcessKey(key.Key == ConsoleKey.Enter ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false) : key);
        return null;
    }

    /// <summary>
    /// The Enter of a ⌥⏎ typed in the composer: the terminal splits it into an Esc — which released the composer —
    /// and an Enter. Within the same window <see cref="TryAltEnter"/> pairs them in, the composer takes the keyboard
    /// back and the Enter is a new line there, rather than a ⏎ sending whatever the command line holds.
    /// </summary>
    private bool TryComposerAltEnter(KeyPressedEventArgs e)
    {
        var at = _composerEscapeAt;
        _composerEscapeAt = null;
        if (e.KeyInfo.Key != ConsoleKey.Enter || e.KeyInfo.Modifiers != 0
            || at is not { } released || _time.GetUtcNow() - released > AltEnterWindow
            || !_dockedComposers.TryGetValue(ActiveWindowId(), out var composer))
        {
            return false;
        }

        e.Handled = true;
        _composerReleased = false;
        SyncComposerArm();
        composer.Editor.ProcessKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        return true;
    }

    /// <summary>
    /// ⌃S in the docked composer: the buffer as one command, built exactly as F1 builds it, to the composer's own
    /// character — echoed, alias-expanded and recorded in that character's history like any line typed.
    /// </summary>
    private void SendDocked(DockedComposer composer)
    {
        if (FindSession(composer.SessionKey) is not { } session)
        {
            RefuseCommand(NothingToSendTo(composer.WindowId));
            return;
        }

        if (ComposeMessage.Build(composer.Verb, composer.Body, composer.Escaping) is not { } line)
        {
            RefuseCommand("nothing to send — the composer is empty");
            return;
        }

        // The text goes; the command stays, for the next pose.
        var verb = composer.Verb;
        composer.Load(string.Empty, composer.Escaping, verb);
        ForgetComposeText(composer.SessionKey, composer.Escaping, verb);
        HistoryFor(composer.SessionKey, BarKind(_input)).Add(line);
        _ = session.SendUserInputAsync(line);
    }

    /// <summary>Types into the armed composer through the window's key handler, one key per character.</summary>
    internal void SimulateComposerTyping(string text)
    {
        foreach (var c in text)
        {
            SimulateKey(c == '\n'
                ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
                : c == '\t'
                ? new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false)
                : new ConsoleKeyInfo(c, char.IsAsciiLetter(c) ? (ConsoleKey)char.ToUpperInvariant(c) : ConsoleKey.NoName, char.IsUpper(c), false, false));
        }
    }

    /// <summary>
    /// What a click in a docked composer's editor does to focus: the framework focuses the control the pointer hit
    /// (<c>FocusReason.Mouse</c>) and raises <c>FocusChanged</c>. The framework's own mouse path runs only inside
    /// <c>Run()</c>, which no test enters.
    /// </summary>
    internal void SimulateComposerClick(string windowId)
    {
        if (_dockedComposers.TryGetValue(windowId, out var composer))
        {
            _window.FocusManager.SetFocus(composer.Editor, FocusReason.Mouse);
        }
    }

    // --- Shared -----------------------------------------------------------------------------------------------

    /// <summary>A markup panel: one control, redrawn from its render function at its pane's size.</summary>
    private sealed class PanelView(string windowId, MarkupControl control, Func<int, int, List<string>> render, Func<string, (int Width, int Height)> box)
    {
        internal string WindowId { get; } = windowId;

        internal MarkupControl Control { get; } = control;

        internal (int Width, int Height) Size { get; private set; }

        internal Action<string>? Click { get; set; }

        /// <summary>Told after every redraw, so a panel floating in a window of its own is republished with it.</summary>
        internal Action<string>? Refreshed { get; init; }

        internal List<string> Markup { get; private set; } = [];

        internal void Refresh()
        {
            Size = box(WindowId);
            Markup = render(Size.Width, Size.Height);
            Control.SetContent(Markup.ToList());
            Refreshed?.Invoke(WindowId);
        }
    }

    private PanelView NewPanelView(TerminalPanelContext context, Func<int, int, List<string>> render)
    {
        var control = new MarkupControl(new List<string>())
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Fill,
        };
        var view = new PanelView(context.WindowId, control, render, PanelBox) { Refreshed = NoteFloatingContent };
        control.LinkClicked += (_, e) => view.Click?.Invoke(e.Url);
        _panelViews[context.WindowId] = view;
        view.Refresh();
        return view;
    }

    /// <summary>What a markup panel last drew, for the tests.</summary>
    internal IReadOnlyList<string> PanelMarkup(string windowId) =>
        _panelViews.TryGetValue(windowId, out var view) ? view.Markup : [];

    /// <summary>A click on a panel's link, as its own handler receives it.</summary>
    internal void SimulatePanelClick(string windowId, string url)
    {
        if (_panelViews.TryGetValue(windowId, out var view))
        {
            view.Click?.Invoke(url);
        }
    }

    /// <summary>Redraws every panel: after a configuration commit, which can change meters and macros.</summary>
    private void RefreshPanels()
    {
        foreach (var view in _panelViews.Values)
        {
            view.Refresh();
        }
    }

    /// <summary>Redraws a panel whose box changed, and fits each composer. Rides <c>PostBufferPaint</c>.</summary>
    private void SyncPanelSizes()
    {
        foreach (var view in _panelViews.Values)
        {
            if (view.Size != PanelBox(view.WindowId))
            {
                view.Refresh();
            }
        }

        foreach (var composer in _dockedComposers.Values)
        {
            var box = PanelBox(composer.WindowId);
            composer.Fit(box.Width, box.Height);
        }
    }

    /// <summary>The cells a panel has: its pane's output rectangle, or a fallback before one is arranged.</summary>
    private (int Width, int Height) PanelBox(string windowId)
    {
        if (_workspace.Layout.FindWindow(windowId) is { } pane
            && PaneOutputRects().TryGetValue(pane.Id, out var rect)
            && !rect.IsEmpty)
        {
            return (rect.Width, rect.Height);
        }

        return (Math.Max(20, _system.ConsoleDriver.ScreenSize.Width - 24), Math.Max(3, _system.ConsoleDriver.ScreenSize.Height - 6));
    }

    /// <summary>A character's game data: its session's, or the demo's for the demo character with nothing open.</summary>
    private GameDataStore? PanelGameData(string sessionKey) =>
        FindSession(sessionKey)?.GameData
        ?? (IsDemoKey(sessionKey) ? _demoGameData : null);

    /// <summary>A character's room roster: its session's, or the demo's.</summary>
    private RoomOccupants? PanelOccupants(string sessionKey) =>
        FindSession(sessionKey)?.Occupants
        ?? (IsDemoKey(sessionKey) ? _demoOccupants : null);

    /// <summary>The meters a character's vitals show: the player's own, then any its server asked for.</summary>
    private IReadOnlyList<StatusMeter> PanelMeters(string sessionKey)
    {
        var own = _config.Text.StatusMeters;
        var served = FindSession(sessionKey)?.MxpMeters ?? (IsDemoKey(sessionKey) ? _demoMeters : []);
        return served.Count == 0
            ? own
            : [.. own, .. served.Where(m => !own.Any(o => string.Equals(o.Value, m.Value, StringComparison.OrdinalIgnoreCase)))];
    }

    // --- The snapshot views -----------------------------------------------------------------------------------

    /// <summary>
    /// Function-key bindings for the demo character, which the demo configuration does not carry: a trigger set
    /// named <c>Quick</c>, added and listed the way F2 and F5 would. For the quick-bar views.
    /// </summary>
    private void LoadDemoBindings()
    {
        if (_config.TriggerSets.Any(s => s.Name == "Quick"))
        {
            return;
        }

        _config.TriggerSets.Add(new TriggerSet
        {
            Name = "Quick",
            Macros =
            {
                new Macro { Name = "kick", Key = "F11", Command = "kick" },
                new Macro { Name = "bash", Key = "F12", Command = "bash" },
                new Macro { Name = "cure light", Key = "Ctrl+F3", Command = "cast 'cure light'" },
                new Macro { Name = "quaff mana", Key = "Ctrl+F4", Command = "quaff mana" },
                new Macro { Name = "flee", Key = "Ctrl+F12", Command = "flee", Danger = true },
            },
        });
        FindCharacter(DemoScene.ActiveSessionKey)?.Character.TriggerSets.Add("Quick");
    }

    /// <summary>
    /// The four panel views, each opened through its real ⌃P entry and placed the way its layout places it:
    /// <c>panel-who</c> a narrow column beside the output; <c>panel-vitals</c> a strip over it, with a target;
    /// <c>panel-quickbar</c> a strip under it, with function-key bindings the demo does not otherwise carry;
    /// <c>panel-composer</c> the Roleplay column — the composer armed and holding a post typed through the real key
    /// path, over who is here. The data is written through <see cref="GameDataStore.SetGmcp"/> and
    /// <see cref="RoomOccupants.Apply"/>, the calls the wire makes.
    /// </summary>
    private void LoadDemoPanels(string view)
    {
        LoadDemoGameData();
        LoadDemoOccupants();

        switch (view)
        {
            case "panel-who":
                OpenDemoPanelBeside(PanelIds.WhoHere, columns: 34);
                Activate(MainWindowId);
                break;

            case "panel-vitals":
                _demoGameData.SetGmcp("Char.Status", """{"enemy":"a rotting ghoul","enemypct":"40"}""");
                OpenDemoPanelStrip(PanelIds.Vitals, Edge.Top);
                Activate(MainWindowId);
                break;

            case "panel-quickbar":
                LoadDemoBindings();
                OpenDemoPanelStrip(PanelIds.QuickBar, Edge.Bottom);
                Activate(MainWindowId);
                break;

            case "panel-composer":
                if (OpenDemoPanelBeside(PanelIds.Composer, columns: 46) is { } composer
                    && _workspace.Layout.FindWindow(composer) is { } column)
                {
                    var who = TerminalPanels.WindowId(PanelIds.WhoHere, DemoScene.ActiveSessionKey);
                    _workspace.OpenWindow(who, PanelTitle(PanelIds.WhoHere), WindowKind.Panel, DemoScene.ActiveSessionKey, column.Id);
                    _workspace.Layout.SplitWithWindow(who, column.Id, Edge.Bottom);
                    RebuildPaneArea();
                    Activate(MainWindowId);
                    Activate(composer);
                    SimulateComposerTyping("\t+pose\tCorvid sets the lantern down by the fountain\nand waits, 100% certain [someone] saw.");
                }

                break;
        }

        RefreshRail();
    }

    /// <summary>The demo room's roster, written through <see cref="RoomOccupants.Apply"/> as the wire would.</summary>
    private void LoadDemoOccupants()
    {
        const string players =
            """[{"name":"Ashlyn","fullname":"Ashlyn the Wanderer","idle":12},{"name":"Brannoc","idle":260},{"name":"Merrow","fullname":"Merrow of the Tide","idle":5},{"name":"Corvid","idle":0}]""";

        // Into the game data too, as a session stores every GMCP package: that is where a layout reads "who is here
        // is known" from (PanelAvailability), so the Roleplay layout draws the panel.
        _demoGameData.SetGmcp("Room.Players", players);
        _demoOccupants.Apply("Room.Players", players);
        _demoOccupants.Apply(
            "Char.Items.List",
            """{"location":"room","items":[{"id":"9101","name":"a town crier","attrib":"m"},{"id":"9102","name":"a stray cat","attrib":"m"},{"id":"9103","name":"a lantern","attrib":"t"}]}""");
    }

    /// <summary>Opens a demo panel in a new pane to the right of the main window's, <paramref name="columns"/> wide.</summary>
    private string? OpenDemoPanelBeside(string panelId, int columns)
    {
        var mainPane = _workspace.Layout.FindWindow(MainWindowId)?.Id;
        if (mainPane is null || OpenPanel(panelId, DemoScene.ActiveSessionKey) is not { } windowId)
        {
            return null;
        }

        _workspace.Layout.SplitWithWindow(windowId, mainPane, Edge.Right);
        RebuildPaneArea();
        Activate(windowId);
        SizeDemoPane(windowId, columns);
        return windowId;
    }

    /// <summary>Opens a demo strip panel in a pane along one edge of the main window's; it is as tall as its content.</summary>
    private void OpenDemoPanelStrip(string panelId, Edge edge)
    {
        var mainPane = _workspace.Layout.FindWindow(MainWindowId)?.Id;
        if (mainPane is null || OpenPanel(panelId, DemoScene.ActiveSessionKey) is not { } windowId)
        {
            return;
        }

        _workspace.Layout.SplitWithWindow(windowId, mainPane, edge);
        RebuildPaneArea();
        Activate(windowId);
    }

    /// <summary>
    /// Moves the border of the split holding a window's pane so that pane is <paramref name="cells"/> along the
    /// split's axis, through <see cref="PaneResize.DragBorder"/> — the arithmetic a drag on the divider uses. Needs
    /// a rendered frame, so the panes have rectangles.
    /// </summary>
    private void SizeDemoPane(string windowId, int cells)
    {
        RenderFrame();
        if (_workspace.Layout.FindWindow(windowId) is not { } pane
            || ParentOf(_workspace.Layout.Root, pane) is not { Children.Count: 2 } parent)
        {
            return;
        }

        var rects = PaneRectsForResize();
        if (!rects.TryGetValue(pane.Id, out var rect))
        {
            return;
        }

        var horizontal = parent.Direction == SplitDirection.Row;
        var start = horizontal ? rect.X : rect.Y;
        var length = horizontal ? rect.Width : rect.Height;
        var boundary = parent.Children.IndexOf(pane) == 0 ? start + cells : start + length - cells;
        PaneResize.DragBorder(parent, 0, boundary, rects);
        RebuildPaneArea();
        ReArmWholeFrame();
    }

    private static SplitNode? ParentOf(Core.Workspaces.LayoutNode node, PaneNode pane) =>
        node is not SplitNode split
            ? null
            : split.Children.Contains(pane)
                ? split
                : split.Children.Select(child => ParentOf(child, pane)).FirstOrDefault(found => found is not null);

    /// <summary>The demo's room roster, for the tests that drive it the way the wire would.</summary>
    internal RoomOccupants DemoOccupants => _demoOccupants;
}
