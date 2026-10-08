using System.Globalization;
using System.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// The cells on one pane line that a server left blank for pictures — a figure laid out inside a box —
/// and what is drawn in them. The line's markup is held in pieces around those cells, so a picture
/// arriving later replaces the blanks it was given and nothing else on the line moves.
/// <para>
/// Mutable on purpose: the line is a <see cref="PaneLine"/> value in a buffer, and this is the one part
/// of it a picture's arrival changes. <see cref="Compose"/> is the line's markup afterwards.
/// </para>
/// </summary>
internal sealed class PictureSlots
{
    private readonly string[] _pieces;
    private readonly Slot[] _slots;

    /// <param name="pieces">
    /// The line's markup cut around the slots (<see cref="MarkupFormatter.ToMarkupPieces"/>): the text
    /// before the first slot, the first slot's blanks, the text after it, and so on.
    /// </param>
    /// <param name="slots">Each slot: the picture it belongs to, its row of that picture, and its width.</param>
    public PictureSlots(string[] pieces, IReadOnlyList<(long Picture, int Row, int Columns)> slots)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(slots);
        if (pieces.Length != slots.Count * 2 + 1)
        {
            throw new ArgumentException("A slot needs the text either side of it.", nameof(pieces));
        }

        _pieces = pieces;
        _slots = slots.Select(slot => new Slot(slot.Picture, slot.Row, slot.Columns)).ToArray();
    }

    /// <summary>Whether a row of <paramref name="picture"/> is on this line.</summary>
    public bool Holds(long picture) => _slots.Any(slot => slot.Picture == picture);

    /// <summary>
    /// Draws <paramref name="rows"/> — a picture's rows, top first — into this line's slot for it. A row
    /// narrower than the slot is padded with blanks; a picture with fewer rows leaves the blanks it had.
    /// </summary>
    /// <returns>Whether anything on this line changed.</returns>
    public bool Draw(long picture, IReadOnlyList<string> rows)
    {
        var changed = false;
        foreach (var slot in _slots)
        {
            if (slot.Picture != picture || slot.Row >= rows.Count)
            {
                continue;
            }

            var row = rows[slot.Row];
            var pad = slot.Columns - Cells(row);
            slot.Drawn = pad > 0 ? row + new string(' ', pad) : row;
            changed = true;
        }

        return changed;
    }

    /// <summary>The line's markup with what has been drawn so far in place of the blanks it replaced.</summary>
    public string Compose()
    {
        var sb = new StringBuilder(_pieces[0]);
        for (var i = 0; i < _slots.Length; i++)
        {
            sb.Append(_slots[i].Drawn ?? _pieces[i * 2 + 1]).Append(_pieces[i * 2 + 2]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The cells a picture row takes: one per text element, since a Kitty placeholder is a code point with
    /// its row and column diacritics combined onto it.
    /// </summary>
    internal static int Cells(string row) => new StringInfo(MarkupText.Plain(row)).LengthInTextElements;

    private sealed class Slot(long picture, int row, int columns)
    {
        public long Picture { get; } = picture;

        public int Row { get; } = row;

        public int Columns { get; } = columns;

        public string? Drawn { get; set; }
    }
}
