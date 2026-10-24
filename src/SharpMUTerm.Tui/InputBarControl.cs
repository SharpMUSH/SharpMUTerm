using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace SharpMUTerm.Tui;

/// <summary>
/// One command line: a prompt, a wrapping buffer that grows with what is typed, and the keys that edit
/// it. Two of these make up the input area — the everyday bar and the optional second one — and the
/// pair is what <see cref="SharpMUTermApp"/> routes typing into.
/// <para>
/// It is our control rather than the framework's <c>PromptControl</c> because that one cannot be made
/// to do this: it stores a single line (its setter turns <c>\n</c> into a space), it measures one row
/// tall, and it scrolls sideways when the text outgrows the field. It also unfocuses itself on ⏎
/// (<c>UnfocusOnEnter</c>, on by default), which is exactly the behaviour the maintainer reported as
/// "pressing enter should not lose focus". None of that is configurable from outside, and SharpConsoleUI
/// is a dependency here, not a fork — so the control is written against its public painting API
/// (<see cref="BaseControl"/>, <see cref="CharacterBuffer"/>, <see cref="MarkupParser"/>) instead.
/// </para>
/// <para>
/// The parts worth testing are deliberately not in here: the text edits are <see cref="InputBuffer"/>
/// and the geometry is <see cref="InputLayout"/>, both terminal-free. What is left is the key table,
/// the painting, and the focus rules.
/// </para>
/// </summary>
internal sealed class InputBarControl : BaseControl, IInteractiveControl, IFocusableControl,
    IMouseAwareControl, ILogicalCursorProvider, ICursorShapeProvider, IPasteTarget, ISelectableControl
{
    /// <summary>The buffer this bar edits. Public so the app can read the draft without a round trip.</summary>
    internal InputBuffer Buffer { get; } = new();

    private string _prompt = string.Empty;
    private bool _armed = true;
    private int _minRows = 3;
    private int _maxRows = 8;
    private int _scroll;

    // True between a button press on the text and its release, so motion with the button held extends
    // the selection from where the press landed.
    private bool _dragging;
    private Point _dragAt;

    /// <summary>Raised when ⏎ sends the line. The payload is the text as it was, newlines included.</summary>
    internal event Action<string>? Entered;

    /// <summary>Raised whenever an edit changes the text — what records the per-window draft.</summary>
    internal event Action<string>? Changed;

    /// <summary>Raised when ⇥ asks for the other bar, or a click asks for this one.</summary>
    internal event Action<InputBarControl>? ActivationRequested;

    /// <summary>Raised when ⇥ should hand the caret to the bar after this one.</summary>
    internal event Action? CycleRequested;

    /// <summary>Whether ⇥ has somewhere to go — false while this is the only bar on screen.</summary>
    internal Func<bool> HasSibling { get; set; } = () => false;

    /// <summary>The prompt drawn on the bar's first row, as markup. Continuation rows indent past it.</summary>
    internal string Prompt
    {
        get => _prompt;
        set => SetProperty(ref _prompt, value ?? string.Empty);
    }

    /// <summary>Whether ⏎ sends from this bar. The unarmed bar is dimmed and shows no caret.</summary>
    internal bool Armed
    {
        get => _armed;
        set
        {
            if (_armed == value)
            {
                return;
            }

            _armed = value;
            Invalidate(Invalidation.Repaint);
        }
    }

    /// <summary>The bar's height before anything wraps — F8's <c>input height</c>.</summary>
    internal int MinRows
    {
        get => _minRows;
        set => SetProperty(ref _minRows, value);
    }

    /// <summary>The most rows it grows to before scrolling inside itself — F8's <c>grows to</c>.</summary>
    internal int MaxRows
    {
        get => _maxRows;
        set => SetProperty(ref _maxRows, value);
    }

    /// <summary>The band colour behind an armed bar.</summary>
    internal Color BandColor { get; set; } = new(0x33, 0x39, 0x4c, 255);

    /// <summary>The band colour behind the bar ⏎ will not send from.</summary>
    internal Color IdleBandColor { get; set; } = new(0x26, 0x2b, 0x3a, 255);

    /// <summary>The colour typed text is drawn in.</summary>
    internal Color TextColor { get; set; } = new(0xe6, 0xe6, 0xe6, 255);

    /// <summary>The colour typed text is drawn in on the bar ⏎ will not send from.</summary>
    internal Color IdleTextColor { get; set; } = new(0x8a, 0x8f, 0x9e, 255);

    /// <summary>The band behind selected text — the same one a pane's selection is drawn on.</summary>
    internal Color SelectionColor { get; set; } = new(0x4a, 0x5a, 0x8a, 255);

    /// <summary>The ink selected text is drawn in, held to the floor against <see cref="SelectionColor"/>.</summary>
    internal Color SelectionTextColor { get; set; } = new(0xff, 0xff, 0xff, 255);

    /// <summary>The whole buffer. Setting it is a recall, not a keystroke: it raises no change event.</summary>
    internal string Text
    {
        get => Buffer.Text;
        set
        {
            if (Buffer.Set(value))
            {
                Invalidate(Invalidation.Relayout);
            }
        }
    }

    /// <summary>
    /// Puts text in and reports the change, which is what a link's <c>prompt:</c> payload wants: the
    /// text arrived from outside, but it is the user's draft now and the window should keep it.
    /// </summary>
    internal void SetAndNotify(string? text)
    {
        if (Buffer.Set(text))
        {
            Invalidate(Invalidation.Relayout);
            Changed?.Invoke(Buffer.Text);
        }
    }

    /// <summary>
    /// Moves the caret one visual row, reporting whether it had anywhere to go. The app asks first and
    /// only falls back to history recall when the answer is no, so ↑ inside a grown command line moves
    /// the caret and ↑ on its first row still recalls.
    /// </summary>
    internal bool TryMoveRow(int delta)
    {
        if (!Buffer.MoveRow(delta, FieldWidth()))
        {
            return false;
        }

        Deselected(Buffer.Deselect());

        Invalidate(Invalidation.Repaint);
        return true;
    }

    /// <summary>How tall the bar currently is, in rows — what the layout asks for and what tests assert.</summary>
    internal int Rows() =>
        InputLayout.Height(InputLayout.Wrap(Buffer.Text, FieldWidth()).Count, MinRows, MaxRows);

    /// <inheritdoc/>
    public override int? ContentWidth => MarkupParser.StripLength(_prompt) + 10 + Margin.Left + Margin.Right;

    /// <inheritdoc/>
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc/>
    public bool CanReceiveFocus => IsEnabled;

    /// <inheritdoc/>
    public bool HasFocus => ComputeHasFocus();

    /// <inheritdoc/>
    public CursorShape? PreferredCursorShape => CursorShape.VerticalBar;

    /// <summary>⇥ is ours whenever there is another bar to hand it to; otherwise it stays traversal.</summary>
    public bool WantsTabKey => HasSibling();

    /// <summary>
    /// The key table.
    /// <para>
    /// ⏎ sends, always — that is the one binding a MU* client may not get wrong. <b>The newline chord is
    /// Alt+⏎</b>, with ⌃L as a second spelling, and Shift+⏎ / Ctrl+⏎ honoured where a host reports them.
    /// The reason it is Alt and not the two that were asked for is a fact about the terminal:
    /// SharpConsoleUI's Unix input parser (<c>AnsiInputParser</c>) decodes no CSI-u and enables no
    /// <c>modifyOtherKeys</c>, so it turns both CR (0x0D) and LF (0x0A) into a bare
    /// <see cref="ConsoleKey.Enter"/> carrying no modifier bits at all — Shift+⏎ and Ctrl+⏎ are
    /// <em>indistinguishable from ⏎</em> there, and a client that bound them would either do nothing or
    /// stop sending lines. They are accepted here anyway because the Windows <c>Console.ReadKey</c> path
    /// does report them, and accepting a modifier that never arrives costs nothing.
    /// </para>
    /// <para>
    /// Alt+⏎ <em>does</em> arrive, in two pieces: the parser emits ESC followed by a control byte as two
    /// key events (<c>ProcessEscape</c>), so Alt+⏎ is Escape and then Enter, in that order, in the same
    /// input batch. <c>SharpMUTermApp.TryAltEnter</c> puts the pair back together and hands this table a
    /// synthetic Alt+Enter, which is why the modifier is honoured here rather than the newline being
    /// inserted from over there: the key table stays the one place that says what Enter does. ⌃L is kept
    /// as the chord that needs no reassembly — <see cref="MacroKeys"/> records that a Ctrl+letter reaches
    /// the app as its control byte except for I, M, J and H, which the terminal spells Tab, Enter, Enter
    /// and Backspace.
    /// </para>
    /// <para>
    /// <b>Word movement is Alt+←/→, not Ctrl+←/→.</b> It was Ctrl, and the chord was given up to
    /// directional pane navigation (<c>SharpMUTermApp.TryFocusKey</c>) — a deliberate trade, not an
    /// oversight. Alt+arrow is delivered every bit as reliably (the parser reads the Alt bit out of
    /// <c>CSI 1;3 D</c>, exactly as it reads Ctrl out of <c>CSI 1;5 D</c>) and is the more widely-used
    /// spelling of the two, so the capability is not reduced — only respelt, and now advertised, which it
    /// never was.
    /// </para>
    /// <para>
    /// <b>Ctrl+A is move-to-line-start, not select-all</b>, and it is not up for grabs. The same parser
    /// limitation makes Ctrl+Shift+A indistinguishable from Ctrl+A, so there is no second chord to move
    /// line-start onto: handing ⌃A to a selection would simply remove it from the readline set
    /// (⌃A ⌃E ⌃K ⌃U) that is the only way around a wrapped, multi-row command line.
    /// </para>
    /// <para>
    /// <b>There is no ⌃W here, and the readline set is four keys rather than five.</b> It used to be
    /// listed, and it used to be handled — <c>case ConsoleKey.W: return Edit(Buffer.KillWordLeft())</c> —
    /// and it could never once have run: <see cref="MacroKeys.AppShortcuts"/> claims ⌃W for
    /// <c>CloseActiveWindow</c>, and a global shortcut is dispatched before any window sees the key, so
    /// the chord was consumed before it could reach this table. A key table advertising a binding nothing
    /// can deliver is the same defect as a settings screen naming a key it does not offer, so the dead
    /// case is gone and the advertisement with it. <c>InputBuffer.KillWordLeft</c> is deliberately kept:
    /// it works and it is tested, and what it lacks is a chord this host can deliver — <em>not</em>
    /// Alt+Backspace, which the parser splits into Escape then Backspace (ESC followed by a control byte
    /// is emitted as two keys), so restoring it means taking ⌃W back off the window command or spending a
    /// function key, which is a decision about which meaning wins rather than a repair.
    /// </para>
    /// <para>
    /// <b>Selection is ⇧←/→ and ⇧Home/End, and a drag or double-click with the mouse.</b> The shifted
    /// keys extend from wherever the caret is, through the same moves the bare keys make; typing, paste,
    /// Backspace and Delete replace what is selected; any bare move drops it. ⇧↑/↓ are not here — they
    /// are the scrollback keys, and the window takes them before the bar sees them — so a selection
    /// across rows is made with ⇧Home/End or the mouse. The bar is an <see cref="ISelectableControl"/>
    /// and registers with the window's <c>SelectionManager</c>, so it and the panes share one selection:
    /// starting one here clears a pane's, and the app's ⌃C copies whichever exists, without learning that
    /// the bar is a second place text can be selected.
    /// </para>
    /// <para>
    /// <b>Every arm below matches the modifiers it wants exactly, and declines everything else.</b> The
    /// navigation keys are the caret's only bare; word movement is Alt and nothing else. Returning
    /// <c>true</c> for a chord this control has no meaning for is how it swallowed <c>Alt+Shift+←</c> —
    /// the pane-resize chord — as a caret move, on any line with a character to the left of the caret.
    /// </para>
    /// </summary>
    public bool ProcessKey(ConsoleKeyInfo key)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        var shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
        var alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);

        if (key.Key == ConsoleKey.Enter)
        {
            return ctrl || shift || alt ? Edit(Buffer.InsertNewline()) : Send();
        }

        // Alt and nothing else. Alt+Shift+←/→ is the pane-resize chord and Alt+Ctrl+← is nobody's here;
        // a `HasFlag` test claimed both, which is the defect the block below is guarded against.
        if (key.Modifiers == ConsoleModifiers.Alt)
        {
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow: return Move(b => b.MoveWordLeft());
                case ConsoleKey.RightArrow: return Move(b => b.MoveWordRight());
            }
        }

        // Shift and nothing else extends a selection. Exact for the reason the Alt block above is: Alt+Shift
        // is the pane resize, and ⇧↑/↓ never arrive here (the scrollback keys are taken first).
        if (key.Modifiers == ConsoleModifiers.Shift)
        {
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow: return Extend(b => b.MoveLeft());
                case ConsoleKey.RightArrow: return Extend(b => b.MoveRight());
                case ConsoleKey.Home: return Extend(b => b.MoveHome(FieldWidth()));
                case ConsoleKey.End: return Extend(b => b.MoveEnd(FieldWidth()));
            }
        }

        if (ctrl)
        {
            switch (key.Key)
            {
                case ConsoleKey.L: return Edit(Buffer.InsertNewline());
                case ConsoleKey.A: return Move(b => b.MoveTo(0));
                case ConsoleKey.E: return Move(b => b.MoveTo(b.Text.Length));
                case ConsoleKey.K: return Edit(Buffer.KillToEnd());
                case ConsoleKey.U: return Edit(Buffer.KillToStart());
            }
        }

        switch (key.Key)
        {
            case ConsoleKey.Backspace: return Edit(Buffer.Backspace());
            case ConsoleKey.Delete: return Edit(Buffer.Delete());
            case ConsoleKey.Tab when HasSibling():
                CycleRequested?.Invoke();
                return true;
        }

        // The navigation keys are the caret's only when nothing is held down. This switch had no modifier
        // test at all, so every modified arrow was a plain caret move — and returning true for one is a
        // claim, not a shrug: Move(Buffer.MoveLeft()) is true whenever there is a character to the left of
        // the caret. That is the defect this project has now paid for three times (TryRecallKey swallowing
        // Shift+↑ from the scrollback work; the framework's move-mode handler taking every unclaimed Ctrl+
        // chord until Movable(false)), and here it was a second cause standing behind the terminal's in the
        // pane-resize report: on an empty command line ←/→ declined and the chord fell through, and one
        // typed character made them claim it — while TryMoveRow hid the vertical half by returning false on
        // a one-row bar. A control that does not handle a chord has to decline it; the window's own key
        // handler is the one with the whole keyboard in view.
        if (key.Modifiers == 0)
        {
            switch (key.Key)
            {
                case ConsoleKey.Home: return Move(b => b.MoveHome(FieldWidth()));
                case ConsoleKey.End: return Move(b => b.MoveEnd(FieldWidth()));
                case ConsoleKey.LeftArrow: return Move(b => b.MoveLeft());
                case ConsoleKey.RightArrow: return Move(b => b.MoveRight());
                case ConsoleKey.UpArrow: return TryMoveRow(-1);
                case ConsoleKey.DownArrow: return TryMoveRow(1);
            }
        }

        // Alt chords are the app's or a macro's, never text: the parser spells them ESC + the character,
        // so accepting them here would type the letter of every Alt binding into the command line.
        if (!key.Modifiers.HasFlag(ConsoleModifiers.Alt) && !ctrl && !char.IsControl(key.KeyChar))
        {
            return Edit(Buffer.Insert(key.KeyChar));
        }

        return false;
    }

    /// <summary>Sends the line and clears it, leaving the caret exactly where it was — on this bar.</summary>
    private bool Send()
    {
        var text = Buffer.Text;
        if (Buffer.Clear())
        {
            _scroll = 0;
            Invalidate(Invalidation.Relayout);
        }

        Entered?.Invoke(text);
        return true;
    }

    /// <summary>Commits an edit: relayout (the bar may have grown) and tell the app the draft moved.</summary>
    private bool Edit(bool changed)
    {
        if (changed)
        {
            Invalidate(Invalidation.Relayout);
            Changed?.Invoke(Buffer.Text);
        }

        return true;
    }

    /// <summary>
    /// Commits a caret move: repaint only, and no draft change — the text did not move. A bare move drops
    /// the selection, so it repaints when that is all it did.
    /// </summary>
    private bool Move(Func<InputBuffer, bool> move)
    {
        var deselected = Buffer.Deselect();
        if (move(Buffer) || deselected)
        {
            Invalidate(Invalidation.Repaint);
        }

        Deselected(deselected);
        return true;
    }

    /// <summary>Commits a shifted move: the caret moves and the anchor stays where the selection began.</summary>
    private bool Extend(Func<InputBuffer, bool> move)
    {
        if (Buffer.Extend(move))
        {
            Invalidate(Invalidation.Repaint);
            SelectionMoved();
        }

        return true;
    }

    /// <summary>
    /// Tells the window this bar holds the selection now, and anyone listening what it is. Through the
    /// window's <c>SelectionManager</c>, the same call a pane makes, which is what clears a pane's
    /// selection when one starts here and what lets ⌃C ask one place for the text.
    /// </summary>
    private void SelectionMoved()
    {
        if (!Buffer.HasSelection)
        {
            SelectionChanged?.Invoke(this, string.Empty);
            return;
        }

        this.GetParentWindow()?.SelectionManager.SetActiveSelection(this);
        SelectionChanged?.Invoke(this, Buffer.SelectedText);
    }

    /// <summary>Reports a selection that a move or a recall dropped.</summary>
    private void Deselected(bool had)
    {
        if (had)
        {
            Invalidate(Invalidation.Repaint);
            SelectionChanged?.Invoke(this, string.Empty);
        }
    }

    #region ISelectableControl

    /// <inheritdoc/>
    public bool HasSelection => Buffer.HasSelection;

    /// <inheritdoc/>
    public event EventHandler<string>? SelectionChanged;

    /// <inheritdoc/>
    public string GetSelectedText() => Buffer.SelectedText;

    /// <summary>
    /// Drops the selection when another control's starts. Called by the window's <c>SelectionManager</c>,
    /// so it must not call back into it — it only resets this bar and repaints.
    /// </summary>
    public void ClearSelection()
    {
        _dragging = false;
        Deselected(Buffer.Deselect());
    }

    #endregion

    /// <summary>Pastes at the caret. Newlines are kept: this bar has rows for them.</summary>
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _dragging = false;
        Edit(Buffer.Insert(text.Replace("\r\n", "\n").Replace('\r', '\n')));
    }

    /// <summary>How many columns of text fit on one row: the field, less the prompt it indents past.</summary>
    private int FieldWidth()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width ?? 80;
        return Math.Max(1, width - Margin.Left - Margin.Right - MarkupParser.StripLength(_prompt));
    }

    /// <summary>
    /// <b>Where row 0 of the text lands and how far it has scrolled, derived once.</b> Both
    /// <see cref="PaintDOM"/> and <see cref="GetLogicalCursorPosition"/> ask this, because the caret is
    /// drawn on the row the text was painted on and there is no version of that sentence in which two
    /// formulas may compute it separately: they agree only for as long as nobody edits one of them, and
    /// a caret computed from state that does not match what was painted is a bug with a new symptom for
    /// every layout change.
    /// <para>
    /// <paramref name="boxHeight"/> is the rows the control was actually <em>given</em>, which is not
    /// always the rows it asked for — the framework clamps a measure to the space left. The paint has
    /// always honoured that (it cannot paint outside its bounds); the cursor did not, so on any frame
    /// where the two differed the scroll offset the caret was placed by was one the text was never drawn
    /// at. Pass 0 when nothing has been arranged yet and the requested height is the only answer there is.
    /// </para>
    /// </summary>
    private (IReadOnlyList<InputRow> Rows, int Row, int Column, int Height) Geometry(
        int fieldWidth, int boxHeight)
    {
        var rows = InputLayout.Wrap(Buffer.Text, fieldWidth);
        var (row, column) = InputLayout.Caret(rows, Buffer.Caret);
        var height = InputLayout.Height(rows.Count, MinRows, MaxRows);
        if (boxHeight > 0)
        {
            height = Math.Min(height, boxHeight);
        }

        _scroll = InputLayout.Scroll(row, rows.Count, height, _scroll);
        return (rows, row, column, height);
    }

    /// <summary>The rows the last arrange gave the text, or 0 before there has been one.</summary>
    private int ArrangedTextHeight() =>
        ActualHeight > 0 ? Math.Max(0, ActualHeight - Margin.Top - Margin.Bottom) : 0;

    /// <inheritdoc/>
    public Point? GetLogicalCursorPosition()
    {
        if (!HasFocus || !Armed)
        {
            return null;
        }

        var (_, row, column, _) = Geometry(FieldWidth(), ArrangedTextHeight());
        return new Point(
            Margin.Left + MarkupParser.StripLength(_prompt) + column,
            Margin.Top + row - _scroll);
    }

    /// <inheritdoc/>
    public void SetLogicalCursorPosition(Point position)
    {
        var deselected = Buffer.Deselect();
        if (Buffer.MoveTo(OffsetAt(position)) || deselected)
        {
            Invalidate(Invalidation.Repaint);
        }

        Deselected(deselected);
    }

    /// <summary>The buffer offset under a point in this control's own coordinates, clamped into the text.</summary>
    private int OffsetAt(Point position)
    {
        var rows = InputLayout.Wrap(Buffer.Text, FieldWidth());
        var row = Math.Clamp(position.Y - Margin.Top + _scroll, 0, rows.Count - 1);
        var column = position.X - Margin.Left - MarkupParser.StripLength(_prompt);
        return InputLayout.Offset(rows, row, column);
    }

    /// <inheritdoc/>
    public override Size GetLogicalContentSize() => new(Width ?? (ContentWidth ?? 0), Rows());

    /// <inheritdoc/>
    public override LayoutSize MeasureDOM(LayoutConstraints constraints)
    {
        var width = (Width ?? constraints.MaxWidth) + Margin.Left + Margin.Right;
        var height = Rows() + Margin.Top + Margin.Bottom;
        return new LayoutSize(
            Math.Clamp(width, constraints.MinWidth, constraints.MaxWidth),
            Math.Clamp(height, constraints.MinHeight, constraints.MaxHeight));
    }

    /// <inheritdoc/>
    public override void PaintDOM(
        CharacterBuffer buffer, LayoutRect bounds, LayoutRect clipRect, Color defaultFg, Color defaultBg)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        SetActualBounds(bounds);

        var band = Armed ? BandColor : IdleBandColor;
        var ink = Armed ? TextColor : IdleTextColor;
        var startX = bounds.X + Margin.Left;
        var startY = bounds.Y + Margin.Top;
        var promptWidth = MarkupParser.StripLength(_prompt);
        var fieldWidth = Math.Max(1, bounds.Width - Margin.Left - Margin.Right - promptWidth);

        var (rows, _, _, height) = Geometry(fieldWidth, bounds.Height - Margin.Top - Margin.Bottom);
        var (selStart, selLength) = Buffer.Selection;

        for (var line = 0; line < height; line++)
        {
            var y = startY + line;
            if (y < clipRect.Y || y >= clipRect.Bottom || y >= bounds.Bottom)
            {
                continue;
            }

            // The whole row is band first, so a short line still paints the bar edge to edge.
            for (var x = bounds.X; x < bounds.Right; x++)
            {
                if (x >= clipRect.X && x < clipRect.Right)
                {
                    buffer.SetNarrowCell(x, y, ' ', ink, band);
                }
            }

            if (line == 0 && promptWidth > 0)
            {
                buffer.WriteCellsClipped(startX, y, MarkupParser.Parse(_prompt, ink, band), clipRect);
            }

            var index = _scroll + line;
            if (index >= rows.Count)
            {
                continue;
            }

            var row = rows[index];
            var textX = startX + promptWidth;
            for (var i = 0; i < row.Length && i < fieldWidth; i++)
            {
                var x = textX + i;
                if (x >= clipRect.X && x < clipRect.Right && x < bounds.Right - Margin.Right)
                {
                    var offset = row.Start + i;
                    var selected = offset >= selStart && offset < selStart + selLength;
                    buffer.SetNarrowCell(
                        x, y, Buffer.Text[offset],
                        selected ? SelectionTextColor : ink,
                        selected ? SelectionColor : band);
                }
            }
        }

        PaintOverflow(buffer, bounds, clipRect, rows.Count, height, ink, band);
    }

    /// <summary>
    /// Marks a bar that has more rows than it is tall. Without it the cap reads as lost text: the line
    /// simply stops, with nothing to say the rest is above or below.
    /// </summary>
    private void PaintOverflow(
        CharacterBuffer buffer, LayoutRect bounds, LayoutRect clipRect, int rows, int height,
        Color ink, Color band)
    {
        if (rows <= height || height <= 0)
        {
            return;
        }

        var x = bounds.Right - Margin.Right - 1;
        if (x < clipRect.X || x >= clipRect.Right)
        {
            return;
        }

        var top = bounds.Y + Margin.Top;
        if (_scroll > 0 && top >= clipRect.Y && top < clipRect.Bottom)
        {
            buffer.SetNarrowCell(x, top, '▴', ink, band);
        }

        var last = top + height - 1;
        if (_scroll + height < rows && last >= clipRect.Y && last < clipRect.Bottom)
        {
            buffer.SetNarrowCell(x, last, '▾', ink, band);
        }
    }

    #region IMouseAwareControl

    /// <inheritdoc/>
    public bool WantsMouseEvents => true;

    /// <inheritdoc/>
    public bool CanFocusWithMouse => CanReceiveFocus;

    /// <inheritdoc/>
    public event EventHandler<SharpConsoleUI.Events.MouseEventArgs>? MouseClick;

    /// <inheritdoc/>
    public event EventHandler<SharpConsoleUI.Events.MouseEventArgs>? MouseDoubleClick;

    /// <inheritdoc/>
    public event EventHandler<SharpConsoleUI.Events.MouseEventArgs>? MouseRightClick;

    /// <inheritdoc/>
    public event EventHandler<SharpConsoleUI.Events.MouseEventArgs>? MouseEnter;

    /// <inheritdoc/>
    public event EventHandler<SharpConsoleUI.Events.MouseEventArgs>? MouseLeave;

    /// <inheritdoc/>
    public event EventHandler<SharpConsoleUI.Events.MouseEventArgs>? MouseMove;

    /// <summary>
    /// Clicking a bar arms it and drops the caret where the pointer landed — the second way (after ⇥)
    /// to say which line ⏎ should send. The hover events are relayed as the framework reports them so a
    /// subscriber sees the same stream any other mouse-aware control gives.
    /// </summary>
    public bool ProcessMouseEvent(SharpConsoleUI.Events.MouseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!IsEnabled)
        {
            return false;
        }

        if (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.MouseEnter))
        {
            MouseEnter?.Invoke(this, args);
        }

        if (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.MouseLeave))
        {
            MouseLeave?.Invoke(this, args);
        }

        if (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.ReportMousePosition))
        {
            MouseMove?.Invoke(this, args);
        }

        if (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button3Clicked))
        {
            MouseRightClick?.Invoke(this, args);
        }

        // A double-click selects the word under it. Before the press arm below, because the framework
        // reports the second press of a double-click with both flags.
        if (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1DoubleClicked))
        {
            Arm();
            _dragging = false;
            if (Buffer.SelectWordAt(OffsetAt(args.Position)))
            {
                Invalidate(Invalidation.Repaint);
            }

            SelectionMoved();
            MouseDoubleClick?.Invoke(this, args);
            args.Handled = true;
            return true;
        }

        // Motion with the button held, which SGR reports as Pressed|Dragged: extend from where the press
        // landed. A bare Pressed at the cell the gesture is already on is the host's auto-repeat of a held
        // button (PaneDragTracker has the details) and continues it; a bare Pressed anywhere else is a new
        // press after a release this control never saw, and starts over.
        var pressed = args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Pressed);
        if (_dragging
            && (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Dragged)
                || (pressed && args.Position == _dragAt)))
        {
            _dragAt = args.Position;
            var target = OffsetAt(args.Position);
            Extend(b => b.MoveTo(target));
            args.Handled = true;
            return true;
        }

        if (pressed)
        {
            Arm();
            SetLogicalCursorPosition(args.Position);
            _dragging = true;
            _dragAt = args.Position;
            args.Handled = true;
            return true;
        }

        if (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Released)
            || args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Clicked))
        {
            // A click that ends a drag leaves the selection the drag made. A click with no press before
            // it (some drivers report only the click) places the caret, as a click always has.
            var wasDragging = _dragging;
            _dragging = false;
            if (!args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Clicked))
            {
                return false;
            }

            if (!wasDragging)
            {
                Arm();
                SetLogicalCursorPosition(args.Position);
            }

            MouseClick?.Invoke(this, args);
            args.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// What any press on a bar does first: take the focus and ask to be the bar ⏎ sends from — the second
    /// way (after ⇥) to say which line that is.
    /// </summary>
    private void Arm()
    {
        if (!HasFocus && CanFocusWithMouse)
        {
            this.GetParentWindow()?.FocusManager.SetFocus(this, FocusReason.Mouse);
        }

        ActivationRequested?.Invoke(this);
    }

    #endregion

    /// <inheritdoc/>
    protected override void OnDisposing()
    {
    }
}
