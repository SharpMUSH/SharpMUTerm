namespace SharpMUTerm.Tui;

/// <summary>
/// The text and caret of one command line, and every edit that can be made to them.
/// <para>
/// It holds a string that may contain newlines, which is the whole point: the framework's
/// <c>PromptControl</c> is single-line by construction — its setter replaces <c>\n</c> with a space and
/// it paints exactly one row — so a command line that wraps, grows, and takes a newline chord cannot be
/// one. Editing lives here rather than in <see cref="InputBarControl"/> so that what a keystroke does
/// to the text is testable without a terminal, the way <see cref="InputLayout"/>'s geometry is; the
/// control is then only the part that paints it and the part that receives keys.
/// </para>
/// <para>
/// Every mutator reports whether it changed anything. The control raises its change event on
/// <c>true</c> and stays quiet on <c>false</c>, so pressing ← at the start of the line does not record
/// a draft, and the caret moves that do nothing do not repaint.
/// </para>
/// </summary>
internal sealed class InputBuffer
{
    private string _text = string.Empty;
    private int _caret;

    // Where a selection was started, or null when there is none. The other end is always the caret, so
    // every move that extends a selection is an ordinary caret move made with the anchor held still.
    private int? _anchor;

    /// <summary>The whole buffer, newlines and all — what ⏎ sends and what the draft store keeps.</summary>
    internal string Text => _text;

    /// <summary>The caret's index into <see cref="Text"/>; may equal its length.</summary>
    internal int Caret => _caret;

    /// <summary>Whether there is nothing to send.</summary>
    internal bool IsEmpty => _text.Length == 0;

    /// <summary>Whether the buffer spans more than one typed line.</summary>
    internal bool IsMultiline => _text.Contains('\n');

    /// <summary>Whether some of the text is selected — an anchor set and the caret moved away from it.</summary>
    internal bool HasSelection => _anchor is { } anchor && anchor != _caret;

    /// <summary>The selected range as a start offset and a length, or an empty range at the caret.</summary>
    internal (int Start, int Length) Selection =>
        _anchor is { } anchor ? (Math.Min(anchor, _caret), Math.Abs(anchor - _caret)) : (_caret, 0);

    /// <summary>The selected text, or empty when nothing is selected.</summary>
    internal string SelectedText
    {
        get
        {
            var (start, length) = Selection;
            return _text.Substring(start, length);
        }
    }

    /// <summary>
    /// Makes a caret move extend the selection instead of replacing it: the anchor is dropped where the
    /// caret was if there was no selection yet, and stays put while <paramref name="move"/> runs. Every
    /// shifted key and a mouse drag go through this, so selecting reuses the moves that already exist
    /// rather than growing a second set of them. Reports whether the caret moved.
    /// </summary>
    internal bool Extend(Func<InputBuffer, bool> move)
    {
        ArgumentNullException.ThrowIfNull(move);
        var anchor = _anchor ?? _caret;
        var moved = move(this);
        _anchor = anchor;
        return moved;
    }

    /// <summary>Selects the given range, leaving the caret at its end.</summary>
    internal bool Select(int start, int end)
    {
        start = Math.Clamp(start, 0, _text.Length);
        end = Math.Clamp(end, 0, _text.Length);
        var changed = _anchor != start || _caret != end;
        _anchor = start;
        _caret = end;
        return changed;
    }

    /// <summary>Selects the run of non-space characters around an offset — a double-click.</summary>
    internal bool SelectWordAt(int offset)
    {
        var start = Math.Clamp(offset, 0, _text.Length);
        var end = start;
        while (start > 0 && !char.IsWhiteSpace(_text[start - 1]))
        {
            start--;
        }

        while (end < _text.Length && !char.IsWhiteSpace(_text[end]))
        {
            end++;
        }

        return Select(start, end);
    }

    /// <summary>Drops the selection, leaving the text and caret alone. Reports whether there was one.</summary>
    internal bool Deselect()
    {
        var had = HasSelection;
        _anchor = null;
        return had;
    }

    /// <summary>Deletes the selected text, leaving the caret where it began. Reports whether any went.</summary>
    private bool DeleteSelection()
    {
        if (!HasSelection)
        {
            _anchor = null;
            return false;
        }

        var (start, length) = Selection;
        _text = _text.Remove(start, length);
        _caret = start;
        _anchor = null;
        return true;
    }

    /// <summary>
    /// Replaces the whole buffer and puts the caret at the end — a recalled draft, a history entry, or
    /// a link's <c>prompt:</c> payload. Reports whether the text actually differs, so recalling the
    /// same text twice is not an edit.
    /// </summary>
    internal bool Set(string? text)
    {
        var value = text ?? string.Empty;
        var changed = !string.Equals(value, _text, StringComparison.Ordinal);
        _anchor = null;
        _text = value;
        _caret = value.Length;
        return changed;
    }

    /// <summary>Empties the buffer, reporting whether there was anything in it.</summary>
    internal bool Clear() => Set(string.Empty);

    /// <summary>
    /// Inserts text at the caret and leaves the caret after it. A selection is replaced by what is
    /// typed or pasted, which is what every text field does and what makes a selection safe to leave
    /// lying around: the next keystroke either uses it or removes it.
    /// </summary>
    internal bool Insert(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        DeleteSelection();
        _text = _text.Insert(_caret, text);
        _caret += text.Length;
        return true;
    }

    /// <summary>Inserts one character at the caret.</summary>
    internal bool Insert(char c) => Insert(c.ToString());

    /// <summary>Breaks the line at the caret — the multiline chord's whole effect.</summary>
    internal bool InsertNewline() => Insert('\n');

    /// <summary>Deletes the character before the caret.</summary>
    internal bool Backspace()
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (_caret == 0)
        {
            return false;
        }

        _text = _text.Remove(_caret - 1, 1);
        _caret--;
        return true;
    }

    /// <summary>Deletes the character at the caret.</summary>
    internal bool Delete()
    {
        if (DeleteSelection())
        {
            return true;
        }

        if (_caret >= _text.Length)
        {
            return false;
        }

        _text = _text.Remove(_caret, 1);
        return true;
    }

    /// <summary>Deletes from the caret to the end of the buffer (⌃K).</summary>
    internal bool KillToEnd()
    {
        _anchor = null;
        if (_caret >= _text.Length)
        {
            return false;
        }

        _text = _text[.._caret];
        return true;
    }

    /// <summary>Deletes from the start of the buffer to the caret (⌃U).</summary>
    internal bool KillToStart()
    {
        _anchor = null;
        if (_caret == 0)
        {
            return false;
        }

        _text = _text[_caret..];
        _caret = 0;
        return true;
    }

    /// <summary>
    /// Deletes the word before the caret — readline's backward-kill-word. <b>No keystroke reaches it.</b>
    /// It was reached from ⌃W, which the application claims globally for <c>CloseActiveWindow</c>, and a
    /// global shortcut runs before any window: the case in <see cref="InputBarControl.ProcessKey"/> could
    /// never have fired and has been removed. This is kept because the editing operation is correct and
    /// tested, and what it is missing is a chord this host can deliver — see that key table for why
    /// Alt+Backspace is not one.
    /// </summary>
    internal bool KillWordLeft()
    {
        _anchor = null;
        var start = WordLeft();
        if (start == _caret)
        {
            return false;
        }

        _text = _text.Remove(start, _caret - start);
        _caret = start;
        return true;
    }

    /// <summary>Moves the caret to an absolute offset, clamped into the buffer.</summary>
    internal bool MoveTo(int offset)
    {
        var target = Math.Clamp(offset, 0, _text.Length);
        if (target == _caret)
        {
            return false;
        }

        _caret = target;
        return true;
    }

    /// <summary>Moves the caret one character left.</summary>
    internal bool MoveLeft() => MoveTo(_caret - 1);

    /// <summary>Moves the caret one character right.</summary>
    internal bool MoveRight() => MoveTo(_caret + 1);

    /// <summary>Moves the caret one word left (⌃←).</summary>
    internal bool MoveWordLeft() => MoveTo(WordLeft());

    /// <summary>Moves the caret one word right (⌃→).</summary>
    internal bool MoveWordRight() => MoveTo(WordRight());

    /// <summary>Moves the caret to the start of the visual row it is on.</summary>
    internal bool MoveHome(int width)
    {
        var rows = InputLayout.Wrap(_text, width);
        var (row, _) = InputLayout.Caret(rows, _caret);
        return MoveTo(rows[row].Start);
    }

    /// <summary>Moves the caret to the end of the visual row it is on.</summary>
    internal bool MoveEnd(int width)
    {
        var rows = InputLayout.Wrap(_text, width);
        var (row, _) = InputLayout.Caret(rows, _caret);
        return MoveTo(rows[row].End);
    }

    /// <summary>
    /// Moves the caret one visual row up or down, keeping its column where the row is long enough.
    /// Returns false at the top or bottom row, which is what lets ↑/↓ fall through to history recall on
    /// a command line that has nowhere further to go.
    /// </summary>
    internal bool MoveRow(int delta, int width)
    {
        var rows = InputLayout.Wrap(_text, width);
        var (row, column) = InputLayout.Caret(rows, _caret);
        var target = row + delta;
        if (target < 0 || target >= rows.Count)
        {
            return false;
        }

        return MoveTo(InputLayout.Offset(rows, target, column));
    }

    /// <summary>The offset one word to the left: back over spaces, then back over the word itself.</summary>
    private int WordLeft()
    {
        var i = _caret;
        while (i > 0 && char.IsWhiteSpace(_text[i - 1]))
        {
            i--;
        }

        while (i > 0 && !char.IsWhiteSpace(_text[i - 1]))
        {
            i--;
        }

        return i;
    }

    /// <summary>The offset one word to the right: over the word, then over the spaces after it.</summary>
    private int WordRight()
    {
        var i = _caret;
        while (i < _text.Length && !char.IsWhiteSpace(_text[i]))
        {
            i++;
        }

        while (i < _text.Length && char.IsWhiteSpace(_text[i]))
        {
            i++;
        }

        return i;
    }
}
