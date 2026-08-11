# CLAUDE.md — SharpMUTerm agent brief

Guidance for any Claude agent working in this repository. Read this first, then read
[`docs/PLAN.md`](docs/PLAN.md) — the plan is the authoritative architecture + roadmap.

## What this project is

**SharpMUTerm** is a cross-platform TUI **MU\*** (MUSH / MUCK / MUD) client in **C# / .NET 10**,
targeting feature parity with [BeipMU](https://beipdev.github.io/BeipMU/), running inside
GPU-accelerated terminals (Kitty, WezTerm, Ghostty) on **Windows and Linux**.

"GPU acceleration" is a property of the terminal *emulator*, not this app. Our job is to emit
rich truecolor/styled text and use the **Kitty graphics protocol** (with Sixel + half-block
fallbacks) for inline images/maps.

## Locked decisions (do not relitigate without asking)

- **Target framework:** `net10.0`.
- **TUI base:** **SharpConsoleUI** (`nickprotop/ConsoleEx`, stable, net8/9/10) — a compositor-based
  framework with split layouts, tabs, resizable/mouse-draggable windows, Spectre-style markup, and
  a **native Kitty graphics protocol** (+ Sixel/half-block) for inline images. Replaced Terminal.Gui
  v2 (which was prerelease with an `[Obsolete]` mid-migration API); the switch is contained to
  `SharpMUTerm.Tui` because `SharpMUTerm.Core` is UI-agnostic.
- **Scripting:** Lua via **MoonSharp** (pure-managed, sandboxed).
- **Inline graphics:** in scope from day one (Kitty Unicode placeholders → Sixel → half-block).
- **Protocols:** aim for all common MU\* protocols. GMCP/MSSP/CHARSET/NAWS/MTTS/EOR via
  TelnetNegotiationCore; **MCCP, MSDP, MXP, and Pueblo are our own app layer.**
- **Config:** fresh JSON schema of our own (worlds hold characters; automation lives in shared
  named trigger sets), versioned with automatic migration between schema revisions.
- **The command line is ours** (`InputBarControl` + `InputBuffer` + `InputLayout`), and stays ours.
  The framework has two text controls and neither is the answer. `PromptControl` genuinely cannot
  do it — single-line by construction (its setter turns `\n` into a space), one row tall, scrolls
  sideways, and unfocuses on ⏎ with no way to switch that off. `MultilineEditControl` is a capable
  *editor* (wrap, undo, find/replace, mouse, paste, a pluggable gutter) and is worth knowing about,
  but a command line is not an editor: ⏎ has to send rather than insert, the bar grows to its
  content between a floor and a ceiling, it carries a prompt the text indents past, and two of them
  share one caret with per-window drafts and history recall behind it. Expanding ours is the call.
  Offering it upstream once it is genuinely finished and problem-free is a someday, not a plan —
  do not treat it as pending work.
- **License:** MIT.

## Repository state

**M1 delivered, plus substantial M2–M4 work.** `SharpMUTerm.slnx` builds all ten projects on
`net10.0`, with the full test suite passing. In place:

- **Core** — `AnsiParser` (SGR 16/256/truecolor), styled-line + `ScrollbackBuffer` model (a capped
  in-memory ring plus a **file-backed spill**, `FileScrollbackSpill`, so history deeper than memory is
  paged off an ephemeral per-session cache under `$XDG_CACHE_HOME`; absolute line indices, ranged
  reads capped at `MaxRangeLines`, and any disk failure degrades to memory-only. Emphatically **not**
  the session log — that stays `PlainTextLogSink`/`HtmlLogSink`, opt-in and kept),
  `TcpTransport` (TLS + IPv6), `TelnetSession` (wraps TelnetNegotiationCore **2.7.0**),
  trigger/alias/macro engines + `IntervalScheduler`, plain-text + HTML logging, versioned JSON
  config (worlds → characters + shared trigger sets, with migration),
  `Theme`/`ThemeLibrary`, and `WorldSession`/`SessionManager` orchestration.
  - **Automation is live, and that is a push rather than a read-through.** Each engine holds its rules in
    two lists: *configured* (what the active `TriggerSet`s contribute, swapped wholesale by
    `ReplaceConfigured`) and *runtime* (what the Lua bridge's `Triggers.Add` contributed, which a reload
    must not delete). `WorldSession.ReloadAutomation(sets)` re-points all three engines, and
    `SharpMUTermApp.SaveConfiguration` — the single funnel every settings screen commits through
    (`ScreenEdits`) — calls it, so adding a rule on F2 or assigning a set on F5 reaches a *connected*
    session on its next line. It is **not** read-through to the configuration: `Process` runs on the telnet
    read loop and those lists are mutated on the UI thread, so enumerating them there would throw. Reading
    a rule's own fields per match (`Trigger.Pattern` drops its compiled regex on write) is safe and is a
    different thing from reading its membership. A timer's *period* still applies at the next connect —
    re-periodising a running one resets every other timer's phase, and this runs on every committed change.
- **Graphics** — Kitty encoder + Unicode placeholders, Sixel + half-block fallbacks, capability
  probe, and `InlineImagePolicy` — the Kitty → Sixel → half-block → text degradation chain (no UI
  dependency). Inside the TUI the *pixels* are drawn by SharpConsoleUI's `ImageControl`; ours
  supplies the policy, because only the framework's renderer can put an image into compositor cells.
  **Sixel inside the compositor is blocked upstream:** at SharpConsoleUI 2.5.14 `IImageRenderer` is
  `internal` and `ImageControl.ResolveRenderer()` is private, so no Sixel back-end can be injected —
  and the framework ships none. Reopening it needs an upstream PR making `IImageRenderer` public and
  `ResolveRenderer` overridable; nothing on our side unblocks it. `InlineImagePolicy` therefore
  degrades a Sixel-only terminal to half-block explicitly rather than pretending.
- **Scripting** — sandboxed MoonSharp `ScriptHost` (world/output/trigger/alias/timer/gmcp/log).
- **Tui** — **SharpConsoleUI** app: a `TabControl` of output windows (main + trigger-routed **spawn
  windows** + web view, with unread badges), each a `MarkupControl` in a `ScrollablePanelControl`
  viewport (PgUp/PgDn, Shift+↑/↓, ⌃Home/⌃End, wheel; unread badges count output arriving below a
  scrolled-back viewport), fed StyledLine → Spectre-style
  markup via `MarkupFormatter` (clickable `[link=…]` MXP/Pueblo/web spans); an `InputBarControl`
  command line (wrapping, auto-growing, per-window drafts, plus an optional per-window second bar),
  status line, `Ctrl+Q` quit, per-pane NAWS (every connected session is told its own pane's output
  rectangle, on every resize and layout change, rate-limited to four writes a second with a trailing
  flush). The tab/pane set is driven by the tested `Core.Workspaces` model, with **splits** (thin
  single-line dividers) and the **connection rail** now rendered as well — and **clickable**: a world,
  character or window row switches to it, dispatched through the rail control's *own* `LinkClicked`
  (never the output panes' handler, so a world cannot drive the client's UI from the wire).
- **Panes come back after a restart, and the log that does it is keyed by *window*** (`RestoreLog`,
  Core; `restore/` beside `config.json`, one `0600` file per window, 500 lines each). This is the third
  thing in the repository that puts session text on disk and it is none of the other two: the spill is
  an ephemeral cache purged next launch, the transcripts are opt-in files a user keeps, and this is a
  small bounded tail nothing but startup reads. **It cannot be built on `WorldSession.Scrollback`** — a
  spawn window's lines never go there (`ProcessOutputLine` raises `SpawnLine`, and a gagging capture
  rule keeps the line out of the transcript entirely), so a session-keyed restore refills the main
  windows and leaves every channel pane empty, which is the exact failure it exists to remove. It is
  therefore fed from the shell, at `OnLine`/`OnSpawnLine`, and **not** from `AppendWindowLine`: that
  seam also carries the client's own chrome and the restore *replay*, so logging there would have each
  launch re-record its own history. Payload is `StyledLineCodec`, not markup, so the game's colours and
  a span's interaction survive and the current theme still renders them. Appends flush to the OS per
  line (a crash loses nothing; there is deliberately no `fsync` per line), the bound is in **lines**
  and never bytes, and space is reclaimed by compaction — a byte-range copy through an atomic rename.
  Restored content is closed off by one `RestoreBarRenderer` row and the lines themselves are left
  alone. Restoring 3,000 lines costs ~18 ms before the first frame. `restore:` is the third member of
  the `save:`/`logRoot:` family — **null by default, so no test and no snapshot owns one**.
- **Coming back to a window you were not watching leaves a bar where you left off, and that covers two
  different absences.** The *window* one is `NEW` and is the common case: a line lands while the window
  is not `Workspace.IsCaughtUp` — visible **and** at its live tail — and `_missedFrom` records the index
  of that first line, exactly, at the moment it happens. `MarkMissedLines` draws it when the window is
  caught up again, from the only two places that can make it so (`Activate`, and `SyncScrollbackState`,
  where every scroll route lands). Three things not to relitigate. **`IsCaughtUp` and not `IsVisible`**,
  because that is already the unread badge's rule and one fact behind both means a badge showing a count
  always has a bar under it saying where the count begins — "the badge said 3 and nothing said which 3"
  was the report. **The reveal is only for *arriving*:** a pane bottom-anchors, so a deep absence's bar
  is drawn far above the fold and nothing on screen would change without the jump — but ⌃End is an
  explicit *take me to the live tail*, and scrolling somewhere else in answer to it is the "attention on
  one pane, keystrokes to another" defect wearing a different hat (`CtrlEndGoesBackToFollowingTheLiveTail`
  is the pin; the pane is re-pinned after the insert instead, because a whole-buffer re-feed leaves the
  offset a frame behind and the newest line would blink off screen). **Nothing accrues before the
  constructor finishes** (`_watching`): until the workspace is laid out, "not visible" means "no pane
  built yet", and `RestorePreviousSession` pours a previous run through the same seam into windows that
  already sit under a `RestoreBarRenderer` bar.
  - **The bar retires on three conjuncts, and the third is a floor in time.** At the live tail, one input
    since it was drawn, **and** `TextSettings.ActivityBarSeconds` elapsed (F7 ▸ ACTIVITY, default 30, `0`
    is the old behaviour exactly). Two were not enough: a shallow absence leaves the pane at its tail, so
    the next keystroke took the bar a second or two after it appeared. Time and not keystrokes because
    that is the unit the complaint was in — a raised input count is an hour on a quiet character and
    three seconds on a busy one. It is a **floor, not a timer**: nothing fires on its own, so the bar
    goes on the first of these checks after it passes, and an untouched client keeps its bar. Measured
    off the app's existing `TimeProvider`, so tests move the clock rather than racing it, and
    `AwayDividerTests` sets the floor to zero so each suite asserts one rule.
- **Coming back to the terminal leaves a bar where you were** (`AwayBarRenderer` + `TerminalFocusWatcher`,
  Tui). The *other* absence, and the harder one, because a terminal reports focus-in and not focus-out —
  so this boundary is reconstructed from the input before the last, where the window one above is
  recorded forwards. Where a window has both, the **older wins**: a reader who typed after lines landed
  in a window they could not see has a terminal boundary at the end of that buffer, claiming they missed
  nothing, and the window's own boundary knows better. Still one bar per window.
  Third of the boundary bars, and it earns its row the same way `FreezeBarRenderer` and
  `RestoreBarRenderer` do: mark the *boundary*, never restyle the content. The signal is real terminal
  focus reporting (`CSI ?1004h`) and **both halves of getting it are workarounds**, which is why they are
  in one file. No released SharpConsoleUI asks for focus (verified against 2.5.18's string heap: `?2004`
  is there, `?1004` is in no version), `IConsoleDriver` has no focus event, and `UnixStdinReader`
  dispatches only key/paste/mouse. So we ask through `IConsoleDriver.WriteClipboardOsc52`, which is named
  for its first customer and is really a verbatim raw write under the renderer's own `_consoleLock` — the
  only public write serialised against frame painting — funnelled through one `EmitTerminalMode` so a
  version that starts validating that payload is one line to fix. And focus-**in** is recognised as the
  **bare Tab keypress that `AnsiInputParser.DispatchCsi` mistranslates it into** — `:511` reads a
  trailing `I` as Tab, which is right for `ESC [ 1;5 I` = Ctrl+Tab and wrong for the bare form. Tab is
  claimed through
  `RegisterGlobalShortcut`'s **declining** overload, deliberately *not* through `MacroKeys.AppShortcuts`:
  it declines nearly every Tab it sees, and listing it would tell F4's readers a key was gone that is not.
  - **The watcher listens to the driver, so its `Input` runs on the driver's reader thread — and the app
    marshals.** The framework's own key path does not run there (its driver handler only enqueues,
    `ConsoleWindowSystem.cs:970-973`, and `InputCoordinator.ProcessInput` drains on the main loop), so
    every other key handler here is on the UI thread and this one cannot be: a queued key arrives too
    late to measure the gap in front of a focus-in Tab. `NoteInput` keeps its timestamps on the reader
    thread; both subscriptions go through `OnUiThread`, because everything past them — pane buffers,
    away marks, the controls they repaint — is the UI thread's. **Marshalling reorders, so the input is
    numbered** (`InputCount`, carried by `Input`; `AwayMark.DrawnAfter` is the stamp): a note raised
    before the return that drew a bar can be delivered after it, and would otherwise set `InputSince` on
    a bar nobody has seen, retiring it to the keystroke that produced it.
  - **Telling that Tab from a real one is a question about time, and the comparison must be against the
    input *before* it.** The disguised focus-in is itself a `KeyPressed`, raised before `InputCoordinator`
    reaches the global shortcuts, so measuring from the latest timestamp finds a gap of zero on every
    return and the feature never fires — a bug indistinguishable from the terminal not supporting `?1004`.
    **The same trap bit the boundary**, one field over: that keypress had already moved `_awayPending` to
    the end of a buffer full of unseen lines, so `_awayBoundary` keeps the value from the input before it.
    `SimulateReturnFromAway` notes an input first for that reason — a seam that skipped it would read a
    boundary the shipping path never reads.
  - **Focus-out is not recoverable.** `ESC [ O` has no case and is dropped as an `UnknownSequenceEvent`,
    so a departure cannot be timestamped; the boundary is the last input event instead, which is seconds
    off. **Unix only** — the Windows branch is a `Console.ReadKey` loop with its own reassembly, so
    `?1004` must not be enabled there — and inert headless, because a harness pressing Tab must get a Tab.
  - **A bar off the fold is scrolled to** (`RevealAwayBar`), and without that the feature is invisible in
    the case that matters most — the reported defect. Come back to more lines than the pane holds and the
    bar is drawn far above the viewport, so *nothing on screen changes*; nothing else covers for it either,
    because a window visible and at its live tail throughout an absence accrues no unread badge. A bar
    already in view is left alone: scrolling a shallow absence would take a pane off its tail to reveal
    what is already on it. `ScrollVerticalBy` and not `ScrollToTop` — it re-syncs metrics from the arranged
    bounds before clamping (so a scroll straight after mutating content is not clamped against a stale
    viewport) and detaches `AutoScroll` on the way up, which a jump that left it armed would have undone
    on the next repaint.
  - **A buffer index is not a viewport row, and conflating them is a bug this has already had.** The
    panel's offset counts *display* rows and a buffer line wraps into as many as it needs, so in a narrow
    pane scrolling to the index landed hundreds of rows adrift, in content from a previous session. The
    height is **measured**, by the framework's own `MarkupControl.MeasureDOM` through a throwaway control
    at the pane's `ViewportWidth`, so it wraps the way the real control will — and only the *tail* is
    measured, from the bar to the newest line, then subtracted from the panel's authoritative
    `TotalContentHeight`. Never re-derive wrapping by counting characters; word breaks, zero-width markup
    tags and wide characters all change the answer.
  - **Consumption is two conjuncts, and `Workspace.IsCaughtUp` is not one of them.** A pane bottom-anchors,
    so it is already "visible and not scrolled back" the instant you return with two hundred unread lines
    above the fold; clearing on it clears the bar before a word is read. It goes when the pane is at its
    *live tail* and *one input* has landed since it was drawn. What makes the first mean anything is the
    reveal: the pane was taken **off** its tail whenever the bar was not on screen, so arriving back at the
    bottom is having read down through what you missed rather than never having left. The second is what
    stops a shallow absence clearing in the frame it appears in. Insert and remove are mid-buffer, so each
    costs one `RepaintPane`; affordable for the timestamp toggle's reason, bounded by a deliberate event
    rather than by lines or frames.
  - The bar is chrome: it never badges unread, never reaches the restore log (already free — that is fed
    from the session's line handlers, not the append seam), and a trim that takes it drops the mark with
    it. A window that gained nothing gets no bar.
  - **`SimulateKey` used to discard a global shortcut's result** and swallow the key either way. Harmless
    while every claim returned true; wrong the moment one declined, and it now honours the decline.
- **Every server's MSSP report is kept, and the INFO screen reads it** (`MsspCache`, Core; `mssp.json`
  beside `config.json`, keyed by `host:port`; F5 ▸ `i`). Fourth of the `save:`/`logRoot:`/`restore:`
  family with **one deliberate difference**: the constructor parameter is null by default like the
  others, but the *field* never is — a `MsspCache` with no path is memory-only **by construction**, so
  the "a snapshot writes nothing" guarantee is a property of the object rather than a null check at
  each use site, and the screen needs no "is there a cache" branch. Three decisions worth not
  relitigating. **Keyed by endpoint, not world**: MSSP describes a *server*, a world name is a
  user-editable label two entries may share, and a rename must not lose a report. **A second report
  replaces the first**: MSSP is not a delta protocol — a server sends its whole table once per
  connection — so a merge would keep variables it has stopped publishing and would leave a report that
  is a snapshot of no moment that existed. **Two timestamps**, because there are *three* states and two
  would only separate two: `ConnectedAt` is written on the `Connected` transition and `ObservedAt` only
  when a report arrives, so "never dialled", "dialled and publishes nothing" and "here is the report,
  as of…" are three different screens. Report capture is bounded at the door
  (`MaxVariables`/`MaxValuesPerVariable`/`MaxValueLength`), not only at the renderer — a value only the
  screen trimmed would still be full size on disk and in memory on every later launch.
- **This client asks no server to enable an option the server has not offered, and that rule was bought
  with a login** (`UnsolicitedNegotiationTests`, Core). `TelnetSession` used to write `IAC DO MSSP`
  straight to the transport on connect — legal telnet (RFC 854 has either party initiating, and requires
  a response even to a refusal), and the way to reach the many servers that support MSSP but never
  volunteer it. **Refusing an option means consuming its three bytes, and a server that implements
  neither leaves them in its line buffer, where they are prepended to the next line the client sends —
  which is always the auto-login.** The server reads `\xFF\xFD\x46connect Name password`, redisplays its
  connect screen and logs nobody in; the transcript shows the welcome screen twice, with no reason for it,
  because the login line is deliberately not echoed or logged. Measured on a live game: with the request
  the login line was never evaluated, without it the same line reached the game, and only the *first*
  line after the request dies — which is why typing the login by hand always worked and the auto-login
  never did. Two things follow. **We are not in a position to know which servers parse telnet properly**,
  and the one that does not is exactly the one whose login we break. And **negotiation is the library's
  to conduct**: TelnetNegotiationCore would never have sent that `DO` — its client-side MSSP answers a
  server's `WILL` and initiates nothing (`MSSPProtocol.OnWillMSSPAsync`) — so a hand-written negotiation
  byte, written around it to avoid `IAC` being escaped as data, is a negotiation nothing keeps state for.
  The `RequestOptions` mechanism is deleted rather than left empty, so there is no seam to reach for.
- **A launch connects nothing unless it is told to** (`StartupConnections.Resolve`, Core). A host on the
  command line wins outright; otherwise it is every character with `ConnectAtStartup` (F5's `at start`),
  in configuration order; otherwise none, and the client says which of the two empty states it is in.
  There is **no migration** marking anybody — the old behaviour (first world, first character,
  unconditionally) is precisely what the setting removes. `StartAsync` builds the windows sequentially
  in that order and then dials them **concurrently**: focus is taken by the first before any packet
  leaves, so several auto-connects always land you in the same place, and a black-holed host cannot hold
  the other worlds' windows hostage. `ConnectAtStartup` is *not* `AutoLogin` — one opens the socket, the
  other sends the connect line once one is open, and either is useful alone.
- **The URLs a world prints as plain text are marked up by this client, because the terminal's own
  detection cannot survive a pane** (`UrlDetector`, Core; F7 ▸ `detect links in output`, default on).
  Kitty, WezTerm and Ghostty all find URLs in the cells they paint — across the terminal **row**. A pane
  is narrower than the row, so a wrapped URL is `https://exa` on one row and `mple.com/page` on the next
  with a divider and possibly another pane's output between them; neither half is a URL, neither is
  clickable, and nothing says so. Marking the span ourselves moves the decision to the layer that knows
  where the line really ends: `MarkupParser` splits a `[link=…]` across every row it wraps onto
  (`MarkupParser.cs:1272-1296`) and `MarkupControl` hit-tests each row. **OSC 8 is not an option** — the
  compositor's cells carry no such attribute.
  - **It runs over the whole line, never span by span.** A server may change colour mid-URL (a highlight
    rule, or a game that paints the scheme differently), and matching per span finds two half-URLs and
    produces two links to two truncated targets — the same defect as the wrap, one layer down. Same
    shape as `EmojiSubstitutor.ApplyToLine`, and for the same reason.
  - **It runs after the emoji substitution, and both feed the spawn dispatch.** After, so a link's target
    is exactly the text under it: a span whose visible text and destination differ is the shape of a
    phishing link, and this client should not manufacture one. `ProcessOutputLine` computes the shown
    line once and hands the *same* line to `SpawnLine` and to `Print` — a capture used to get
    `result.Line` while the main window got the substituted one, so one line read differently in the two
    panes it landed in.
  - **A run overlapping an existing `SpanInteraction` is skipped.** What MXP marked up is MXP's; this may
    neither replace a `<SEND>` nor wrap one in a second link.
  - **The setting is ingest-time, and that is stated rather than papered over.** It joins
    `strip incoming colour`, tab width and `emoji substitution`: unticking it stops the next line and
    leaves history alone. It is *not* the timestamp gutter's situation — the gutter is glued on when a
    control is fed, so it can repaint history, while a link is a property of the line's spans and a
    pane's history is markup by then.
  - **Two schemes only, `http://` and `https://`, spelt out.** No `www.`, no bare host, no `mailto:`.
    The output of this detector is eventually handed to the desktop, so what it can name is a security
    property. Trailing `.,;:!?'"` and *unbalanced* closers are given back to the sentence (a wiki URL
    keeps its parentheses); a scheme inside a URL does not start a second link; there is a length cap.
- **Every http(s) link clicked in a pane opens in the desktop's browser; the built-in web view is
  reached by its own anchors and by `/web <url>`** (`ExternalBrowser`, Tui). `LinkAction.Web` is routed
  **by the surface the click came from**, not by the payload: `windowId == WebWindowId` navigates the
  view, anything else launches. Without that split the built-in browser would eject you to Firefox on
  its first in-page link. The window id is a trusted parameter set where the handler is subscribed —
  never server text — which is the same reasoning that makes that handler take one at all.
  - **The scheme gate is at the moment of opening, and that is the security boundary.** `UrlDetector`
    only ever produces http(s), but this path also carries what a *server* marked up: an MXP
    `<A HREF="file:///…">`, a `javascript:`, or one of the schemes a desktop registers to applications
    (`ms-msdt:` and relatives). Handing any of those to `xdg-open`/`ShellExecute` is letting the world
    choose which program runs on the machine. The URL is launched as `ProcessStartInfo.FileName`, never
    composed into a shell string, and what is launched is `Uri.AbsoluteUri` — what .NET parsed, not a
    second reading of the same bytes.
  - **The launcher is caller-supplied and null by default**, the fourth member of the
    `save:`/`logRoot:`/`restore:` family: **a snapshot and a test start no browser**, and an app with no
    opener refuses out loud (`AutoLinkTests.AnAppWithNoOpenerLaunchesNothingAndSaysSo`).
- **F1 is the composer: a full-screen editor for writing a post, sent as one command**
  (`ComposeOverlay`, Tui; `ComposeMessage`, Core; ⌃P ▸ *Compose a post*; `--view compose` /
  `compose-literal`). It is **the one place `MultilineEditControl` is right**. CLAUDE.md rules that
  control out of the *command line* because ⏎ there has to send rather than insert; a composer is the
  opposite case, so undo, find, selection, mouse and a caret over wrapped rows all come free instead of
  being written again.
  - **The buffer is the whole command**, verb and all — nothing is prepended and nothing is guessed at —
    and its line breaks are joined with `%r`, because a MUSH stores a post as one string and renders the
    breaks itself. Blank rows at the ends are where the caret was left and are dropped; interior ones are
    paragraph breaks and become `%r%r`.
  - **⌥L switches escaping, and the escaping runs *before* the join.** In `literal` the body's `%`, `[`,
    `]`, `{`, `}`, `;` and `\` are escaped so the post shows what was typed; in `as typed` nothing is.
    Escaping after the join would produce `%%r` — the characters "%r" posted into the body instead of a
    line break, on every line of every literal post. The mode travels with the draft.
  - **It is modal, and that is what makes it possible at all.** `PinFocusToArmedBar` would fight an editor
    needing real focus for ever — except it stands down while the main window is inactive, which a modal
    guarantees. Same reason the settings screens are modal.
  - **Paste is the framework's here, and must stay the only path.** `SettingsOverlay` takes paste off the
    *driver* because its screens have no focusable target, and its own remarks warn that a focusable
    `IPasteTarget` would make both fire. `MultilineEditControl` is one. That is why F1 **refuses over an
    open settings screen** (and why two modal windows with two `PreviewKeyPressed` handlers could not be
    driven headlessly anyway).
  - **The editor is sized from the driver, not by `Fill` alone.** `VerticalAlignment.Fill` reads arranged
    bounds only once arranged, and the first frame is laid out against the control's ten-row default — a
    maximised window with a ten-row editor and the footer immediately under it. `FitEditor` sets
    `ViewportHeight` from `ConsoleDriver.ScreenSize`, and re-runs on `ScreenResized`. Its colours must set
    **both** pairs: the control paints from the *focused* pair, and it always has focus here, so setting
    only `BackgroundColor` leaves the framework's grey on screen.
  - **Drafts are per character and in memory only**, for the life of the run. Not on disk deliberately: a
    post is a few minutes' work, and a file would be a fourth thing this client writes, a purge entry, a
    `--help` line and somebody's unsent post in their home directory. Keyed by the *window's* owner, never
    `_active`; a window belonging to no connection keeps none. **`Close()` raises `Closed`, which is what
    stores the draft, so a send must close first and forget after** — the other order posts the text and
    hands it straight back next time the window opens.
  - **F1 is claimed in `MacroKeys.AppShortcuts` but is not a settings screen**, so `ShortcutAction` answers
    it *before* the screen lookup — an arm reached only on a miss would make every future unclaimed F-key
    silently open the composer. Claiming it takes it off `MacroKeys.Bindable` automatically, which is why
    a macro test that used F1 as "a free function key" had to move to F12.
  - **The footer names ⌃S, ⌥L and Esc and nothing else.** No `⌃F find`: the control has a find *API* and
    no chord bound to it, and this screen is held to the same honesty rule as the settings screens.
- **Every `[link=…]` payload a pane carries is scheme-tagged by `InteractionKind`** (`LinkPayload`:
  `mux:send:` / `mux:prompt:` / `mux:web:`), and the panes' handler takes the *window id* the click
  came from. Both are security properties, not tidiness. The tagging is disjoint because the
  **parser** decides the kind (`<SEND>` → `SendCommand`, `<A HREF>` → `Hyperlink`) and a world cannot
  choose that — while the hyperlink case passed `href` through bare, `<A HREF="mux:send:@shutdown">`
  was byte-identical to a real `<SEND>` and the click sent it. Never re-introduce a bare passthrough,
  and never add a "probably a URL" fallback for an untagged payload. The window id is what stops a
  link clicked in a background pane sending to whichever character is focused.

## Building and testing

- **.NET 10 SDK**: install via `apt-get install -y dotnet-sdk-10.0` (the Microsoft CDN is often
  blocked; Ubuntu's repo works). NuGet (`api.nuget.org`) is reachable.
- **Tests are TUnit on Microsoft.Testing.Platform** (`Exe` projects, not xUnit). `dotnet test` does
  **not** work — .NET 10 dropped VSTest. Run each suite directly, and keep the `</dev/null`: it
  detaches stdin so the test host doesn't hang waiting on it.
  ```bash
  dotnet run -c Release --project tests/SharpMUTerm.Core.Tests </dev/null
  ```
  There are five: Core, Graphics, Scripting, Web, Tui. Primary signal is
  `dotnet build SharpMUTerm.slnx` plus all five green and warning-free.
- **Building against the local SharpConsoleUI clone surfaces 2 NuGet advisory warnings** for
  AngleSharp. They are the framework's, not ours; a build against the package has none.

## Visual verification — the snapshot pipeline

A headless environment can't run `NetConsoleDriver` or render Kitty graphics, but the TUI is *not*
therefore unverifiable: it renders real frames headlessly.

```bash
dotnet build -c Release SharpMUTerm.slnx                     # -c Release, or --no-build lies to you
dotnet run -c Release --project src/SharpMUTerm.Tui --no-build -- \
  --snapshot --demo-config --view <name> --size 120x32 --out frame.ansi
python3 tools/ansi_frame_to_image.py frame.ansi frame.html   # or .svg
```

- **`-c Release` on the build, and it is not a formality.** A bare `dotnet build` produces *Debug*,
  `--no-build` runs the *Release* output, and nothing warns you: the snapshot renders happily from a
  binary that predates your change, so a new view comes out byte-identical to the default frame and a
  changed one shows the old behaviour. That reads exactly like a feature that does not work, and it
  has cost real time. Either build Release first, or drop `--no-build` (`--no-build` is only there to
  keep the render fast). Running the test suites also refreshes Release, which is why the trap hides
  whenever you happen to have just run them.
- **`--demo-config` is not optional for verification work.** Without it the snapshot renders
  whatever config is on the machine, and a saved `~/.config/SharpMUTerm/` quietly replaces the demo
  worlds — you end up checking your own data and calling it the demo.
- **The demo has no live session, so anything a session *writes* has to be written into `DemoScene` by
  hand — and it has to match.** Its saved main-window title is `Corvid` because that is what
  `BindSession` writes (`SessionTitle(session)`); it used to say `main`, and that one word of divergence
  hid the rail repeating the world's name under the character for as long as the rail has existed. Three
  separate bugs have now hidden in this gap. `RailWindowRowTests.TheDemoScenesMainWindowIsTitledTheWayA…`
  holds the two sides together; when you add state the demo fakes, pin it against the live writer the
  same way.
- **Views:** `worlds`/`settings`, `triggers`, `route`, `highlight`, `aliases`, `timers`, `keypad`,
  `set`, `textansi`, `input`, `logging`, `password`, `startup`, `freeze`, `spawn`, `split`, `move`, `drag`,
  `history`, `history-search`, `history-search-filter`, `draft`, `draft2`, `menu`, `menu-split`,
  `messages`, `quit`, `connections` (**two connections on one world** — the one view where the header's
  fraction, the rail's dots and the quit prompt's count are all visible together and all have to agree;
  every other view has at most one character connected per world, which is what hid a header dividing
  connections by *worlds* and a quit prompt reducing them to distinct world names), `characters` (**two
  characters genuinely open** — the one state the rail's ⌥J/⌥K column can be seen in, and the one thing
  `connections` cannot fake: that view marks dots connected and opens no session, while the cycle walks
  the sessions this client holds), `tint` (**two characters' panes in two different colours**, one
  focused and one not — the one frame where the per-character pane tint can be seen doing its job, since
  a single tinted pane cannot answer "whose pane is this", and the one that shows the two cues are on
  separate channels: identity in hue, focus in luminance. It writes the colours onto the real
  `CharacterDefinition`s; the **demo config carries none**, because `PaneTint.None` is the default and a
  tinted demo would make every other frame in the gallery show a state most clients are not in) and
  `tint-input`/`tint-input-moved` (the same scene with **both command lines up**, before and after a real
  ⌃→ — the only geometry that can show the *bar* wearing the focused character's colour beside a pane
  wearing it, an armed tinted band over an idle tinted one, and the colour travelling with the focus.
  The moved frame is also the one that shows a bar wearing a character's hue while its prompt reads
  `no connection ›`, which is the composition rule stated in paint: hue says whose, not whether),
  `deletions`,
  `compose`/`compose-literal` (the F1 composer in each of its two escaping modes — the pair exists
  because ⌥L changes what is *sent* and only the header says which way it is set; the demo has no
  session, so the target is handed in and pinned against the live writer by `ComposeWindowTests`),
  `mssp`/`mssp-none`/`mssp-never` (the **three** states of the F5 ▸ `i` server-information report —
  a report, a server that answered and publishes none, and a world nothing has dialled; all three
  reached by driving the real `i` into a real F5, and all three needed because the two empty ones are
  the pair it is easy to conflate), `web`,
  `rail-long`, `scrollback`, `scrollback-up`, `freeze-scrollback`,
  `links` (a URL too long for the pane it arrived in — a split, so the pane is narrower than the
  terminal, which is the whole defect; the frame shows one underlined span running to the pane's edge
  and continuing on the next row), `away`/`away-scrollback` (the bar marking where the reader was when they tabbed away from the
  *terminal* — the shallow absence, where the bar and everything below it are on screen at once and the
  pane is left on its live tail, and the deep one, where more arrived than the pane holds and the client
  has scrolled the pane to the bar itself; the second is the only frame that can show a bottom-anchored
  pane being "caught up" while nothing has been read, and the only one that would catch a scroll landing
  at the wrong row), `activity-bar` (the *other* absence — a window the reader was not watching: three
  lines land in the main window while Chat is in front of it, and picking main back lands on the `NEW`
  bar with those three under it. Separate from `away` because the two are separate facts with separate
  wording, and this is the one that happens many times an hour), `prefix-panel` (the ⌃B which-key
  panel — the state `prefix` becomes a few hundred milliseconds later, if no key has arrived),
  `focus`/`focus-moved` (a split *and* a second command line — the one geometry showing a focused pane
  beside an unfocused one and an armed bar above an idle one, before and after a real ⌃→), plus the
  default workspace
  (no `--view`). Any settings screen also takes a `-edit` suffix, which opens it and drives real
  keys in so the frame shows a field mid-edit. State toggles: `collapsed`, `prefix`, `timestamps`,
  and `timestamps-toggled` — the same column reached the *other* way, by dispatching the real ⌃P entry
  after the scene is already on screen, over a split so one frame carries a session window beside a
  spawn window. The pair has to match cell for cell; when it did not, that was the reported bug.
- **A snapshot never writes configuration.** `SharpMUTermApp` takes its `save` action from the caller and
  the snapshot path passes none, so an app that isn't the live entry point owns no file. That matters
  because the settings screens persist each committed change as it is made: without the gate, a
  `--demo-config` frame that drove a key into a field (`logging-edit`, `keypad-edit`, `deletions`) would
  write the demo worlds straight over your own `config.json`. It now also protects a **second** file:
  character passwords are saved in `secrets.json` beside the config (`SecretsStore`, `0600`), with
  `config.json` carrying only a meaningless `passwordRef` GUID, so a save writes a secret-bearing file too.
  `CommandSurfaceSettingsTests.AnAppWithNoSaveActionPersistsNothing` is the pin.
- **A snapshot writes no session log either, and neither does a test.** Same shape, same reason:
  `SharpMUTermApp` takes its `logRoot` from the caller and only `Program` supplies one, so an app that
  isn't the live entry point owns no log directory. It used to resolve the directory of
  `ConfigurationStore.DefaultPath` unconditionally, and the demo scene's `Aetherfall.Corvid` is
  `Logging.Format = Html` with no directory — so *every headless run that opened a session for it* created
  a real file under `~/.config/SharpMUTerm/logs`, beside genuine transcripts and the diagnostics log. 277
  empty ones had piled up. **Null means no logging at all, not "no default location"**: a character's
  explicit `Logging.Directory` is refused too, because the root is the app's answer to *may I write
  transcripts* and the character's directory only ever chose *where* within that — read the other way,
  every fixture naming a path is free to write outside itself. Nothing may then claim otherwise: the
  header's `LOG` cell reads `LOG off` (`HeaderLogFormat`, the same reasoning as
  `WorldSession.CurrentEncoding` — a configured value is a *preference* and a status cell may not report
  one as in force, which is why the `--demo-config` frames now read `LOG off`), and ⌃P ▸ *Start logging*
  refuses out loud. **The pin is a file count, not a mock** (`LogRootTests` +
  `LiveLogDirectoryGuard`, which lists the real log directory before the first test and after the last):
  the old code was internally consistent and still wrote those 277 files, so anything asserting on
  `LogFolder` or a fake sink would have passed all along. It also let a pile of per-test
  `character.Logging = new LoggingSettings()` workarounds be deleted — with them gone the suite exercises
  the gate, and an unfixed build leaks seven files a run instead of three.
- **Four views have more output than a pane holds** — `scrollback`, `scrollback-up`,
  `freeze-scrollback` and `away-scrollback`. Name them, rather than saying "the `scroll*` views": the
  prefix has now twice lagged behind the set it claimed to describe, and a reader looking for a
  long-output view goes by the list. (`away` is the shallow one and fits.) Every other view fits too,
  which is exactly why no snapshot caught the panes being unable to scroll at all. Reach for one of
  these (or `LoadLongScene`) whenever a change touches the output area.
- **Send the user the `.svg`.** For your *own* inspection render the `.html` — Chromium clips the
  bottom of a bare `.svg` through aspect-ratio scaling, which will make you chase a layout bug that
  isn't there.
- **Decoding a frame precisely:** the `.ansi` is cursor-addressed SGR. To check exact column widths
  or which background band covers which row, walk it into a `{row:{col:ch}}` grid tracking
  `48;2;r;g;b` (background) — note `48`, not `38`, or you will read foreground and conclude wrongly.

## SharpConsoleUI — the traps that cost the most time

Package `SharpConsoleUI`, repo `nickprotop/ConsoleEx`, pinned at **2.5.14**. **Always the package** —
there is no source-reference path and no switch. A sibling clone at `../SharpConsoleUI` used to be
detected and preferred *by default*, which meant merely having it on disk changed what this repo
compiled against, silently and differently from CI; that is gone. Clone it and read it, and never wire
it into this build.

App shape: `ConsoleWindowSystem(new NetConsoleDriver(RenderMode.Buffer), new ConsoleWindowSystemOptions())`;
fluent `WindowBuilder`/`Controls` factories; `AddControl` is builder-time, so keep refs and mutate at
runtime. Marshal background work with `system.EnqueueOnUIThread`; global keys via
`RegisterGlobalShortcut`; `system.Run()` blocks, `RequestExit(code)` ends it. Text is Spectre-style
markup (`[bold #rrggbb on #rrggbb]…[/]`, `[[`/`]]` escaping, `[link=url]…[/]` → `LinkClicked`).

- **Controls default to `HorizontalAlignment.Left`, which self-sizes to content** instead of filling
  the slot. This is the single biggest cause of "why doesn't this fill the width?" Use
  `.WithAlignment(HorizontalAlignment.Stretch)`. A `.Flex(n)` column only fills if the grid is
  arranged at full width *and* the child in it is Stretch.
- **Nothing focuses a control for you, and nothing keeps it focused.** A click in the output pane, a
  click on a tab strip, ⇥, and every overlay's `SetIsActive` all move focus. Typing is routed
  explicitly from `PreviewKeyPressed` and so survives that; **paste is not — it follows
  `FocusManager`**, which is why paste broke after any click while typing appeared fine.
  `FocusChanged → PinFocusToArmedBar()` makes "which bar ⏎ sends from", "what the framework pastes
  into" and "where the caret is drawn" one fact. Keep the pin; don't re-sync three places.
- **A caret assertion must read the frame, not the function.** `InputBarControl.GetLogicalCursorPosition`
  is where a caret bug lives, so a test built on it (`SharpMUTermApp.CaretReported`) agrees with the code
  while the screen disagrees. `CaretOnScreen()` reads the cell the *driver* was handed — it goes through
  `ConsoleWindowSystem.ProcessOnce` because `ForceRender` paints and stops, and the cursor pass
  (`UpdateCursor`) is a separate, `internal` step of the real loop. `FrameGrid.Decode` turns a frame back
  into cells so "where the text is" is read off the paint. **`PaintDOM` and `GetLogicalCursorPosition`
  derive the text's origin and scroll from one place** (`InputBarControl.Geometry`): the paint has always
  been clamped to the rows it was *given* and the cursor was not, so on any frame where the arranged
  height was short of the requested one they disagreed about the scroll.
- **The caret was placed from a rectangle only the *mouse* refreshed — an upstream bug, fixed in the
  clone, and a whole class of defect no test here can see.** `ControlBounds.ControlContentBounds` is
  written by `WindowEventDispatcher.UpdateControlLayout()` and by nothing else, and that has exactly one
  caller: `ProcessMouseEvent`. Both cursor readers preferred it and fell through to the layout node's
  (always fresh) `AbsoluteBounds` only while it was still `Rectangle.Empty` — so the caret was correct
  until **the first mouse event the window ever saw**, which populated the cache and froze the caret at
  wherever the pointer had last seen the control. Every later layout change (the input bar growing on
  F8, the second bar showing or hiding) then moved the text and left the caret behind, permanently:
  typing does not fix it, and leaving the terminal and coming back does, because that crosses it with
  the pointer. Fixed in `../SharpConsoleUI` (`CursorBoundsStalenessTests`) by preferring the node in
  both readers. **Two things follow for this repo.** That fix is not in the pinned package, and this
  repo builds only against the package, so we still have the bug until a release ships it. And nothing
  in our suite can catch this class of bug: the framework only registers its driver-mouse handler inside
  `Run()`, which no test calls, so `ControlContentBounds` is never populated headlessly and every caret
  test here silently exercises the *fresh* path. A caret report that our tests cannot reproduce is
  evidence for looking upstream, not evidence that it is not happening.
- **Ask the driver for the terminal size, never a literal.** `_system.ConsoleDriver.ScreenSize` is
  correct from the moment the window system exists — before any window does. Chrome built in the
  app constructor against a literal wrapped the header on the first frame of any narrower terminal,
  and snapshots never saw it because every render path rebuilds the header on the way past. Same
  shape as gating chrome on `headless`: right in a snapshot, wrong in a terminal. Test the class of
  bug by reading chrome width off a *freshly constructed* app.
- **Vertical space at the window root is sticky-first, Fill-last** (`Layout/WindowContentLayout.cs`).
  Sticky-top and sticky-bottom children are measured first and then trusted; Fill children divide
  what remains. A flow control therefore *cannot* starve a sticky one — so "the workspace is greedy
  and squeezed the input bars" is a diagnosis this layout cannot produce. But there is no
  `MinHeight` concept and **nothing checks the two sticky bands fit each other**: at 80×6 they
  over-commit and the status line is arranged off-screen. `SyncInputHeights`' veto counts chrome
  rows to prevent it, and `PaintStatus` re-runs it because the status line's length changes at
  runtime. Assert layout with **arranged bounds** (`ActualHeight` after a real frame), not arithmetic.
- **The desktop panels are off unconditionally** (`ShowTopPanel: false, ShowBottomPanel: false`).
  They restate our own header and trim window titles to fifteen cells — on this app, one row reading
  `SharpMU...lient`. They were once hidden in headless only, so no snapshot showed them and the
  truncated title survived to a real terminal.
- **`WindowBuilder.Centered()` must come *after* `WithSize()`** — it reads `_bounds` and falls back
  to 80×25, so centring first positions the window as if it were that size.
- **`PromptControl` is not the command line** — `InputBarControl` is. The framework's prompt is
  single-line by construction (`SetInput` replaces `\n` with a space) and unfocuses itself on ⏎
  (`UnfocusOnEnter`, default true, not settable through the builder).
- **Settings screens have no `IPasteTarget` and cannot easily have one.** They are markup rebuilt
  wholesale every key, with the edited field a buffer in `SettingsSession`. `SettingsOverlay`
  listens at `IConsoleDriver.Paste` instead. That can't double-fire *only* because no control on
  those windows accepts paste — add a focusable `IPasteTarget` there and both paths will.
- **The framework's own `ExitKey` defaults to Ctrl+Q** and calls `RequestExit` with nothing in
  between. Ours won only because application shortcuts are tried first. It is set to `null`.
- **The framework claims `Alt+1`–`Alt+9` too** — `InputCoordinator.HandleAltInput` selects among
  top-level windows by index. It is the third framework default found outranking us, and the one with
  the least warning: it is reached from `ProcessInput`'s fall-through for *any* unhandled Alt chord and,
  unlike the move and resize handlers on the two lines above it, is **not** gated on
  `IsMovable`/`IsResizable` — so `Movable(false)`, which switched off the ⌃X-swallowing move handler,
  did nothing here. `⌥1`–`⌥9` are ours (`JumpToWindow`, go to the numbered **window**) and are claimed as
  **application shortcuts**, which `InputCoordinator` tries before it offers a key to any window at all;
  `PreviewKeyPressed` would also have won (`WindowEventDispatcher.ProcessInput` raises it first and
  returns immediately when handled), one step later. **All nine are claimed, in range or not**: an
  out-of-range ⌥7 reports and stops rather than falling through to a window selector that would do
  something else. `⌥0` is deliberately left free — the framework ignores it too, so it stays bindable.
- **Ctrl+digit is not a chord this terminal has, and that is why the numbered jump is Alt.** Read off a
  pty with `kitten @ send-key` at a raw-mode reader, the same way `Alt+Shift+arrow` was established:
  every `Alt+digit` is `ESC` + the digit (one Alt chord out of `ProcessEscape`), while Ctrl+digit is
  `1`→`0x31`, `2`→NUL, `3`→**`0x1B` (Escape)**, `4`–`7`→`0x1C`–`0x1F`, `8`→**`0x7F` (Backspace)**,
  `9`→`0x39`, `0`→`0x30`. So three of them are keys the client cannot afford to bind over and three are
  indistinguishable from typing — binding any breaks the plain key, exactly as with Ctrl+H/I/J/M.
  Recorded per digit in `MacroKeys.DigitBytes`, which is what F4 prints.
- **Where a chord could be Ctrl or Alt, it is Alt — and "safer" is measured, not preferred.** Alt+key is
  `ESC` + the key and arrives intact; Ctrl+key collapses onto a byte the terminal may already spend
  (`⌃H` Backspace, `⌃I` Tab, `⌃M`/`⌃J` Enter, **`⌃Tab` a bare Tab**), and Ctrl+digit has no usable
  encoding at all. Every chord in `MacroKeys.AppShortcuts` has been driven at a raw-mode reader with
  `kitten @ send-key`; the bytes are in `DigitBytes`/`ControlBytes` and F4 reads them.
  - **The connection pair is `⌥D` / `⌥R`.** Disconnect was `⌃D` on one justification ("the terminal's
    hang-up chord") and reconnect `⌥R` on a different one ("one modifier over from `⌃R`") — each fine
    alone, and together making a reader learn two modifiers for one concept. `⌃D` is **released**, not
    kept as an alias: a second key for one action is either a secret or a duplicate row on every surface
    that lists chords, and letting it go hands a clean Ctrl chord back for macros. Nothing takes it —
    `HandleMoveInput` is gated on `IsMovable` (false here) and acts only on the arrows and `X`.
  - **`⌃Tab` is gone and was never real.** It was claimed as a second spelling of "next window"; a
    terminal writes `0x09` for it, which *is* Tab, so the parser reports `ConsoleKey.Tab` with no
    Control bit and the claim could never have matched — while `Claimed` is consulted first, so F4 told
    users a chord was taken that cannot arrive. `⌃N` is the chord; the byte is in `ControlBytes`.
  - **Deliberately left on Ctrl**, because the convention is worth more than the pattern: `⌃R`
    (readline's reverse history search), `⌃P` (command surface), `⌃Q` (quit — and safe here because
    `TerminalRawMode` clears `IXON`, so it is not XON), `⌃B` (tmux's prefix), `⌃O` (pane cycle),
    `⌃N`/`⌃W`, and the command line's `⌃A`/`⌃E`/`⌃K`/`⌃U`/`⌃L`. A sweep that moved everything
    would be as wrong as one that moved nothing.
  - **Freeze is `⌥F`, and `⌃F` is find.** Freeze was on `⌃F` and left it for exactly the reason the
    keys above stay where they are: `⌃F` means *find* to everyone who has used a computer, and that
    convention outweighs freeze's claim on the chord. Freeze kept its letter and changed its modifier —
    the smallest move that frees it; `⌥F` is `ESC f`, measured. Nothing is left behind on `⌃F` as an
    alias (the `⌃D` rule). The label a **frozen** reader is looking at (`FreezeBarRenderer`) moves with
    the chord: a bar naming a key that no longer thaws the pane would be the worst possible place to
    leave a stale chord.
  - **History recall has its own chord: `⌥↑`/`⌥↓`.** The bare arrows still recall where the caret has
    nowhere further to go, and that is unchanged — but it is a rule that stops being usable the moment
    the bar grows to a second row, which was the report. `⌃↑`/`⌃↓`, the alternative offered, was never
    available: the terminal writes `ESC [ 1;5 A` and this client already spends it on pane selection
    and on the ladder onto the second command line. `ESC [ 1;3 A` is Alt, is free, and is what
    `TryRecallKey` now matches on — **exactly**, so `⌥⇧↑` (the pane resize) still reaches its own
    handler. A macro bound to `Alt+Up` wins over recall, because `DispatchMacro` runs first: the same
    relationship `Ctrl+←/→` has with pane selection.
  - **Known and not fixed here**: `⌃N` and `⌃O` have no reverse (the character cycle does — `⌥J`/`⌥K`),
    and `⌃W` and `⌃B x` are two chords for one action. Both are shape complaints rather than defects,
    and both are behaviour changes rather than modifier moves.
- **There are two numberings and one cycle, over three different sets, spelt differently everywhere.**
  **Windows** are `⌥N` in `Workspace.WindowsFor(active)` order; **panes** are `pane N` in `Layout.Panes`
  order; **characters** have no number at all and are reached by the `⌥J`/`⌥K` cycle. Both numberings are
  *creation* order, both take the number from the **index** rather than the sequence, and neither is ever
  written in the other's vocabulary. That separation is the whole safety property: the digits are the
  same ten characters, so if a surface could print a bare number that might be either, a user reading it
  cannot know which key to press.
  - **⌥1–⌥9 go to a window of the *active character*, numbered from 1 within that character**
    (`JumpToWindow`). Windows rather than panes because a capture sharing a pane with its character's own
    window had no number of its own and was reachable only while it happened to be that pane's active tab
    — "switch not just characters, but captures, etc." **Scoped rather than global**, because global
    failed on a real client the first day: three characters sharing pane 1 as tabs, and the sidebar
    giving all three of them `⌥1`. Nine digits also do not stretch over everybody's windows.
    An **unowned** window (the web view) is in *every* character's list, so it wears a different digit
    under each — which is exactly the set `BuildRailWindows` draws, so the sidebar and the chord are one
    list read twice. `WorkspaceWindow.Sequence` is a per-workspace counter assigned at creation,
    persisted in `WorkspaceWindowState`, never reused; a window restored without one is seeded from the
    saved order.
  - **⌥J / ⌥K cycle characters**, forward and back. Letters and not a third digit row because there is
    no third digit-bearing modifier this terminal delivers: kitty writes `⌥⇧1` as `CSI 49;4u` and `⌃⇧N`
    as `CSI 110;6u`, kitty-keyboard-protocol sequences the parser does not decode and silently drops
    (measured, not assumed). It walks **only the characters already open**, because
    `SwitchToCharacter` *creates* a session and a window for one that is not, and a cycle key that did
    that per press would dial through a configuration by accident.
    **`Workspace.Windows` is a dictionary's values and is not the numbering** — its order is unspecified
    after a removal, so opening a window into a closed one's slot renumbers everything after it
    (`WindowNumberingTests.AWindowOpenedIntoAClosedOnesSlotStillTakesTheLastNumber` is the pin, and it
    took a four-window fixture to expose: with fewer, both orders agree).
    Only **placed** windows are numbered — one no pane holds is drawn `closed` and a digit for it would
    name nowhere to go.
  - **⌃B 1–⌃B 9 go to a pane.** It was ⌥N until windows took that; it is on the prefix because that is
    where the rest of the pane keymap lives, and it is kept rather than dropped because the pane
    numbering does not go away with the chord — move mode badges each pane with it, the move and drag
    prompts say `pane 2`, the ⌃P entry says `Go to pane 2`, and ⌃O counts in it. It also refuses on a
    single-pane workspace *including ⌃B 1*, because the which-key panel dims that row.
  - **`Layout.Panes` is creation order; `LayoutNode.Panes()` is tree order, and they are different
    things.** Tree order (left-to-right, then top-to-bottom) is geometry and is what `LayoutSolver`,
    `PaneResize` and the renderer walk. It used to be the numbering too, and a number that is a function
    of *where a pane is* moves when a pane is inserted before it: dropping a window on the left edge of
    pane 2 made that pane into pane 3, so the digit that meant it stopped meaning it while the user was
    doing something else. `PaneNode.Sequence` is the same mechanism as the window one, seeded from tree
    order for a config written before the field.
  - **The number is the *index*, never the `Sequence` itself,** in both numberings. Sequences have holes
    after a close; the numbering may not, or a digit is a silent no-op with the things still on the
    screen. `PaneNumberingTests`, `WindowNumberingTests`,
    `WindowJumpTests.ClosingAWindowCompactsTheNumberingOnTheChordAndInTheSidebar`.
  - **⌃O cycles panes in the pane order.** It read tree order once, which agreed with the numbering back
    when the numbering *was* tree order. Its partner is ⌃B N, not ⌥N — the window movers (⌥N, ⌃N) are a
    separate ladder — and three presses of ⌃O from pane 1 must land where ⌃B 4 does.
- **The rail is the only place you read either chord, and every row carries the one that reaches it.**
  Window rows carry their own `⌥N`; the two character rows either side of you in the cycle carry `⌥J`
  and `⌥K`, and every other character row carries nothing, because nothing else is one keystroke away.
  A character row used to carry the chord of its own *window* — which, once the numbering was scoped,
  printed `⌥1` against every character on the screen. **The sidebar prints `⌥…` and never `pane N`**: it
  is about windows and characters, and a pane noun there would be a second reading of the same column.
  - **The chord leads the row; the badges trail it.** The reported complaint was the gap — five blank
    cells (the reserved pen and unread fields) between a window's name and its chord. Those fields
    cannot be removed (a cell that costs only when it has something to say resizes the sidebar on a
    keystroke or a line of output), so the chord moved to the front instead, against the name it names.
    The row's measured width is unchanged by the move: the demo rail is 22 columns before and after.
  - **The chord field is reserved per *row kind*.** Reserved, so the width does not move as unread
    arrives, as a draft is typed, or as the `⌥J`/`⌥K` pair travels between rows. Per kind, because with
    fewer than two characters open no character row can hold a cycle chord, and reserving across both
    spent three cells on every character row of the commonest client there is. `RailChordColumnTests` is
    the pin, on the sidebar's column count *and* the pane rectangles.
- **The ordinal movers carry a zoom; the directional ones cannot.** A zoomed workspace realises
  exactly one pane, so ⌃O, ⌃B N or ⌥N moving the selection and leaving `ZoomedPaneId` behind puts the
  selection, the session the bar talks to and the caret on a pane that is **not on the screen** — the
  "attention on one pane, keystrokes to another" defect again. `WorkspaceLayout.CarryZoomToFocused`
  re-points an existing zoom (it never starts or ends one) and both movers call it. ⌃←/→/↑/↓ do not and
  must not: with one pane realised there is no neighbour to ask for, which is why they refuse out loud.
- **`MarkupControl` does not scroll and does not bottom-anchor.** `PaintDOM` paints rows from index 0
  until the box runs out, with no offset of its own — so a control holding 100 lines in a 10-row box
  renders lines 1–10 for ever and everything appended lands off-screen. Scrolling lives in
  `ScrollablePanelControl`, and every output region in this app is now wrapped in one
  (`SharpMUTermApp.ScrollViewFor`). Two things about it:
  - **`AutoScroll` is not "on AddControl"**, whatever the property doc says: it re-pins to the bottom on
    *any* repaint while enabled, detaches when the user scrolls up and re-attaches at the bottom
    (`ScrollablePanelControl.Rendering.cs:125-133`, `.Scrolling.cs:145-152`). That is terminal
    behaviour; don't reimplement it. But it moves the offset **during paint**, after the children were
    arranged, so the frame that discovers new content is one frame stale — a headless snapshot or test
    must render a settling frame (`SettleScroll` / `RenderWholeFrame`).
  - **`ScrollToTop`/`ScrollToBottom` do not touch `AutoScroll`.** Only `ScrollVerticalBy` treats itself
    as a user gesture. A jump-to-top that leaves auto-scroll armed is undone by the next repaint.
  - **A disposed `ScrollablePanelControl` clears its children**, and `RebuildPaneArea` disposes the whole
    old tree. The kept viewports therefore refill themselves; markup controls survive disposal (they
    override nothing), which is why the same one is re-parented for the life of the app.
- **Only `MarkupControl.AppendLine` and `FeedRange` hand pane content to a control** — the seam a
  windowed feed replaces. Appending re-parses the whole control (the parse cache is keyed on a content
  version), so never "refresh" a pane by re-`SetContent`-ing the full buffer on a scroll or a frame.
  `BindSession`'s scrollback replay used to be a third route, painting straight onto the control; the
  buffer and the control then disagreed and the next thing to re-feed that pane dropped those lines.
- **A buffered line is a `PaneLine` — markup plus, held apart from it, when the line arrived** — and the
  timestamp gutter is glued on in `Compose`, on the way to a control. That is what makes *show
  timestamps* a render-time decision: it repaints history in every window, including spawn windows,
  whose history is markup in this buffer and nothing else (there are no `StyledLine`s left to
  re-render). Baked into the markup at append time it reached only lines yet to arrive, which is
  indistinguishable from a dead command on a quiet connection — the reported bug. **Whatever else a
  setting does, do not decide it at append time if it describes lines that have already arrived.**
  The re-feed it costs (`RepaintPanes`) is the expensive whole-buffer path, and it is affordable only
  because it is bounded by one deliberate keystroke; do not reach for it per line or per frame.
- **A ⌃P view toggle must not persist through `SaveConfiguration`.** That is the settings screens'
  funnel and it also runs `ReloadAutomation`, which re-periodises every running timer and so resets
  every other timer's phase. The write-only half is `PersistConfiguration`.
- **The rail's width is derived from its widest row, so the rail must be re-measured whenever its rows
  change — not only when the pane area is rebuilt.** `RefreshRail` recomputes it and resizes the sidebar's
  own grid column (`ApplyRailWidth`); the width was once computed only in `BuildWorkspaceRow`, so the
  startup retitle poured longer rows into a column sized for shorter ones and the framework **wrapped**
  them. A wrapped rail row is what got reported as "the sidebar looks broken". Rows are also **elided**
  to `RailMaxWidth - RailMargin` before they are measured (`RailRenderer.Render`'s `maxWidth`), because the
  width is *clamped*: without that, any label past the clamp — a web page's title, most easily — wraps no
  matter how carefully the column is sized. The width feeds per-pane NAWS through the pane rectangles, and
  that report rides the frame (`PostBufferPaint → ReportPaneSizes`), so nothing needs to announce it here.
- **Nothing volatile on a rail row may cost a cell only when it has something to say.** The width is the
  widest row's, so a badge that appears out of nothing widens the sidebar, narrows every pane and
  re-announces a new terminal size to every connected server. The unsent-draft ✎ did exactly that on the
  *first keystroke of every line* (the reported "sidebar grows by one character"), and the unread count
  does it unbidden from the wire — twice, once appearing and again at 9 → 10. Both now render into
  reserved fields that are blank when empty (`RailRenderer.UnsentFieldWidth` / `UnreadFieldWidth`, the
  count capped at `99+` so the field is finite). The sidebar is a few columns wider than it used to be at
  rest and it no longer moves. What is *left* variable, deliberately: the labels (they change when the
  thing they name does, and elision caps them), the hosting-pane column (only in a split, and appearing
  when the layout changes, which is already a relayout), and the set of rows itself (a spawn window opening
  is structural). `RailRendererTests.Render_An*DoesNotChangeARowsWidth` and
  `FocusIndicationTests.TypingDoesNotMoveAnyPaneRectangle` are the pins — the latter is the typing
  counterpart of `MovingFocusDoesNotMoveAnyPaneRectangle`, and it has to read the rail's *widest* row,
  because in the demo scene the Chat row's unread badge coincidentally masked the pen's two cells.
- **A tab title is *markup*, and that is what makes the new-activity tint free.** `TabControl.Rendering`
  parses each label with `MarkupParser.Parse`, and every width it is measured by — the header paint, the
  strip's desired width, and the click hit test that picks the tab and its `×` — is
  `MarkupParser.StripLength`. So a `[#rrggbb]` tag on a tab costs **no cells** and moves no hit test.
  `TabTitles` claimed the opposite for as long as it existed, and the claim had two costs: the unread
  count went out untinted, and a window title was never escaped — a window called `[Chat]`, or a web view
  titled from the page it loaded, had that eaten as a tag by the parser *and* by the hit test. Titles are
  `MarkupText.Escape`d now. The tint covers the name and the count only; the `▌` stays outside it, because
  focus and activity are independent facts and a marker that changed colour on an incoming line would be
  reporting the wrong one. **The two cues are different channels on purpose** — focus is said entirely in
  *backgrounds* from the theme's chrome family, activity in a *foreground* no plane is painted in.
- **One unread count, one spelling: `UnreadBadge`.** The sidebar and the tab strip are two views of
  `WorkspaceWindow.Unread`, and they had two formatters — the rail capped at `99+`, the tab printed the
  raw integer, so a busy channel read `99+` in one place and `(4127)` in the other. Cap, field width and
  tint colour (the app accent, previously a `#00f5b7` literal in `RailRenderer`) now live in one type.
  **The tab strip deliberately does *not* get the rail's reserved-width treatment**, and the asymmetry is
  real rather than an oversight: a rail row's width sizes the sidebar's grid column and the pane area is
  what is left over, so there a badge that appears narrows every pane; a tab strip is a `TabControl`
  arranged `Fill`+`Stretch` inside the pane it already fills, and the framework pads the header row out to
  the pane's own edge, so a longer label only shifts the tabs beside it. Reserving three cells per tab
  would cost width on every strip for ever to prevent a reflow this layout cannot produce. The cap is kept
  for the *other* two reasons: the two surfaces must agree, and an unbounded count must not push a narrow
  pane's later tabs off the end. `TabActivityIndicatorTests.ActivityMovesNoPaneRectangle` is the pin.
- **The status row's scrollback distance was the same bug one field over, and it was live.** That segment
  counts lines below the viewport — a number that grows *unbidden from the wire* while a reader sits in
  their scrollback — and it was written out raw. Its own comment already warned that a wordier phrasing
  had once wrapped the row and that "a status line that grows a row takes one off the workspace"; the
  number was never guarded. At 80 columns the 99 → 100 step took the row from 80 cells to 81, it wrapped,
  every pane lost a row, and per-pane NAWS re-announced the new size to every connected server. It is now
  in a reserved field capped by `UnreadBadge`, the same as the rail's. **The lesson generalises: any cell
  on the chrome whose width is a function of something arriving from the wire needs a reserved field, and
  "it is only one digit" is exactly the size of every instance of this bug so far.**
- **A rail window row is `what` then `how you get there`, and neither column may wear the other's word.**
  A character's own session window reads `main` (`RailWindowLabel`); its title names the *connection*,
  which the row's own ancestors — its character, under its world — have already said. The second column
  is the `⌥N` that goes to that window and exists only once there are two windows (one window, one place
  to be). It used to be the hosting *pane*, from when ⌥N named panes — and before that it called the
  first pane `main` too, and `▪ main   main` is two meanings in one line.
- **Focus is indicated by recolouring what is already drawn — never by spending a cell.** Per-pane NAWS
  is derived from the pane rectangle (`PaneOutputRects`), so a border, gutter or marker column that only
  the focused pane has would re-announce a different terminal size to every connected server on every
  focus change and reflow the game's own output. The cues are the pane's own plane
  (`WorkspacePalette.Focus`), the active tab's chip colour (`TabControl.Active*BackgroundColor`), and a
  `▌` in the tab *title* — all zero-cost. `FocusIndicationTests.MovingFocusDoesNotMoveAnyPaneRectangle`
  is the test that stops this being "improved" into a border. Colours live in `WorkspacePalette`, whose
  constants are all derived from a `ScreenPalette` pair so the workspace and the settings screens share
  one idea of what focus looks like; the focus step is `CursorBg ÷ EditBg`. The **command line** is the
  fourth cue and the one that follows you between panes: it wears the armed band when ⏎ sends from it,
  and — when the character behind the focused window has chosen a colour — that character's hue as well
  (see the tint entry below).
- **A plane says two things, and they are kept on separate channels: identity is *hue*, focus is
  *luminance*.** A character may be given a colour (`CharacterDefinition.Tint` → `PaneTint`, Core;
  `WorkspacePalette.Tint`, Tui; F5's `tint` row), that colour becomes the pane's base plane, and the same
  focus multiplication is applied on top of it — `PaneSurfaceTone` is two lines and the composition is
  the whole design. Load-bearing, in order. **All six tints sit at exactly one luminance**: the plane is
  re-lit to the target before the anchor is mixed into it (`AtLuma`), and luma is linear in the channels,
  so both ends of the blend share that luma and so does every point between them — by construction, on
  any theme, for any `TintStrength`. No character's pane is brighter than another's, and the focus step —
  a *multiplication* — therefore lands the same **ratio** above each. **That one luminance is `TintDepth`
  below the untinted surface, not level with it**, and the change is deliberate: the first cut held the
  tinted plane at the surface's own luma to preserve every contrast ratio the theme was designed around,
  and MU\* servers are written for **black** terminals, so the plane their bright ANSI is read on wants
  to be darker than the client's chrome rather than level with it. **The depth is bounded and the bound
  is arithmetic**: a client may hold tinted and untinted characters at once, so a depth reaching
  `1 ÷ FocusScale` would leave a *focused* tinted pane no brighter than an *unfocused* untinted one —
  the focus cue reporting the wrong fact. `TintDepth` is the geometric mean of that floor and no
  darkening at all, so the untinted surface sits midway in ratio (√FocusScale ≈ 1.26 either way);
  `EveryFocusedPaneOutshinesEveryUnfocusedOneAcrossTheWholePalette` is the pin, and it is why "make them
  darker still" is a change that has to move `FocusScale` too. **The anchors are saturated for the same
  reason**: chroma is bounded by luminance, so at the darker target a muted anchor has nothing left —
  the first set's two closest colours were ΔE 8.2 apart and its nearest was ΔE 7.7 from the untinted
  plane; these are ΔE 14.4 and ΔE 12.1. **It says nothing on a monochrome terminal, deliberately** — the
  cue that must survive a lost hue is focus, and the rail and the tab title still name the character in
  words — and it is a **truecolor** cue, which is unacceptable for focus and is why
  `FocusSurvivesA256ColourTerminal` exists and has no tint counterpart. And **it costs no cells**, for the
  same NAWS reason the focus cue does not: `PaneTintTests.TintingACharacterMovesNoPaneRectangle` is that
  pin (rectangles *and* `LaidOutRows`, since the command line is a sticky band and one that grew a row
  would take that row off every pane), and it commits through `SaveConfiguration`, which is also what
  makes an F5 edit reach the panes *now* rather than at the user's next focus move. A pane wears the
  colour of the window **in front of it** — a pane can host several characters' windows as tabs and
  paints one rectangle — resolved through the workspace's ownership record and never through `_active`,
  because a background pane wearing the focused character's colour would say the opposite of what it
  means. The palette is a **closed set of six names** and not a hex: a free colour cannot be validated
  against a theme the user may change tomorrow, and a name survives that change where a hex picked
  against a dark theme becomes a hole. `PaneTint.None` is the default and **no migration marks anybody**
  — the same reasoning as `ConnectAtStartup`, and the reason the schema version did not move for it.
  - **The command line wears it too, and takes the hue without the depth** (`PaintInputBands`,
    `WorkspacePalette.IdleBand(theme, tint)` / `ArmedBand(theme, tint)`). On a pane, luminance carries
    focus as a ratio that a step applied equally to all six leaves intact; on the input row luminance is
    *already* spoken for — it is the whole armed-versus-idle cue — so a colour that moved it would put a
    second fact on a channel that carries one. Hue-only means the armed bar stays exactly the step above
    the idle one that it always was, in every colour and on every theme, and that `IdleInk` (measured
    against the untinted band, and shared with the tab chips) keeps the contrast it was picked with.
    The armed band is derived **from the tinted idle band** rather than tinted itself, so the lean toward
    `Theme.Prompt` survives on top of the character's hue and the pair still differ in brightness *and*
    colour. The one theme where the step narrows is **Light**, where the lift clamps against white before
    the prompt lean is applied — it predates tints, its untinted band is already the narrowest of the
    three, and `ATintedCommandLineIsStillObviouslyArmedOrIdle` holds it to `Visible` there and to
    `Obvious` everywhere else.
  - **Whose colour the bar wears is `SendTarget`'s answer, falling back to the focused window's recorded
    owner** (`InputTint`) — never `_active`, which is the misdelivery bug in every shape it has had. The
    fallback is the second arm `WindowSession` already walks and the same record `PaneTintOf` reads, so
    the bar and the pane above it are *one* answer rather than two that agree most of the time.
    **The colour says whose, never whether**: a focused window whose owner has no session this run wears
    that owner's colour while the prompt reads `no connection ›` and ⏎ refuses out loud — identity and
    reachability are two facts, and the row already states the second twice. A window nobody owns (the
    web view) leaves both bands exactly as they were.
- **The Ctrl+arrows move pane *selection*, not keyboard focus — but selection carries the session.** The
  pin (`FocusChanged → PinFocusToArmedBar`) is untouched: typing always lands in the armed command line
  wherever you have navigated to. That is a fact about which *control* gets a keystroke, and it says
  nothing about **which character the bar talks to**; the first cut of these keys reasoned "it never moves
  focus, so no third piece of state is needed" and left the command line pointed at the world you had
  navigated away from. Keep the two separate and keep both.
  `TryFocusKey` sits in `HandleWindowKey` **after** `DispatchMacro` (so `MacroKeys.Verdict` reporting a
  macro on `Ctrl+Left` as live stays true) and **before** `TryScrollKey`/`TryRecallKey` and the command
  line. Word movement moved from `Ctrl+←/→` to `Alt+←/→` to make room. Vertically the panes and the bars
  are one ladder: ⌃↓ off the last pane arms the second command line, ⌃↑ leaves it (the second bar is per
  *window*, so the ladder is taken from a pane whose window has one).
- **A handler that does not handle a chord must *decline* it, not claim it — ordering is the second line
  of defence, never the first.** Three separate defects have been the same sentence: `TryRecallKey`
  looked at the key and never the modifiers and swallowed `Shift+↑`; the framework's move-mode handler
  took every unclaimed `Ctrl+` chord until `Movable(false)`; and `InputBarControl.ProcessKey` reached an
  **unguarded** arrow switch below its `alt`/`ctrl` blocks, so every modified arrow was a plain caret
  move — claimed with `return true`, because `Move(Buffer.MoveLeft())` is true whenever there is a
  character to the left of the caret. That last one had a signature worth remembering: it worked on an
  empty command line and died on the first thing you typed, and `TryMoveRow` returning false on a
  one-row bar hid the vertical half of it entirely. Both now match on **exact** modifiers
  (`key.Modifiers == 0` for the bare navigation keys, `== ConsoleModifiers.Alt` for word movement).
- **The pane-resize chord is `Alt+Shift+arrow`, and it is `Alt` because of the *terminal*, not the
  parser.** It was `Ctrl+Shift+arrow`, chosen on the strength of a test proving `AnsiInputParser` decodes
  `CSI 1;6 <final>` — a true claim, and a smaller one than it was read as. `kitty_mod` is `ctrl+shift`
  and kitty binds all four arrows by default: `ctrl+shift+left`/`right` are `previous_tab`/`next_tab`,
  whose handlers return `None`, which kitty's `dispatch_action` treats as *consumed* — those bytes are
  never written to the pty and no app-side ordering can reach them. `ctrl+shift+up`/`down` are
  `scroll_line_up`/`_down`, which return `True` (pass through) while the alternate screen is up, which
  is the whole reason the vertical half appeared to work and the horizontal half looked broken. Nothing
  in kitty's default map claims `alt+shift+arrow`; its encoder writes `CSI 1;4 <final>` for it (observed
  with `kitten @ send-key`), the parser reads Alt+Shift out of that, and it is distinct from Shift alone
  (scrollback), Alt alone (word movement) and Ctrl alone (pane selection). **A decode test is not an
  arrival test** — check the emulator's keymap before spending a chord, and record the answer in
  `MacroKeys.Verdict`.
- **A resize arrow names what happens to the focused pane, never which way the divider travels**:
  ⌥⇧↑ taller, ⌥⇧↓ shorter, ⌥⇧→ wider, ⌥⇧← narrower, from either end of the split. The vertical pair was
  once read off the divider, so the bottom pane's ↑ made it shorter — and the test only exercised the
  top pane, where the two rules agree. `PaneResize.StepCells` is **1**, the same on both axes.
- **`SharpMUTermApp.Activate` is the one activation path, and activating a window activates its session.**
  Every gesture that brings a window forward goes through it: a tab click (`OnTabChanged`), a rail or ⌃P
  entry, a character switch, an MXP `PROMPT`, the web view, and both movers of pane selection (`FocusPane`
  for ⌃arrows and the ⌃P `Focus pane …` entries, `CyclePane` for ⌃O and ⌃B o). They were five paths and
  they disagreed — the pane movers and the tab click reloaded the drafts but left `_active` behind, so
  typing after navigating went to the world you had left. It does four things: resolve and adopt the
  session (`AdoptSessionOf`), select the pane's tab, `ChangeWindow()` (drafts, second bar, history
  cursors), and `SyncToFocusedPane()` (indicator, scrollback segment, NAWS). Re-entrancy through the
  framework's own `TabChanged` is guarded by `_activating`.
- **Whose window is this? One resolver, `WindowSession`: the session printing into it, else the owner the
  workspace records, else refuse.** There is no third arm falling back on `_active` — that fallback is the
  bug, in every shape it has had (a link clicked in a background pane sending to the focused character; a
  pane selection moving without the bar following; **⏎ itself**, see below). Bounded through `Snippet`
  where a message names a window, because a window title can be a *world's* text (the web view is titled
  from the page it loaded). **Ownership is recorded on every path that binds or adopts a window**
  (`BindSession` and `OpenSessionWindow`), because the main window is built before any session exists and
  the rail and this resolver both read ownership.
- **⏎, a macro key and the prompt all resolve through the *focused window*, not `_active`
  (`SendTarget`).** `AdoptSessionOf` deliberately leaves `_active` on the previous world when the window
  you navigated to has no session of its own, and `OnCommandEntered` used to send to `_active` — each
  half defensible, and together a misdelivery: with a connected Ann in one pane and a session-less window
  in the other (a *resumed* workspace, which is every pane at startup), ⌃→ moved the focus, the indicator
  and the tab marker, and the next line went to Ann, whose pane was not the focused one. **Navigation
  always succeeds** — asking to go somewhere arrives, and the pane takes the focus and the caret like any
  other. It is *sending* that needs a target: with none, ⏎ refuses out loud at the moment of sending
  (`NothingToSendTo`, which names the pane's owner and what opens it) and the prompt reads
  `no connection ›` (`PromptLabel`) rather than naming a world it cannot reach. A client with nothing open
  anywhere keeps the resting prompt it always had — there is no other world for a keystroke to reach, and
  that is the arm the snapshot demo renders through.
- **The scrollback keys are routed from `PreviewKeyPressed`** (`TryScrollKey`), and the wheel from the
  driver (`ScrollPaneUnderPointer`), for the same reason everything else in this window is: focus is
  pinned to the armed bar, so `ScrollablePanelControl.ProcessKey` — which returns false unless it has
  focus — would never see a key.
- **Control chords collapse onto their ASCII bytes, so some are unbindable.** `AnsiInputParser`
  decodes no CSI-u and enables no `modifyOtherKeys`: `Ctrl+H` arrives as `Backspace` with
  `control: false` (byte 0x08), and I/M/J are Tab/Enter/Enter — the app cannot even tell the modifier
  was held, so binding those breaks the plain key instead. **`Ctrl+⏎` and `Shift+⏎` are the same
  problem and cannot be bound at all**: CR (0x0D) and LF (0x0A) both become a bare `ConsoleKey.Enter`
  with no modifier bits. They stay in `InputBarControl`'s key table because the Windows
  `Console.ReadKey` path does report them, but **no surface may advertise them** — that is
  test-enforced (`AdvertisedKeyHonestyTests`). `MacroKeys.Verdict` is the readable form of all this.
- **ESC + a control byte arrives as two keys, and that is a chord you can reassemble.** Only
  `ESC` + a *printable* byte becomes a single Alt chord; `ESC` + a control byte is emitted as
  **two** key events (`AnsiInputParser.ProcessEscape`) — which is why `Alt+Backspace` is not
  available. **`Alt+⏎` is, though**, and it is the newline chord: `SharpMUTermApp.TryAltEnter` pairs
  an Escape with an Enter arriving inside the framework's own `UnixStdinReader.EscTimeoutMs` (50 ms)
  and hands the bar a synthetic Alt+Enter. It is safe because Escape in the command line is a genuine
  no-op and every other meaning of Escape is handled earlier in `HandleWindowKey`; and it is reliable
  because both halves land in *one* read, one parse and one dispatch batch (a terminal writes `ESC CR`
  in a single write), so the observed gap is microseconds, not milliseconds. ⌃L is kept as the second
  spelling. **Getting `Ctrl+⏎`/`Shift+⏎` properly needs the Kitty keyboard protocol, and that cannot
  be done consumer-side** — see below.
- **The input stack cannot be extended from here.** Enabling the Kitty keyboard protocol is trivial
  (`IConsoleDriver.WriteClipboardOsc52` is a de-facto public raw-escape emitter, and `Start`/`Stop`
  already pair `CSI ?2004h`/`l` for bracketed paste). *Decoding* it is the wall: `AnsiInputParser`,
  `UnixStdinReader`, `InputEvent` and `TerminalRawMode` are all `internal`; `NetConsoleDriver` has
  **zero** virtual members, a private `WriteOutput`, field-like events a subclass cannot raise, and it
  constructs its parser and reader as *locals* inside `Start()`. So enabling reporting without a
  matching decoder makes the affected keys **vanish silently** (`DispatchCsi`'s `default:` emits
  `UnknownSequenceEvent`, which `UnixStdinReader` drops). Owning input means a from-scratch
  `IConsoleDriver` (~900–1400 lines re-authoring internal termios + parser logic). The cheap unblock is
  upstream: make `AnsiInputParser`/`InputEvent` public and add an `UnknownSequenceHandler` hook, or add
  an input-reader factory to `NetConsoleDriverOptions`. ~15 lines there; do not try it from here.
- **A global shortcut runs before any window**, so a chord in `MacroKeys.AppShortcuts` can never reach
  a control's own key table. That is why the command line has no ⌃W (`CloseActiveWindow` claims it) and
  why `InputBuffer.KillWordLeft` currently has no chord that can reach it.

## Other dependency notes

- **TelnetNegotiationCore 2.8.1** (repo owner is its author — extend it by PR rather than working
  around it). Fluent builder API; negotiates MCCP/MSDP/MXP itself; ships the keepalive interpreter
  (`WithKeepAlive(TimeSpan?, …)`, default 30s, clamped to 1s–24h). `TelnetSession` sets the
  init-only `CallbackOnByteAsync` reflectively to see raw bytes including unterminated prompts — a
  first-class `OnByte` builder hook remains a good upstream PR. It handles the option handshake
  (TELOPT, GA, TTYPE/MTTS, EOR, NAWS, CHARSET, MSSP, GMCP) — **Pueblo and all ANSI/MXP/Pueblo
  payload _parsing_ stay our layer.**
  - **2.8.1 changes that affected us:** `TelnetInterpreter.SendNAWS(short, short)` was removed —
    NAWS now reports the full RFC 1073 unsigned-16 range via
    `NAWSProtocol.SendWindowSizeAsync(int, int)` on the plugin. `TerminalTypeProtocol` gained a
    public `WithTerminalTypes(string[])` method — the private-field reflection hack (`_terminalTypes`)
    in `ApplyTerminalTypes` was replaced with it. Plaintext MSSP-REQUEST is now an opt-in plugin
    (`MSSPPlaintextProtocol`) rather than a hidden library fallback. `fix(line)`: the library now
    submits genuinely blank lines instead of silently dropping them.
- **MSSP is read by the library, not by us — since 2.6.5, and that is the standing example of the
  rule above.** 2.6.0's reader destroyed the protocol's own array notation inside the library:
  `PORT "80" "23" "4201"` arrived as the integer `80234201`, `REFERRAL` (array-only) arrived null,
  booleans failed to bind from `1`/`0`, `CHARSET` and every invented name were dropped,
  `CRAWL_DELAY`/`MINIMUM_AGE` bound to nothing, and a variable with no value wedged MSSP for the rest
  of the connection. We carried a byte-level `MsspSubnegotiationParser` for exactly as long as that was
  true; the fix went upstream (PR #56) and the parser is **deleted**. Do not re-add one.
  `MSSPConfig.Variables` is now an ordered name → value-**list** map, `MsspData.From` projects it, and
  `MsspData` is a projection with **no parsing in it** — what it adds is ours: ports validated as
  ports, and `-1` read as the specification's "data not available" rather than as minus one. Two
  further upstream defects — MSSP fields decoded as **ASCII** rather than the negotiated charset, and
  an escaped `IAC IAC` inside a value **losing the literal byte** — were fixed in **2.7.0**, and
  `MsspParsingTests` now pins the fixed behaviour by name rather than the bugs. MSSP still has no
  payload size cap upstream — `SubnegotiationBuffer` guards GMCP, MSDP and CHARSET's TTABLE, but not
  this — so a hostile server can make a session buffer as much as it likes.
- **MSSP is waited for, never asked for, and the client surfaces what arrives** (`MsspCache`; the F5 ▸ `i`
  INFO screen). The MSSP specification writes one handshake and only one: the server "should send
  IAC WILL MSSP", the client answers `IAC DO MSSP` or `IAC DONT MSSP`. It says nothing about a client
  opening with `DO` — crawlers do that on RFC 854's authority, not MSSP's — and this client used to, which
  cost it the auto-login on any server whose telnet parser leaks an unknown option into its command
  buffer (see the entry above; the mechanism is gone). The library's opening negotiation is
  `IAC WILL NAWS` and nothing else, so MSSP is reached only when the server volunteers it, and the servers
  that never do are exactly what the INFO screen's *dialled, publishes none* state is for. **Do not
  re-add the ask** — not as an option, not per world: the cost lands on the login, silently, on the users
  least able to diagnose it.
- **Text encoding is CHARSET's answer, not a setting** (`SessionEncoding`, `TelnetSession.CurrentEncoding`).
  A world's `encoding` is `auto` by default — state the app's `CharsetOrder`, decode with whatever RFC
  2066 settles on — and naming one is an *override*: still offered at the head of the order so a
  cooperative server agrees, but used regardless of what it says. Four things about this library will
  bite you, and all four already have:
  - **`TelnetInterpreter.CurrentEncoding` defaults to `Encoding.ASCII`**, and that default is not inert:
    it is handed to `CallbackOnByteAsync`/`CallbackOnSubmitAsync` for every byte and used for GMCP, MSDP
    and everything we send. On a server that never negotiates CHARSET — most MU\* servers — every
    byte above 0x7F became `?`. `TelnetSession` seeds that property (reflectively, `internal set`, the
    same way `CharsetProtocol` itself writes it) with the head of the stated order.
    **MSSP fields were decoded as `Encoding.ASCII` through 2.6.x** (fixed in 2.7.0, pinned by
    `MsspParsingTests`); treat non-ASCII in an MSSP field from an older library as unrecoverable.
    Two consequences worth knowing from the pre-fix era: the plaintext `MSSP-REQUEST` fallback went
    through a different code path (also fixed in 2.7.0), and 2.6.0 additionally `ToUpper()`ed variable
    names with the *current culture*, a Turkish-I hazard (also gone).
  - **The encodings we state must be the platform provider's own instances** (`Encoding.UTF8`, *not*
    `new UTF8Encoding(false)`). `CharsetProtocol` ranks a server's offer by `IndexOf` over our list
    against encodings from `Encoding.GetEncodings()`, and `UTF8Encoding.Equals` compares the BOM flag —
    so a BOM-less instance matched nothing, scored −1, and sorted *below* every charset that did match.
    A `GetBytes` never emits a preamble, so the BOM that instance was avoiding was never at risk.
  - **The interpreter's `CurrentEncoding` is updated *after* the read batch returns**, so polling it is a
    batch late; `CharsetProtocol.OnCharsetChange`'s own argument is the prompt, authoritative signal.
    But that callback is **only raised when the server offers a list and we choose** — the direction
    where we offer and the server accepts updates the interpreter silently. Both arms are needed.
    (An `OnCharsetChange` on the accepted path is a good upstream PR.)
  - The seed is a `Clone()` for a reason: it doubles as the "nothing has negotiated" marker by reference,
    and seeding a provider instance would make a successful negotiation of that same charset look like
    the seed.
- **MoonSharp** — package id `MoonSharp`, pure-managed, no native deps.
- **Serilog** behind `Microsoft.Extensions.Logging` (`ClientDiagnostics`) feeds a capped in-memory
  `ClientMessageLog` (⌃P ▸ *Show client messages*) and a rolling file kept **separate from session
  transcripts**. Never add a console sink — it would paint over the TUI.

## Architecture rule (non-negotiable)

`SharpMUTerm.Core` stays **UI-agnostic and fully unit-testable**. All transport, telnet, parsing
(ANSI/MXP/Pueblo), GMCP/MSDP routing, scrollback, and trigger/alias/macro engines live there.
SharpConsoleUI is referenced **only** from `SharpMUTerm.Tui`.

Planned solution layout:

| Project | Responsibility |
|---|---|
| `SharpMUTerm.Core` | Transport, telnet, ANSI/MXP/Pueblo parsers, GMCP/MSDP routing, scrollback, engines, logging (no UI deps) |
| `SharpMUTerm.Graphics` | Kitty/Sixel encoders, capability probe, half-block fallback, `InlineImagePolicy` (no UI deps) |
| `SharpMUTerm.Scripting` | MoonSharp host + scripting API |
| `SharpMUTerm.Tui` | SharpConsoleUI application |
| `*.Tests` (Core, Graphics, Scripting, Web, Tui) | TUnit |

## Milestone M1 — first task (delivered)

Kept for context; **M1 is done** (see *Repository state* above). As originally scoped:

1. Create `SharpMUTerm.slnx` with the projects above targeting `net10.0`, plus the TUnit test projects.
2. Add NuGet references (see *Other dependency notes*).
3. Runnable stub: connect over TCP (+ optional TLS via `SslStream`, IPv6-capable), pipe received
   bytes through a first-pass `AnsiParser` (SGR: 16 / 256 / 24-bit color), render colored output
   in a SharpConsoleUI window with an input line + history.
4. Unit-test `AnsiParser` and the telnet-session wrapper in `SharpMUTerm.Core.Tests`.

## Verification

- Primary signal: `dotnet build SharpMUTerm.slnx` plus all five suites (see *Building and testing*).
  Keep coverage in `SharpMUTerm.Core.Tests` — ANSI/SGR parser, telnet round-trips, engines.
- **The TUI is verifiable headlessly** via the snapshot pipeline above; a claim about layout or
  chrome should be backed by a rendered frame you actually looked at, not by reading the markup.
- **Kitty graphics cannot be rendered here.** Treat that layer as build-verified and
  capability-probed, never visually confirmed, and make sure it degrades cleanly when no protocol is
  available — this environment is exactly that case. `SHARPMUTERM_GRAPHICS=halfblock` makes the
  `web` view draw a real decoded picture as half-block cells, which is the closest available look.

## Working conventions

- Branch from `main`; open a **PR**. Do **not** commit directly to `main`.
- Follow `.editorconfig`: file-scoped namespaces, 4-space C#, LF line endings.
- Keep commits focused with clear messages.
