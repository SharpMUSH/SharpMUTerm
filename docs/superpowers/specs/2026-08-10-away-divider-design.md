# The away divider: where you were when you left the terminal

**Date:** 2026-08-10
**Status:** proposed — design only, nothing implemented

## Problem

Tab away from the terminal, come back later, and there is no way to tell which of the lines on
screen you have already read. Every pane has bottom-anchored through your absence, so what you land
on is the newest output with no boundary in it. The client already knows how to say "the rows above
this are not live" twice over — `FreezeBarRenderer` divides pinned scrollback from the live tail,
`RestoreBarRenderer` closes off content carried over from a previous run — and has nothing to say
about the one absence that happens many times a day.

The unread counts on the rail and the tab strip do not answer it. `Workspace.NoteActivity` only
counts a line when the window is *not* `IsCaughtUp` — not the visible tab, or scrolled off its live
tail (`Workspace.cs:278`). The window you were looking at when you alt-tabbed away is visible and at
its tail the whole time you are gone, so it accrues nothing. The count is a "which pane should I
look at" signal for background windows and is silent about the foreground one, which is exactly the
one you were reading.

## What the reader gets

One row, drawn inline in each window at the point where you left:

```
▾ AWAY  37 lines · 12 min ────────────────────────────────────────
```

Everything below it arrived while you were away. It is a boundary marker and not a restyling of the
content, for the reason `RestoreBarRenderer` gives: the lines are worth having because they are the
game's own text in the game's own colours, and recolouring them to prove they are new would destroy
the thing being marked. The restored bar sits *below* its content because a pane bottom-anchors and
the boundary is what you land on; this one sits *above* its content for the same reason read the
other way — what you land on is the newest line, and the thing you are looking for is up.

## Why this is hard: the terminal will not tell us, quite

A terminal reports focus only if asked. `CSI ?1004h` turns it on, after which the terminal writes
`ESC [ I` when its window gains focus and `ESC [ O` when it loses it, down the same pipe as
keystrokes. Neither half is reachable through SharpConsoleUI as shipped:

- **No released version asks.** Checked against 2.5.18, the newest on nuget (we are pinned at
  2.5.14): the assembly's UTF-16 string heap holds `[?2004h`/`[?2004l` for bracketed paste and no
  `?1004` in any version. The `FocusChanged` symbols in it are `FocusManager.FocusChanged` —
  control focus, which the app already hooks for `PinFocusToArmedBar`.
- **`ESC [ O` is discarded.** `AnsiInputParser.DispatchCsi` has no case for it, so it becomes an
  `UnknownSequenceEvent`, and `UnixStdinReader` dispatches only key, paste and mouse events
  (`UnixStdinReader.cs:147`). Nothing downstream can see it.
- **`ESC [ I` arrives disguised as a Tab keypress.** `DispatchCsi` reads a trailing `I` as Tab
  (`AnsiInputParser.cs:511`), which is right for the forms carrying modifiers — `ESC [ 1;5 I` is
  genuinely Ctrl+Tab in xterm — and wrong for the bare form, which is focus-in. That is an upstream
  bug, and it is also the only way the message reaches us.

`IConsoleDriver` exposes no focus event and `NetConsoleDriverOptions` no hook, so there is no
supported seam. The input-stack wall in CLAUDE.md holds: owning this properly means either an
upstream PR or a from-scratch `IConsoleDriver`.

Two decisions follow, and both are exploiting an implementation detail rather than a contract. Both
are contained in one file so the blast radius is one file.

### Asking for focus reports

`IConsoleDriver.WriteClipboardOsc52(string)` is named for its first customer and does not do what
its name says: its body takes the console lock and writes the string verbatim, with no validation,
wrapping or encoding (`NetConsoleDriver.cs:667`). It is a raw-escape emitter.

We need one. Escape sequences go to stdout, the framework owns stdout and paints whole frames
through it, and a `Console.Out.Write` of our own can land mid-frame and corrupt a paint. This is the
only public write that is serialised against the renderer, because it takes the same `_consoleLock`.

The risk is that we are depending on a body rather than a signature: a future version that validates
the payload is OSC 52 breaks us. Mitigated by putting both writes behind one `EmitTerminalMode`
method, so the change is one line, and by filing the upstream ask for a `WriteRaw` or a focus option
regardless.

### Recognising the disguised focus-in

`ConsoleWindowSystem.RegisterGlobalShortcut(modifiers, key, Func<bool>)` registers a handler that
decides whether the key is consumed — return `false` and it continues down the normal pipeline
(`ConsoleWindowSystem.cs:1683`). Global shortcuts are tried before any window sees the key, so this
also covers the case where a settings or quit overlay is open, which a `PreviewKeyPressed` hook on
the main window would miss.

So: claim bare Tab, consume it only when it is a focus-in, decline otherwise. A declined Tab reaches
`InputBarControl.cs:278` (cycle to the sibling command bar) and the overlays exactly as it does
today.

**Telling the two apart is a question about time, not about the key.** A Tab arriving after a long
quiet gap is a return; a Tab arriving while you are demonstrably at the keyboard is a Tab. The
misfire is benign in the direction it fires: a genuine Tab pressed after ten minutes of silence is
*also* a return, because you had to come back to press it. `Ctrl+I`, which the terminal spells as a
bare Tab too (`MacroKeys` records this), is covered by the same gap test.

The threshold is a debounce, not an idle timer — nothing is drawn without an actual focus-in — so it
can be short. **30 seconds**, not configurable in the first cut.

### What we do not get

`ESC [ O` is unreachable, so we cannot timestamp your departure. The boundary is anchored to the
last input event we saw instead (see below), which is seconds off at worst: you stop typing, then
you leave.

**Unix only.** The Windows branch of `NetConsoleDriver` is a `Console.ReadKey` loop with its own
ad-hoc sequence reassembly, not `AnsiInputParser`, so `?1004` must not be enabled there — the
reassembler would see `ESC [ I` and make something else of it. On Windows the feature is inert. It
is also inert on any terminal that does not implement `?1004`; kitty, WezTerm and Ghostty all do,
and tmux passes them through only with `focus-events on`.

## Design

### 1. `TerminalFocusWatcher` (Tui)

One file, owning the whole trick.

- `Start()` emits `\x1b[?1004h` through `EmitTerminalMode`; `Stop()` emits `\x1b[?1004l`. Both are
  no-ops off Unix.
- Subscribes to the **driver's** `KeyPressed`, `MouseEvent` and `Paste` — driver level, so it sees
  input routed to overlays as well as to the workspace — and keeps `LastInputAt`.
- Registers bare Tab as a declining global shortcut. On a Tab, if the gap since the *previous* input
  exceeds `ReturnThreshold`, raise `Returned(awaySince)` and return `true`; otherwise `false`.
- **Ordering trap:** the disguised focus-in *is* a `KeyPressed`, and the driver raises that before
  `InputCoordinator` reaches the global shortcuts. The watcher must therefore compare against the
  input before this one, not the timestamp it has just written.
- Takes an `Func<DateTimeOffset>` clock, so the timing is unit-testable with no terminal.

Enabled only by `Program`, the same gate `save`, `logRoot` and `restore` use: an app that is not the
live entry point holds no watcher and writes nothing to any terminal. That keeps the snapshot
pipeline and the test suite from emitting mode changes into whatever console is attached.

### 2. Where the boundary comes from

`PaneLine` is `(string Markup, string? Stamp)` and the stamp is *formatted text*, not a time
(`PaneLine.cs:35`), so the boundary cannot be found retroactively by walking the buffer for lines
newer than some instant. Widening `PaneLine` to carry an arrival time would touch every append and
the restore codec.

Track it forward instead: on every input event, record `PendingMark[windowId] = _lines[windowId].Count`
for each window. Input events are at human rate and the map is a few entries, so this is free. When
the return arrives, that index is where you left.

### 3. Drawing it

`AwayBarRenderer`, a sibling of `FreezeBarRenderer` and `RestoreBarRenderer`, sharing their 48-cell
rule and taking a resolved `#rrggbb` accent so it is pure and testable without a terminal. Both
figures are on the bar for the reason the restore bar carries two: the count answers "is this a
glance or a session's worth", the duration answers "how far behind am I".

Inserted into `_lines` at the recorded index, which is a mid-buffer insert and therefore costs one
`RepaintPanes` for that window — the expensive whole-buffer re-feed. That is affordable here for the
same reason it is affordable for the timestamp toggle: it is bounded by one deliberate event, a
return, and not by lines or frames.

Two things it must not do:

- **Not count as unread.** It is the client's own chrome; `NoteActivity` is not called for it.
- **Not reach the restore log.** Already true for free — `RestoreLog` is fed at `OnLine`/`OnSpawnLine`
  and deliberately not at `AppendWindowLine`, so client chrome has never gone into it.

A window with no lines at all gets no bar: there is no boundary to mark.

### 4. Consumption

The divider is cleared when you have read past it, and "read past it" needs care, because the
obvious test does not survive contact. A bottom-anchored pane is *already* `IsCaughtUp` the instant
you return — that predicate is "visible and not scrolled back" (`Workspace.cs:384`) and says nothing
about how much arrived. Clearing on it would clear the divider before you had read a word of the two
hundred lines above the fold.

The rule is therefore three conjuncts:

1. the divider row has been **inside the viewport** — computable per frame from the panel's
   `VerticalScrollOffset`, `ViewportHeight` and the row's index; and
2. the pane is at its **live tail**; and
3. at least **one input event** has landed since the bar was drawn.

Read together: you saw the marker, and you are now at the bottom, so you crossed the gap between
them. (1) is what makes a deep absence keep its divider until you scroll up and find it. (3) is what
stops a shallow absence — a handful of lines, all on screen with the marker — clearing in the very
frame it appears.

Clearing is a removal from `_lines` and so costs the same single-window re-feed the insertion did.

Each window carries at most one. A return while a previous divider is still unconsumed replaces it,
because two boundaries in one pane cannot both be "where you left".

## Verification

- `AwayBarRendererTests` — the markup, both figures, escaping.
- `TerminalFocusWatcherTests` — on a fake clock: a Tab inside the threshold declines and is passed
  through; a Tab outside it consumes and raises `Returned`; the disguised focus-in's own `KeyPressed`
  does not move the baseline it is about to be compared against; nothing is emitted off Unix or
  without a live driver.
- `AwayDividerTests` — insertion at the recorded index; the three consumption conjuncts, each failing
  alone; replacement on a second return; no bar for an empty window; the bar is not counted as unread.
- Snapshot views `away` (divider on screen) and `away-scrollback` (divider above the fold, over
  `LoadLongScene`). CLAUDE.md is explicit that the three `scroll*` views are the only ones with more
  output than a pane holds, and that anything touching the output area needs one.
- The whole suite and `dotnet build SharpMUTerm.slnx` warning-free.

## Out of scope

- **Focus-out.** Unreachable without an upstream change; the last-input anchor is the answer until
  then.
- **Windows.** Inert, deliberately. An idle-time fallback there is a separate decision and would
  bring back the misfire this design exists to avoid.
- **A jump-to-divider chord, and a "while you were away" digest across windows.** Both were
  considered and dropped from the first cut; the divider is the thing that was asked for.
- **Configurability.** One threshold, one appearance, no settings-screen entry until there is a
  reason.

## Upstream

Worth filing against `nickprotop/ConsoleEx` whatever we ship, because both tricks here are working
around it:

1. Bare, parameterless `CSI I` is focus-in, not Tab — a bug on its own terms.
2. A `FocusInputEvent`, a case for it in `UnixStdinReader`'s dispatch switch, a `FocusChanged` event
   on `IConsoleDriver`, and `?1004h`/`?1004l` paired where `?2004` already is
   (`NetConsoleDriver.cs:446`, `:531`). Roughly 60 lines on the Unix path.
3. A `WriteRaw` that means what it says, so `WriteClipboardOsc52` stops being one by accident.

If that lands, this design's §1 swaps its signal and nothing else in the feature moves.
