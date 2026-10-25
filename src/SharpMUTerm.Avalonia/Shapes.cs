using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The characters drawn as geometry instead of glyphs, so they meet their neighbours edge to edge: block
/// elements (U+2580–U+259F, the half-block pictures and the focus <c>▌</c>), the light, heavy and rounded
/// box-drawing lines, and the powerline wedges the header and status row are built from.
/// </summary>
internal static class Shapes
{
    // Box drawing as arms: up, right, down, left — 0 none, 1 light, 2 heavy.
    private static readonly Dictionary<int, (byte Up, byte Right, byte Down, byte Left)> Lines = new()
    {
        [0x2500] = (0, 1, 0, 1), [0x2501] = (0, 2, 0, 2), [0x2502] = (1, 0, 1, 0), [0x2503] = (2, 0, 2, 0),
        [0x250C] = (0, 1, 1, 0), [0x250F] = (0, 2, 2, 0), [0x2510] = (0, 0, 1, 1), [0x2513] = (0, 0, 2, 2),
        [0x2514] = (1, 1, 0, 0), [0x2517] = (2, 2, 0, 0), [0x2518] = (1, 0, 0, 1), [0x251B] = (2, 0, 0, 2),
        [0x251C] = (1, 1, 1, 0), [0x2523] = (2, 2, 2, 0), [0x2524] = (1, 0, 1, 1), [0x252B] = (2, 0, 2, 2),
        [0x252C] = (0, 1, 1, 1), [0x2533] = (0, 2, 2, 2), [0x2534] = (1, 1, 0, 1), [0x253B] = (2, 2, 0, 2),
        [0x253C] = (1, 1, 1, 1), [0x254B] = (2, 2, 2, 2),
        [0x2574] = (0, 0, 0, 1), [0x2575] = (1, 0, 0, 0), [0x2576] = (0, 1, 0, 0), [0x2577] = (0, 0, 1, 0),
    };

    public static bool TryDraw(SKCanvas canvas, int codePoint, float left, float top, float width, float height, SKPaint paint, float stroke)
    {
        // Blocks and straight lines are pixel-aligned rectangles, and antialiasing their edges would blend
        // each one into the cell beside it.
        if (codePoint is >= 0x2580 and <= 0x259F)
        {
            WithoutAntialias(paint, () => Block(canvas, codePoint, left, top, width, height, paint));
            return true;
        }

        if (Lines.TryGetValue(codePoint, out var arms))
        {
            WithoutAntialias(paint, () => Arms(canvas, arms, left, top, width, height, paint, stroke));
            return true;
        }

        if (codePoint is >= 0x256D and <= 0x2570)
        {
            Rounded(canvas, codePoint, left, top, width, height, paint, stroke);
            return true;
        }

        if (codePoint is >= 0xE0B0 and <= 0xE0B3)
        {
            Powerline(canvas, codePoint, left, top, width, height, paint, stroke);
            return true;
        }

        return false;
    }

    private static void WithoutAntialias(SKPaint paint, Action draw)
    {
        var antialias = paint.IsAntialias;
        paint.IsAntialias = false;
        draw();
        paint.IsAntialias = antialias;
    }

    private static void Block(SKCanvas canvas, int codePoint, float l, float t, float w, float h, SKPaint paint)
    {
        void Rect(float x0, float y0, float x1, float y1) =>
            canvas.DrawRect(SKRect.Create(l + (x0 * w), t + (y0 * h), (x1 - x0) * w, (y1 - y0) * h), paint);

        switch (codePoint)
        {
            case 0x2580: Rect(0, 0, 1, 0.5f); break;
            case >= 0x2581 and <= 0x2588: Rect(0, 1 - ((codePoint - 0x2580) / 8f), 1, 1); break;
            case >= 0x2589 and <= 0x258F: Rect(0, 0, (0x2590 - codePoint) / 8f, 1); break;
            case 0x2590: Rect(0.5f, 0, 1, 1); break;
            case >= 0x2591 and <= 0x2593:
                {
                    var alpha = paint.Color.Alpha;
                    paint.Color = paint.Color.WithAlpha((byte)(alpha * (codePoint - 0x2590) / 4));
                    Rect(0, 0, 1, 1);
                    paint.Color = paint.Color.WithAlpha(alpha);
                    break;
                }

            case 0x2594: Rect(0, 0, 1, 1 / 8f); break;
            case 0x2595: Rect(7 / 8f, 0, 1, 1); break;
            default:
                {
                    // Quadrants: upper-left, upper-right, lower-left, lower-right.
                    var (ul, ur, ll, lr) = codePoint switch
                    {
                        0x2596 => (false, false, true, false),
                        0x2597 => (false, false, false, true),
                        0x2598 => (true, false, false, false),
                        0x2599 => (true, false, true, true),
                        0x259A => (true, false, false, true),
                        0x259B => (true, true, true, false),
                        0x259C => (true, true, false, true),
                        0x259D => (false, true, false, false),
                        0x259E => (false, true, true, false),
                        _ => (false, true, true, true),
                    };
                    if (ul) Rect(0, 0, 0.5f, 0.5f);
                    if (ur) Rect(0.5f, 0, 1, 0.5f);
                    if (ll) Rect(0, 0.5f, 0.5f, 1);
                    if (lr) Rect(0.5f, 0.5f, 1, 1);
                    break;
                }
        }
    }

    private static void Arms(SKCanvas canvas, (byte Up, byte Right, byte Down, byte Left) arms, float l, float t, float w, float h, SKPaint paint, float stroke)
    {
        var light = MathF.Max(1, MathF.Round(stroke));
        float Thick(byte weight) => weight == 2 ? light * 2 : light;

        // The centre lines are snapped to whole pixels so a column of │ is one crisp line, not two blurred ones.
        var cx = l + MathF.Floor(w / 2);
        var cy = t + MathF.Floor(h / 2);
        var vertical = Thick(Math.Max(arms.Up, arms.Down));
        var horizontal = Thick(Math.Max(arms.Left, arms.Right));
        if (arms.Up > 0) canvas.DrawRect(cx - (Thick(arms.Up) / 2), t, Thick(arms.Up), cy - t + (horizontal / 2), paint);
        if (arms.Down > 0) canvas.DrawRect(cx - (Thick(arms.Down) / 2), cy - (horizontal / 2), Thick(arms.Down), t + h - cy + (horizontal / 2), paint);
        if (arms.Left > 0) canvas.DrawRect(l, cy - (Thick(arms.Left) / 2), cx - l + (vertical / 2), Thick(arms.Left), paint);
        if (arms.Right > 0) canvas.DrawRect(cx - (vertical / 2), cy - (Thick(arms.Right) / 2), l + w - cx + (vertical / 2), Thick(arms.Right), paint);
    }

    private static void Rounded(SKCanvas canvas, int codePoint, float l, float t, float w, float h, SKPaint paint, float stroke)
    {
        var thickness = MathF.Max(1, MathF.Round(stroke));
        var cx = l + MathF.Floor(w / 2) + (thickness / 2);
        var cy = t + MathF.Floor(h / 2) + (thickness / 2);
        var r = MathF.Min(w, h) / 2;
        using var path = new SKPath();
        switch (codePoint)
        {
            case 0x256D: // ╭ right and down
                path.MoveTo(l + w, cy);
                path.LineTo(cx + r, cy);
                path.QuadTo(cx, cy, cx, cy + r);
                path.LineTo(cx, t + h);
                break;
            case 0x256E: // ╮ left and down
                path.MoveTo(l, cy);
                path.LineTo(cx - r, cy);
                path.QuadTo(cx, cy, cx, cy + r);
                path.LineTo(cx, t + h);
                break;
            case 0x256F: // ╯ left and up
                path.MoveTo(l, cy);
                path.LineTo(cx - r, cy);
                path.QuadTo(cx, cy, cx, cy - r);
                path.LineTo(cx, t);
                break;
            default: // ╰ right and up
                path.MoveTo(l + w, cy);
                path.LineTo(cx + r, cy);
                path.QuadTo(cx, cy, cx, cy - r);
                path.LineTo(cx, t);
                break;
        }

        var style = paint.Style;
        var width = paint.StrokeWidth;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = thickness;
        canvas.DrawPath(path, paint);
        paint.Style = style;
        paint.StrokeWidth = width;
    }

    private static void Powerline(SKCanvas canvas, int codePoint, float l, float t, float w, float h, SKPaint paint, float stroke)
    {
        using var path = new SKPath();
        var solid = codePoint is 0xE0B0 or 0xE0B2;
        var pointsRight = codePoint is 0xE0B0 or 0xE0B1;
        if (pointsRight)
        {
            path.MoveTo(l, t);
            path.LineTo(l + w, t + (h / 2));
            path.LineTo(l, t + h);
        }
        else
        {
            path.MoveTo(l + w, t);
            path.LineTo(l, t + (h / 2));
            path.LineTo(l + w, t + h);
        }

        var style = paint.Style;
        var width = paint.StrokeWidth;
        if (solid)
        {
            path.Close();
            paint.Style = SKPaintStyle.Fill;
        }
        else
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = MathF.Max(1, stroke);
        }

        canvas.DrawPath(path, paint);
        paint.Style = style;
        paint.StrokeWidth = width;
    }
}
