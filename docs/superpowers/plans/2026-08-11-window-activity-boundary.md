# PR 2 — The activity boundary: "since you were last here", and a bar that lasts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan
> task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mark where the reader left off in *any* window they were not watching — not just when they
tabbed away from the terminal — and stop the bar retiring before it has been read.

**Architecture:** One new per-window boundary (`_missedFrom`), recorded at the moment a line lands in
a window that is not caught up, and materialised into the existing away-bar machinery when that window
becomes caught up again. The terminal-away path is unchanged and still uses its input-proxy boundary,
because focus-out is unobservable; where both boundaries exist the older wins. Retirement gains a third
conjunct: a dwell floor from a new F7 setting, measured off the app's existing `TimeProvider`.

**Tech Stack:** C# / .NET 10, SharpConsoleUI 2.5.14 (package only), TUnit.

**Spec:** `docs/superpowers/specs/2026-08-11-pane-search-and-activity-design.md`, part 3.

**Branch:** `feat/window-activity-boundary`, off `feat/find-chords`. Second of a three-PR stack.

## Global Constraints

- Target framework `net10.0`; file-scoped namespaces, 4-space C#, LF endings.
- `SharpMUTerm.Core` stays UI-agnostic. The only Core change here is one settings property.
- Run suites directly, never `dotnet test`, and keep the `</dev/null`.
- Work in `/home/grave/RiderProjects/SharpMUTerm-find`.
- **The bar is chrome.** It goes into the buffer through `buffer.Insert`, never through
  `AppendWindowLine`: it must not badge the window unread and must not reach the restore log.
- **Nothing on the chrome may cost a cell only when it has something to say** — not relevant to a
  full-width bar row, but the rule that governs anything added beside it.
- A new defaulted setting means **no schema bump and no migration** (`ConnectAtStartup`, `PaneTint.None`).

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/SharpMUTerm.Tui/AwayBarRenderer.cs` | Modify | Adds the window-absence wording beside the terminal-absence one |
| `src/SharpMUTerm.Tui/SharpMUTermApp.cs` | Modify | `_missedFrom`, its recording, materialising, trim fixups, dwell floor |
| `src/SharpMUTerm.Core/Configuration/PreferenceSettings.cs` | Modify | `TextSettings.ActivityBarSeconds` |
| `src/SharpMUTerm.Tui/OptionsScreenRenderer.cs` | Modify | The F7 row |
| `tests/SharpMUTerm.Tui.Tests/AwayBarRendererTests.cs` | Modify | The new bar's words |
| `tests/SharpMUTerm.Tui.Tests/WindowActivityBoundaryTests.cs` | Create | Accrual, materialising, and what must *not* accrue |
| `tests/SharpMUTerm.Tui.Tests/ActivityBarDwellTests.cs` | Create | The floor, against an injected clock |
| `src/SharpMUTerm.Tui/DemoScene.cs` + `SharpMUTermApp.RenderSnapshot` | Modify | The `activity-bar` view |
| `CLAUDE.md` | Modify | The away-bar entry generalises |

---

### Task 1: The window-absence bar

**Files:**
- Modify: `src/SharpMUTerm.Tui/AwayBarRenderer.cs`
- Modify: `tests/SharpMUTerm.Tui.Tests/AwayBarRendererTests.cs`

**Interfaces:**
- Produces: `AwayBarRenderer.Missed(int lines, string accentHex)` → `string` markup, and
  `AwayBarRenderer.MissedLabel` = `"NEW"`. Task 2 calls it.

- [ ] **Step 1: Write the failing test** — append to `AwayBarRendererTests`:

```csharp
    /// <summary>
    /// The window-absence bar says what it can and no more. There is no duration on it: the terminal
    /// bar measures from the last input before the reader vanished, which is a real (if approximate)
    /// instant, while this one is made when a line lands in a window nobody is watching — a moment the
    /// reader was not part of. A "3 min" on it would be timing the *output*, not the absence.
    /// </summary>
    [Test]
    public async Task TheMissedBarCountsTheLinesAndClaimsNoDuration()
    {
        var bar = AwayBarRenderer.Missed(47, "#c678dd");

        await Assert.That(bar).Contains($"[#c678dd]{Glyphs.Away} {AwayBarRenderer.MissedLabel}[/]");
        await Assert.That(bar).Contains("47 lines since you were here");
        await Assert.That(bar).DoesNotContain("min");
    }

    [Test]
    public async Task TheMissedBarSaysOneLineRatherThanOneLines()
    {
        await Assert.That(AwayBarRenderer.Missed(1, "#c678dd")).Contains("1 line since you were here");
    }
```

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/AwayBarRendererTests/*" </dev/null
```

Expected: compile error — `AwayBarRenderer.Missed` does not exist.

- [ ] **Step 3: Add the renderer**

In `AwayBarRenderer`, beside `Label`:

```csharp
    /// <summary>
    /// The label on the bar marking a *window* the reader was not watching, as against
    /// <see cref="Label"/>'s terminal absence. Two words rather than one bar with two meanings: "AWAY"
    /// is about the reader, "NEW" is about the window, and a reader who sees both in one client should
    /// be able to tell which absence they are looking at without counting the lines.
    /// </summary>
    internal const string MissedLabel = "NEW";

    /// <summary>
    /// The bar for <paramref name="lines"/> lines that arrived in a window while the reader was not
    /// watching it, on an already-resolved <c>#rrggbb</c> accent.
    /// <para>
    /// It carries a count and no duration, which is the one way it differs from <see cref="Bar"/>. The
    /// terminal bar's span is measured from the last input before the reader vanished — approximate,
    /// but an instant the reader was part of. This boundary is made when a line lands in a window
    /// nobody is watching, so a duration on it would be timing the output rather than the absence.
    /// </para>
    /// </summary>
    public static string Missed(int lines, string accentHex)
    {
        ArgumentException.ThrowIfNullOrEmpty(accentHex);

        var count = lines == 1 ? "1 line" : $"{lines} lines";
        var rule = new string('─', RuleCells);
        return $"[{accentHex}]{Glyphs.Away} {MissedLabel}[/] "
            + $"[dim]{MarkupText.Escape($"{count} since you were here")} {rule}[/]";
    }
```

- [ ] **Step 4: Run the tests**

Same command. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(activity): a bar for the window you were not watching

Beside the terminal-absence bar rather than replacing it: AWAY is about the
reader, NEW is about the window. No duration on this one — its boundary is made
when a line lands in a window nobody is watching, so a span on it would be
timing the output rather than the absence.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015nuKnWthnELNkrd86q5KWN"
```

---

### Task 2: The boundary — record it where it happens, draw it when they come back

**Files:**
- Modify: `src/SharpMUTerm.Tui/SharpMUTermApp.cs`
- Create: `tests/SharpMUTerm.Tui.Tests/WindowActivityBoundaryTests.cs`

**Interfaces:**
- Consumes: `Workspace.IsCaughtUp(string)` → `bool` (visible **and** not scrolled back);
  `AwayBarRenderer.Missed(int, string)` from Task 1; the existing `_awayMarks`, `RemoveAwayBar`,
  `RevealAwayBar`, `RepaintPane`, `FrozenAccentHex()`.
- Produces: `SharpMUTermApp.AwayBarIndex(string windowId)` (already internal) now also answers for a
  window bar; `MarkMissedLines()` private. Task 3 adds a conjunct to `ConsumeReadAwayBars`.

- [ ] **Step 1: Write the failing test**

Create `tests/SharpMUTerm.Tui.Tests/WindowActivityBoundaryTests.cs`:

```csharp
using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The boundary that marks a window the reader was not watching. It is the same bar the terminal
/// absence draws and it answers to the same rule the unread badge does — <c>IsCaughtUp</c>, visible
/// *and* at the live tail — so a badge showing a count always has a bar under it saying where the
/// count begins.
/// </summary>
/// <remarks>Serialised: constructing the app touches the process-global console streams.</remarks>
[NotInParallel]
public class WindowActivityBoundaryTests
{
    private const string Main = "main";
    private const string Chat = "chat";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 34));
        app.RenderSnapshot();
        return app;
    }

    /// <summary>
    /// A background tab gains lines, and going to it lands the reader on a bar with the missed lines
    /// under it. This is the reported case: the badge said 3 and nothing said where the 3 began.
    /// </summary>
    [Test]
    public async Task AWindowTheReaderCannotSeeAccruesABoundaryAndShowsItOnReturn()
    {
        var app = App();

        app.SimulateLine(Chat, "<OOC> Ana: anyone about?");
        app.SimulateLine(Chat, "<OOC> Bo: here");
        await Assert.That(app.AwayBarIndex(Chat)).IsNull();

        app.SimulateWindowChange(Chat);

        await Assert.That(app.AwayBarIndex(Chat)).IsNotNull();
        await Assert.That(app.RenderSnapshot()).Contains("2 lines since you were here");
    }

    /// <summary>
    /// The window in front of the reader, at its live tail, accrues nothing — those lines went past
    /// their eyes. Same rule as the unread badge, which is the point: the two must not disagree.
    /// </summary>
    [Test]
    public async Task TheWindowInFrontOfTheReaderGetsNoBar()
    {
        var app = App();

        app.SimulateLine(Main, "The goblin snarls at you.");
        app.RenderSnapshot();

        await Assert.That(app.AwayBarIndex(Main)).IsNull();
        await Assert.That(app.RenderSnapshot()).DoesNotContain("since you were here");
    }

    /// <summary>
    /// A window the reader is looking at but has scrolled back off is exactly as blind as one they are
    /// not looking at — the reasoning <c>OnLine</c> already uses for the badge — so it accrues, and the
    /// bar appears when they come back down to the tail.
    /// </summary>
    [Test]
    public async Task AVisibleWindowScrolledBackAccruesAndTheBarAppearsOnTheWayBackDown()
    {
        var app = App();
        app.LoadLongScene();
        app.SimulateScrollKey(new ConsoleKeyInfo('\0', ConsoleKey.PageUp, false, false, false));

        app.SimulateLine(Main, "A goblin corpse lies here.");
        await Assert.That(app.AwayBarIndex(Main)).IsNull();

        app.SimulateScrollKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, true));

        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();
    }

    /// <summary>
    /// One bar per window, and the terminal absence is the older of the two boundaries: a window that
    /// had already missed lines before the reader left the terminal must not have its bar moved *down*
    /// to where they left, which would hide the lines it was drawn for.
    /// </summary>
    [Test]
    public async Task TheOlderBoundaryWins()
    {
        var app = App();

        app.SimulateLine(Chat, "first, missed while the tab was in the background");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(5));

        await Assert.That(app.AwayBarIndex(Chat)).IsEqualTo(0);
    }
}
```

- [ ] **Step 2: Add the test seam if it is missing**

`SimulateLine(windowId, text)` must append one line of *world* output to a window, through the same
path `OnLine` uses (`AppendWindowLine` + `NoteActivity`). If `SharpMUTermApp` has no such internal
method, add one beside `SimulateWindowChange`:

```csharp
    /// <summary>
    /// Appends one line of a world's output to a window, the way <see cref="OnLine"/> does — including
    /// the unread accounting, because that is the fact the activity boundary shares a rule with.
    /// </summary>
    internal void SimulateLine(string windowId, string text)
    {
        AppendWindowLine(windowId, MarkupText.Escape(text), StampNow());
        if (!_workspace.IsCaughtUp(windowId))
        {
            _workspace.NoteActivity(windowId);
            RefreshTabTitles();
        }
    }
```

- [ ] **Step 3: Run it and watch it fail**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/WindowActivityBoundaryTests/*" </dev/null
```

Expected: `TheWindowInFrontOfTheReaderGetsNoBar` passes (nothing draws a bar yet); the other three fail.

- [ ] **Step 4: Record the boundary where it happens**

In `SharpMUTermApp`, beside `_awayPending`/`_awayBoundary`:

```csharp
    /// <summary>
    /// Where the lines the reader has not seen start, per window, for the windows that have any — the
    /// buffer index of the first line that landed while the window was not <em>caught up</em>.
    /// <para>
    /// <b>Recorded forwards, unlike the terminal boundary beside it.</b> `_awayBoundary` has to be
    /// reconstructed from the last input before the reader vanished, because a terminal reports focus-in
    /// and this client cannot see focus-out at all. This one needs none of that: a line arrives, the
    /// window either is or is not caught up, and if it is not then *this* is the boundary. Exact, and
    /// one comparison in a method that already runs per line.
    /// </para>
    /// <para>
    /// <see cref="Workspace.IsCaughtUp"/> and not <c>IsVisible</c>, which is already the rule the unread
    /// badge answers to: a visible tab whose output the reader has scrolled back off is exactly as blind
    /// as a tab they are not looking at. One fact behind both, so a badge showing a count always has a
    /// bar under it saying where the count begins.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, int> _missedFrom = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether a line landing out of sight is an <em>absence</em> yet. False until the constructor has
    /// finished, because until the workspace is laid out "not visible" means "no pane has been built",
    /// which is not something the reader missed — and because the restore replay pours a previous
    /// session's lines through <see cref="AppendWindowLine"/> into windows that are not visible yet.
    /// Every one of those already sits under a <see cref="RestoreBarRenderer"/> bar saying exactly what
    /// it is; a NEW bar over the top would be the client marking its own startup as news.
    /// </summary>
    private bool _watching;
```

Set it at the very end of the constructor, after `_system.AddWindow(_window);`:

```csharp
        _watching = true;
```

In `AppendWindowLine`, immediately before `buffer.Add(...)`:

```csharp
        // The moment a boundary is made: a line landing in a window the reader is not watching. Before
        // the Add, so the index is the first line they missed rather than the one after it.
        if (_watching && !_missedFrom.ContainsKey(windowId) && !_workspace.IsCaughtUp(windowId))
        {
            _missedFrom[windowId] = buffer.Count;
        }
```

and in the trim block, beside the other index fixups:

```csharp
            if (_missedFrom.TryGetValue(windowId, out var missed))
            {
                _missedFrom[windowId] = Math.Max(0, missed - excess);
            }
```

- [ ] **Step 5: Draw it when the window is watched again**

Add, beside `MarkWhereTheReaderLeft`:

```csharp
    /// <summary>
    /// Draws the boundary in every window that has missed lines and is now caught up again — the
    /// reader has come back to it, so this is the moment to say where they left off.
    /// <para>
    /// Called from the two places a window can *become* caught up: <see cref="Activate"/>, which raises
    /// a tab and focuses its pane, and <see cref="SyncScrollbackState"/>, which is where every scroll
    /// route lands. There is no third: <c>Workspace.IsCaughtUp</c> is visibility and scroll position,
    /// and nothing else moves either.
    /// </para>
    /// <para>
    /// The bar is client chrome, so it is inserted through the buffer rather than through
    /// <see cref="AppendWindowLine"/>: it must not badge the window unread, and it must not reach the
    /// restore log. Recursion is not a risk — the entry is removed before the reveal, and the reveal's
    /// scroll comes back through <see cref="SyncScrollbackState"/> with nothing pending.
    /// </para>
    /// </summary>
    private void MarkMissedLines()
    {
        foreach (var windowId in _missedFrom.Keys.ToArray())
        {
            if (!_workspace.IsCaughtUp(windowId)
                || !_lines.TryGetValue(windowId, out var buffer)
                || string.Equals(windowId, WebWindowId, StringComparison.Ordinal))
            {
                continue;
            }

            var at = Math.Clamp(_missedFrom[windowId], 0, buffer.Count);
            _missedFrom.Remove(windowId);

            var missed = buffer.Count - at;
            if (missed <= 0)
            {
                continue;
            }

            // At most one bar per window: whichever absence drew the last one, this replaces it. It goes
            // first, because removing it shifts every index after it — the boundary included.
            RemoveAwayBar(windowId);
            at = Math.Clamp(at, 0, buffer.Count);

            buffer.Insert(at, new PaneLine(AwayBarRenderer.Missed(buffer.Count - at, FrozenAccentHex())));
            if (_freezePoints.TryGetValue(windowId, out var freeze) && freeze > at)
            {
                _freezePoints[windowId] = freeze + 1;
            }

            var mark = new AwayMark { Index = at, DrawnAfter = _focus.InputCount, DrawnAt = _time.GetUtcNow() };
            _awayMarks[windowId] = mark;
            RepaintPane(windowId);
            RevealAwayBar(windowId, mark);
        }
    }
```

Call it from `SyncScrollbackState`, immediately before `ConsumeReadAwayBars()`:

```csharp
        MarkMissedLines();
```

and from `Activate`, immediately after `SyncToFocusedPane();`.

- [ ] **Step 6: Let the older boundary win**

In `MarkWhereTheReaderLeft`, replace

```csharp
            var at = Math.Clamp(_awayBoundary.GetValueOrDefault(windowId), 0, buffer.Count);
```

with

```csharp
            // Two boundaries can exist for one window: the terminal absence's, reconstructed from the
            // last input before the reader vanished, and the window's own, recorded exactly when a line
            // landed out of sight. The *older* wins — one bar per window, marking the earlier of the two
            // things they missed. Taking the terminal one unconditionally would move the bar down past
            // lines it had already been made for.
            var boundary = _awayBoundary.GetValueOrDefault(windowId);
            if (_missedFrom.TryGetValue(windowId, out var missedFrom))
            {
                boundary = Math.Min(boundary, missedFrom);
                _missedFrom.Remove(windowId);
            }

            var at = Math.Clamp(boundary, 0, buffer.Count);
```

- [ ] **Step 7: Run the tests**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/WindowActivityBoundaryTests/*" </dev/null
```

Expected: PASS, all four. Then the whole Tui suite — `AwayDividerTests` (or whatever the away suite is
called) is the one most likely to notice, and a failure there is real.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(activity): mark where you left off in any window, not just the terminal

A window accrues a boundary from the first line that lands while it is not
caught up — visible and at its live tail, the same rule the unread badge
answers to, so a badge showing a count always has a bar under it saying where
the count begins. That was the report: the badge said 3 and nothing said which 3.

Recorded forwards, unlike the terminal boundary beside it, which has to be
reconstructed from the last input because focus-out is unobservable. Where both
exist the older wins: still one bar per window, marking the earlier absence.

Nothing accrues before the constructor finishes — until the workspace is laid
out 'not visible' means 'no pane built yet', and the restore replay pours a
previous session through the same seam into windows under a restore bar already.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015nuKnWthnELNkrd86q5KWN"
```

---

### Task 3: The dwell floor

**Files:**
- Modify: `src/SharpMUTerm.Core/Configuration/PreferenceSettings.cs`
- Modify: `src/SharpMUTerm.Tui/OptionsScreenRenderer.cs`
- Modify: `src/SharpMUTerm.Tui/SharpMUTermApp.cs` (`AwayMark`, `ConsumeReadAwayBars`, the two draw sites)
- Create: `tests/SharpMUTerm.Tui.Tests/ActivityBarDwellTests.cs`

**Interfaces:**
- Consumes: `SharpMUTermApp._time` (the constructor's existing `TimeProvider`, defaulting to
  `TimeProvider.System`) — no new constructor parameter; tests pass a `FakeTimeProvider`-alike.
- Produces: `TextSettings.ActivityBarSeconds` (`int`, default `TextSettings.DefaultActivityBarSeconds`
  = 30, max `TextSettings.MaxActivityBarSeconds` = 600).

- [ ] **Step 1: Write the failing test**

Create `tests/SharpMUTerm.Tui.Tests/ActivityBarDwellTests.cs`:

```csharp
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The bar does not retire the instant it has been read past. Two conjuncts were not enough: on a
/// shallow absence the pane is already at its live tail and the very next keystroke retired the bar,
/// which is a second or two after it appeared. The third conjunct is a floor in *time*, because that
/// is the unit the complaint was in — a raised input count would be an hour on a quiet character and
/// three seconds on a busy one.
/// </summary>
/// <remarks>Serialised: constructing the app touches the process-global console streams.</remarks>
[NotInParallel]
public class ActivityBarDwellTests
{
    private const string Chat = "chat";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>A clock the test moves by hand, so the floor is not a race against a loaded CI box.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static (SharpMUTermApp App, Clock Time) App(int seconds)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.Text.ActivityBarSeconds = seconds;
        var clock = new Clock();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 34), clock);
        app.RenderSnapshot();
        return (app, clock);
    }

    /// <summary>Draws a bar in the Chat window and reads it past: at the tail, one input since.</summary>
    private static void DrawAndRead(SharpMUTermApp app)
    {
        app.SimulateLine(Chat, "<OOC> Ana: anyone about?");
        app.SimulateWindowChange(Chat);
        app.SimulateKey(new ConsoleKeyInfo('x', ConsoleKey.NoName, false, false, false));
        app.SimulateScrollKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, true));
    }

    [Test]
    public async Task TheBarSurvivesBeingReadPastUntilTheFloorHasElapsed()
    {
        var (app, clock) = App(30);
        DrawAndRead(app);

        await Assert.That(app.AwayBarIndex(Chat)).IsNotNull();

        clock.Advance(TimeSpan.FromSeconds(29));
        app.SimulateKey(new ConsoleKeyInfo('y', ConsoleKey.NoName, false, false, false));
        await Assert.That(app.AwayBarIndex(Chat)).IsNotNull();

        clock.Advance(TimeSpan.FromSeconds(2));
        app.SimulateKey(new ConsoleKeyInfo('z', ConsoleKey.NoName, false, false, false));
        await Assert.That(app.AwayBarIndex(Chat)).IsNull();
    }

    /// <summary>Zero is a real answer, and it is exactly the behaviour this feature replaced.</summary>
    [Test]
    public async Task AFloorOfZeroRetiresTheBarAsSoonAsItHasBeenReadPast()
    {
        var (app, _) = App(0);
        DrawAndRead(app);

        await Assert.That(app.AwayBarIndex(Chat)).IsNull();
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/ActivityBarDwellTests/*" </dev/null
```

Expected: compile error — `TextSettings.ActivityBarSeconds` does not exist.

- [ ] **Step 3: Add the setting**

In `src/SharpMUTerm.Core/Configuration/PreferenceSettings.cs`, inside `TextSettings`:

```csharp
    /// <summary>
    /// How long the activity bar stays put after the reader has read past it, in seconds. Thirty by
    /// default; zero retires it as soon as the pane is back at its live tail and one input has landed,
    /// which is what this client did before the floor existed.
    /// <para>
    /// A floor in *time* rather than in keystrokes, because the complaint was in time: the bar went
    /// before it had been read. A raised input count would be an hour on a quiet character and three
    /// seconds on a busy one.
    /// </para>
    /// <para>
    /// It is only a floor. The bar still has to be read past — the pane back at its tail, one input
    /// since it was drawn — and it retires on the first of those checks *after* the floor, which is the
    /// next keystroke or scroll rather than a timer firing on its own. A client with nothing happening
    /// in it keeps the bar, which is the right answer for a reader who has walked away again.
    /// </para>
    /// </summary>
    public int ActivityBarSeconds { get; set; } = DefaultActivityBarSeconds;

    /// <summary>Thirty seconds — long enough to read a screenful, short enough not to become furniture.</summary>
    public const int DefaultActivityBarSeconds = 30;

    /// <summary>Ten minutes. Past this the bar is not a boundary marker, it is a pin.</summary>
    public const int MaxActivityBarSeconds = 600;
```

- [ ] **Step 4: Add the F7 row**

In `OptionsScreenRenderer.TextAnsiScreen`, after the UNICODE section:

```csharp
            new(string.Empty, null, null),
            new("├ ACTIVITY", null, null),

            // Seconds, and its own section: it is the only row here that describes the client's own
            // chrome rather than how a world's text is drawn. Zero is a real answer — it retires the
            // bar as soon as it has been read past, which is what this client did before the floor.
            new("activity bar holds for (seconds)",
                settings.ActivityBarSeconds.ToString(CultureInfo.InvariantCulture), null, null, null,
                ScreenField.Integer(
                    "activity bar holds for (seconds)",
                    () => settings.ActivityBarSeconds, v => settings.ActivityBarSeconds = v,
                    0, TextSettings.MaxActivityBarSeconds)),
```

- [ ] **Step 5: Add the conjunct**

In `AwayMark`:

```csharp
        /// <summary>
        /// When this bar was drawn, off the app's own <c>TimeProvider</c>. The dwell floor is measured
        /// from here — see <see cref="TextSettings.ActivityBarSeconds"/>.
        /// </summary>
        public DateTimeOffset DrawnAt;
```

Set it at both draw sites (`MarkWhereTheReaderLeft` and `MarkMissedLines`):
`DrawnAt = _time.GetUtcNow()`.

In `ConsumeReadAwayBars`, replace the condition:

```csharp
            // Three conjuncts, and the third is the one the reader asked for. At the live tail, which
            // means something because the reveal took the pane *off* its tail whenever the bar was not
            // on screen; one input since it was drawn, which stops a shallow absence clearing in the
            // frame it appears in; and drawn long enough ago to have been read.
            var held = TimeSpan.FromSeconds(Math.Max(0, _config.Text.ActivityBarSeconds));
            if (mark.InputSince && panel.AutoScroll && _time.GetUtcNow() - mark.DrawnAt >= held)
```

- [ ] **Step 6: Run the tests**

```bash
dotnet run -c Release --project tests/SharpMUTerm.Tui.Tests --treenode-filter "/*/*/ActivityBarDwellTests/*" </dev/null
```

Expected: PASS. Then the whole Tui suite: the existing away tests assume immediate retirement, and the
demo config's default is now 30 s — where one fails, give that test's config `ActivityBarSeconds = 0`
and say in a comment that it is asserting the retirement rule and not the floor.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(activity): the bar holds for 30s after it has been read past

Two conjuncts were not enough: on a shallow absence the pane is already at its
live tail, so the next keystroke retired the bar a second or two after it
appeared. The third is a floor in time, because that is the unit the complaint
was in — a raised input count is an hour on a quiet character and three seconds
on a busy one.

F7 setting, default 30, 0 restores the old behaviour exactly. New defaulted
field, so no schema bump and no migration. Measured off the app's existing
TimeProvider, so the test moves the clock by hand instead of racing it.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_015nuKnWthnELNkrd86q5KWN"
```

---

### Task 4: The frame, the brief, and the PR

**Files:**
- Modify: `src/SharpMUTerm.Tui/SharpMUTermApp.cs` (`RenderSnapshot`'s view table)
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add the `activity-bar` view**

In `RenderSnapshot`, beside the `away` view:

```csharp
        // The window absence, as against `away`'s terminal one: lines arrive in a background tab and
        // the reader goes to it. The one frame that shows the NEW bar with the missed lines under it,
        // and the only one that would catch the boundary landing at the wrong index.
        if (string.Equals(view, "activity-bar", StringComparison.OrdinalIgnoreCase))
        {
            LoadScene();
            foreach (var line in new[]
                     {
                         "<OOC> Ana: anyone seen the vault key?",
                         "<OOC> Bo: try the east store room",
                         "<OOC> Ana: found it, thanks",
                     })
            {
                SimulateLine("chat", line);
            }

            Activate("chat");
            return RenderWholeFrame();
        }
```

Match the surrounding views' exact idiom for scene loading and returning a frame — copy the `away`
view's shape rather than the sketch above where they differ.

- [ ] **Step 2: Render it and look at it**

```bash
dotnet build SharpMUTerm.slnx
dotnet run -c Release --project src/SharpMUTerm.Tui --no-build -- \
  --snapshot --demo-config --view activity-bar --size 120x32 --out /tmp/activity.ansi
python3 tools/ansi_frame_to_image.py /tmp/activity.ansi /tmp/activity.html
```

Open the `.html` (not the `.svg` — Chromium clips a bare SVG's bottom). Expected: the Chat pane shows
`▾ NEW  3 lines since you were here ───` with the three lines under it.

- [ ] **Step 3: Update the brief**

In `CLAUDE.md`, the away-bar entry gains the generalisation — the two boundaries, which one wins, the
`IsCaughtUp` rule shared with the badge, the `_watching` gate, and the dwell floor with its F7 setting.
Add `activity-bar` to the snapshot views list.

- [ ] **Step 4: Full verification**

```bash
dotnet build SharpMUTerm.slnx
for p in Core Graphics Scripting Web Tui; do
  printf "%-10s " "$p"
  dotnet run -c Release --project tests/SharpMUTerm.$p.Tests </dev/null 2>&1 | grep -E "^  (total|failed):" | tr '\n' ' '
  echo
done
```

Expected: build clean and warning-free, five suites green.

- [ ] **Step 5: Push, open the PR against `feat/find-chords`, and branch PR 3**

```bash
git push -u origin feat/window-activity-boundary
gh pr create --base feat/find-chords --title "feat(activity): a boundary for the window you were not watching, and one that lasts"
git checkout -b feat/pane-search
```

## Self-review notes

- **Spec coverage:** part 3 in full — the `IsCaughtUp` rule, the exact forward-recorded boundary, the
  older-wins composition with the terminal absence, the two wordings, and the dwell floor with its
  injected clock and F7 setting.
- **Not in scope:** `PaneMarks`, the shared mid-buffer bookkeeping extraction. The spec assigns it to
  "whichever PR lands first", and after this one there is still exactly *one* kind of inserted chrome —
  `MarkMissedLines` and `MarkWhereTheReaderLeft` share `RemoveAwayBar`/`RevealAwayBar`/`AwayMark`
  already. The extraction earns itself in PR 3, where the search bar becomes a second kind; doing it
  here would be a refactor with one caller.
