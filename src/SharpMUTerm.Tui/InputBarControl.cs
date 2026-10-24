using System.Collections;
using System.Reflection;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using Point = System.Drawing.Point;

namespace SharpMUTerm.Tui;

/// <summary>
/// One command line: a prompt, a wrapping buffer that grows with what is typed, and the keys that edit
/// it. Two of these make up the input area — the everyday bar and the optional second one — and the
/// pair is what <see cref="SharpMUTermApp"/> routes typing into.
/// <para>
/// <b>It is the F1 composer's editor, <see cref="MultilineEditControl"/>, with the command line's rules
/// laid over it.</b> The editor brings what a hand-written bar would have to write again: undo and redo
/// (⌃Z/⌃Y), a caret that moves over wrapped rows, word-aware wrapping, mouse placement, drag and
/// double-click selection, and one selection shared with the panes through the window's
/// <c>SelectionManager</c>. What it does not do, and this class does, is four things:
/// </para>
/// <list type="bullet">
/// <item><b>⏎ sends.</b> The editor inserts a newline on ⏎ and declines every modified ⏎; here a bare ⏎
/// sends and clears, and the modified forms (⌥⏎, reassembled by <c>SharpMUTermApp.TryAltEnter</c>, and
/// ⌃L) insert the newline. Keys reach this table from the window's <c>PreviewKeyPressed</c> and from the
/// framework through <see cref="IInteractiveControl"/>, which is re-implemented so both arrive at
/// <see cref="ProcessKey"/>.</item>
/// <item><b>It grows to its text</b>, between <see cref="MinRows"/> and <see cref="MaxRows"/>. The editor
/// has a fixed viewport, so <see cref="MeasureDOM"/> sets it from the wrapped row count at the width it
/// is about to be given. That count is read from the editor's own wrapper by reflection
/// (<see cref="WrappedRowCount"/>), because the public one is measured at the width of the <em>last</em>
/// paint and so runs a frame behind every resize.</item>
/// <item><b>The prompt is a gutter</b> (<see cref="PromptGutter"/>) as wide as the prompt, written on
/// the first row and blank under it, so every row of text indents past it.</item>
/// <item><b>Copy and cut go through the app.</b> The editor's ⌃C and ⌃X write to the system clipboard
/// through a static helper nothing can substitute, so a test would overwrite the developer's clipboard.
/// They are never passed on: ⌃C is answered by <c>SharpMUTermApp.CopyFocusedSelection</c>, which asks the
/// window's selection owner — this bar, when the selection is here — and ⌃X hands the selection to
/// <see cref="CutTo"/> and deletes it only once that says the text is on the clipboard.</item>
/// </list>
/// <para>
/// <b>Every arm in <see cref="ProcessKey"/> matches the modifiers it wants exactly, and declines
/// everything else.</b> Returning <c>true</c> for a chord this control has no meaning for is how the old
/// bar once swallowed <c>Alt+Shift+←</c>, the pane-resize chord, as a caret move. The editor's own table
/// is wider than a command line's (⌃D duplicate line, ⌥↑ move line, ⇥ indent), so it only
/// ever sees a key this table has decided it should.
/// </para>
/// </summary>
internal sealed class InputBarControl : MultilineEditControl,
    IInteractiveControl, IMouseAwareControl, ILogicalCursorProvider, ISelectableControl
{
    private static readonly MethodInfo? WrappedLinesMethod = typeof(MultilineEditControl).GetMethod(
        "GetWrappedLines", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(int)]);

    private readonly PromptGutter _gutter;
    private bool _armed = true;
    private int _minRows = 3;
    private int _maxRows = 8;
    private bool _quiet;
    private Color _band = new(0x33, 0x39, 0x4c, 255);
    private Color _idleBand = new(0x26, 0x2b, 0x3a, 255);
    private Color _ink = new(0xe6, 0xe6, 0xe6, 255);
    private Color _idleInk = new(0x8a, 0x8f, 0x9e, 255);

    internal InputBarControl()
        : base(viewportHeight: 3)
    {
        WrapMode = WrapMode.WrapWords;
        IsEditing = true;
        EscapeExitsEditMode = false;
        ShowEditingHints = false;
        HorizontalScrollbarVisibility = ScrollbarVisibility.Never;
        VerticalScrollbarVisibility = ScrollbarVisibility.Auto;
        _gutter = new PromptGutter(this);
        AddGutterRenderer(_gutter);
        ContentChanged += (_, _) =>
        {
            if (!_quiet)
            {
                Changed?.Invoke(Text);
            }
        };
        ApplyColors();
    }

    /// <summary>Raised when ⏎ sends the line. The payload is the text as it was, newlines included.</summary>
    internal event Action<string>? Entered;

    /// <summary>Raised whenever an edit changes the text — what records the per-window draft.</summary>
    internal event Action<string>? Changed;

    /// <summary>Raised when ⇥ asks for the other bar, or a click asks for this one.</summary>
    internal event Action<InputBarControl>? ActivationRequested;

    /// <summary>Raised when ⇥ should hand the caret to the bar after this one.</summary>
    internal event Action? CycleRequested;

    /// <summary>
    /// Where ⌃X puts the selection. True means it reached the clipboard and may be deleted; false (no
    /// clipboard, or nothing selected) leaves the text where it is, because a cut that lands nowhere is a
    /// delete.
    /// </summary>
    internal Func<string, bool> CutTo { get; set; } = _ => false;

    /// <summary>Whether ⇥ has somewhere to go — false while this is the only bar on screen.</summary>
    internal Func<bool> HasSibling { get; set; } = () => false;

    /// <summary>The prompt drawn on the bar's first row, as markup. Every row of text indents past it.</summary>
    internal string Prompt
    {
        get => _gutter.Markup;
        set
        {
            var markup = value ?? string.Empty;
            if (_gutter.Markup == markup)
            {
                return;
            }

            _gutter.Markup = markup;
            Invalidate(Invalidation.Relayout);
        }
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
            ApplyColors();
        }
    }

    /// <summary>The bar's height before anything wraps — F8's <c>input height</c>.</summary>
    internal int MinRows
    {
        get => _minRows;
        set
        {
            if (_minRows != value)
            {
                _minRows = value;
                Invalidate(Invalidation.Relayout);
            }
        }
    }

    /// <summary>The most rows it grows to before scrolling inside itself — F8's <c>grows to</c>.</summary>
    internal int MaxRows
    {
        get => _maxRows;
        set
        {
            if (_maxRows != value)
            {
                _maxRows = value;
                Invalidate(Invalidation.Relayout);
            }
        }
    }

    /// <summary>The band colour behind an armed bar.</summary>
    internal Color BandColor
    {
        get => _band;
        set { _band = value; ApplyColors(); }
    }

    /// <summary>The band colour behind the bar ⏎ will not send from.</summary>
    internal Color IdleBandColor
    {
        get => _idleBand;
        set { _idleBand = value; ApplyColors(); }
    }

    /// <summary>The colour typed text is drawn in.</summary>
    internal Color TextColor
    {
        get => _ink;
        set { _ink = value; ApplyColors(); }
    }

    /// <summary>The colour typed text is drawn in on the bar ⏎ will not send from.</summary>
    internal Color IdleTextColor
    {
        get => _idleInk;
        set { _idleInk = value; ApplyColors(); }
    }

    /// <summary>The band behind selected text — the same one a pane's selection is drawn on.</summary>
    internal Color SelectionColor
    {
        get => SelectionBackgroundColor;
        set => SelectionBackgroundColor = value;
    }

    /// <summary>The ink selected text is drawn in, held to the floor against <see cref="SelectionColor"/>.</summary>
    internal Color SelectionTextColor
    {
        get => SelectionForegroundColor;
        set => SelectionForegroundColor = value;
    }

    /// <summary>The band and ink this bar paints in now, which <see cref="Armed"/> picks between.</summary>
    private Color Band => _armed ? _band : _idleBand;

    private Color Ink => _armed ? _ink : _idleInk;

    /// <summary>
    /// Sets both of the editor's colour pairs. It paints from the <em>focused</em> pair while it has focus
    /// and the other pair otherwise, and which bar holds focus is the app's business, not this one's — so
    /// the pair a bar is drawn in is decided by <see cref="Armed"/> alone.
    /// </summary>
    private void ApplyColors()
    {
        BackgroundColor = Band;
        FocusedBackgroundColor = Band;
        ForegroundColor = Ink;
        FocusedForegroundColor = Ink;
    }

    /// <summary>Whether the bar is holding nothing.</summary>
    internal bool IsEmpty => LineCount <= 1 && Text.Length == 0;

    /// <summary>
    /// The whole buffer, lines joined with <c>\n</c> whatever the platform. Setting it is a recall, not a
    /// keystroke: the caret goes to the end and no change event is raised.
    /// </summary>
    internal string Text
    {
        get
        {
            var content = GetContent();
            return Environment.NewLine == "\n" ? content : content.Replace(Environment.NewLine, "\n");
        }
        set => Replace(value, notify: false);
    }

    /// <summary>
    /// Puts text in and reports the change, which is what a link's <c>prompt:</c> payload wants: the
    /// text arrived from outside, but it is the user's draft now and the window should keep it.
    /// </summary>
    internal void SetAndNotify(string? text) => Replace(text, notify: true);

    private void Replace(string? text, bool notify)
    {
        var value = text ?? string.Empty;
        if (string.Equals(value, Text, StringComparison.Ordinal))
        {
            return;
        }

        _quiet = true;
        try
        {
            SetContent(value);
            GoToEnd();
        }
        finally
        {
            _quiet = false;
        }

        Invalidate(Invalidation.Relayout);
        if (notify)
        {
            Changed?.Invoke(Text);
        }
    }

    /// <summary>
    /// Moves the caret one visual row, reporting whether it had anywhere to go. The app asks first and
    /// only falls back to history recall when the answer is no, so ↑ inside a grown command line moves
    /// the caret and ↑ on its first row still recalls.
    /// </summary>
    internal bool TryMoveRow(int delta)
    {
        var (line, column) = (CurrentLine, CurrentColumn);
        Pass(delta < 0 ? ConsoleKey.UpArrow : ConsoleKey.DownArrow);
        return line != CurrentLine || column != CurrentColumn;
    }

    /// <summary>How tall the bar currently is, in rows — what the layout asks for and what tests assert.</summary>
    internal int Rows() =>
        InputLayout.Height(WrappedRowCount(FieldWidth(ActualWidth > 0 ? ActualWidth : Width ?? 80)), MinRows, MaxRows);

    /// <inheritdoc/>
    public override int? ContentWidth => MarkupParser.StripLength(Prompt) + 10 + Margin.Left + Margin.Right;

    /// <summary>⇥ is ours whenever there is another bar to hand it to; otherwise it stays traversal.</summary>
    public new bool WantsTabKey => HasSibling();

    bool IInteractiveControl.WantsTabKey => WantsTabKey;

    /// <summary>
    /// The key table. It decides what reaches the editor, and the editor only ever sees a key that means
    /// the same thing to a command line as it does to a document.
    /// <para>
    /// ⏎ sends, always — that is the one binding a MU* client may not get wrong. <b>The newline chord is
    /// Alt+⏎</b>, with ⌃L as a second spelling, and Shift+⏎ / Ctrl+⏎ honoured where a host reports them.
    /// SharpConsoleUI's Unix input parser decodes no CSI-u and enables no <c>modifyOtherKeys</c>, so CR and
    /// LF both arrive as a bare ⏎ — Shift+⏎ and Ctrl+⏎ are indistinguishable from ⏎ there, and are
    /// accepted only because the Windows <c>Console.ReadKey</c> path reports them. Alt+⏎ arrives as Escape
    /// then Enter and <c>SharpMUTermApp.TryAltEnter</c> reassembles it.
    /// </para>
    /// <para>
    /// <b>Word movement is Alt+←/→</b> (Ctrl+←/→ is pane selection), handed to the editor as its Ctrl
    /// form. <b>⌃A selects everything and ⌃X cuts the selection</b> (<see cref="CutTo"/>), as an editor
    /// does; ⌃A used to be readline's start-of-buffer, which Home still is. <b>⌃E/⌃K/⌃U are readline's</b>:
    /// end of the buffer, kill to the end and to the start. Cuts and kills go through the editor's own
    /// delete, so ⌃Z puts them back. <b>⌃Z/⌃Y are undo and redo.</b> There is no ⌃W:
    /// <c>CloseActiveWindow</c> claims it as a global shortcut, ahead of any window.
    /// </para>
    /// <para>
    /// <b>Selection is ⇧←/→ and ⇧Home/End, and a drag or double-click.</b> ⇧↑/↓ are the scrollback keys
    /// and are taken before the bar sees them.
    /// </para>
    /// </summary>
    public new bool ProcessKey(ConsoleKeyInfo key)
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
            return ctrl || shift || alt ? Claim(ConsoleKey.Enter) : Send();
        }

        if (key.Key == ConsoleKey.Tab)
        {
            if (!HasSibling())
            {
                return false;
            }

            CycleRequested?.Invoke();
            return true;
        }

        // Alt and nothing else. Alt+Shift+←/→ is the pane-resize chord and Alt+Ctrl+← is nobody's here.
        if (key.Modifiers == ConsoleModifiers.Alt)
        {
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow: return Claim(ConsoleKey.LeftArrow, ConsoleModifiers.Control);
                case ConsoleKey.RightArrow: return Claim(ConsoleKey.RightArrow, ConsoleModifiers.Control);
            }

            return false;
        }

        // Shift and nothing else extends a selection.
        if (key.Modifiers == ConsoleModifiers.Shift)
        {
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                case ConsoleKey.Home:
                case ConsoleKey.End:
                    return Claim(key.Key, ConsoleModifiers.Shift);
            }
        }

        if (key.Modifiers == ConsoleModifiers.Control)
        {
            switch (key.Key)
            {
                case ConsoleKey.L: return Claim(ConsoleKey.Enter);
                case ConsoleKey.A: return SelectAll();
                case ConsoleKey.X: return Cut();
                case ConsoleKey.E: return Claim(ConsoleKey.End, ConsoleModifiers.Control);
                case ConsoleKey.K: return Kill(toEnd: true);
                case ConsoleKey.U: return Kill(toEnd: false);
                case ConsoleKey.Z:
                case ConsoleKey.Y:
                    return Claim(key.Key, ConsoleModifiers.Control);
            }

            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.Backspace when key.Modifiers == 0:
            case ConsoleKey.Delete when key.Modifiers == 0:
                return Claim(key.Key);
        }

        // The navigation keys are the caret's only when nothing is held down.
        if (key.Modifiers == 0)
        {
            switch (key.Key)
            {
                case ConsoleKey.Home:
                case ConsoleKey.End:
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                    return Claim(key.Key);
                case ConsoleKey.UpArrow: return TryMoveRow(-1);
                case ConsoleKey.DownArrow: return TryMoveRow(1);
            }
        }

        // Alt chords are the app's or a macro's, never text: the parser spells them ESC + the character,
        // so accepting them here would type the letter of every Alt binding into the command line.
        if (!alt && !ctrl && !char.IsControl(key.KeyChar))
        {
            Pass(key);
            return true;
        }

        return false;
    }

    bool IInteractiveControl.ProcessKey(ConsoleKeyInfo key) => ProcessKey(key);

    /// <summary>Hands a key to the editor and claims it whether or not it changed anything.</summary>
    private bool Claim(ConsoleKey key, ConsoleModifiers modifiers = 0)
    {
        Pass(key, modifiers);
        return true;
    }

    private void Pass(ConsoleKey key, ConsoleModifiers modifiers = 0) =>
        Pass(new ConsoleKeyInfo(
            key == ConsoleKey.Enter ? '\r' : '\0',
            key,
            modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt),
            modifiers.HasFlag(ConsoleModifiers.Control)));

    private void Pass(ConsoleKeyInfo key) => base.ProcessKey(key);

    /// <summary>
    /// ⌃A. Through <see cref="MultilineEditControl.SelectRange"/> rather than the editor's own ⌃A, which
    /// sets the selection without telling the window's <c>SelectionManager</c> — so ⌃C would not find it and
    /// a pane's selection would stay up beside it.
    /// </summary>
    private bool SelectAll()
    {
        if (Text.Length > 0)
        {
            SelectRange(0, 0, LineCount - 1, int.MaxValue);
        }

        return true;
    }

    /// <summary>⌃X: the selection to <see cref="CutTo"/>, then deleted as one undo step if it got there.</summary>
    private bool Cut()
    {
        if (CutTo(HasSelection ? GetSelectedText() : string.Empty) && HasSelection)
        {
            Pass(ConsoleKey.Delete);
        }

        return true;
    }

    /// <summary>⌃K and ⌃U: select from the caret to an end of the buffer and delete it, as one undo step.</summary>
    private bool Kill(bool toEnd)
    {
        var line = CurrentLine - 1;
        var column = CurrentColumn - 1;
        if (toEnd ? IsAtEnd(line, column) : line == 0 && column == 0)
        {
            return true;
        }

        if (toEnd)
        {
            SelectRange(line, column, LineCount - 1, int.MaxValue);
        }
        else
        {
            SelectRange(0, 0, line, column);
        }

        if (HasSelection)
        {
            Pass(ConsoleKey.Delete);
        }

        return true;
    }

    private bool IsAtEnd(int line, int column)
    {
        if (line != LineCount - 1)
        {
            return false;
        }

        var text = Text;
        var lastBreak = text.LastIndexOf('\n');
        return column >= text.Length - (lastBreak + 1);
    }

    /// <summary>Sends the line and clears it, leaving the caret exactly where it was — on this bar.</summary>
    private bool Send()
    {
        var text = Text;
        Replace(string.Empty, notify: false);
        Entered?.Invoke(text);
        return true;
    }

    /// <summary>How many columns of text fit on one row: the field, less the prompt it indents past.</summary>
    private int FieldWidth(int width) =>
        Math.Max(1, width - Margin.Left - Margin.Right - MarkupParser.StripLength(Prompt));

    /// <summary>
    /// The rows the text takes at <paramref name="fieldWidth"/>: the editor's own wrapped rows, plus one
    /// when the last of them is exactly full. That row is where the next character goes and where the
    /// caret sits until it is typed; the editor puts a caret at the end of a full row one cell past the
    /// text, which on this bar is off the control (<see cref="GetLogicalCursorPosition"/> moves it down).
    /// <para>
    /// Read by reflection because the public measure (<c>GetLogicalContentSize</c>) wraps at the width of
    /// the last paint, so it runs a frame behind every resize; falls back to it if the private method is
    /// ever renamed.
    /// </para>
    /// </summary>
    private int WrappedRowCount(int fieldWidth)
    {
        if (WrappedLinesMethod?.Invoke(this, [fieldWidth]) is not IList rows || rows.Count == 0)
        {
            return Math.Max(1, GetLogicalContentSize().Height);
        }

        var last = rows[rows.Count - 1];
        var text = last?.GetType().GetProperty("DisplayText")?.GetValue(last) as string ?? string.Empty;
        return rows.Count + (text.Length > 0 && UnicodeWidth.GetStringWidth(text) >= fieldWidth ? 1 : 0);
    }

    /// <inheritdoc/>
    public override LayoutSize MeasureDOM(LayoutConstraints constraints)
    {
        var width = Width ?? constraints.MaxWidth;
        ViewportHeight = InputLayout.Height(WrappedRowCount(FieldWidth(width)), MinRows, MaxRows);
        return base.MeasureDOM(constraints);
    }

    /// <summary>The caret is drawn only on the bar ⏎ sends from, and only while it holds the focus.</summary>
    public new Point? GetLogicalCursorPosition()
    {
        if (!HasFocus || !Armed || base.GetLogicalCursorPosition() is not { } caret)
        {
            return null;
        }

        // Past the last column means the end of a full row; the row under it is the one Rows() added.
        // A bar past its cap shows the editor's scrollbar, which takes the field's last column.
        var field = FieldWidth(ActualWidth > 0 ? ActualWidth : Width ?? 80);
        var right = Margin.Left + MarkupParser.StripLength(Prompt) + field
            - (WrappedRowCount(field) > ViewportHeight ? 1 : 0);
        if (caret.X >= right && caret.Y + 1 - Margin.Top < ViewportHeight)
        {
            return new Point(Margin.Left + MarkupParser.StripLength(Prompt), caret.Y + 1);
        }

        return caret.X >= right ? caret with { X = right - 1 } : caret;
    }

    Point? ILogicalCursorProvider.GetLogicalCursorPosition() => GetLogicalCursorPosition();

    /// <summary>
    /// Drops the selection when another control's starts. Called by the window's <c>SelectionManager</c>;
    /// the editor's own clear resets the selection without repainting it, which would leave the band on
    /// screen until the next keystroke.
    /// </summary>
    public new void ClearSelection()
    {
        var had = HasSelection;
        base.ClearSelection();
        if (had)
        {
            Invalidate(Invalidation.Repaint);
        }
    }

    void ISelectableControl.ClearSelection() => ClearSelection();

    /// <summary>
    /// A press on a bar arms it first — the second way (after ⇥) to say which line ⏎ sends from — and
    /// then the editor places the caret, starts a drag or selects a word.
    /// </summary>
    public new bool ProcessMouseEvent(MouseEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Pressed)
            || args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1Clicked)
            || args.HasFlag(SharpConsoleUI.Drivers.MouseFlags.Button1DoubleClicked))
        {
            Arm();
        }

        return base.ProcessMouseEvent(args);
    }

    bool IMouseAwareControl.ProcessMouseEvent(MouseEventArgs args) => ProcessMouseEvent(args);

    private void Arm()
    {
        if (!HasFocus && IsEnabled)
        {
            this.GetParentWindow()?.FocusManager.SetFocus(this, FocusReason.Mouse);
        }

        ActivationRequested?.Invoke(this);
    }

    /// <summary>
    /// The prompt, drawn as a gutter: as wide as the prompt on every row, written on the first and blank
    /// under it, so a wrapped line indents past the prompt rather than running back under it.
    /// </summary>
    private sealed class PromptGutter(InputBarControl bar) : IGutterRenderer
    {
        public string Markup { get; set; } = string.Empty;

        // The prompt's width is re-read on every measure, so there is nothing for this to announce.
        public event EventHandler? Invalidated { add { } remove { } }

        public int GetWidth(int totalLineCount) => MarkupParser.StripLength(Markup);

        public void Render(in GutterRenderContext context, int width)
        {
            var band = bar.Band;
            var ink = bar.Ink;
            for (var x = 0; x < width; x++)
            {
                context.Buffer.SetNarrowCell(context.X + x, context.Y, ' ', ink, band);
            }

            if (context.SourceLineIndex == 0 && context.IsFirstWrappedSegment)
            {
                context.Buffer.WriteCellsClipped(
                    context.X, context.Y, MarkupParser.Parse(Markup, ink, band),
                    new LayoutRect(context.X, context.Y, width, 1));
            }
        }
    }
}
