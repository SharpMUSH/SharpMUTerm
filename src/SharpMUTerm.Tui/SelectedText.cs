using System.Reflection;
using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace SharpMUTerm.Tui;

/// <summary>
/// The text a pane's selection copies, with the spaces a word-wrap broke at put back.
/// <para>
/// <b>The framework's copy loses them, and that is an upstream defect.</b> <c>MarkupParser.WrapCellLine</c>
/// breaks a long line at a space and <em>consumes</em> the run of spaces it broke at — trailing ones are
/// trimmed off the row, leading ones skipped on the next — so <c>the courier's road</c> is drawn as
/// <c>the courier's</c> over <c>road</c>. That is right for the paint. But
/// <c>MarkupControl.RenderRowsToText</c> joins a soft-wrap continuation onto the row before it with
/// nothing between them, so the copy read <c>the courier'sroad</c>: a space gone at every wrap point of
/// every line, which is what was reported. Unchanged through SharpConsoleUI 2.7.8.
/// </para>
/// <para>
/// <b>How many spaces were taken is not on the row, so it is recovered from the line.</b> A wrap can take
/// a run (a MUSH pads columns and sentences with more than one), and a break between two CJK characters
/// takes none, so "put one space back" would be wrong both ways. The rows of one logical line are slices of
/// that line in order with only breakable spaces between them — the same walk the framework's own
/// <c>SliceSpansAcrossRows</c> does to lay links across a wrap — so re-parsing the line the rows came from
/// gives each row's offset, and the gap between one row's end and the next's start is exactly what the
/// wrap took.
/// </para>
/// <para>
/// <b>It reads the control's private layout cache by reflection</b>, the grid the selection was hit-tested
/// against, because nothing public says which rows a selection covers. Each row is checked against the line
/// it is claimed to be a slice of, and any surprise — a field renamed in a later release, a row that does
/// not match — hands back the framework's own text rather than a guess. <c>PaneSelectionTests</c> pins the
/// spaces, so a release that moves those fields fails a test instead of quietly closing words up again.
/// </para>
/// </summary>
internal static class SelectedText
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? RowsField = typeof(MarkupControl).GetField("_cachedRows", Private);
    private static readonly FieldInfo? SourcesField = typeof(MarkupControl).GetField("_cachedRowSourceLine", Private);
    private static readonly FieldInfo? SoftField = typeof(MarkupControl).GetField("_cachedRowIsSoftWrapContinuation", Private);
    private static readonly FieldInfo? ContentField = typeof(MarkupControl).GetField("_content", Private);
    private static readonly FieldInfo? AnchorRowField = typeof(MarkupControl).GetField("_selAnchorRow", Private);
    private static readonly FieldInfo? AnchorColField = typeof(MarkupControl).GetField("_selAnchorCol", Private);
    private static readonly FieldInfo? EndRowField = typeof(MarkupControl).GetField("_selEndRow", Private);
    private static readonly FieldInfo? EndColField = typeof(MarkupControl).GetField("_selEndCol", Private);

    /// <summary>What <paramref name="control"/> has selected, as plain text, or empty when nothing is.</summary>
    internal static string Of(MarkupControl control)
    {
        var framework = control.GetSelectedText();
        return framework.Length == 0 ? framework : Rebuild(control) ?? framework;
    }

    /// <summary>
    /// The selection rebuilt from the painted rows, gaps included — or null whenever anything it reads is
    /// not what it expects, so the caller falls back to the framework's text.
    /// </summary>
    private static string? Rebuild(MarkupControl control)
    {
        if (RowsField?.GetValue(control) is not List<List<Cell>> { Count: > 0 } rows
            || SourcesField?.GetValue(control) is not List<int> sources
            || SoftField?.GetValue(control) is not List<bool> soft
            || ContentField?.GetValue(control) is not List<string> content
            || AnchorRowField?.GetValue(control) is not int anchorRow
            || AnchorColField?.GetValue(control) is not int anchorCol
            || EndRowField?.GetValue(control) is not int endRow
            || EndColField?.GetValue(control) is not int endCol
            || sources.Count != rows.Count
            || soft.Count != rows.Count)
        {
            return null;
        }

        // The same ordering and clamping RenderRowsToText applies, so the two agree on what is selected.
        var (startRow, startCol, lastRow, lastCol) =
            anchorRow < endRow || (anchorRow == endRow && anchorCol <= endCol)
                ? (anchorRow, anchorCol, endRow, endCol)
                : (endRow, endCol, anchorRow, anchorCol);
        startRow = Math.Clamp(startRow, 0, rows.Count - 1);
        lastRow = Math.Clamp(lastRow, 0, rows.Count - 1);

        // A row's offset is only known by walking its line from the line's first row.
        var first = startRow;
        while (first > 0 && sources[first - 1] == sources[startRow])
        {
            first--;
        }

        var lines = new List<Cell>?[rows.Count];
        var offsets = new int[rows.Count];
        List<List<Cell>>? hardLines = null;
        var source = -1;
        var hard = -1;
        var cursor = 0;

        for (var row = first; row <= lastRow; row++)
        {
            if (sources[row] != source)
            {
                source = sources[row];
                if (source < 0 || source >= content.Count || soft[row])
                {
                    return null;
                }

                // One row per hard line: at an unbounded width nothing wraps, which is how the framework
                // itself parses for a copy made before any paint.
                hardLines = MarkupParser.ParseLines(
                    content[source], int.MaxValue, Color.White, Color.Black, out _, out _);
                hard = -1;
            }

            if (!soft[row])
            {
                hard++;
                cursor = 0;
            }
            else
            {
                while (cursor < hardLines![hard].Count && IsBreakableSpace(hardLines[hard][cursor]))
                {
                    cursor++;
                }
            }

            if (hard >= hardLines!.Count || !IsSliceOf(rows[row], hardLines[hard], cursor))
            {
                return null;
            }

            lines[row] = hardLines[hard];
            offsets[row] = cursor;
            cursor += rows[row].Count;
        }

        var text = new StringBuilder();
        for (var row = startRow; row <= lastRow; row++)
        {
            if (row > startRow)
            {
                if (!soft[row])
                {
                    text.Append('\n');
                }
                else
                {
                    // The run the wrap took, between the end of the row above and the start of this one.
                    Append(text, lines[row]!, offsets[row - 1] + rows[row - 1].Count, offsets[row]);
                }
            }

            var cells = rows[row];
            var from = Math.Clamp(row == startRow ? startCol : 0, 0, cells.Count);
            var to = Math.Clamp(row == lastRow ? lastCol : cells.Count, 0, cells.Count);
            Append(text, cells, from, to);
        }

        return text.ToString();
    }

    /// <summary>Cells [<paramref name="from"/>, <paramref name="to"/>) as text, the way the framework copies them.</summary>
    private static void Append(StringBuilder text, List<Cell> cells, int from, int to)
    {
        for (var c = from; c < to; c++)
        {
            if (!cells[c].IsWideContinuation)
            {
                text.Append(cells[c].Character.ToString());
            }
        }
    }

    /// <summary>Whether <paramref name="row"/> is the cells of <paramref name="line"/> starting at <paramref name="offset"/>.</summary>
    private static bool IsSliceOf(List<Cell> row, List<Cell> line, int offset)
    {
        if (offset + row.Count > line.Count)
        {
            return false;
        }

        for (var c = 0; c < row.Count; c++)
        {
            if (row[c].Character != line[offset + c].Character
                || row[c].IsWideContinuation != line[offset + c].IsWideContinuation)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The framework's own test for a space a wrap may break at and consume.</summary>
    private static bool IsBreakableSpace(Cell cell) => cell.Character == new Rune(' ') && !cell.IsWideContinuation;
}
