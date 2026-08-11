# Finding things: ⌃F search, the ⌥↑ history chord, and an activity boundary that means "since you were last here"

**Date:** 2026-08-11
**Status:** designed — implemented as a three-PR stack (`feat/find-chords` → `feat/window-activity-boundary` → `feat/pane-search`)

## Problem

Three complaints, one theme: the client holds text you cannot get back to.

1. **There is no way to search output.** Every MU\* client has a find. A pose scrolled off ten
   minutes ago, the room description with the exit you missed, who said the thing in `Chat` — the
   text is in the buffer and there is no key that looks at it. `⌃F`, the chord every reader on
   every platform reaches for, is spent on freeze.
2. **Command history has no dedicated key.** `↑`/`↓` recall only when the caret has nowhere further
   to go, which is a rule you have to hold in your head and which stops being true the moment the
   bar grows to a second row. A key that means *history* and nothing else is missing.
3. **The activity boundary only marks one kind of absence, and it vanishes too fast.** The away
   divider marks where you were when you tabbed away from the *terminal*. Look away from a window
   instead — switch tabs, switch characters, scroll a pane back — and the client counts the lines on
   a badge but marks no boundary. Come back to a busy channel and the badge says `47` and nothing on
   screen says where the 47 start. And the bar that does get drawn retires on the first keystroke
   after you reach the tail, which on a shallow absence is a second or two.

## Locked decisions

Made explicitly during design; not to be relitigated without asking.

| Decision | Chosen | Why not the alternative |
|---|---|---|
| Search UX | A modal results surface, the `⌃R` idiom | An in-pane `less`-style bar has nowhere to say *which pane* a hit is in, and multi-pane search is half the request |
| Search scope | Focused window, `⌥A` widens to every window | "Visible panes only" omits background tabs, which is where a busy channel's history is; "always everything" buries the pane you are looking at |
| Landing | A boundary bar above the hit | Painting the matched span would destroy the game's own colours — the reason the away bar marks rather than restyles |
| Activity boundary | Accrues whenever the window is not caught up | One rule feeding the badge *and* the bar, so a badge never shows a count with no bar to explain it |
| Bar lifetime | A dwell floor in seconds | The complaint is measured in time, so the fix is; a raised input count means an hour on a quiet character and three seconds on a busy one |

## The chords, and why these ones

Every chord below was driven at a raw-mode reader inside the target terminal with
`kitten @ send-key` before it was spent. A decode test is not an arrival test — the rule
`Alt+Shift+arrow` was bought with.

| chord | bytes written to the pty | what the parser makes of it |
|---|---|---|
| `alt+up` / `alt+down` | `ESC [ 1;3 A` / `ESC [ 1;3 B` | `AnsiInputParser.ParseModifiers` reads `3-1 = 2`, bit 1 → **Alt**, and the key is `UpArrow`/`DownArrow` |
| `ctrl+up` / `ctrl+down` | `ESC [ 1;5 A` / `ESC [ 1;5 B` | Arrives, and is **already spent**: pane selection, and the ladder onto the second command line |
| `alt+f` | `ESC f` | `ProcessEscape` → Alt+F |
| `ctrl+f` | `0x06` | A control byte with no other meaning; free once freeze moves off it |
| `alt+g` / `alt+shift+g` | `ESC g` / `ESC G` | `ProcessEscape` sets `shift = char.IsUpper(c)`, so the pair is distinguishable |

`⌃↑`/`⌃↓` was one of the two the request offered and it was never available. `⌥↑`/`⌥↓` is the other,
and it is also the *right* one on this keyboard: word movement is already `⌥←`/`⌥→`, so the four
arrows under Alt are one family.

### `⌃F` becomes search; freeze becomes `⌥F`

`⌃F` is in CLAUDE.md's "deliberately left on Ctrl" list, and it leaves it for the reason the others
stay: the convention is worth more than the pattern, and `⌃F` means *find* to everyone who has used
a computer. Freeze keeps its letter and changes its modifier, which is the smallest move that frees
the chord — `⌥F` is delivered as `ESC f` and claimed by nothing.

Everything that says `⌃F` moves with it, and the list is the point: `FreezeBarRenderer.Bar` (the
`❄ FROZEN ⌃F` label a user reads *while frozen*), the `⌃P` command-surface entry, `MacroKeys.AppShortcuts`,
`docs/design/README.md`, and CLAUDE.md's own Ctrl list. A chord that moved in the handler and not in
the bar would be a client telling the user to press a key that no longer does anything.

There is no `⌃F` alias left behind. The `⌃D` precedent: a second key for one action is either a
secret or a duplicate row on every surface that lists chords, and letting it go hands a clean chord
back to macros.

### `⌥↑` / `⌥↓` recall history, unconditionally

`TryRecallKey` today declines anything with a modifier and recalls on the bare arrows only when
`bar.TryMoveRow` reports the caret has nowhere further to go. That stays exactly as it is — it is
how the client has always behaved and nothing is taken away. The Alt pair is added beside it and
skips the caret test entirely: on a three-row draft, `⌥↑` recalls and `↑` moves the caret, which is
the distinction the request asked for.

Matched on **exact** modifiers (`key.Modifiers == ConsoleModifiers.Alt`). Three separate defects in
this repository have been a handler looking at the key and not the modifiers; ordering is the second
line of defence and never the first.

A macro bound to `Alt+Up` still wins, because `DispatchMacro` runs ahead of the recall keys in
`HandleWindowKey` — the same relationship `Ctrl+←/→` already has with pane selection, and
`MacroKeys.Verdict` continues to report it honestly.

### `⌥G` / `⌥⇧G` repeat the last search

Reachable without reopening the surface, so walking hits is one key rather than four. `⌥⇧G` is bound
only if `alt+shift+g` measures as a distinct arrival at a raw reader; if it does not, `⌥G` wraps
forward and there is no backward chord, and no surface advertises one.

## Part 2: `⌃F` search

### What gets searched, and what does not

**The pane line buffer** (`SharpMUTermApp._lines`), which is markup plus a stamp per line. Not
`WorldSession.Scrollback` and not the file-backed spill — for `RestoreLog`'s reason, one layer over:
a spawn window's lines never reach a session's scrollback at all (`ProcessOutputLine` raises
`SpawnLine`, and a gagging capture rule keeps the line out of the transcript entirely), so a
session-keyed search would find nothing in exactly the windows people search hardest.

That makes the searchable region *what the client is holding*, which is bounded and smaller than the
session's history. The surface says so rather than implying otherwise: the counter reads
`12 of 38 · 4,812 lines held`, so a reader who does not find a line from an hour ago can see why.

**The visible text, with markup stripped.** A `[bold #ff0000]` in the middle of a word must not split
a match, and a user must not be able to search for `#ff0000` and hit every red line. This is the same
rule `UrlDetector` follows for the same reason — run over the *line*, never span by span.

`PaneLine` therefore gains a third component:

```csharp
internal readonly record struct PaneLine(string Markup, string? Stamp = null, string Plain = "");
```

computed once, at append, by a new `MarkupText.Plain`. The alternative — stripping on demand — would
restrip every line of every window on every keystroke of the query, and the whole point of an
incremental surface is that it refilters as you type. The cost is one extra string per buffered line,
usually shorter than the markup beside it; the buffer is already capped per window, so the memory is
bounded by the same constant that bounds the buffer.

The timestamp gutter is not searched. It is glued on at render time (`Compose`) and is not part of
the line — searching it would mean `12:` matching every line printed in the twelfth hour.

### `Core.Search.OutputSearch`

Beside `HistorySearch`, UI-free, and where the tests live:

```csharp
internal readonly record struct OutputMatch(int LineIndex, int Start, int Length);

internal static class OutputSearch
{
    internal static IReadOnlyList<OutputMatch> Find(IReadOnlyList<string> lines, string query, bool regex);
}
```

- **Case-insensitive in both modes.** `HistorySearch` is (`StringComparison.OrdinalIgnoreCase`), and
  two search surfaces in one client disagreeing about case would be a bug report. Regex mode says
  `(?-i)` inline when it wants otherwise, which is a documented .NET feature rather than a rule we
  invented.
- **An invalid pattern is a state, not an exception.** The query line says `invalid pattern` and the
  list is empty. A regex is typed one character at a time, so *most* of the time a regex query is
  being typed it is invalid; throwing, or listing stale results, are both worse than saying so.
- **A match timeout.** `new Regex(pattern, options, TimeSpan.FromMilliseconds(100))`. Catastrophic
  backtracking on a user's own pattern must not wedge the UI thread, and the surface refilters on
  every keystroke over every line of every window.
- **One match per line.** The result is a list of *lines* to jump to, not of every occurrence; the
  first match's offsets are kept so the row can mark why it is listed, exactly as
  `HistorySearchPrompt.Row` does.

### The surface

`SearchPrompt` (pure — keystroke → decision, and the renderer) and `SearchSurface` (framework calls
only). The split is `HistorySearchPrompt`/`HistorySurface` verbatim, and for its reason: the rules
and the wording are the part a headless test can pin.

```
┌ search ────────────────────────────────────────────────┐
│ search  goblin▌            12 of 38 · 4,812 lines held │
│                                                        │
│   main   The goblin snarls at you.                     │
│ ▸ Chat   <OOC> Ana: goblin room is bugged              │
│   main   You hit the goblin for 12.                    │
│   main   A goblin corpse lies here.                    │
│                                                        │
│ type to filter · ↑↓ pick · ⏎ go · ⌥E regex             │
│ ⌥A all windows · Esc cancel · ⌃F closes                │
└────────────────────────────────────────────────────────┘
```

- Opens on the focused pane's active window. `⌥A` widens to every window in the workspace, including
  background tabs and windows in other panes; the window column names each hit's home and only
  appears when widened.
- `⌥E` toggles regex. Both toggles are named in the footer and both travel with the query while the
  surface is open.
- `⏎` goes. `Esc` cancels. `⌃F` closes — the toggle answers its own chord, `HistorySurface`'s rule,
  because a global shortcut runs before any window and the key never reaches the handler.
- Anything unrecognised is swallowed. A modal surface that let stray keys through would be typing
  into a command line the user cannot see.
- The footer names exactly the keys `Interpret` honours, held to the honesty rule the settings
  screens and the composer are held to, and pinned by a test that presses every key it names.
- It joins `AnyOverlayOpen`, and it **refuses over an open settings screen or the composer** — two
  modal windows with two `PreviewKeyPressed` handlers cannot be driven headlessly, and the composer's
  `MultilineEditControl` is a focusable `IPasteTarget` that would make `SettingsOverlay`'s
  driver-level paste listener double-fire.

### Landing on a hit

`⏎` closes the surface and then, in order:

1. **Activates the target window** through `SharpMUTermApp.Activate` — the one activation path. It
   selects the pane, raises the tab, adopts the session and re-syncs the caret and NAWS. A second
   route that "just scrolled the pane" is how `_active` and the focused pane came apart before.
2. **Inserts a search bar above the hit**, through the buffer rather than through `AppendLine`: it is
   the client's own chrome, so it must not badge the window unread and must not reach the restore log.

   ```
   ── ⌕ goblin (12 of 38) ───────────────────────────── ⌥G next ──
   ```

3. **Reveal-scrolls it**, with the measured tail height. A buffer index is not a viewport row: the
   panel's offset counts *display* rows and a buffered line wraps into as many as it needs, which is
   the defect `RevealAwayBar` was first written with and the reason the height is measured through
   the framework's own `MarkupControl.MeasureDOM` at the pane's real width.

At most one search bar exists at a time, client-wide — `⌥G` moves it. It is removed by the next
search, by the next `⌃F`, and by a trim that takes it.

**Not by `Esc`**, tempting as that is. An Escape that is claimed does not set `_escapeAt`, and
`TryAltEnter` pairs an unclaimed Escape with an Enter arriving within 50 ms to reassemble `Alt+⏎` —
the newline chord. Binding Escape to "clear the search bar" would break inserting a newline for as
long as a search bar was on screen, which is a defect nobody would connect to the search feature.

## Part 3: the activity boundary

### One boundary, recorded where it actually happens

Today the away boundary is reconstructed *backwards*, from the last input before the reader
disappeared, because focus-out is unrecoverable: `ESC [ O` has no case in `DispatchCsi` and is
dropped as an `UnknownSequenceEvent`, so a departure cannot be timestamped. That machinery stays
exactly as it is for the terminal case, because nothing better is available.

The window case needs none of it, because it is **observable at the moment it happens**. A line is
appended; the window either is or is not caught up; if it is not, and no boundary is pending, the
boundary is the buffer count *before* this line. That is exact rather than approximate, and it is one
comparison in a method that already runs per line.

```
Watched(window) ≡ terminal has focus  ∧  Workspace.IsCaughtUp(window)
```

`IsCaughtUp` and not `IsVisible`, which is already the rule the unread badge uses: a visible tab whose
output you have scrolled back off is exactly as blind as a tab you are not looking at. So the badge
and the bar answer to one fact, and a badge showing `47` always has a bar under it explaining where
the 47 begin.

When a window becomes watched again and something is pending, the bar materialises, is revealed, and
the pending boundary clears. Where both a window boundary and a terminal-away boundary exist for one
window, the **older** wins: it is still one bar per window, and it marks the earlier of the two things
you missed.

The bar's wording distinguishes the two absences, because they are different facts:

```
▾ AWAY  37 lines · 12 min ──────────────    (terminal absence, today's wording)
▾ NEW   47 lines since you were here ───    (window absence)
```

### Lifetime

Three conjuncts, the first two unchanged:

1. The pane is back at its **live tail** (`ScrollablePanelControl.AutoScroll`, the framework's own
   "showing the newest line" bit). This means something only because the reveal took the pane *off*
   its tail whenever the bar was not on screen, so arriving at the bottom is having read down through
   what you missed.
2. **One input** has landed since the bar was drawn, numbered rather than timestamped
   (`AwayMark.DrawnAfter` against `InputCount`), because marshalling to the UI thread reorders and a
   note raised before the frame that drew a bar can be delivered after it.
3. **New:** the bar has existed for at least the dwell floor.

The floor is a wall-clock span held on the mark at draw time and compared in `ConsumeReadAwayBars`.
Default **30 seconds**, settable on F7 (Text & ANSI, where the other output-rendering settings live)
as `activity bar holds for`. `0` is today's behaviour exactly, so the change is reversible by the
person who disagrees with it.

New defaulted field in the configuration, so **no schema bump and no migration** — the reasoning
`ConnectAtStartup` and `PaneTint.None` already establish: a default that describes the state most
clients are in does not need anybody marked.

The clock is **injected**, a `Func<DateTimeOffset>` defaulting to `DateTimeOffset.Now`, joining the
`save:`/`logRoot:`/`restore:`/launcher family of caller-supplied seams. A dwell floor tested against
the wall clock is a test that fails on a loaded CI box, and a snapshot that waited 30 seconds would
be a snapshot nobody runs.

## Part 4: the shared machinery

By the end of this there are two kinds of client chrome inserted mid-buffer — the activity bar and
the search bar — and every index into that buffer has to survive both: the freeze point, the pending
boundary, the other bar, and the trim that reclaims the buffer's cap. That bookkeeping exists once
today, spread across `RemoveAwayBar`, `TrimWindow` and `MarkWhereTheReaderLeft`, and would exist
twice by the end.

It is extracted into one `PaneMarks` type — insert, remove, and "fix up every index past N" — as part
of whichever PR lands first. Deliberately not a wider refactor: the freeze point stays where it is,
and nothing else moves.

## Verification

- **Core:** `OutputSearchTests` — plain and regex, case-insensitivity in both, `(?-i)`, invalid
  patterns, a pathological pattern against the timeout, and the offsets a row marks itself with.
- **Tui, pure:** `SearchPromptTests` — every key the footer names, and no other key doing anything.
- **Tui, integration:** the chord moves (`⌥F` freezes, `⌃F` opens the surface, `⌥↑` recalls from a
  grown bar's middle row, `⌥↑` declines to a macro), landing (`⏎` activates the right *window*, not
  the focused one), and the boundary (a background tab accrues, a visible tab at its tail does not, a
  bar survives the dwell floor and retires after it, against an injected clock).
- **Honesty pins:** `AdvertisedKeyHonestyTests` covers the new chords; `MacroKeys.Bindable` must not
  hand out anything the app has just claimed.
- **Snapshots:** `search`, `search-regex`, `search-all`, `search-landed` (the bar in a pane, in a
  split so the pane is narrower than the terminal — the geometry that catches a reveal landing at the
  wrong row), and `activity-bar`. The existing `freeze` views' bar text changes to `⌥F`, which
  `FreezeBarRendererTests` already pins.
- Primary signal as always: `dotnet build SharpMUTerm.slnx` plus all five suites green and
  warning-free.

## The stack

Three PRs, each branched off the one before it, each independently reviewable and each green on its
own:

1. **`feat/find-chords`** — `⌃F` → `⌥F` for freeze, `⌥↑`/`⌥↓` for history recall, and this document.
   Frees `⌃F`. Small, and touches nothing the other two need to agree with.
2. **`feat/window-activity-boundary`** — the boundary generalisation, the dwell floor and its F7
   setting. Owns the extraction of `PaneMarks`, so the machinery is already general when the search
   bar arrives.
3. **`feat/pane-search`** — `OutputSearch`, the surface, the landing bar, `⌥G`.

The order is chosen so that each PR's hardest part is already built: `⌃F` is free before search wants
it, and the mid-buffer chrome bookkeeping is one type before there are two kinds of it.
