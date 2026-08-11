# PR 3 — `⌃F` search across the panes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan
> task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `⌃F` opens a modal results surface over the panes; typing filters the output the client is
holding, `⌥E` switches to regex, `⌥A` widens from the focused window to every window, `⏎` goes to a
hit and marks it with a bar, `⌥G` walks to the next one.

**Architecture:** Matching is a pure Core function over plain text. The pane buffer carries that plain
text per line, computed once at append. The surface is the `HistorySearchPrompt`/`HistorySurface` split
verbatim — a pure class owning what a keystroke means and what the surface says, and a host owning
nothing but framework calls. Landing reuses the activity bar's machinery, which this PR finishes
extracting into two shared operations.

**Tech Stack:** C# / .NET 10, SharpConsoleUI 2.5.14 (package only), TUnit.

**Spec:** `docs/superpowers/specs/2026-08-11-pane-search-and-activity-design.md`, part 2.

**Branch:** `feat/pane-search`, off `feat/window-activity-boundary`. Third of a three-PR stack.

## Global Constraints

- Target framework `net10.0`; file-scoped namespaces, 4-space C#, LF endings.
- `SharpMUTerm.Core` stays UI-agnostic — `OutputSearch` takes plain strings and knows nothing of panes.
- Run suites directly, never `dotnet test`, keep the `</dev/null`.
- **Snapshots: `dotnet build -c Release` first.** `--no-build` runs the Release output; a bare
  `dotnet build` produces Debug and the frame silently comes from a stale binary.
- Work in `/home/grave/RiderProjects/SharpMUTerm-find`.
- **Two schemes only reach the desktop.** Nothing in this PR touches `ExternalBrowser`; the search bar
  and the surface render text, never links.
- The search bar is chrome: inserted through the buffer, never through `AppendWindowLine`.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/SharpMUTerm.Core/Text/OutputSearch.cs` | Create | Matching: plain, regex, case, invalid patterns, timeout |
| `src/SharpMUTerm.Tui/MarkupText.cs` | Modify | `Plain(markup)` — the visible text of a markup line |
| `src/SharpMUTerm.Tui/PaneLine.cs` | Modify | Carries the plain text beside the markup |
| `src/SharpMUTerm.Tui/SearchPrompt.cs` | Create | What a keystroke means, and what the surface says |
| `src/SharpMUTerm.Tui/SearchSurface.cs` | Create | The modal window; framework calls only |
| `src/SharpMUTerm.Tui/SearchBarRenderer.cs` | Create | The `⌕` bar drawn above a landed hit |
| `src/SharpMUTerm.Tui/MacroKeys.cs` | Modify | Claims `⌃F`, `⌥G`, `⌥⇧G` |
| `src/SharpMUTerm.Tui/SharpMUTermApp.cs` | Modify | Wiring, landing, the chrome-row extraction |
| `tests/…/OutputSearchTests.cs` (Core) | Create | Matching rules |
| `tests/…/SearchPromptTests.cs` (Tui) | Create | Every key the footer names |
| `tests/…/SearchEndToEndTests.cs` (Tui) | Create | The chord, the scope, the landing |

---

### Task 1: `OutputSearch` (Core)

**Files:** create `src/SharpMUTerm.Core/Text/OutputSearch.cs`, `tests/SharpMUTerm.Core.Tests/OutputSearchTests.cs`.

**Produces:**

```csharp
public readonly record struct OutputMatch(int LineIndex, string Text, int MatchStart, int MatchLength);
public readonly record struct OutputSearchResult(IReadOnlyList<OutputMatch> Matches, string? Error);
public static class OutputSearch
{
    public const int MaxQueryLength = 200;
    public static OutputSearchResult Match(IReadOnlyList<string> lines, string query, bool regex);
}
```

**Decisions, all of which get a test:**

- **Case-insensitive in both modes.** `HistorySearch` is (`OrdinalIgnoreCase`) and two search surfaces
  in one client disagreeing about case would be a bug report. Regex mode says `(?-i)` inline when it
  wants otherwise — a documented .NET feature rather than one we invented.
- **One match per line**, the first: the result is a list of *lines to go to*, and the offsets exist so
  a row can show why it is listed. `HistorySearchPrompt.Row` does the same.
- **An empty query matches nothing**, unlike `HistorySearch`, where an empty query is the opening
  chronological list. A pane buffer is thousands of lines and "everything, oldest first" is not a
  result set anybody asked for; the surface says what to type instead.
- **An invalid pattern is a state.** `Error` carries the message, `Matches` is empty, nothing throws. A
  regex is typed one character at a time, so most of the time a regex query is being typed it is
  invalid.
- **A match timeout** (`TimeSpan.FromMilliseconds(100)`), and a timeout is an `Error` rather than an
  exception: this runs on the UI thread on every keystroke, over every line of every window.
- **Order is oldest-first**, the buffer's own. The rows are a transcript, not a ranking, and the reader
  is looking for a place in it.

- [ ] **Step 1:** Write `OutputSearchTests` first — plain substring; case-insensitivity both ways;
  `(?-i)` honoured in regex mode; a regex metacharacter treated *literally* in plain mode (`a.c` does
  not match `abc`); empty query → no matches, no error; invalid pattern → empty + non-null `Error`;
  `(a+)+$` against a long non-matching line → `Error` rather than a hang; over-long query rejected;
  offsets point at the matched run; line indices are the caller's own.
- [ ] **Step 2:** Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/OutputSearchTests/*" </dev/null`. Expected: compile failure.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Re-run; expected PASS. Then the whole Core suite.
- [ ] **Step 5:** Commit.

---

### Task 2: The pane buffer carries plain text

**Files:** `MarkupText.cs`, `PaneLine.cs`, `SharpMUTermApp.cs` (`AppendWindowLine`, and the other
`new PaneLine(...)` sites), `tests/SharpMUTerm.Tui.Tests/MarkupTextTests.cs`.

`MarkupText.Plain(markup)` strips `[tag]` wrappers and unescapes `[[`/`]]` — the same protect-then-strip
shape `VisibleLength` already uses, which is where the two must agree: `Plain(x).Length` and
`VisibleLength(x)` are the same number for every input, and that is the test.

`PaneLine` becomes `(string Markup, string? Stamp, string Plain)`, with `Plain` computed at the one
place lines are appended. Why not on demand: the surface refilters on every keystroke over every
window, and stripping thousands of lines eight times while somebody types a word is the difference
between a search that feels instant and one that does not. The cost is one string per buffered line,
bounded by the same cap the buffer already has.

- [ ] **Step 1:** Test `Plain` against markup with tags, escaped brackets, and a link span; assert
  `Plain(x).Length == VisibleLength(x)` over a table of cases.
- [ ] **Step 2:** Run, watch it fail.
- [ ] **Step 3:** Implement `Plain`; give `PaneLine` its third component; fill it in at every
  construction site (the compiler will list them).
- [ ] **Step 4:** Whole Tui suite green.
- [ ] **Step 5:** Commit.

---

### Task 3: `SearchPrompt` — what a keystroke means, and what the surface says

**Files:** create `SearchPrompt.cs` and `tests/SharpMUTerm.Tui.Tests/SearchPromptTests.cs`.

Mirrors `HistorySearchPrompt` exactly:

```csharp
internal enum SearchAction { None, Redraw, Go, Cancel }
internal readonly record struct SearchDecision(SearchAction Action, string Query, int Selected, bool Regex, bool AllWindows);
internal static class SearchPrompt
{
    internal const string Hints = "type to search · ↑↓ pick · ⏎ go · ⌥E regex · ⌥A all windows · Esc cancel";
    internal static SearchDecision Interpret(ConsoleKeyInfo key, string query, int selected, int count, bool regex, bool all);
    internal static List<string> Render(IReadOnlyList<SearchRow> rows, string query, string? error, bool regex, bool all, int held, int width = 0, int listRows = 0, int first = 0);
    internal static int Scroll(int first, int selected, int count, int listRows);
    internal static int MaxWidth(IReadOnlyList<string> lines);
}
internal readonly record struct SearchRow(string WindowId, string WindowLabel, int LineIndex, string Text, int MatchStart, int MatchLength);
```

- `⌥E` and `⌥A` flip their flags and redraw. `⌃F` cancels (the toggle answers it in the running client;
  spelt out here so a test can read the rule back). Escape cancels. `⏎` with nothing listed does
  nothing and leaves the surface up — the query is what needs fixing.
- Printable characters filter; `⌫` un-filters; everything else is swallowed, because a modal that let
  keys through would be typing into a command line the reader cannot see.
- The window column is drawn **only when `all`** — one window's results do not need a column saying
  which window.
- The header carries the counted state and the searched bound: `12 of 38 · 4,812 lines held`. The bound
  is on the frame rather than implied, because the search sees the pane buffer and not the session's
  whole scrollback.
- An `Error` replaces the count with the message; the list is empty.
- **The footer names exactly the keys `Interpret` honours** — the honesty rule the settings screens and
  the composer are held to, pinned by a test that presses every key it names and asserts each does
  something.

- [ ] **Step 1:** Write `SearchPromptTests` — one per rule above, plus the footer-honesty test.
- [ ] **Step 2:** Run, watch it fail. **Step 3:** Implement. **Step 4:** Re-run, PASS. **Step 5:** Commit.

---

### Task 4: `SearchSurface`, the chord, and the scope

**Files:** create `SearchSurface.cs`; modify `MacroKeys.cs`, `SharpMUTermApp.cs`; create
`tests/SharpMUTerm.Tui.Tests/SearchEndToEndTests.cs`.

- `SearchSurface` is `HistorySurface` with a different prompt: same window construction, same
  `PreviewKeyPressed` wiring, same `SimulateKey`/`SimulateTyping` seams, same size-once rule.
- It is handed **a function that returns the corpus** — for each searchable window, its id, its label
  and its plain lines — read at the moment a key changes the query, so a hit's index is an index into
  the buffer as it is *now*.
- `MacroKeys`: `new(ConsoleModifiers.Control, ConsoleKey.F, "searches the output")`, plus `⌥G` /`⌥⇧G`.
  `ShortcutAction` gains the three arms. `⌃F` refuses over a settings screen or the composer and says
  so (`ComposerIsInTheWay`), for the paste reason and because two modal `PreviewKeyPressed` handlers
  cannot be driven headlessly.
- `AnyOverlayOpen` gains `_search.IsOpen`; `OpenOverlayName` gains "the search surface".
- Scope: focused window only, or every window in `_lines` except the web view (whose pane is not fed
  from the buffer). The window label is `WindowTitle(id)`, `Snippet`-bounded — a window title can be a
  *world's* text.

- [ ] **Step 1:** `SearchEndToEndTests`: ⌃F opens it; typing lists hits from the focused window only;
  `⌥A` brings in a background window's hits; `⌃F` again closes; ⌃F over an open settings screen refuses
  and says so; nothing reaches the wire (a recording transport, `HistorySearchEndToEndTests`' shape).
- [ ] **Step 2:** Run, watch it fail. **Step 3:** Implement. **Step 4:** Whole Tui suite. **Step 5:** Commit.

---

### Task 5: Landing — the bar, the jump, and `⌥G`

**Files:** create `SearchBarRenderer.cs`; modify `SharpMUTermApp.cs`; extend `SearchEndToEndTests`.

- `SearchBarRenderer.Bar(query, ordinal, total, accentHex)` → `⌕ goblin (12 of 38) ─── ⌥G next ──`,
  fourth of the boundary bars, `Glyphs`-based, pure, unit-tested. The query is `MarkupText.Escape`d —
  it is user text going into markup.
- **The chrome-row extraction the spec assigns to whichever PR needs it second.** There are now two
  kinds of inserted row, so `InsertChromeRow(windowId, at, markup)` and `RemoveChromeRow(windowId, at)`
  become the one place that fixes up everything indexing into a buffer: the freeze point, the pending
  boundary, the away mark, and the search mark. `RemoveAwayBar` and the trim block call through them.
- `⏎` → `Activate(windowId)` (the one activation path — it selects the pane, raises the tab, adopts the
  session), then insert the bar above the hit, `RepaintPane`, and reveal with the *measured* tail
  height (`RevealAwayBar`'s arithmetic, generalised: a buffer index is not a viewport row).
- **One search bar client-wide**; `⌥G` moves it to the next hit, wrapping, and re-activates that
  window. `⌥⇧G` goes back. With no search yet, both refuse out loud.
- **Not cleared by `Esc`.** A claimed Escape does not set `_escapeAt`, and `TryAltEnter` pairs an
  unclaimed one with a following Enter to make `Alt+⏎`. Binding Escape here would break the newline
  chord for as long as a search bar was on screen — a defect nobody would connect to search. It goes on
  the next search, the next `⌃F`, or a trim that takes it.

- [ ] **Step 1:** Tests — `⏎` on a hit in a *background* window activates that window (not the focused
  one) and leaves the bar directly above the hit; the bar names the ordinal; `⌥G` moves it and wraps;
  `⌥G` with no search refuses; a trim that passes the bar drops it; `Esc` in a pane leaves it alone and
  `Alt+⏎` still makes a newline with a bar up.
- [ ] **Step 2:** Run, watch it fail. **Step 3:** Implement. **Step 4:** Whole Tui suite. **Step 5:** Commit.

---

### Task 6: Frames, brief, PR

- [ ] **Step 1:** Snapshot views `search`, `search-regex`, `search-all`, `search-landed` (the last over
  a **split**, so the pane is narrower than the terminal — the geometry that catches a reveal landing at
  the wrong row). Drive real keys through the surface's own handler, as `history-search-filter` does.
- [ ] **Step 2:** `dotnet build -c Release SharpMUTerm.slnx`, render each, look at the `.html`.
- [ ] **Step 3:** CLAUDE.md: the search entry (corpus, the plain-text field, the scheme-free bar, the
  scope rule, the Escape reasoning), the new chords, and the four views.
- [ ] **Step 4:** Five suites green, build warning-free.
- [ ] **Step 5:** Push; `gh pr create --base feat/window-activity-boundary`.

## Self-review notes

- **Spec coverage:** part 2 in full — corpus and its bound, plain text at append, the Core matcher and
  its five decisions, the surface and its footer, the scope toggle, the landing bar, `⌥G`.
- **Deliberately not here:** searching the file-backed spill or a session's `WorldSession.Scrollback`.
  The spec rules both out — a spawn window's lines exist in neither — and the surface states the bound
  it does search rather than implying a bigger one.
