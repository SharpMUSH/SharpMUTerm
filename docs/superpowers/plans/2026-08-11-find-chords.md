# PR 1 — Find chords: `⌥F` freeze, `⌥↑`/`⌥↓` history Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan
> task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Free `⌃F` for search by moving freeze to `⌥F`, and give command history a dedicated
`⌥↑`/`⌥↓` that works wherever the caret is.

**Architecture:** Two independent keyboard changes in `SharpMUTerm.Tui`. Freeze moves by editing the
one claim list the app registers from (`MacroKeys.AppShortcuts`) and the matching arm of
`ShortcutAction`; everything that *prints* the chord moves with it. History recall gains an
Alt-modified path in `TryRecallKey` beside the bare-arrow path, which is untouched.

**Tech Stack:** C# / .NET 10, SharpConsoleUI 2.5.14 (package only), TUnit on
Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-08-11-pane-search-and-activity-design.md`, parts 1 and 2.

**Branch:** `feat/find-chords`, off `main`. First of a three-PR stack.

## Global Constraints

- Target framework `net10.0`. File-scoped namespaces, 4-space C#, LF endings (`.editorconfig`).
- `SharpMUTerm.Core` stays UI-agnostic; nothing in this PR touches it.
- Tests are TUnit `Exe` projects. `dotnet test` does **not** work. Run:
  `dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests </dev/null` — keep the `</dev/null`.
- Primary signal: `dotnet build SharpMUTerm.slnx` plus all five suites green and warning-free.
- Work in the worktree at `/home/grave/RiderProjects/SharpMUTerm-find`. Do not build in the primary
  checkout — another session holds it.
- **Every chord in this plan has already been measured** at a raw-mode reader with
  `kitten @ send-key`: `alt+f` is `ESC f`, `alt+up`/`alt+down` are `ESC [ 1;3 A`/`B`. Do not
  re-derive; do not substitute a chord that has not been measured.
- A handler that does not handle a chord must **decline** it. Match on exact modifiers
  (`key.Modifiers == ConsoleModifiers.Alt`), never `HasFlag` alone.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/SharpMUTerm.Tui/MacroKeys.cs` | Modify (`Fixed()`, ~line 138) | The single list of chords the app claims; F4 reads it |
| `src/SharpMUTerm.Tui/SharpMUTermApp.cs` | Modify (`ShortcutAction`, ~4659 and ~4722) | Maps a claim to its action |
| `src/SharpMUTerm.Tui/FreezeBarRenderer.cs` | Modify (line 18) | The label a user reads *while frozen* |
| `src/SharpMUTerm.Tui/SharpMUTermApp.cs` | Modify (`TryRecallKey`, ~3524) | History recall on the arrows |
| `tests/SharpMUTerm.Tui.Tests/FreezeBarRendererTests.cs` | Modify (line 13) | Pins the bar's text |
| `tests/SharpMUTerm.Tui.Tests/FreezeChordTests.cs` | Create | The chord actually freezes; the old one does not |
| `tests/SharpMUTerm.Tui.Tests/HistoryChordTests.cs` | Create | `⌥↑`/`⌥↓` recall regardless of caret row; bare arrows unchanged |
| `docs/design/README.md` | Modify (lines 254-255, 426) | Reader-facing chord list |
| `CLAUDE.md` | Modify (the "deliberately left on Ctrl" list) | Agent brief |

---

### Task 1: Freeze moves to `⌥F`

**Files:**
- Modify: `src/SharpMUTerm.Tui/MacroKeys.cs` (in `Fixed()`)
- Modify: `src/SharpMUTerm.Tui/SharpMUTermApp.cs` (`ShortcutAction`, the Alt branch and the Control switch)
- Modify: `src/SharpMUTerm.Tui/FreezeBarRenderer.cs:18`
- Modify: `tests/SharpMUTerm.Tui.Tests/FreezeBarRendererTests.cs:13`
- Create: `tests/SharpMUTerm.Tui.Tests/FreezeChordTests.cs`
- Modify: `docs/design/README.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: `SharpMUTermApp.ToggleFreeze()` (private, unchanged), `FrozenScrollbackOf(string)` (internal,
  returns `ScrollbackView?` — null when the window is not frozen).
- Produces: `⌃F` is unclaimed after this task. PR 3 claims it for search. In between, `MacroKeys.Verdict`
  correctly reports `Ctrl+F` as a chord a macro can fire on; that is the honest intermediate state and no
  surface needs changing for it.

- [ ] **Step 1: Write the failing test**

Create `tests/SharpMUTerm.Tui.Tests/FreezeChordTests.cs`:

```csharp
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Freeze answers to ⌥F and not to ⌃F, which search takes in the PR after this one. Both halves are
/// the claim: a chord that moved in the handler and not in the bar would leave the client telling a
/// frozen reader to press a key that no longer thaws it.
/// </summary>
/// <remarks>Serialised: constructing the app touches the process-global console streams.</remarks>
[NotInParallel]
public class FreezeChordTests
{
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 34));
        app.RenderSnapshot("default");
        return app;
    }

    private static ConsoleKeyInfo Chord(ConsoleKey key, bool alt = false, bool control = false) =>
        new('\0', key, shift: false, alt: alt, control: control);

    [Test]
    public async Task AltFFreezesTheFocusedPaneAndPressingItAgainResumes()
    {
        var app = App();
        await Assert.That(app.FrozenScrollbackOf(Main)).IsNull();

        app.SimulateKey(Chord(ConsoleKey.F, alt: true));
        await Assert.That(app.FrozenScrollbackOf(Main)).IsNotNull();

        app.SimulateKey(Chord(ConsoleKey.F, alt: true));
        await Assert.That(app.FrozenScrollbackOf(Main)).IsNull();
    }

    [Test]
    public async Task CtrlFNoLongerFreezesAnything()
    {
        var app = App();

        app.SimulateKey(Chord(ConsoleKey.F, control: true));

        await Assert.That(app.FrozenScrollbackOf(Main)).IsNull();
    }

    [Test]
    public async Task TheClaimListNamesAltFAndNoLongerNamesCtrlF()
    {
        var claims = MacroKeys.AppShortcuts;

        await Assert.That(claims.Any(c => c.Modifiers == ConsoleModifiers.Alt && c.Key == ConsoleKey.F)).IsTrue();
        await Assert.That(claims.Any(c => c.Modifiers == ConsoleModifiers.Control && c.Key == ConsoleKey.F)).IsFalse();
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd /home/grave/RiderProjects/SharpMUTerm-find
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/FreezeChordTests/*" </dev/null
```

Expected: `AltFFreezesTheFocusedPaneAndPressingItAgainResumes` fails (⌥F does nothing, so the first
assertion after the keypress finds null), and `TheClaimListNamesAltFAndNoLongerNamesCtrlF` fails on both
assertions. `CtrlFNoLongerFreezesAnything` fails too — it freezes today.

- [ ] **Step 3: Move the claim**

In `src/SharpMUTerm.Tui/MacroKeys.cs`, inside `Fixed()`, delete the line

```csharp
        new(ConsoleModifiers.Control, ConsoleKey.F, "freezes the pane"),
```

and add this beside the other Alt claims, immediately after the `⌥D`/`⌥R` pair:

```csharp
        // Freeze is ⌥F and not ⌃F, and it moved rather than gaining a second spelling. ⌃F means *find*
        // to everyone who has used a computer, and the search surface takes it; freeze keeps its letter
        // and changes its modifier, which is the smallest move that frees the chord. ⌥F is delivered as
        // `ESC f` — measured at a raw reader with `kitten @ send-key`, the same way ⌥D and ⌥R were — and
        // is claimed by nothing else here or in the framework.
        //
        // No ⌃F alias is left behind, for the reason ⌃D was released rather than kept: a second key for
        // one action is either a secret or a duplicate row on every surface that lists chords.
        new(ConsoleModifiers.Alt, ConsoleKey.F, "freezes the pane"),
```

- [ ] **Step 4: Move the action**

In `src/SharpMUTerm.Tui/SharpMUTermApp.cs`, delete this arm from the `claim.Key switch` in
`ShortcutAction` (~line 4722):

```csharp
            ConsoleKey.F => () => { ToggleFreeze(); return true; },
```

and in the same method's `if (claim.Modifiers == ConsoleModifiers.Alt)` block, after the `⌥K` arm and
before the `WindowJumpNumber` check, add:

```csharp
            // ⌥F freezes and resumes the focused pane. Same delivery story as ⌥D and ⌥R: ESC + a
            // printable byte, decoded as that letter with Alt set.
            if (claim.Key == ConsoleKey.F)
            {
                return () => { ToggleFreeze(); return true; };
            }
```

- [ ] **Step 5: Move the label the frozen reader is looking at**

In `src/SharpMUTerm.Tui/FreezeBarRenderer.cs`, line 18, change `FROZEN ⌃F` to `FROZEN ⌥F`, and the
comment on line 17 with it. In `tests/SharpMUTerm.Tui.Tests/FreezeBarRendererTests.cs`, line 13, change
the expected string to `$"[#c678dd]{Glyphs.Freeze} FROZEN ⌥F[/]"` and the comment on line 12 with it.

- [ ] **Step 6: Run the whole Tui suite**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests </dev/null
```

Expected: PASS, including `FreezeChordTests` and `AdvertisedKeyHonestyTests`. If a doc-comment
elsewhere in `SharpMUTermApp.cs` still says `⌃F` (there are five: ~2324, ~4100, ~6275, ~7062, ~7429,
~7434, ~7441, ~7461), fix each to `⌥F` — they describe the chord to the next reader of the code.

- [ ] **Step 7: Update the reader-facing docs**

In `docs/design/README.md`: line 254-255 (`Freezing (⌃F) …` and the `▲ FROZEN ⌃F` bar) and line 426
(the chord list) become `⌥F`. In `CLAUDE.md`, the "Deliberately left on Ctrl" bullet drops `⌃F` from
its list, and a sentence is added to the Alt paragraph above it:

```markdown
  - **Freeze is `⌥F`.** It was `⌃F`, and moved so search could have the chord every reader on every
    platform reaches for. Nothing is left on `⌃F` as an alias — the `⌃D` rule: a second key for one
    action is either a secret or a duplicate row on every surface that lists chords.
```

- [ ] **Step 8: Verify the frame says so**

```bash
dotnet build SharpMUTerm.slnx
dotnet run -c Release --project src/SharpMUTerm.Tui --no-build -- \
  --snapshot --demo-config --view freeze --size 120x32 --out /tmp/freeze.ansi
grep -c "FROZEN ⌥F" /tmp/freeze.ansi
```

Expected: build clean, `grep` prints `1`. A frame you actually looked at, not markup you read.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(keys): freeze moves to ⌥F, freeing ⌃F for find

⌃F means find to everyone who has used a computer, and the search surface in
the third PR of this stack takes it. Freeze keeps its letter and changes its
modifier — ⌥F is `ESC f`, measured at a raw reader with kitten @ send-key.

No ⌃F alias is left behind: the ⌃D rule, that a second key for one action is
either a secret or a duplicate row on every surface listing chords. The label
a frozen reader is looking at moves with the chord.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015nuKnWthnELNkrd86q5KWN"
```

---

### Task 2: `⌥↑` / `⌥↓` recall history unconditionally

**Files:**
- Modify: `src/SharpMUTerm.Tui/SharpMUTermApp.cs` (`TryRecallKey`, ~3524)
- Create: `tests/SharpMUTerm.Tui.Tests/HistoryChordTests.cs`
- Modify: `docs/design/README.md` (the chord list), `CLAUDE.md`

**Interfaces:**
- Consumes: `SharpMUTermApp.ActiveBar()` → `InputBarControl`; `BarKind(InputBarControl)` → `InputBar`;
  `HistoryFor(InputBar)` → `InputHistory` with `Recall(string)`, `Forward()`, `IsRecalling`;
  `InputBarControl.TryMoveRow(int)` → `bool`; `_drafts.Record(string windowId, InputBar kind, string text)`.
  All private/internal members of `SharpMUTermApp` already used by the bare-arrow path.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing test**

Create `tests/SharpMUTerm.Tui.Tests/HistoryChordTests.cs`:

```csharp
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌥↑/⌥↓ mean history and nothing else. The bare arrows still recall at the edges — nothing is taken
/// away — but they answer to the caret first, which stops being a usable rule the moment the command
/// line grows to a second row. That was the reported complaint.
/// <para>
/// ⌃↑/⌃↓, the other chord the request offered, was never available: the terminal writes `ESC [ 1;5 A`
/// for it and this client already spends that on pane selection and the ladder onto the second command
/// line. `ESC [ 1;3 A` — Alt — is free, and was measured at a raw reader before it was spent.
/// </para>
/// </summary>
/// <remarks>Serialised: constructing the app touches the process-global console streams.</remarks>
[NotInParallel]
public class HistoryChordTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 34));
        app.RenderSnapshot("default");
        return app;
    }

    private static ConsoleKeyInfo Chord(ConsoleKey key, bool alt = false) =>
        new('\0', key, shift: false, alt: alt, control: false);

    /// <summary>One printable character, as the command line's own tests spell it.</summary>
    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(Key(c));
        }
    }

    private static void Send(SharpMUTermApp app, string text)
    {
        Type(app, text);
        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    /// <summary>Sends two lines so the history has something in it, and leaves the bar empty.</summary>
    private static void Seed(SharpMUTermApp app)
    {
        Send(app, "look");
        Send(app, "say hello");
    }

    [Test]
    public async Task AltUpRecallsTheNewestLineAndAltDownWalksBack()
    {
        var app = App();
        Seed(app);

        app.SimulateKey(Chord(ConsoleKey.UpArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");

        app.SimulateKey(Chord(ConsoleKey.UpArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("look");

        app.SimulateKey(Chord(ConsoleKey.DownArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");
    }

    /// <summary>
    /// The point of the chord. With a draft tall enough to have a row above the caret, the bare ↑ is the
    /// caret's and ⌥↑ is history's — on the same keystroke, from the same position.
    /// </summary>
    [Test]
    public async Task OnAGrownBarTheBareArrowMovesTheCaretAndTheAltArrowRecalls()
    {
        var app = App();
        Seed(app);
        var draft = new string('x', 400); // wraps to several rows at 120 columns
        Type(app, draft);

        app.SimulateKey(Chord(ConsoleKey.UpArrow));
        await Assert.That(app.ArmedInputText).IsEqualTo(draft);

        app.SimulateKey(Chord(ConsoleKey.UpArrow, alt: true));
        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");
    }

    /// <summary>
    /// ⌥↓ with nothing recalled is not ours: it must not clear the bar, and it must not be claimed on
    /// the way past. Exactly the bare-arrow rule, which returns false rather than blanking a draft.
    /// </summary>
    [Test]
    public async Task AltDownWithNothingRecalledLeavesTheDraftAlone()
    {
        var app = App();
        Seed(app);
        Type(app, "half a thought");

        app.SimulateKey(Chord(ConsoleKey.DownArrow, alt: true));

        await Assert.That(app.ArmedInputText).IsEqualTo("half a thought");
    }
}
```

`ArmedInputText` (`SharpMUTermApp.cs:7754`) reads whichever bar is armed; `Type`/`Send` are lifted from
`InputAreaEndToEndTests` so the two suites drive the command line the same way. There is no setter for
the bar's text on purpose — assigning `Text` raises no change event, so a test that set it would skip
the draft recording the real path does.

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/HistoryChordTests/*" </dev/null
```

Expected: the two recall tests fail — `⌥↑` reaches `RouteToInput`, which drops Alt chords, so the bar
still holds what it held. `AltDownWithNothingRecalledLeavesTheDraftAlone` passes already; it is a
regression guard, not a driver.

- [ ] **Step 3: Take the Alt pair in `TryRecallKey`**

In `src/SharpMUTerm.Tui/SharpMUTermApp.cs`, replace the early modifier guard of `TryRecallKey`:

```csharp
        if (e.KeyInfo.Modifiers != 0)
        {
            return false;
        }
```

with:

```csharp
        // Two ways in, and the difference is the caret. The bare arrows are the command line's first and
        // history's only where the caret has nowhere further to go — unchanged, because it is how this
        // client has always behaved. ⌥↑/⌥↓ mean history and nothing else, which is what makes them usable
        // on a draft tall enough to have another row: the rule "recall happens at the edges" stops being
        // one you can hold in your head the moment the bar grows.
        //
        // Exact modifiers on both. Three separate defects here have been a handler that looked at the key
        // and never at the modifiers; ordering is the second line of defence and never the first.
        var history = e.KeyInfo.Modifiers switch
        {
            (ConsoleModifiers)0 => RecallMode.AtTheEdges,
            ConsoleModifiers.Alt => RecallMode.Always,
            _ => RecallMode.No,
        };

        if (history == RecallMode.No)
        {
            return false;
        }
```

and change the two `TryMoveRow` guards so they only apply to the bare arrows:

```csharp
            case ConsoleKey.UpArrow:
                if (history == RecallMode.AtTheEdges && bar.TryMoveRow(-1))
                {
                    e.Handled = true;
                    return true;
                }

                text = entries.Recall(bar.Text);
                break;
```

Rename the existing local `history` (the `InputHistory` from `HistoryFor(kind)`) to `entries`, so the
mode local above can take the name `history`; both uses in the `DownArrow` arm move with it. The `DownArrow` arm changes the same way:

```csharp
            case ConsoleKey.DownArrow:
                if (history == RecallMode.AtTheEdges && bar.TryMoveRow(1))
                {
                    e.Handled = true;
                    return true;
                }

                if (!entries.IsRecalling)
                {
                    return false;
                }

                text = entries.Forward();
                break;
```

Add the enum beside the method:

```csharp
    /// <summary>Which of the two ways into history a keystroke is: the bare arrows, or ⌥↑/⌥↓.</summary>
    private enum RecallMode
    {
        /// <summary>Not a recall key at all.</summary>
        No,

        /// <summary>A bare arrow: the caret gets it first, and history only at the edges.</summary>
        AtTheEdges,

        /// <summary>⌥↑ or ⌥↓: history, wherever the caret is.</summary>
        Always,
    }
```

- [ ] **Step 4: Run the tests**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/HistoryChordTests/*" </dev/null
```

Expected: PASS, all four.

- [ ] **Step 5: Run every suite**

```bash
dotnet build SharpMUTerm.slnx
for p in Core Graphics Scripting Web Tui; do
  dotnet run -c Release --project tests/SharpMUTerm.$p.Tests </dev/null || echo "FAILED: $p"
done
```

Expected: build clean and warning-free, five suites green. `InputAreaEndToEndTests` and
`MacroDispatchEndToEndTests` are the two most likely to notice this change; if either fails, the
regression is real — do not adjust the test to match the new behaviour without reading why it was
written.

- [ ] **Step 6: Say so on the surfaces that list chords**

`docs/design/README.md`'s chord list gains `⌥↑/⌥↓ command history`. In `CLAUDE.md`, under the
Alt-versus-Ctrl entry, add:

```markdown
  - **History recall has its own chord: `⌥↑`/`⌥↓`.** The bare arrows still recall where the caret has
    nowhere further to go, and that is unchanged — but it is a rule that stops being usable the moment
    the bar grows to a second row, which was the report. `⌃↑`/`⌃↓`, the alternative offered, was never
    available: the terminal writes `ESC [ 1;5 A` and this client already spends it on pane selection
    and the ladder onto the second command line. `ESC [ 1;3 A` is Alt and is free. A macro bound to
    `Alt+Up` still wins, because `DispatchMacro` runs ahead of recall — the same relationship
    `Ctrl+←/→` has with pane selection.
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(input): ⌥↑/⌥↓ recall command history wherever the caret is

The bare arrows recall only where the caret has nowhere further to go, which
stops being a rule you can hold in your head once the command line grows to a
second row. They are unchanged; the Alt pair is added beside them and skips the
caret test.

⌃↑/⌃↓ was the other chord offered and was never available — the terminal writes
ESC [ 1;5 A for it and this client already spends that on pane selection.
ESC [ 1;3 A is Alt, is free, and was measured before it was spent.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015nuKnWthnELNkrd86q5KWN"
```

---

### Task 3: Open the PR

- [ ] **Step 1: Push and open**

```bash
git push -u origin feat/find-chords
gh pr create --base main --title "feat(keys): ⌥F freezes, ⌥↑/⌥↓ recall, and ⌃F is free" --body "$(cat <<'BODY'
First of a three-PR stack (`feat/find-chords` → `feat/window-activity-boundary` → `feat/pane-search`).
Design: `docs/superpowers/specs/2026-08-11-pane-search-and-activity-design.md`.

**Freeze moves to `⌥F`.** `⌃F` means *find* to everyone who has used a computer, and the search
surface in PR 3 takes it. Freeze keeps its letter and changes its modifier — the smallest move that
frees the chord. No `⌃F` alias is left behind (the `⌃D` rule). The `❄ FROZEN ⌥F` label a frozen
reader is actually looking at moves with it, as do the `⌃P` entry and the docs.

**History gets its own chord, `⌥↑`/`⌥↓`.** The bare arrows still recall at the edges and are
unchanged; the Alt pair skips the caret test, so it works on a draft tall enough to have another row.
That was the report.

**Every chord was measured** at a raw-mode reader with `kitten @ send-key` before it was spent:
`⌥F` is `ESC f`, `⌥↑`/`⌥↓` are `ESC [ 1;3 A`/`B`. `⌃↑`/`⌃↓` — the alternative the request offered —
arrives as `ESC [ 1;5 A` and was already spent on pane selection.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_015nuKnWthnELNkrd86q5KWN
BODY
)"
```

- [ ] **Step 2: Branch the next PR off this one**

```bash
git checkout -b feat/window-activity-boundary
```

The stack continues in `docs/superpowers/plans/2026-08-11-window-activity-boundary.md`, written after
this PR lands its first review.

## Self-review notes

- **Spec coverage:** this plan covers spec parts "The chords, and why these ones" and "`⌃F` becomes
  search; freeze becomes `⌥F`" and "`⌥↑`/`⌥↓` recall history, unconditionally". `⌥G`/`⌥⇧G` is
  deliberately *not* here — it repeats a search that does not exist yet, and belongs to PR 3.
- **Intermediate state:** between this PR and PR 3, `⌃F` is claimed by nobody and `MacroKeys.Verdict`
  says a macro on it fires. That is true while it lasts and needs no stub.
- **Not in scope:** the `⌃P` command-surface entries for freeze carry no chord hint
  (`CommandCatalog.cs:175-176` passes only a label and an id), so nothing there needs to change.
