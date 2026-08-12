using System.Globalization;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Turns a rendered frame — cursor-addressed ANSI, the way the driver emits it — back into a grid of
/// cells, so a test can ask what is <em>on screen</em> at a row and column.
/// <para>
/// It exists because the questions worth asking about the input area are about painted cells. A caret
/// test written against <c>InputBarControl.GetLogicalCursorPosition</c> agrees with the code it is
/// testing and can disagree with the screen for as long as nobody looks; the frame cannot lie. Only the
/// characters are kept by <see cref="Decode"/> — the <em>background</em> of each cell is
/// <see cref="Backgrounds"/>'s answer, beside it, because the two questions are asked of the same
/// escapes and a suite that walked them twice could come to two views of one frame.
/// </para>
/// </summary>
internal static class FrameGrid
{
    /// <summary>Decodes <paramref name="frame"/> into <paramref name="height"/> rows of text.</summary>
    internal static IReadOnlyList<string> Decode(string frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var cells = new char[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[y, x] = ' ';
            }
        }

        int row = 0, column = 0, i = 0;
        while (i < frame.Length)
        {
            if (frame[i] == '\u001b' && i + 1 < frame.Length && frame[i + 1] == '[')
            {
                var end = i + 2;
                while (end < frame.Length && !char.IsLetter(frame[end]))
                {
                    end++;
                }

                // Only cursor addressing moves the write head; every other sequence here is styling.
                if (end < frame.Length && frame[end] == 'H')
                {
                    var parts = frame[(i + 2)..end].Split(';');
                    row = parts.Length > 0 && int.TryParse(parts[0], out var r) ? r - 1 : 0;
                    column = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c - 1 : 0;
                }

                i = end + 1;
                continue;
            }

            var ch = frame[i];
            i++;
            if (ch == '\n')
            {
                row++;
                column = 0;
                continue;
            }

            if (ch == '\r')
            {
                column = 0;
                continue;
            }

            if (row >= 0 && row < height && column >= 0 && column < width)
            {
                cells[row, column] = ch;
            }

            column++;
        }

        var lines = new List<string>(height);
        for (var y = 0; y < height; y++)
        {
            var line = new System.Text.StringBuilder(width);
            for (var x = 0; x < width; x++)
            {
                line.Append(cells[y, x]);
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    /// <summary>One painted cell: the glyph on screen and the two colours it was written in.</summary>
    internal readonly record struct Cell(char Glyph, Rgb? Foreground, Rgb? Background);

    /// <summary>
    /// The frame as a <c>{(row, column): cell}</c> grid — the glyph <em>and</em> both its colours, which is
    /// what separates this from <see cref="Decode"/> (glyphs only) and <see cref="Backgrounds"/>
    /// (backgrounds only).
    /// <para>
    /// <b>It is a grid rather than a stream, and that is the point.</b> The frame is cursor-addressed, so
    /// a walker that reads it linearly counts every glyph the driver <em>wrote</em> rather than the ones
    /// left <em>on screen</em>; a cell painted and then overwritten would be counted twice, in two
    /// different colours. That direction of error is safe for a contrast audit — the stream is a superset
    /// of the screen, so it can raise a false alarm but never miss an offender — and on the 72 frames
    /// <see cref="FrameContrastTests"/> walks the two agree exactly, glyph for glyph. Asking the grid
    /// anyway costs nothing and removes the caveat.
    /// </para>
    /// <para>
    /// Only the escape positions are handed to a regex. <c>Regex.Match(input, startat)</c> searches
    /// <em>forward</em> to the next match anywhere in the rest of the string, so asking at every character
    /// re-scans each upcoming sequence from progressively later starts — quadratic in the length of every
    /// unstyled run, and a frame is mostly padding spaces.
    /// </para>
    /// </summary>
    internal static Dictionary<(int Row, int Column), Cell> Cells(string frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var cells = new Dictionary<(int, int), Cell>();
        foreach (var (at, cell) in Walk(frame, width, height))
        {
            cells[at] = cell;
        }

        return cells;
    }

    /// <summary>
    /// The one walk: every glyph the frame writes, in order, with the position and the colours in force
    /// when it was written. Both <see cref="Cells"/> and <see cref="Backgrounds"/> are projections of
    /// it, so the suite cannot come to two views of one frame.
    /// </summary>
    private static IEnumerable<((int Row, int Column) At, Cell Cell)> Walk(string frame, int width, int height)
    {
        Rgb? foreground = null;
        Rgb? background = null;
        int row = 0, column = 0, i = 0;

        while (i < frame.Length)
        {
            if (frame[i] == '\u001b')
            {
                var sgr = SgrPattern.Match(frame, i);
                if (sgr.Success && sgr.Index == i)
                {
                    ApplySgr(sgr.Groups[1].Value, ref foreground, ref background);
                    i = sgr.Index + sgr.Length;
                    continue;
                }

                var csi = CsiPattern.Match(frame, i);
                if (csi.Success && csi.Index == i)
                {
                    if (frame[csi.Index + csi.Length - 1] == 'H')
                    {
                        var at = csi.Groups[1].Value.Split(';');
                        row = at.Length > 0 && at[0].Length > 0 ? int.Parse(at[0], CultureInfo.InvariantCulture) - 1 : 0;
                        column = at.Length > 1 && at[1].Length > 0 ? int.Parse(at[1], CultureInfo.InvariantCulture) - 1 : 0;
                    }

                    i = csi.Index + csi.Length;
                    continue;
                }
            }

            var ch = frame[i++];
            if (ch == '\n')
            {
                row++;
                column = 0;
                continue;
            }

            if (ch == '\r')
            {
                column = 0;
                continue;
            }

            if (row >= 0 && row < height && column >= 0 && column < width)
            {
                yield return ((row, column), new Cell(ch, foreground, background));
            }

            column++;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex SgrPattern =
        new(@"\x1b\[([0-9;]*)m", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex CsiPattern =
        new(@"\x1b\[([0-9;?]*)[A-Za-z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Applies one SGR sequence's parameters to the running foreground and background.
    /// <para>
    /// <b>Empty parameters are a reset.</b> ECMA-48 makes <c>CSI m</c> equivalent to <c>CSI 0 m</c>, and
    /// a parser that split an empty string into no codes would run no loop body and leave both colours
    /// standing — so a glyph after it would be audited in colours it is not wearing. Likewise <c>39</c>
    /// and <c>49</c>, which return one channel to the terminal's default without touching the other.
    /// </para>
    /// <para>
    /// None of the three appears in any frame this driver emits — it always writes an explicit
    /// <c>0;38;2;…;48;2;…</c> — so this corrects no live reading. It is here because this walker is the
    /// one every suite that asks about painted cells goes through, and a parser that quietly mistracks
    /// state on a legal sequence is the "quietly green on a frame it has misread" failure this file
    /// warns about, one function down.
    /// </para>
    /// <para>
    /// The indexed forms (<c>38;5;n</c>) are <em>not</em> decoded, and degrade rather than corrupt: the
    /// <c>2</c> guard below fails, so the code and its arguments fall through as unknowns and the colour
    /// stays as it was. This driver emits truecolor only; building the 256-colour cube for a sequence
    /// nothing writes would be inventing coverage.
    /// </para>
    /// </summary>
    private static void ApplySgr(string parameters, ref Rgb? foreground, ref Rgb? background)
    {
        if (parameters.Length == 0)
        {
            foreground = background = null;
            return;
        }

        var codes = parameters.Split(';')
            .Where(p => p.Length > 0)
            .Select(p => int.Parse(p, CultureInfo.InvariantCulture))
            .ToList();

        for (var i = 0; i < codes.Count;)
        {
            if (codes[i] == 0)
            {
                foreground = background = null;
                i++;
            }
            else if (codes[i] == 39)
            {
                foreground = null;
                i++;
            }
            else if (codes[i] == 49)
            {
                background = null;
                i++;
            }
            else if (codes[i] is 38 or 48 && i + 4 < codes.Count && codes[i + 1] == 2)
            {
                var colour = new Rgb((byte)codes[i + 2], (byte)codes[i + 3], (byte)codes[i + 4]);
                if (codes[i] == 38)
                {
                    foreground = colour;
                }
                else
                {
                    background = colour;
                }

                i += 5;
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>The truecolor background escape a colour is written as, e.g. <c>48;2;51;57;76</c>.</summary>
    internal static string Sgr(SharpConsoleUI.Color color) => $"48;2;{color.R};{color.G};{color.B}";

    /// <summary>
    /// Walks a frame into a <c>{(row, column): background}</c> grid, the way a terminal walks it: the last
    /// <c>48;2;r;g;b</c> seen is the background of every cell written until the next one. Note <c>48</c>
    /// and not <c>38</c> — reading foreground here and concluding about bands is the classic mistake.
    /// <para>
    /// <b>One copy, deliberately.</b> This walker parses SGR parameters and cursor addressing, and it lived
    /// verbatim in three suites (<c>FocusIndicationTests</c>, <c>PaneJumpTests</c>,
    /// <c>WindowJumpTests</c>) that all assert about painted planes. Three copies of a parser can drift
    /// into disagreeing about which cells are painted, and the failure that produces is a suite going
    /// quietly green on a frame it has misread — not a visible break.
    /// </para>
    /// </summary>
    internal static Dictionary<(int Row, int Column), string?> Backgrounds(string ansi)
    {
        ArgumentNullException.ThrowIfNull(ansi);

        // Derived from Cells rather than walked again. This used to be its own parser, and the two had
        // already drifted: it tested for the background reset with `parameters.Contains("49")`, which
        // reads the 49 in a truecolor *argument* — `38;2;49;5;6`, a foreground whose red channel is 49 —
        // as the reset code, and clears a background that sequence never mentions. Splitting on `;` is
        // not enough to see that either; only a walk that consumes `38;2;r;g;b` as one unit can tell an
        // SGR code from a colour argument, and Cells already does.
        //
        // Unbounded, as this always was: callers ask about cells at coordinates the frame chose, so
        // there is no width and height to clamp to here.
        var cells = new Dictionary<(int, int), string?>();
        foreach (var (at, cell) in Walk(ansi, int.MaxValue, int.MaxValue))
        {
            cells[at] = cell.Background is { } bg ? $"48;2;{bg.R};{bg.G};{bg.B}" : null;
        }

        return cells;
    }

    /// <summary>How many cells of a frame are painted in a given background.</summary>
    internal static int CellsPainted(string ansi, SharpConsoleUI.Color colour)
    {
        var wanted = Sgr(colour);
        return Backgrounds(ansi).Values.Count(bg => bg?.StartsWith(wanted, StringComparison.Ordinal) == true);
    }

    /// <summary>How many cells inside a rectangle are painted in a given background.</summary>
    internal static int CellsPaintedIn(string ansi, SharpMUTerm.Core.Workspaces.PaneRect rect, SharpConsoleUI.Color colour)
    {
        var wanted = Sgr(colour);
        var cells = Backgrounds(ansi);
        var count = 0;
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                if (cells.GetValueOrDefault((y, x))?.StartsWith(wanted, StringComparison.Ordinal) == true)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Markup with its style and link tags removed and its <c>[[</c>/<c>]]</c> escapes undone — a row's
    /// <em>visible</em> cells, which is what a rail assertion is about. A rail row is wrapped in a
    /// <c>[link=cmd%3Acharacter%3AAlfa.Ann]</c> span, so matching raw markup finds names inside link
    /// targets as well as in the text, which is a false positive nobody spots.
    /// </summary>
    internal static string Visible(string markup) =>
        System.Text.RegularExpressions.Regex
            .Replace(markup, @"\[(?:/|[^\]\[]*)\]", string.Empty)
            .Replace("[[", "[")
            .Replace("]]", "]");
}
