# MXP Conformance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make SharpMUTerm parse MXP the way the specification defines it — line-security modes obeyed, ANSI understood inside MXP, the negotiated option deciding which parser runs, and `<VERSION>`/`<SUPPORT>` answered.

**Architecture:** `MxpParser` grows an escape-sequence state machine (sharing SGR decoding with `AnsiParser` through a new pure helper) and a two-variable line-mode model (`_defaultMode` + `_lineMode`) that gates every tag by category. `WorldSession` stops choosing its parser once at construction from static config and instead swaps to MXP when the telnet option actually negotiates.

**Tech Stack:** C# / .NET 10, TUnit on Microsoft.Testing.Platform, TelnetNegotiationCore 2.11.0.

**Spec:** <https://www.zuggsoft.com/zmud/mxp.htm> — the MXP specification. Every requirement below quotes it directly; keep it open while implementing.

## Global Constraints

- **Target framework `net10.0`**, `LangVersion latest`, nullable enabled, file-scoped namespaces, 4-space C#, LF endings (`.editorconfig`).
- **`SharpMUTerm.Core` stays UI-agnostic.** Everything in Tasks 1–5 lives in Core; no SharpConsoleUI reference, no `SharpMUTerm.Tui` type may be named.
- **Tests are TUnit `Exe` projects. `dotnet test` does not work.** Run a suite with:
  `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests </dev/null`
  Keep the `</dev/null`; it detaches stdin so the host does not hang.
  Filter to one class with `--treenode-filter "/*/*/ClassName/*"`.
- **Assertions are `await Assert.That(actual).IsEqualTo(expected)`** and friends — not xUnit's `Assert.Equal`.
- **Branch from `main`, open a PR. Never commit to `main`.**
- **The primary signal is `dotnet build SharpMUTerm.slnx` plus all five suites green** (Core, Tui, Graphics, Scripting, Web).

## Findings this plan fixes

Measured on `tdome.nukefire.org:4000`, 2026-08-20, with `--trace`:

1. **The line-security model does not exist.** `MxpParser`'s own header says it treats input as "secure/open line mode — every tag is processed". The spec: *"Only the tags described in this section are OPEN tags. All other MXP tags are SECURE tags"*, and *"On an 'Open' MUD Line, only 'Open' MXP commands are allowed."* `<SEND>` is secure, so a player typing `<SEND href="@shutdown">click</SEND>` into a public channel currently becomes a clickable command in this client — the exact exploit the spec's rationale names: *"players on a MUD can exploit this power and cause problems… you would not want to allow them to… execute script commands on the client of other users."*
2. **`ESC[Nz` line tags are emitted as literal text.** `MxpParser.ProcessText`'s default arm appends ESC verbatim.
3. **ANSI colour is lost in MXP mode.** Measured by running the parser standalone: `"\x1b[0;33mYellow\x1b[0m and <B>bold</B>\n"` yields text `<ESC>[0;33mYellow<ESC>[0m and bold` with `fg=default`. The tags parse; the colour is dropped *and* the escape leaks into the visible text. `MxpParser`'s header claims "Raw ANSI SGR is assumed to have been handled upstream", but `WorldSession.CreateParser` returns `MxpParser` **or** `AnsiParser`, never chained — there is no upstream.
4. **The negotiated option does not choose the parser.** `WorldSession.CreateParser` reads `WorldDefinition.ContentFormat`, a manual per-world field defaulting to `Ansi`. `TelnetSession` passes `onMXPEnabled: static () => ValueTask.CompletedTask` — a no-op. NukeFire sent `IAC WILL MXP`, we answered `DO`, and then handed the stream to the ANSI parser.
5. **`<VERSION>` and `<SUPPORT>` are unanswered.** The spec has the client reply with a secure `<VERSION>` / `<SUPPORTS>` tag.

**Out of scope, recorded here so it is not lost:** TelnetNegotiationCore drops `IAC SB MXP … IAC SE` into `BadSubNegotiation` (observed at byte #2409), and has no client-side handler for `IAC DO MXP` (`Do --[MXP]--> BadDo`, byte #731). Neither blocks this plan — `WILL`/`DO` is what tells us MXP is on, and `<VERSION>`/`<SUPPORT>` travel as tags in the text stream, not as subnegotiation. File an upstream issue separately.

## The mode model

Two variables, because the spec has two: a **default** mode and the **current line's** mode.

| `ESC[Nz` | Name | Effect |
|---|---|---|
| 0 | OPEN | Only open tags. Reverts to default at newline. |
| 1 | SECURE | All tags. Reverts to default at newline. |
| 2 | LOCKED | No parsing at all — every character is literal. Reverts at newline. |
| 3 | RESET | Close all open tags, set mode **and default** to Open, reset style to default. |
| 4 | TEMP SECURE | Secure for the next tag only, then back to the previous mode. |
| 5 | LOCK OPEN | Set mode **and default** to Open. |
| 6 | LOCK SECURE | Set mode **and default** to Secure. |
| 7 | LOCK LOCKED | Set mode **and default** to Locked. |

**Default at connection start is Open.** The spec does not say so in one sentence, but it is the only safe reading: it is what every mode reverts to before a `LOCK` has been seen, and starting in Secure would mean the very first line a server sends is fully trusted.

**Open tags, verbatim from the spec:** `B`, `BOLD`, `STRONG`, `I`, `ITALIC`, `EM`, `U`, `UNDERLINE`, `S`, `STRIKEOUT`, `C`, `COLOR`, `H`, `HIGH`, `FONT`. *"All other MXP tags are SECURE tags."* Closing tags take the category of the element they close.

**A secure tag on an open line is rendered as literal text**, not silently dropped. That is the behaviour that makes an injection attempt visible to the player rather than invisible to everyone.

**Worked example — the real NukeFire prompt.** The captured bytes are:

```
^[[1z<send>^[[7zY^[[1z</send>^[[7z/^[[1z<send>^[[7zN^[[1z</send>^[[7z)?
```

Read through the table: `ESC[1z` secures the line for `<send>`, `ESC[7z` locks it so the *content* `Y` cannot itself contain tags, `ESC[1z` secures `</send>`, and so on. The server is using the mode system exactly as designed — putting its own tags in secure mode and player-visible content in locked mode. This is the acceptance case for Task 3.

## File Structure

| File | Responsibility |
|---|---|
| `src/SharpMUTerm.Core/Text/SgrCodes.cs` | **New.** Pure SGR decoding: `TextStyle Apply(TextStyle, string)`. Extracted from `AnsiParser` so both parsers share one implementation. |
| `src/SharpMUTerm.Core/Text/AnsiParser.cs` | Modified: delegates SGR to `SgrCodes`. No behaviour change. |
| `src/SharpMUTerm.Core/Protocols/MxpLineMode.cs` | **New.** The mode enum and the `ESC[Nz` number → mode mapping. |
| `src/SharpMUTerm.Core/Protocols/MxpTagCategory.cs` | **New.** The open/secure classification, one table, spec-ordered. |
| `src/SharpMUTerm.Core/Protocols/MxpParser.cs` | Modified: escape state machine (Task 2), mode model and tag gating (Task 3), `<VERSION>`/`<SUPPORT>` (Task 5). |
| `src/SharpMUTerm.Core/Session/WorldSession.cs` | Modified: parser chosen by negotiation rather than by static config (Task 4). |
| `src/SharpMUTerm.Core/Telnet/TelnetSession.cs` | Modified: surface MXP-enabled as an event instead of swallowing it (Task 4). |
| `src/SharpMUTerm.Core/Telnet/TelnetEvents.cs` | Modified: the event args for the above. |

---

### Task 1: Extract SGR decoding into a shared pure helper

`MxpParser` needs to apply SGR without inheriting from or embedding `AnsiParser`. Today the logic is three private methods that mutate `AnsiParser._current`. Extract them, unchanged in behaviour, so both parsers share one implementation and neither can drift.

**Files:**
- Create: `src/SharpMUTerm.Core/Text/SgrCodes.cs`
- Modify: `src/SharpMUTerm.Core/Text/AnsiParser.cs` (delete `ApplySgr`, `ParseExtendedColor`, `ApplyColonColor`; call the helper)
- Test: `tests/SharpMUTerm.Core.Tests/Text/SgrCodesTests.cs`

**Interfaces:**
- Consumes: `TextStyle`, `TerminalColor`, `TextAttributes` (existing, `SharpMUTerm.Core.Text`).
- Produces: `public static class SgrCodes` with `public static TextStyle Apply(TextStyle current, string parameters)`. `parameters` is the CSI parameter string **without** the `ESC[` prefix and without the final `m` — e.g. `"0;33"`, `""`, `"38;5;196"`, `"38:2:255:0:0"`. An empty string means SGR reset, per ECMA-48.

- [ ] **Step 1: Write the failing test**

```csharp
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

public class SgrCodesTests
{
    [Test]
    public async Task Apply_EmptyParameters_ResetsToDefault()
    {
        var bold = new TextStyle(TerminalColor.FromIndex(1), TerminalColor.Default, TextAttributes.Bold);

        await Assert.That(SgrCodes.Apply(bold, string.Empty)).IsEqualTo(TextStyle.Default);
    }

    [Test]
    public async Task Apply_SetsForegroundFromAnIndexedCode()
    {
        var result = SgrCodes.Apply(TextStyle.Default, "33");

        await Assert.That(result.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
    }

    [Test]
    public async Task Apply_KeepsUnrelatedStateWhenOnlyOneAttributeChanges()
    {
        var yellow = SgrCodes.Apply(TextStyle.Default, "33");

        var result = SgrCodes.Apply(yellow, "1");

        await Assert.That(result.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
        await Assert.That(result.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
    }
}
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/SgrCodesTests/*" </dev/null`
Expected: build failure — `SgrCodes` does not exist.

- [ ] **Step 3: Create the helper by moving the existing code**

Create `src/SharpMUTerm.Core/Text/SgrCodes.cs`. Move the bodies of `AnsiParser.ApplySgr`, `AnsiParser.ParseExtendedColor` and `AnsiParser.ApplyColonColor` across **unchanged except for threading the style through explicitly** — every `_current = X` becomes `current = X` and the method returns it. Do not take the opportunity to rewrite the colour logic; a behaviour change here is invisible until it reaches someone's screen.

```csharp
namespace SharpMUTerm.Core.Text;

/// <summary>
/// Decodes an ECMA-48 SGR parameter string into a <see cref="TextStyle"/>.
/// </summary>
/// <remarks>
/// Extracted from <see cref="AnsiParser"/> so <see cref="SharpMUTerm.Core.Protocols.MxpParser"/> can
/// share it: MXP explicitly permits ANSI inside a document ("ANSI and VT100 codes can still be used
/// as normal"), and two implementations of SGR would drift the moment one gained a colour format the
/// other did not. Pure — it takes the current style and returns the next one — because the two
/// parsers keep that state in different places.
/// </remarks>
public static class SgrCodes
{
    /// <summary>
    /// Applies one SGR sequence's parameters to <paramref name="current"/>.
    /// </summary>
    /// <param name="current">The style in force before the sequence.</param>
    /// <param name="parameters">
    /// The CSI parameter string with no <c>ESC[</c> prefix and no trailing <c>m</c>, for example
    /// <c>"0;33"</c>. Empty means a reset, which is what a bare <c>ESC[m</c> encodes.
    /// </param>
    public static TextStyle Apply(TextStyle current, string parameters)
    {
        // ... moved verbatim from AnsiParser.ApplySgr, minus its leading FlushRun() call,
        // with `_current` threaded through as `current` and returned.
    }
}
```

- [ ] **Step 4: Point `AnsiParser` at it**

In `AnsiParser`, replace the three deleted methods with a call. `FlushRun()` stays in `AnsiParser` — it is buffer management, not SGR decoding:

```csharp
    private void ApplySgr(string parameters)
    {
        // Text accumulated so far keeps the pre-change style.
        FlushRun();
        _current = SgrCodes.Apply(_current, parameters);
    }
```

- [ ] **Step 5: Run the new tests and the whole existing ANSI suite**

Run:
```bash
dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/SgrCodesTests/*" </dev/null
dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/AnsiParserTests/*" </dev/null
```
Expected: both PASS. `AnsiParserTests` passing unchanged is the real check — this task is a refactor and any diff in its results is a regression.

- [ ] **Step 6: Commit**

```bash
git add src/SharpMUTerm.Core/Text/SgrCodes.cs src/SharpMUTerm.Core/Text/AnsiParser.cs tests/SharpMUTerm.Core.Tests/Text/SgrCodesTests.cs
git commit -m "refactor(text): extract SGR decoding so MXP can share it"
```

---

### Task 2: Teach `MxpParser` to read ANSI escapes

**Files:**
- Modify: `src/SharpMUTerm.Core/Protocols/MxpParser.cs`
- Test: `tests/SharpMUTerm.Core.Tests/Protocols/MxpParserTests.cs`

**Interfaces:**
- Consumes: `SgrCodes.Apply(TextStyle, string)` from Task 1.
- Produces: nothing new publicly. `MxpParser.Mode` gains `Escape` and `Csi` members, private.

- [ ] **Step 1: Write the failing test**

Add to `MxpParserTests`:

```csharp
    /// <summary>
    /// The spec permits ANSI inside MXP — "ANSI and VT100 codes can still be used as normal" — and
    /// nothing upstream of this parser strips it: WorldSession picks MxpParser *or* AnsiParser, never
    /// both. Before this, an SGR sequence was appended to the line as literal text and its colour was
    /// lost, so an MXP world rendered "<ESC>[0;33mYellow" in place of yellow text.
    /// </summary>
    [Test]
    public async Task Ansi_SgrSetsTheStyleAndLeavesNoEscapeInTheText()
    {
        var parser = new MxpParser();

        var line = parser.Feed("\x1b[33mYellow\x1b[0m plain\n")[0];

        await Assert.That(line.Text).IsEqualTo("Yellow plain");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
        await Assert.That(line.Spans[^1].Style.Foreground).IsEqualTo(TerminalColor.Default);
    }

    /// <summary>A non-SGR CSI is consumed and discarded, exactly as AnsiParser does with it.</summary>
    [Test]
    public async Task Ansi_NonSgrCsiIsDiscarded()
    {
        var parser = new MxpParser();

        var line = parser.Feed("a\x1b[2Kb\n")[0];

        await Assert.That(line.Text).IsEqualTo("ab");
    }

    /// <summary>ANSI and MXP compose: the tag applies on top of the SGR colour.</summary>
    [Test]
    public async Task Ansi_AndMxpTagsCompose()
    {
        var parser = new MxpParser();

        var line = parser.Feed("\x1b[33m<B>both</B>\n")[0];

        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
        await Assert.That(line.Spans[0].Style.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
    }
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/MxpParserTests/*" </dev/null`
Expected: the three new tests FAIL. `Ansi_SgrSetsTheStyleAndLeavesNoEscapeInTheText` will report the actual text as `\x1b[33mYellow\x1b[0m plain`.

- [ ] **Step 3: Add the escape states**

In `MxpParser`, extend the mode enum and add the two handlers. Mirror `AnsiParser.ProcessCsi`'s ranges exactly — final byte `0x40`–`0x7E`, parameters `0x30`–`0x3F`, intermediates `0x20`–`0x2F`, anything else aborts — so the two parsers agree about what a malformed sequence is:

```csharp
    private enum Mode
    {
        Text,
        Tag,
        Entity,
        Escape,
        Csi,
    }

    private const int MaxSequenceLength = 128;
    private readonly StringBuilder _seq = new();
```

In `ProcessText`, replace the comment on the default arm with a real case:

```csharp
            case '\x1b':
                _seq.Clear();
                _mode = Mode.Escape;
                break;
```

Add to `Process`'s switch: `case Mode.Escape: ProcessEscape(ch); break;` and `case Mode.Csi: ProcessCsi(ch); break;`.

```csharp
    private void ProcessEscape(char ch)
    {
        if (ch == '[')
        {
            _seq.Clear();
            _mode = Mode.Csi;
            return;
        }

        // Two-byte escapes (ESC c, ESC M, …) are consumed and ignored, as in AnsiParser.
        _mode = Mode.Text;
    }

    private void ProcessCsi(char ch)
    {
        if (ch is >= '\x40' and <= '\x7e')
        {
            if (ch == 'm')
            {
                // Text accumulated so far keeps the pre-change style.
                FlushRun();
                _current = SgrCodes.Apply(_current, _seq.ToString());
            }

            // 'z' is the MXP line tag and is handled in Task 3. Every other final byte — cursor
            // movement, erase — is discarded, which is what a line-oriented view can do with it.
            _seq.Clear();
            _mode = Mode.Text;
            return;
        }

        if (ch is >= '\x20' and <= '\x3f')
        {
            if (_seq.Length < MaxSequenceLength)
            {
                _seq.Append(ch);
            }

            return;
        }

        // A control character inside the sequence aborts it as malformed.
        _seq.Clear();
        _mode = Mode.Text;
    }
```

Update `Reset()` to clear `_seq`, and `HasPendingContent` already covers the new modes because it tests `_mode != Mode.Text`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/MxpParserTests/*" </dev/null`
Expected: PASS, including every pre-existing test in the class.

- [ ] **Step 5: Correct the class header**

`MxpParser`'s XML doc currently says *"Raw ANSI SGR is assumed to have been handled upstream. An ESC byte (0x1b) seen here is passed through untouched into the span text."* That is now false in both halves. Replace that list item with:

```csharp
/// <item>ANSI SGR is decoded here, through <see cref="SharpMUTerm.Core.Text.SgrCodes"/>, because the
/// spec permits ANSI inside MXP and nothing upstream strips it — a session runs this parser
/// <em>or</em> <see cref="SharpMUTerm.Core.Text.AnsiParser"/>, never both. Other CSI sequences are
/// consumed and discarded.</item>
```

- [ ] **Step 6: Commit**

```bash
git add src/SharpMUTerm.Core/Protocols/MxpParser.cs tests/SharpMUTerm.Core.Tests/Protocols/MxpParserTests.cs
git commit -m "fix(mxp): decode ANSI escapes instead of printing them"
```

---

### Task 3: Line-security modes

The security task. Everything before this is groundwork.

**Files:**
- Create: `src/SharpMUTerm.Core/Protocols/MxpLineMode.cs`
- Create: `src/SharpMUTerm.Core/Protocols/MxpTagCategory.cs`
- Modify: `src/SharpMUTerm.Core/Protocols/MxpParser.cs`
- Test: `tests/SharpMUTerm.Core.Tests/Protocols/MxpLineModeTests.cs`

**Interfaces:**
- Consumes: the `Mode.Csi` handler from Task 2 (the `'z'` final byte lands there).
- Produces:
  - `public enum MxpLineMode { Open, Secure, Locked }`
  - `public static class MxpTagCategory` with `public static bool IsOpen(string tagName)` — case-insensitive, takes the bare element name with no `<`, `>` or leading `/`.

- [ ] **Step 1: Write the failing tests**

Create `tests/SharpMUTerm.Core.Tests/Protocols/MxpLineModeTests.cs`:

```csharp
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>
/// MXP's line-security model. The spec's rationale is the whole point of this file: "players on a
/// MUD can exploit this power and cause problems… you would not want to allow them to… execute
/// script commands on the client of other users." Without it, a SEND tag typed by another player
/// into a public channel becomes a clickable command in this client.
/// </summary>
public class MxpLineModeTests
{
    private const string Open = "\x1b[0z";
    private const string Secure = "\x1b[1z";
    private const string Locked = "\x1b[2z";
    private const string Reset = "\x1b[3z";
    private const string TempSecure = "\x1b[4z";
    private const string LockOpen = "\x1b[5z";
    private const string LockSecure = "\x1b[6z";
    private const string LockLocked = "\x1b[7z";

    /// <summary>The attack the mode system exists to stop.</summary>
    [Test]
    public async Task SecureTagOnAnOpenLine_IsShownAsTextAndIsNotClickable()
    {
        var parser = new MxpParser();

        var line = parser.Feed("Rivane says, '<SEND href=\"@shutdown\">click</SEND>'\n")[0];

        await Assert.That(line.Text).Contains("<SEND href=\"@shutdown\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    [Test]
    public async Task OpenTagOnAnOpenLine_IsHonoured()
    {
        var parser = new MxpParser();

        var line = parser.Feed("plain <B>bold</B>\n")[0];

        await Assert.That(line.Text).IsEqualTo("plain bold");
        await Assert.That(line.Spans[^1].Style.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
    }

    [Test]
    public async Task SecureTagOnASecureLine_IsHonoured()
    {
        var parser = new MxpParser();

        var line = parser.Feed(Secure + "<SEND href=\"look\">look</SEND>\n")[0];

        await Assert.That(line.Text).IsEqualTo("look");
        await Assert.That(line.Spans[0].IsInteractive).IsTrue();
    }

    [Test]
    public async Task LockedLine_ParsesNothingAtAll()
    {
        var parser = new MxpParser();

        var line = parser.Feed(Locked + "<B>not bold</B> &amp; not an entity\n")[0];

        await Assert.That(line.Text).IsEqualTo("<B>not bold</B> &amp; not an entity");
    }

    /// <summary>Spec: OPEN, SECURE and LOCKED all revert "when a newline is received".</summary>
    [Test]
    public async Task ModeRevertsToTheDefaultAtTheNextNewline()
    {
        var parser = new MxpParser();

        var lines = parser.Feed(Secure + "<SEND href=\"look\">a</SEND>\n<SEND href=\"look\">b</SEND>\n");

        await Assert.That(lines[0].Spans[0].IsInteractive).IsTrue();
        await Assert.That(lines[1].Text).Contains("<SEND");
    }

    /// <summary>Spec: LOCK SECURE makes "Secure mode … the new default mode".</summary>
    [Test]
    public async Task LockSecure_SurvivesTheNewline()
    {
        var parser = new MxpParser();

        var lines = parser.Feed(LockSecure + "<SEND href=\"look\">a</SEND>\n<SEND href=\"look\">b</SEND>\n");

        await Assert.That(lines[0].Spans[0].IsInteractive).IsTrue();
        await Assert.That(lines[1].Spans[0].IsInteractive).IsTrue();
    }

    /// <summary>Spec: TEMP SECURE sets "secure mode for the next tag only".</summary>
    [Test]
    public async Task TempSecure_CoversExactlyOneTag()
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + "<SEND href=\"look\">a</SEND><SEND href=\"x\">b</SEND>\n")[0];

        await Assert.That(line.Spans[0].IsInteractive).IsTrue();
        await Assert.That(line.Text).Contains("<SEND href=\"x\">");
    }

    /// <summary>Spec: RESET closes open tags, returns to Open, and resets the style.</summary>
    [Test]
    public async Task Reset_ReturnsToOpenAndClearsStyle()
    {
        var parser = new MxpParser();

        var line = parser.Feed(LockSecure + "<B>bold" + Reset + "after<SEND href=\"x\">c</SEND>\n")[0];

        await Assert.That(line.Spans[^1].Style.Attributes.HasFlag(TextAttributes.Bold)).IsFalse();
        await Assert.That(line.Text).Contains("<SEND href=\"x\">");
    }

    /// <summary>
    /// The real prompt from tdome.nukefire.org, byte for byte: the server secures each of its own
    /// tags and locks the content between them so a player-supplied name cannot inject one.
    /// </summary>
    [Test]
    public async Task NukeFirePrompt_ParsesIntoTwoClickableAnswers()
    {
        var parser = new MxpParser();

        parser.Feed(
            "at right, Pemberton ("
            + Secure + "<send>" + LockLocked + "Y" + Secure + "</send>" + LockLocked + "/"
            + Secure + "<send>" + LockLocked + "N" + Secure + "</send>" + LockLocked + ")?");
        var line = parser.Flush()!;

        await Assert.That(line.Text).IsEqualTo("at right, Pemberton (Y/N)?");
        await Assert.That(line.Spans.Count(s => s.IsInteractive)).IsEqualTo(2);
    }

    [Test]
    public async Task LockOpen_MakesOpenTheDefaultAgain()
    {
        var parser = new MxpParser();

        var lines = parser.Feed(LockSecure + "a\n" + LockOpen + "<SEND href=\"x\">b</SEND>\n");

        await Assert.That(lines[1].Text).Contains("<SEND");
    }

    [Test]
    public async Task ClosingTagTakesTheCategoryOfItsElement()
    {
        var parser = new MxpParser();

        var line = parser.Feed("</SEND> and </B>\n")[0];

        await Assert.That(line.Text).IsEqualTo("</SEND> and ");
    }
}
```

- [ ] **Step 2: Run them to make sure they fail**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/MxpLineModeTests/*" </dev/null`
Expected: build failure until `MxpLineMode` exists; once it does, `SecureTagOnAnOpenLine_IsShownAsTextAndIsNotClickable` FAILs with the span reported as interactive — that failure is the vulnerability, reproduced.

- [ ] **Step 3: Write the mode enum and the mapping**

Create `src/SharpMUTerm.Core/Protocols/MxpLineMode.cs`:

```csharp
namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// How much of MXP a line is allowed to use, per the specification's line-security model.
/// </summary>
public enum MxpLineMode
{
    /// <summary>Only tags in the open category are honoured. The default at connection start.</summary>
    Open,

    /// <summary>Every tag is honoured.</summary>
    Secure,

    /// <summary>Nothing is parsed. Every character is literal text.</summary>
    Locked,
}
```

Create `src/SharpMUTerm.Core/Protocols/MxpTagCategory.cs`:

```csharp
namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// Which MXP tags may appear on an open line.
/// </summary>
/// <remarks>
/// The spec draws the line in one sentence — "Only the tags described in this section are OPEN tags.
/// All other MXP tags are SECURE tags" — so this is an allow-list and must stay one. A deny-list
/// would make every tag added to the spec in future secure-by-omission in the wrong direction.
/// </remarks>
public static class MxpTagCategory
{
    private static readonly HashSet<string> OpenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "B", "BOLD", "STRONG",
        "I", "ITALIC", "EM",
        "U", "UNDERLINE",
        "S", "STRIKEOUT",
        "C", "COLOR",
        "H", "HIGH",
        "FONT",
    };

    /// <summary>
    /// True when <paramref name="tagName"/> is an open tag.
    /// </summary>
    /// <param name="tagName">
    /// The bare element name: no angle brackets, no attributes, and no leading slash. A closing tag
    /// takes the category of the element it closes, so the caller strips the slash first.
    /// </param>
    public static bool IsOpen(string tagName) => OpenTags.Contains(tagName);
}
```

- [ ] **Step 4: Wire the mode into `MxpParser`**

Add the state, defaulting to Open:

```csharp
    private MxpLineMode _defaultMode = MxpLineMode.Open;
    private MxpLineMode _lineMode = MxpLineMode.Open;
    private bool _tempSecure;
```

In `ProcessCsi`'s final-byte branch, handle `'z'` before the discard:

```csharp
            if (ch == 'z')
            {
                ApplyLineTag(_seq.ToString());
                _seq.Clear();
                _mode = Mode.Text;
                return;
            }
```

```csharp
    /// <summary>
    /// Applies an <c>ESC[#z</c> line tag. An unparseable or unknown number is ignored rather than
    /// guessed at: a mode this client invents is a mode the server did not ask for, and the
    /// consequences run in the direction of trusting text that was not meant to be trusted.
    /// </summary>
    private void ApplyLineTag(string parameters)
    {
        if (!int.TryParse(parameters, out var tag))
        {
            return;
        }

        switch (tag)
        {
            case 0: _lineMode = MxpLineMode.Open; break;
            case 1: _lineMode = MxpLineMode.Secure; break;
            case 2: _lineMode = MxpLineMode.Locked; break;
            case 3: ApplyReset(); break;
            case 4: _tempSecure = true; break;
            case 5: _defaultMode = _lineMode = MxpLineMode.Open; break;
            case 6: _defaultMode = _lineMode = MxpLineMode.Secure; break;
            case 7: _defaultMode = _lineMode = MxpLineMode.Locked; break;
        }
    }

    /// <summary>Spec: "close all open tags. Set mode to Open. Set text color and properties to default."</summary>
    private void ApplyReset()
    {
        FlushRun();
        CloseInteractionsAtBoundary();
        _stack.Clear();
        _current = TextStyle.Default;
        _interaction = null;
        _tempSecure = false;
        _defaultMode = _lineMode = MxpLineMode.Open;
    }
```

In `CompleteLine`, revert the line mode after the line is emitted — the spec's "when a newline is received from the MUD, the mode reverts back to the Default mode":

```csharp
        _lineMode = _defaultMode;
        _tempSecure = false;
```

- [ ] **Step 5: Gate parsing on the mode**

Two gates. In `ProcessText`, a locked line takes no special characters at all:

```csharp
    private void ProcessText(char ch)
    {
        // A locked line is not parsed: "no MXP or HTML commands are allowed in the line. The line is
        // not parsed for any tags at all." Newline still ends the line, or nothing ever would.
        if (_lineMode == MxpLineMode.Locked && ch != '\n' && ch != '\r' && ch != '\x1b')
        {
            _run.Append(ch);
            return;
        }

        // ... existing switch
    }
```

Note the `\x1b` exemption: a locked line must still be able to *leave* locked mode, and `ESC[Nz` is how a server does it — the NukeFire prompt does exactly this between every tag.

In `HandleOpener` and the closing-tag path, gate on the category. The gate belongs at the one point where a completed tag is about to be acted on, so no handler needs to know about modes:

```csharp
    /// <summary>
    /// Whether a tag may act on this line, per the mode. A tag that may not is written out as the
    /// literal text it arrived as, so an injection attempt is visible to the player rather than
    /// silently swallowed.
    /// </summary>
    private bool TagIsAllowed(string name)
    {
        if (_lineMode == MxpLineMode.Secure || _tempSecure)
        {
            _tempSecure = false;
            return true;
        }

        return MxpTagCategory.IsOpen(name);
    }
```

At the point where the parser has a complete tag body and is about to dispatch it, reconstruct and emit literally when the gate refuses:

```csharp
        var bare = name.TrimStart('/');
        if (!TagIsAllowed(bare))
        {
            _run.Append('<').Append(raw).Append('>');
            return;
        }
```

`raw` is the exact tag body as it arrived, which the parser already accumulates in `_tag` — do not re-serialise from the parsed name and attributes, or a player learns they can smuggle characters through the round trip.

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/MxpLineModeTests/*" </dev/null
dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/MxpParserTests/*" </dev/null
```
Expected: both PASS.

**Pre-existing `MxpParserTests` will need review, not blind fixing.** Several were written against the "every tag is processed" assumption and feed `<SEND>` with no mode tag. For each one that now fails, decide deliberately: if it is testing SEND's *behaviour*, prefix the input with `\x1b[1z` and say so in a comment; if it is asserting that an unsecured SEND is honoured, it was encoding the bug and should be inverted.

- [ ] **Step 7: Update the class header**

Replace the scope note that says the security state machine is not implemented:

```csharp
/// <item>The line-security model (<c>ESC[#z</c> line tags, open/secure/locked, RESET, TEMP SECURE
/// and the three LOCK modes) <b>is</b> implemented — see <see cref="MxpLineMode"/>. A secure tag on
/// an open line is emitted as literal text rather than honoured, which is what stops a player's
/// <c>&lt;SEND&gt;</c> becoming a clickable command in someone else's client.</item>
```

- [ ] **Step 8: Commit**

```bash
git add src/SharpMUTerm.Core/Protocols/ tests/SharpMUTerm.Core.Tests/Protocols/
git commit -m "fix(mxp): obey the line-security model"
```

---

### Task 4: Let negotiation choose the parser

**Files:**
- Modify: `src/SharpMUTerm.Core/Telnet/TelnetEvents.cs`
- Modify: `src/SharpMUTerm.Core/Telnet/TelnetSession.cs:411` (the `onMXPEnabled` no-op)
- Modify: `src/SharpMUTerm.Core/Telnet/ITelnetSession.cs`
- Modify: `src/SharpMUTerm.Core/Session/WorldSession.cs:84,139-144`
- Test: `tests/SharpMUTerm.Core.Tests/Session/WorldSessionTests.cs`, `tests/SharpMUTerm.Core.Tests/Session/FakeTelnetSession.cs`

**Interfaces:**
- Consumes: `MxpParser` from Tasks 2–3.
- Produces:
  - `ITelnetSession.MxpEnabled` — `event EventHandler? MxpEnabled`.
  - `WorldSession` swaps `_parser` on that event when `World.ContentFormat` is `Ansi`.

- [ ] **Step 1: Write the failing test**

Add to `WorldSessionTests`:

```csharp
    /// <summary>
    /// MXP is a negotiated telnet option, so the client learns it is in force from the wire and not
    /// from a config field a user has to know to set. NukeFire sent IAC WILL MXP, this client
    /// answered DO, and then parsed the stream with AnsiParser anyway — which is why its prompt
    /// showed a literal "<send>Y</send>".
    /// </summary>
    [Test]
    public async Task Mxp_NegotiationSwitchesTheParser()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();

        telnet.EmitLine("<B>before</B>");
        telnet.RaiseMxpEnabled();
        telnet.EmitLine("<B>after</B>");

        var lines = session.Scrollback.Snapshot();
        await Assert.That(lines.Any(l => l.Text == "<B>before</B>")).IsTrue();
        await Assert.That(lines.Any(l => l.Text == "after")).IsTrue();
    }

    /// <summary>
    /// A world explicitly set to Pueblo is a user's decision about a server that speaks a different
    /// markup, and a stray MXP negotiation must not overrule it.
    /// </summary>
    [Test]
    public async Task Mxp_NegotiationDoesNotOverrideAnExplicitContentFormat()
    {
        var world = World();
        world.ContentFormat = ContentFormat.Pueblo;
        var (session, telnet) = Create(world);
        await session.ConnectAsync();

        telnet.RaiseMxpEnabled();
        telnet.EmitLine("<B>after</B>");

        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "after")).IsFalse();
    }
```

Add the seam to `FakeTelnetSession`:

```csharp
    public event EventHandler? MxpEnabled;

    public void RaiseMxpEnabled() => MxpEnabled?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/WorldSessionTests/*" </dev/null`
Expected: build failure — `ITelnetSession` has no `MxpEnabled`.

- [ ] **Step 3: Surface the event from `TelnetSession`**

Add to `ITelnetSession`:

```csharp
    /// <summary>
    /// Raised once the peer has negotiated MXP (RFC-less option 91). A consumer uses this to decide
    /// how to parse the stream: MXP is a property of the connection, not of the user's configuration.
    /// </summary>
    event EventHandler? MxpEnabled;
```

In `TelnetSession`, replace the no-op callback:

```csharp
                onMXPEnabled: OnMxpEnabledAsync,
```

```csharp
    public event EventHandler? MxpEnabled;

    private ValueTask OnMxpEnabledAsync()
    {
        _logger.LogInformation("MXP negotiated.");
        MxpEnabled?.Invoke(this, EventArgs.Empty);
        return ValueTask.CompletedTask;
    }
```

- [ ] **Step 4: Swap the parser in `WorldSession`**

`_parser` is `readonly`; drop that. Subscribe beside the other telnet handlers (near `telnet.OutputReceived += OnOutputReceived;`):

```csharp
        telnet.MxpEnabled += OnMxpEnabled;
```

```csharp
    /// <summary>
    /// Upgrades to the MXP parser when the option negotiates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only from <see cref="ContentFormat.Ansi"/>, which is the default and therefore means "nobody
    /// chose" rather than "somebody chose ANSI". A world explicitly set to Pueblo has been configured
    /// by a user who knows what that server speaks, and a negotiation must not overrule them.
    /// </para>
    /// <para>
    /// The old parser is flushed first, so a partial line already buffered is delivered under the
    /// rules it arrived under rather than being re-read as markup by a parser that never saw its
    /// beginning. Style does not carry across: MXP's own RESET is how a server re-establishes it, and
    /// inventing a carry-over would make the first line after negotiation depend on parser internals.
    /// </para>
    /// </remarks>
    private void OnMxpEnabled(object? sender, EventArgs e)
    {
        if (World.ContentFormat != ContentFormat.Ansi || _parser is MxpParser)
        {
            return;
        }

        if (_parser.Flush() is { } tail)
        {
            ProcessOutputLine(tail);
        }

        _parser = new MxpParser();
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/WorldSessionTests/*" </dev/null`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SharpMUTerm.Core/Telnet/ src/SharpMUTerm.Core/Session/WorldSession.cs tests/SharpMUTerm.Core.Tests/Session/
git commit -m "fix(mxp): let the negotiated option choose the parser"
```

---

### Task 5: Answer `<VERSION>` and `<SUPPORT>`

**Files:**
- Modify: `src/SharpMUTerm.Core/Protocols/MxpParser.cs`
- Modify: `src/SharpMUTerm.Core/Session/WorldSession.cs`
- Test: `tests/SharpMUTerm.Core.Tests/Protocols/MxpParserTests.cs`

**Interfaces:**
- Consumes: `MxpTagCategory` and the mode gate from Task 3 — both tags are secure, so both are already refused on an open line, which is correct and must stay.
- Produces: `MxpParser.ClientReply` — `event EventHandler<string>? ClientReply`, carrying the exact line to send to the server (no trailing newline; the caller's `SendLineAsync` adds the terminator).

- [ ] **Step 1: Write the failing test**

```csharp
    /// <summary>
    /// Spec: the client "sends the version information back to the MUD in the format of a SECURE
    /// &lt;VERSION&gt; MXP tag". A server that asks and gets nothing has to guess what we support.
    /// </summary>
    [Test]
    public async Task Version_IsAnswered()
    {
        var parser = new MxpParser();
        var replies = new List<string>();
        parser.ClientReply += (_, r) => replies.Add(r);

        parser.Feed("\x1b[1z<VERSION>\n");

        await Assert.That(replies).HasCount(1);
        await Assert.That(replies[0]).StartsWith("<VERSION ");
        await Assert.That(replies[0]).Contains("CLIENT=SharpMUTerm");
    }

    /// <summary>A VERSION request on an open line is a player's, not the server's, and is refused.</summary>
    [Test]
    public async Task Version_OnAnOpenLine_IsNotAnswered()
    {
        var parser = new MxpParser();
        var replies = new List<string>();
        parser.ClientReply += (_, r) => replies.Add(r);

        parser.Feed("<VERSION>\n");

        await Assert.That(replies).IsEmpty();
    }

    /// <summary>Spec: the client returns a SECURE &lt;SUPPORTS&gt; tag naming what it implements.</summary>
    [Test]
    public async Task Support_IsAnsweredWithTheTagsThisParserImplements()
    {
        var parser = new MxpParser();
        var replies = new List<string>();
        parser.ClientReply += (_, r) => replies.Add(r);

        parser.Feed("\x1b[1z<SUPPORT>\n");

        await Assert.That(replies).HasCount(1);
        await Assert.That(replies[0]).StartsWith("<SUPPORTS ");
        await Assert.That(replies[0]).Contains("+send");
        await Assert.That(replies[0]).Contains("+color");
    }
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/MxpParserTests/*" </dev/null`
Expected: build failure — `ClientReply` does not exist.

- [ ] **Step 3: Implement the two handlers**

```csharp
    /// <summary>
    /// A line this parser owes the server — the answer to a <c>&lt;VERSION&gt;</c> or
    /// <c>&lt;SUPPORT&gt;</c> request. An event rather than a return value because it is not output:
    /// nothing about it belongs in the scrollback, and the session sends it as a line.
    /// </summary>
    public event EventHandler<string>? ClientReply;

    /// <summary>What this client answers a VERSION request with.</summary>
    private const string ClientName = "SharpMUTerm";

    /// <summary>
    /// The tags this parser genuinely implements, in the spec's <c>+tag</c> form.
    /// </summary>
    /// <remarks>
    /// An honest list, and deliberately not an aspirational one: a SUPPORTS answer is a claim a
    /// server acts on, so naming a tag we ignore makes it send markup we will render as text. Add to
    /// this only when the handler exists — the same rule the MTTS bit vector is held to.
    /// </remarks>
    private static readonly string[] SupportedTags =
        ["+b", "+i", "+u", "+s", "+color", "+font", "+high", "+send", "+a", "+br"];

    private void HandleVersionRequest()
    {
        ClientReply?.Invoke(this, $"<VERSION CLIENT={ClientName} VERSION=1 MXP=1.0>");
    }

    private void HandleSupportRequest()
    {
        ClientReply?.Invoke(this, $"<SUPPORTS {string.Join(' ', SupportedTags)}>");
    }
```

Dispatch them from `HandleOpener`, after the mode gate has already run:

```csharp
            case "VERSION":
                HandleVersionRequest();
                break;

            case "SUPPORT":
                HandleSupportRequest();
                break;
```

- [ ] **Step 4: Send the replies from `WorldSession`**

Where the parser is constructed — both in `CreateParser` and in `OnMxpEnabled` from Task 4 — subscribe. Extract one helper so the two construction sites cannot diverge:

```csharp
    private ILineParser NewParser(ContentFormat format)
    {
        var parser = CreateParser(format);
        if (parser is MxpParser mxp)
        {
            mxp.ClientReply += (_, reply) => _ = SendRawAsync(reply);
        }

        return parser;
    }
```

`WorldSession.SendRawAsync(string command, CancellationToken cancellationToken = default)` at `WorldSession.cs:581` is the existing path a command takes to the wire — use it rather than adding a second one. It is deliberately not `SendUserInputAsync` (`:418`): that one echoes, records a draft and runs the alias engine, none of which a protocol reply is.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run -c Release --project tests/SharpMUTerm.Core.Tests --treenode-filter "/*/*/MxpParserTests/*" </dev/null`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SharpMUTerm.Core/ tests/SharpMUTerm.Core.Tests/
git commit -m "feat(mxp): answer VERSION and SUPPORT requests"
```

---

### Task 6: Document the model and verify the whole client

**Files:**
- Modify: `CLAUDE.md` (the *Other dependency notes* area, beside the prompt-marker entry)
- Test: all five suites

- [ ] **Step 1: Run every suite**

```bash
dotnet build SharpMUTerm.slnx
for p in Core Tui Graphics Scripting Web; do
  dotnet run -c Release --project tests/SharpMUTerm.$p.Tests </dev/null
done
```
Expected: all green, no warnings.

- [ ] **Step 2: Render a frame with MXP content**

The `--view prompt` snapshot added with the prompt row uses the NukeFire prompt text. With Task 3 done it should render `at right, Pemberton (Y/N)?` with `Y` and `N` as clickable spans and no literal `<send>` anywhere:

```bash
dotnet build -c Release SharpMUTerm.slnx
dotnet run -c Release --project src/SharpMUTerm.Tui --no-build -- \
  --snapshot --demo-config --view prompt --size 120x32 --out frame.ansi
python3 tools/ansi_frame_to_image.py frame.ansi frame.html
```
Look at the frame. A claim about what the screen shows is not backed by reading the markup.

- [ ] **Step 3: Write the CLAUDE.md entry**

Record, in the register of the surrounding entries: that MXP's line modes are the security boundary and why an allow-list; that ANSI is decoded inside MXP because nothing chains the two parsers; that negotiation and not `ContentFormat` decides the parser, and that an explicit non-Ansi format still wins; and that `SUPPORTS` is an honest list held to the same rule as the MTTS bit vector.

- [ ] **Step 4: Commit and open the PR**

```bash
git add CLAUDE.md
git commit -m "docs: record the MXP line-security model"
git push -u origin <branch>
gh pr create --title "fix: parse MXP the way the spec defines it" --body "..."
```

---

## Self-Review

**Spec coverage.** Line modes 0–7 → Task 3 (each has a named test). Open/secure categories → Task 3, allow-list from the spec's own sentence. "ANSI can still be used as normal" → Task 2. `<VERSION>`/`<SUPPORT>` → Task 5. Entities → **not covered**: the spec's `&#nnn;` rule ("values less than 32 are ignored") was never checked against `ProcessEntityChar`, and this plan does not touch it. That is a deliberate gap, not an oversight — it needs its own reading of the entity code first, and it is not a security property.

**Type consistency.** `MxpLineMode` and `MxpTagCategory.IsOpen(string)` are defined in Task 3 and used only there and in Task 5. `SgrCodes.Apply(TextStyle, string)` is defined in Task 1 and used in Task 2. `ITelnetSession.MxpEnabled` is defined in Task 4 and used only there. `MxpParser.ClientReply` is defined in Task 5 and consumed in Task 5.

**Known soft spot.** Task 3 Step 5 says to emit the tag's `raw` body when the gate refuses. `MxpParser` accumulates the tag in `_tag`, but the implementer must confirm that buffer still holds the exact bytes at the dispatch point rather than a normalised form — if it does not, capture the raw text at the point `<` was seen. Getting this wrong is a smuggling hole, so verify it with a test that feeds mixed-case attributes and whitespace and asserts they come back byte-identical.
