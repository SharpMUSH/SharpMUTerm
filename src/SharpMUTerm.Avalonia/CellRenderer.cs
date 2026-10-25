using SharpConsoleUI;
using SharpConsoleUI.Core;
using SharpMUTerm.Graphics;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// Paints a <see cref="ScreenFrame"/> onto a Skia canvas, one cell at a time, in three passes: backgrounds,
/// pictures, then text and the caret. It is the terminal emulator's half of the job, and the only part of this
/// host that decides what anything looks like.
/// <para>
/// <b>A Kitty placeholder cell is drawn as its slice of the picture.</b> The cell's character is
/// <c>U+10EEEE</c>, its foreground is the image id and its two combining marks are the picture's row and column
/// (<see cref="KittyGraphicsProtocol.RowColumnDiacritics"/>) — the same cells kitty would have been sent — so a
/// picture scrolls, clips and is cut by a pane edge exactly as the text around it is, with nothing here
/// knowing about panes at all.
/// </para>
/// <para>
/// Block elements, the box-drawing lines this client uses and the two powerline wedges are drawn as shapes
/// rather than glyphs. A font draws them to its own ascent and descent, which are not the cell's, so a half
/// block drawn from a font leaves a gap under it and a column of <c>│</c> is a dashed line.
/// </para>
/// </summary>
internal sealed class CellRenderer(CellFonts fonts, SKColor defaultForeground, SKColor defaultBackground)
{
    private static readonly Dictionary<int, int> DiacriticIndex = KittyGraphicsProtocol.RowColumnDiacritics
        .Select((codePoint, index) => (codePoint, index))
        .ToDictionary(p => p.codePoint, p => p.index);

    public CellFonts Fonts { get; } = fonts;

    public SKColor DefaultForeground { get; } = defaultForeground;

    public SKColor DefaultBackground { get; } = defaultBackground;

    public void Render(SKCanvas canvas, ScreenFrame frame, PictureStore pictures, bool caretShown = true)
    {
        var cw = Fonts.CellWidth;
        var ch = Fonts.CellHeight;
        canvas.Clear(DefaultBackground);

        using var fill = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        // Backgrounds, as runs: one rectangle per stretch of a row sharing a colour.
        for (var y = 0; y < frame.Height; y++)
        {
            var start = 0;
            var current = Background(frame[0, y]);
            for (var x = 1; x <= frame.Width; x++)
            {
                var next = x < frame.Width ? Background(frame[x, y]) : default;
                if (x < frame.Width && next == current)
                {
                    continue;
                }

                if (current != DefaultBackground)
                {
                    fill.Color = current;
                    canvas.DrawRect(start * cw, y * ch, (x - start) * cw, ch, fill);
                }

                start = x;
                current = next;
            }
        }

        DrawPictures(canvas, frame, pictures);

        using var ink = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var cell = frame[x, y];
                if (cell.IsWideContinuation || IsPlaceholder(cell))
                {
                    continue;
                }

                var codePoint = cell.Rune.Value;
                if (codePoint == ' ' && (cell.Decorations & (TextDecoration.Underline | TextDecoration.Strikethrough)) == 0)
                {
                    continue;
                }

                ink.Color = Foreground(cell);
                var span = x + 1 < frame.Width && frame[x + 1, y].IsWideContinuation ? 2 : 1;
                var left = x * cw;
                var top = y * ch;
                if (!Shapes.TryDraw(canvas, codePoint, left, top, cw * span, ch, ink, Fonts.StrokeWidth))
                {
                    DrawGlyph(canvas, cell, left, top, cw * span, ink);
                }

                if ((cell.Decorations & TextDecoration.Underline) != 0)
                {
                    canvas.DrawRect(left, top + Fonts.Baseline + Fonts.UnderlineOffset, cw * span, Fonts.StrokeWidth, ink);
                }

                if ((cell.Decorations & TextDecoration.Strikethrough) != 0)
                {
                    canvas.DrawRect(left, top + (ch / 2f), cw * span, Fonts.StrokeWidth, ink);
                }
            }
        }

        if (caretShown)
        {
            DrawCaret(canvas, frame);
        }
    }

    /// <summary>Which picture, and which cell of it, a placeholder names — or null for any other cell.</summary>
    internal static (uint Id, int Row, int Column)? Placeholder(GridCell cell)
    {
        if (!IsPlaceholder(cell) || cell.Combiners is not { Length: > 0 } marks)
        {
            return null;
        }

        var runes = marks.EnumerateRunes().Select(r => r.Value).ToArray();
        if (runes.Length < 2 || !DiacriticIndex.TryGetValue(runes[0], out var row) || !DiacriticIndex.TryGetValue(runes[1], out var column))
        {
            return null;
        }

        var id = ((uint)cell.Foreground.R << 16) | ((uint)cell.Foreground.G << 8) | cell.Foreground.B;

        // A third mark carries the id's most significant byte, per the protocol. Nothing here sends one.
        if (runes.Length >= 3 && DiacriticIndex.TryGetValue(runes[2], out var high))
        {
            id |= (uint)high << 24;
        }

        return (id, row, column);
    }

    private static bool IsPlaceholder(GridCell cell) => cell.Rune.Value == KittyGraphicsProtocol.PlaceholderCodePoint;

    private void DrawPictures(SKCanvas canvas, ScreenFrame frame, PictureStore pictures)
    {
        if (pictures.Count == 0)
        {
            return;
        }

        var cw = Fonts.CellWidth;
        var ch = Fonts.CellHeight;
        using var paint = new SKPaint { IsAntialias = false };
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (Placeholder(frame[x, y]) is not { } at || !pictures.TryGet(at.Id, out var picture))
                {
                    continue;
                }

                // A run of consecutive cells of one picture row is drawn as one slice, so a cubic resample
                // has neighbours to read at every interior cell boundary and leaves no seam there.
                var run = 1;
                while (x + run < frame.Width && Placeholder(frame[x + run, y]) is { } next
                    && next.Id == at.Id && next.Row == at.Row && next.Column == at.Column + run)
                {
                    run++;
                }

                var sx = picture.Image.Width / (float)picture.Columns;
                var sy = picture.Image.Height / (float)picture.Rows;
                var source = new SKRect(at.Column * sx, at.Row * sy, (at.Column + run) * sx, (at.Row + 1) * sy);
                var dest = new SKRect(x * cw, y * ch, (x + run) * cw, (y + 1) * ch);
                canvas.DrawImage(picture.Image, source, dest, sampling, paint);
                x += run - 1;
            }
        }
    }

    private void DrawGlyph(SKCanvas canvas, GridCell cell, float left, float top, float width, SKPaint ink)
    {
        var bold = (cell.Decorations & TextDecoration.Bold) != 0;
        var italic = (cell.Decorations & TextDecoration.Italic) != 0;
        var font = Fonts.For(cell.Rune.Value, bold, italic);
        var text = cell.Combiners is null ? cell.Rune.ToString() : cell.Rune + cell.Combiners;

        // A fallback face is not held to the cell: an emoji or a CJK glyph from it can be wider than the
        // cells the framework gave it, and would paint over its neighbour. Squeeze it in instead.
        var advance = font.MeasureText(text);
        if (advance > width * 1.05f)
        {
            canvas.Save();
            canvas.Translate(left, 0);
            canvas.Scale(width / advance, 1);
            canvas.DrawText(text, 0, top + Fonts.Baseline, SKTextAlign.Left, font, ink);
            canvas.Restore();
            return;
        }

        canvas.DrawText(text, left, top + Fonts.Baseline, SKTextAlign.Left, font, ink);
    }

    private void DrawCaret(SKCanvas canvas, ScreenFrame frame)
    {
        var caret = frame.Caret;
        if (!caret.Visible || caret.Shape == CursorShape.Hidden
            || caret.X < 0 || caret.Y < 0 || caret.X >= frame.Width || caret.Y >= frame.Height)
        {
            return;
        }

        var cell = frame[caret.X, caret.Y];
        var left = caret.X * Fonts.CellWidth;
        var top = caret.Y * Fonts.CellHeight;
        var thickness = Math.Max(2f, Fonts.StrokeWidth * 1.5f);
        using var paint = new SKPaint { Color = Foreground(cell), IsAntialias = false };
        switch (caret.Shape)
        {
            case CursorShape.VerticalBar:
                canvas.DrawRect(left, top, thickness, Fonts.CellHeight, paint);
                break;
            case CursorShape.Underline:
                canvas.DrawRect(left, top + Fonts.CellHeight - thickness, Fonts.CellWidth, thickness, paint);
                break;
            default:
                // A block, drawn inverted so the character under it stays readable.
                canvas.DrawRect(left, top, Fonts.CellWidth, Fonts.CellHeight, paint);
                if (cell.Rune.Value != ' ')
                {
                    using var ink = new SKPaint { Color = Background(cell), IsAntialias = true };
                    if (!Shapes.TryDraw(canvas, cell.Rune.Value, left, top, Fonts.CellWidth, Fonts.CellHeight, ink, Fonts.StrokeWidth))
                    {
                        DrawGlyph(canvas, cell, left, top, Fonts.CellWidth, ink);
                    }
                }

                break;
        }
    }

    private SKColor Background(GridCell cell)
    {
        var colour = (cell.Decorations & TextDecoration.Invert) != 0 ? cell.Foreground : cell.Background;
        var fallback = (cell.Decorations & TextDecoration.Invert) != 0 ? DefaultForeground : DefaultBackground;
        return Resolve(colour, fallback);
    }

    private SKColor Foreground(GridCell cell)
    {
        var inverted = (cell.Decorations & TextDecoration.Invert) != 0;
        var colour = Resolve(inverted ? cell.Background : cell.Foreground, inverted ? DefaultBackground : DefaultForeground);
        if ((cell.Decorations & TextDecoration.Dim) != 0)
        {
            var behind = Background(cell);
            colour = new SKColor(
                (byte)((colour.Red + behind.Red) / 2),
                (byte)((colour.Green + behind.Green) / 2),
                (byte)((colour.Blue + behind.Blue) / 2));
        }

        return colour;
    }

    /// <summary>
    /// A default colour is the terminal's own, and here this host is the terminal. A transparent one is a
    /// plane handed to the terminal's background (F7's transparency fallback), which a window has no
    /// translucency behind to show — so it is the default background too.
    /// </summary>
    private static SKColor Resolve(Color colour, SKColor fallback) =>
        colour.IsDefault || colour.A == 0 ? fallback : new SKColor(colour.R, colour.G, colour.B);
}
