using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The command line's key table, driven through the very method the framework calls. The pair that
/// matters most is ⏎ against the newline chord: one sends and the other must not, and a client that
/// got those the wrong way round would send half a pose every time somebody reached for a second line.
/// </summary>
public class InputBarKeyTests
{
    private static InputBarControl Bar()
    {
        var bar = new InputBarControl { Width = 60 };
        return bar;
    }

    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private static ConsoleKeyInfo Chord(ConsoleKey key, bool ctrl = false, bool shift = false) =>
        new('\0', key, shift, false, ctrl);

    private static void Type(InputBarControl bar, string text)
    {
        foreach (var c in text)
        {
            bar.ProcessKey(Key(c));
        }
    }

    [Test]
    public async Task Enter_SendsTheLineAndEmptiesTheBar()
    {
        var bar = Bar();
        string? sent = null;
        bar.Entered += text => sent = text;

        Type(bar, "look");
        bar.ProcessKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        await Assert.That(sent).IsEqualTo("look");
        await Assert.That(bar.Text).IsEqualTo(string.Empty);
    }

    /// <summary>
    /// Ctrl+L is the newline chord this host can actually deliver, and it must not send. Shift+⏎ and
    /// Ctrl+⏎ do the same where a host reports them (Windows' <c>Console.ReadKey</c> does; the Unix
    /// parser cannot, having no CSI-u) — all three are asserted as "no send, one more line".
    /// </summary>
    [Test]
    [Arguments(ConsoleKey.L, true, false)]
    [Arguments(ConsoleKey.Enter, true, false)]
    [Arguments(ConsoleKey.Enter, false, true)]
    public async Task TheNewlineChords_BreakTheLineAndSendNothing(ConsoleKey key, bool ctrl, bool shift)
    {
        var bar = Bar();
        var sends = 0;
        bar.Entered += _ => sends++;

        Type(bar, "first");
        bar.ProcessKey(Chord(key, ctrl, shift));
        Type(bar, "second");

        await Assert.That(sends).IsEqualTo(0);
        await Assert.That(bar.Text).IsEqualTo("first\nsecond");
    }

    /// <summary>
    /// And the whole multiline line goes out on one ⏎ — the newlines are the payload, not a reason to
    /// send twice.
    /// </summary>
    [Test]
    public async Task AMultilineDraft_IsSentWholeOnOneEnter()
    {
        var bar = Bar();
        string? sent = null;
        bar.Entered += text => sent = text;

        Type(bar, "pose ");
        bar.ProcessKey(Chord(ConsoleKey.L, ctrl: true));
        Type(bar, "smiles.");
        bar.ProcessKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        await Assert.That(sent).IsEqualTo("pose \nsmiles.");
    }

    [Test]
    public async Task Typing_RaisesTheChangeThatRecordsTheDraft()
    {
        var bar = Bar();
        var last = string.Empty;
        bar.Changed += text => last = text;

        Type(bar, "say hi");

        await Assert.That(last).IsEqualTo("say hi");
    }

    /// <summary>A caret move is not an edit: it must not re-record a draft or disturb history recall.</summary>
    [Test]
    public async Task MovingTheCaret_RaisesNoChange()
    {
        var bar = Bar();
        Type(bar, "say hi");
        var changes = 0;
        bar.Changed += _ => changes++;

        bar.ProcessKey(Chord(ConsoleKey.LeftArrow));
        bar.ProcessKey(Chord(ConsoleKey.Home));
        bar.ProcessKey(Chord(ConsoleKey.End));

        await Assert.That(changes).IsEqualTo(0);
    }

    /// <summary>
    /// The bar grows as its text wraps, from the configured floor to the configured cap, and stops
    /// there — past the cap it scrolls inside itself instead of eating the output window.
    /// </summary>
    [Test]
    public async Task TheBar_GrowsWithWhatIsTypedAndStopsAtItsCap()
    {
        var bar = new InputBarControl { Width = 20, MinRows = 3, MaxRows = 5 };

        await Assert.That(bar.Rows()).IsEqualTo(3);

        Type(bar, new string('x', 60)); // 3 rows of 20
        await Assert.That(bar.Rows()).IsEqualTo(4); // 3 full rows + the caret's own

        Type(bar, new string('y', 200));
        await Assert.That(bar.Rows()).IsEqualTo(5);
    }

    /// <summary>Alt chords belong to the app's bindings; typing their letter into the line is not the answer.</summary>
    [Test]
    public async Task AnAltChord_IsNotTyping()
    {
        var bar = Bar();

        await Assert.That(bar.ProcessKey(new ConsoleKeyInfo('n', ConsoleKey.N, false, true, false))).IsFalse();
        await Assert.That(bar.Text).IsEqualTo(string.Empty);
    }

    /// <summary>⇥ only belongs to the bar when there is another bar to hand the caret to.</summary>
    [Test]
    public async Task Tab_IsOnlyTheBarsWhenASecondBarIsUp()
    {
        var bar = Bar();

        await Assert.That(bar.WantsTabKey).IsFalse();
        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.Tab))).IsFalse();

        var cycles = 0;
        bar.HasSibling = () => true;
        bar.CycleRequested += () => cycles++;

        await Assert.That(bar.WantsTabKey).IsTrue();
        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.Tab))).IsTrue();
        await Assert.That(cycles).IsEqualTo(1);
    }

    /// <summary>A pasted paragraph keeps its line breaks — this bar has rows for them.</summary>
    [Test]
    public async Task Paste_KeepsNewlines()
    {
        var bar = Bar();

        bar.Paste("one\r\ntwo\rthree");

        await Assert.That(bar.Text).IsEqualTo("one\ntwo\nthree");
    }

    [Test]
    public async Task ShiftArrowsAndShiftHomeSelect()
    {
        var bar = Bar();
        Type(bar, "say hello");

        bar.ProcessKey(Chord(ConsoleKey.LeftArrow, shift: true));
        await Assert.That(bar.GetSelectedText()).IsEqualTo("o");

        bar.ProcessKey(Chord(ConsoleKey.Home, shift: true));
        await Assert.That(bar.HasSelection).IsTrue();
        await Assert.That(bar.GetSelectedText()).IsEqualTo("say hello");
    }

    [Test]
    public async Task TypingOverASelectionReplacesItAndReportsTheEdit()
    {
        var bar = Bar();
        Type(bar, "say hello");
        string? changed = null;
        bar.Changed += text => changed = text;

        bar.ProcessKey(Chord(ConsoleKey.Home, shift: true));
        Type(bar, "x");

        await Assert.That(bar.Text).IsEqualTo("x");
        await Assert.That(changed).IsEqualTo("x");
    }

    [Test]
    public async Task ABareArrowDropsTheSelection()
    {
        var bar = Bar();
        Type(bar, "look");
        bar.ProcessKey(Chord(ConsoleKey.LeftArrow, shift: true));

        bar.ProcessKey(Chord(ConsoleKey.LeftArrow));

        await Assert.That(bar.HasSelection).IsFalse();
        await Assert.That(bar.Text).IsEqualTo("look");
    }

    /// <summary>
    /// Shift alone, exactly. Alt+Shift+← is the pane resize, and a selection arm that took it would be the
    /// swallowed-chord defect this key table has paid for before.
    /// </summary>
    [Test]
    public async Task AltShiftArrowIsStillDeclined()
    {
        var bar = Bar();
        Type(bar, "look");

        var claimed = bar.ProcessKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, true, true, false));

        await Assert.That(claimed).IsFalse();
        await Assert.That(bar.HasSelection).IsFalse();
    }

    [Test]
    public async Task ClearSelectionFromTheWindowDropsIt()
    {
        var bar = Bar();
        Type(bar, "look");
        bar.ProcessKey(Chord(ConsoleKey.Home, shift: true));

        bar.ClearSelection();

        await Assert.That(bar.HasSelection).IsFalse();
        await Assert.That(bar.Text).IsEqualTo("look");
    }

    // ---- what the editor underneath brings, and what it is not allowed to -------------------------

    /// <summary>⌃Z takes back the last edit and ⌃Y puts it back — the editor's undo, reached from the bar.</summary>
    [Test]
    public async Task CtrlZUndoesAndCtrlYRedoes()
    {
        var bar = Bar();
        Type(bar, "look");
        bar.ProcessKey(Chord(ConsoleKey.Backspace));
        await Assert.That(bar.Text).IsEqualTo("loo");

        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.Z, ctrl: true))).IsTrue();
        await Assert.That(bar.Text).IsEqualTo("look");

        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.Y, ctrl: true))).IsTrue();
        await Assert.That(bar.Text).IsEqualTo("loo");
    }

    /// <summary>⌃K and ⌃U kill to either end of the whole buffer, across rows, and ⌃Z brings it back.</summary>
    [Test]
    public async Task TheKillsReachAcrossLinesAndCanBeUndone()
    {
        var bar = Bar();
        Type(bar, "one");
        bar.ProcessKey(Chord(ConsoleKey.L, ctrl: true));
        Type(bar, "two");
        bar.ProcessKey(Chord(ConsoleKey.UpArrow));
        bar.ProcessKey(Chord(ConsoleKey.Home));
        bar.ProcessKey(Chord(ConsoleKey.RightArrow));

        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.K, ctrl: true))).IsTrue();
        await Assert.That(bar.Text).IsEqualTo("o");

        bar.ProcessKey(Chord(ConsoleKey.Z, ctrl: true));
        await Assert.That(bar.Text).IsEqualTo("one\ntwo");

        bar.ProcessKey(Chord(ConsoleKey.E, ctrl: true));
        bar.ProcessKey(Chord(ConsoleKey.LeftArrow));
        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.U, ctrl: true))).IsTrue();
        await Assert.That(bar.Text).IsEqualTo("o");
    }

    /// <summary>
    /// The editor's own Ctrl table is a document's: ⌃C writes the system clipboard directly, ⌃D duplicates
    /// a line. Neither may reach it — ⌃C is the app's, through the selection owner, and ⌃D means nothing
    /// on a command line — so each is declined and the text is left alone.
    /// </summary>
    [Test]
    [Arguments(ConsoleKey.C)]
    [Arguments(ConsoleKey.V)]
    [Arguments(ConsoleKey.D)]
    public async Task TheEditorsDocumentChordsAreDeclined(ConsoleKey key)
    {
        var bar = Bar();
        Type(bar, "look");
        bar.ProcessKey(Chord(ConsoleKey.LeftArrow, shift: true));

        await Assert.That(bar.ProcessKey(Chord(key, ctrl: true))).IsFalse();
        await Assert.That(bar.Text).IsEqualTo("look");
    }

    /// <summary>⌃A selects the whole buffer, every row of it, and typing then replaces it.</summary>
    [Test]
    public async Task CtrlASelectsEverything()
    {
        var bar = Bar();
        Type(bar, "one");
        bar.ProcessKey(Chord(ConsoleKey.L, ctrl: true));
        Type(bar, "two");

        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.A, ctrl: true))).IsTrue();
        await Assert.That(bar.GetSelectedText().ReplaceLineEndings("\n")).IsEqualTo("one\ntwo");
        Type(bar, ">");
        await Assert.That(bar.Text).IsEqualTo(">");
    }

    /// <summary>
    /// ⌃X hands the selection over and deletes it once it is taken, as one undo step. Refused, it deletes
    /// nothing: a cut that reaches no clipboard is a delete.
    /// </summary>
    [Test]
    public async Task CtrlXCutsTheSelectionOnlyIfItWasTaken()
    {
        var bar = Bar();
        var taken = new List<string>();
        var accept = false;
        bar.CutTo = text =>
        {
            taken.Add(text);
            return accept;
        };
        Type(bar, "say hello");
        foreach (var _ in "hello")
        {
            bar.ProcessKey(Chord(ConsoleKey.LeftArrow, shift: true));
        }

        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.X, ctrl: true))).IsTrue();
        await Assert.That(bar.Text).IsEqualTo("say hello").Because("the cut was refused");

        accept = true;
        bar.ProcessKey(Chord(ConsoleKey.X, ctrl: true));
        await Assert.That(taken).IsEquivalentTo(new[] { "hello", "hello" });
        await Assert.That(bar.Text).IsEqualTo("say ");

        bar.ProcessKey(Chord(ConsoleKey.Z, ctrl: true));
        await Assert.That(bar.Text).IsEqualTo("say hello");
    }

    /// <summary>
    /// Escape is declined. The editor would take it (to leave edit mode, or to swallow it), and a claimed
    /// Escape is one <c>TryAltEnter</c> never sees, which would break ⌥⏎.
    /// </summary>
    [Test]
    public async Task EscapeIsDeclined()
    {
        var bar = Bar();
        Type(bar, "look");

        await Assert.That(bar.ProcessKey(Chord(ConsoleKey.Escape))).IsFalse();
        await Assert.That(bar.IsEditing).IsTrue();
    }
}
