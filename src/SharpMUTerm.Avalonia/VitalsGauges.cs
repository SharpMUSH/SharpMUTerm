using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The vitals panel as native gauges, over the pane whose cells draw the same <see cref="VitalsModel"/> as a strip:
/// a smooth bar per meter with its name and numbers, <c>Mana · low</c> in the warning colour, and what the
/// character is fighting. Drawn in the window's UI font, not the cell font — this is the window's presentation.
/// <para>
/// <b>Two arrangements, chosen from the pane's height alone</b>, so a number arriving never re-arranges it: a pane
/// tall enough for every meter as a block of its own (name and numbers over the bar — the combat layout's side
/// column) stacks them; anything shorter (the strip a layout puts over the output) lays them side by side, each a
/// name, a bar and its numbers.
/// </para>
/// <para>It takes no input (see <see cref="DesktopLayer"/>): the pane under it is the client's.</para>
/// </summary>
internal sealed class VitalsGauges : Control
{
    private VitalsModel _model = VitalsModel.Empty;
    private PanelInk _ink = new("#5fafff", "#ff5f5f", "#3a3a40");
    private Color _plane = Colors.Black;
    private Color _text = Colors.White;
    private double _cellHeight = 16;

    public VitalsGauges()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>What it is showing. Internal for tests.</summary>
    internal VitalsModel Model => _model;

    /// <summary>Whether the meters are stacked (true) or side by side. Internal for tests.</summary>
    internal bool Stacked => StackedFor(Bounds.Height > 0 ? Bounds.Height : Height, _model);

    /// <summary>The bars drawn on the last render, in order: each bar's track and filled part. Internal for tests.</summary>
    internal IReadOnlyList<(Rect Track, Rect Fill, bool Low)> DrawnBars => _drawn;

    private readonly List<(Rect Track, Rect Fill, bool Low)> _drawn = [];

    public void Show(VitalsModel model, PanelInk ink, Color plane, Color text, double cellHeight)
    {
        if (model.Equals(_model) && ink == _ink && plane == _plane && text == _text && Math.Abs(cellHeight - _cellHeight) < 0.01)
        {
            return;
        }

        _model = model;
        _ink = ink;
        _plane = plane;
        _text = text;
        _cellHeight = cellHeight;
        InvalidateVisual();
    }

    /// <summary>Height one stacked block takes: a line of text and a bar under it.</summary>
    private static double BlockHeight(double cellHeight) => Math.Max(30, cellHeight * 2.1);

    private bool StackedFor(double height, VitalsModel model)
    {
        var blocks = model.Meters.Count + (model.Target is null ? 0 : 1);
        return blocks > 0 && height >= (blocks * BlockHeight(_cellHeight)) + 8;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        _drawn.Clear();

        // The plane covers the cells under it: the strip is drawn by this, not beside it.
        context.FillRectangle(new SolidColorBrush(_plane), bounds);
        if (bounds.Width < 8 || bounds.Height < 4 || _model.IsEmpty)
        {
            return;
        }

        if (StackedFor(bounds.Height, _model))
        {
            RenderStacked(context, bounds);
        }
        else
        {
            RenderRow(context, bounds);
        }
    }

    private void RenderStacked(DrawingContext context, Rect bounds)
    {
        var block = BlockHeight(_cellHeight);
        var size = Math.Clamp(_cellHeight * 0.68, 10, 15);
        var barHeight = Math.Clamp(_cellHeight * 0.45, 6, 10);
        var inner = bounds.Deflate(new Thickness(12, 8));
        var y = inner.Top;
        foreach (var meter in _model.Meters)
        {
            var low = meter.Low;
            var title = low ? $"{meter.Title} · low" : meter.Title;
            DrawText(context, title, new Point(inner.Left, y), size, low ? Parse(_ink.Low) : Dim(), bold: low, alignRight: false, inner.Width);
            DrawText(context, Numbers(meter), new Point(inner.Right, y), size, _text, bold: true, alignRight: true, inner.Width);
            var bar = new Rect(inner.Left, y + (size * 1.45), inner.Width, barHeight);
            DrawBar(context, bar, meter.HasMax ? meter.Share ?? 0 : 1, low);
            y += block;
        }

        if (_model.Target is { } target)
        {
            DrawText(context, $"Target · {target.Name}", new Point(inner.Left, y), size, Dim(), bold: false, alignRight: false, inner.Width * 0.75);
            if (target.Share is { } share)
            {
                DrawText(context, Percent(share), new Point(inner.Right, y), size, _text, bold: true, alignRight: true, inner.Width);
                DrawBar(context, new Rect(inner.Left, y + (size * 1.45), inner.Width, barHeight), share, target.Low);
            }
        }
    }

    /// <summary>The narrowest a side-by-side bar is drawn; below it the words get shorter first.</summary>
    private const double MinimumBar = 24;

    private void RenderRow(DrawingContext context, Rect bounds)
    {
        var size = Math.Clamp(_cellHeight * 0.62, 9, 14);
        var barHeight = Math.Clamp(bounds.Height * 0.32, 4, 9);
        var inner = bounds.Deflate(new Thickness(10, 0));
        var gap = 18.0;
        var shares = _model.Meters.Count + (_model.Target is null ? 0 : 1.4);
        var unit = (inner.Width - (gap * Math.Max(0, Math.Ceiling(shares) - 1))) / shares;
        var middle = bounds.Height / 2;
        var top = middle - (size * 0.65);

        // The words shrink before the bars do, one step for every meter at once so the row reads as one:
        // "Mana · low  23 / 120", then "mp!  23/120", then "mp  23".
        static (string Title, string Numbers) Words(VitalsMeter m, int step) => step switch
        {
            0 => (m.Low ? $"{m.Title} · low" : m.Title, Numbers(m)),
            1 => (m.Low ? $"{m.Label}!" : m.Label, m.HasMax ? $"{m.Value}/{m.Max ?? "?"}" : m.Value),
            _ => (m.Label, m.Value),
        };

        var step = 0;
        while (step < 2 && _model.Meters.Any(m =>
               {
                   var (t, n) = Words(m, step);
                   return unit - Measure(t, size, m.Low) - Measure(n, size, true) - 16 < MinimumBar;
               }))
        {
            step++;
        }

        var x = inner.Left;
        foreach (var meter in _model.Meters)
        {
            var low = meter.Low;
            var (title, numbers) = Words(meter, step);
            var titleWidth = DrawText(context, title, new Point(x, top), size, low ? Parse(_ink.Low) : Dim(), bold: low, alignRight: false, unit * 0.45);
            var numbersWidth = DrawText(context, numbers, new Point(x + unit, top), size, _text, bold: true, alignRight: true, unit * 0.45);
            var barLeft = x + titleWidth + 8;
            var barRight = x + unit - numbersWidth - 8;
            if (barRight - barLeft >= 8)
            {
                DrawBar(context, new Rect(barLeft, middle - (barHeight / 2), barRight - barLeft, barHeight), meter.HasMax ? meter.Share ?? 0 : 1, low);
            }

            x += unit + gap;
        }

        if (_model.Target is { } target)
        {
            var width = inner.Right - x;
            var pct = target.Share is { } s ? Percent(s) : null;
            var pctWidth = pct is null ? 0 : Measure(pct, size, true);
            var room = pct is null ? width : width - pctWidth - 16 - MinimumBar;
            var nameWidth = DrawText(context, step == 0 ? $"Target · {target.Name}" : target.Name, new Point(x, top), size, Dim(), bold: false, alignRight: false, Math.Max(8, room));
            if (pct is not null)
            {
                DrawText(context, pct, new Point(inner.Right, top), size, _text, bold: true, alignRight: true, width);
                var barLeft = x + nameWidth + 8;
                var barRight = inner.Right - pctWidth - 8;
                if (barRight - barLeft >= 8)
                {
                    DrawBar(context, new Rect(barLeft, middle - (barHeight / 2), barRight - barLeft, barHeight), target.Share!.Value, target.Low);
                }
            }
        }
    }

    private static double Measure(string text, double size, bool bold) => Format(text, size, Colors.White, bold, double.PositiveInfinity).Width;

    private static FormattedText Format(string text, double size, Color colour, bool bold, double maxWidth)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(NativeTokens.UiFont, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal),
            size,
            new SolidColorBrush(colour))
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        if (!double.IsInfinity(maxWidth))
        {
            formatted.MaxTextWidth = Math.Max(1, maxWidth);
        }

        return formatted;
    }

    private void DrawBar(DrawingContext context, Rect track, double share, bool low)
    {
        var radius = track.Height / 2;
        context.DrawRectangle(new SolidColorBrush(Parse(_ink.Track)), null, track, radius, radius);
        var fill = new Rect(track.X, track.Y, track.Width * Math.Clamp(share, 0, 1), track.Height);
        if (fill.Width > 0.5)
        {
            var colour = Parse(low ? _ink.Low : _ink.Bar);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Lighten(colour, 0.18), 0),
                    new GradientStop(colour, 1),
                },
            };
            context.DrawRectangle(brush, null, fill, radius, radius);
        }

        _drawn.Add((track, fill, low));
    }

    /// <summary>Draws text at <paramref name="at"/> (its right edge there when right-aligned) and returns its width.</summary>
    private static double DrawText(DrawingContext context, string text, Point at, double size, Color colour, bool bold, bool alignRight, double maxWidth)
    {
        var formatted = Format(text, size, colour, bold, maxWidth);
        var width = Math.Min(formatted.Width, Math.Max(1, maxWidth));
        context.DrawText(formatted, alignRight ? new Point(at.X - width, at.Y) : at);
        return width;
    }

    private static string Numbers(VitalsMeter meter) =>
        meter.HasMax ? $"{meter.Value} / {meter.Max ?? "?"}" : meter.Value;

    private static string Percent(double share) =>
        $"{Math.Clamp((int)Math.Round(share * 100), 0, 999).ToString(CultureInfo.InvariantCulture)}%";

    private Color Dim() => Color.FromArgb(0xB0, _text.R, _text.G, _text.B);

    private static Color Parse(string hex) => Color.TryParse(hex, out var colour) ? colour : Colors.Gray;

    private static Color Lighten(Color colour, double amount) => Color.FromRgb(
        (byte)(colour.R + ((255 - colour.R) * amount)),
        (byte)(colour.G + ((255 - colour.G) * amount)),
        (byte)(colour.B + ((255 - colour.B) * amount)));
}
