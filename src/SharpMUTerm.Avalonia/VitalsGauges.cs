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

    private (bool Above, bool Below) _seams;

    /// <summary>
    /// The edges another pane meets (<see cref="PanelOverlay.RuleAbove"/>/<c>RuleBelow</c>): a hairline is drawn there, the
    /// mockups' rule between the side column's panels, since in the window a strip keeps the panels' plane.
    /// </summary>
    internal (bool Above, bool Below) Seams
    {
        get => _seams;
        set
        {
            if (value != _seams)
            {
                _seams = value;
                InvalidateVisual();
            }
        }
    }

    private void DrawSeams(DrawingContext context, Rect bounds, Color text, Color plane)
    {
        var rule = new SolidColorBrush(PanelText.Rule(text, plane));
        if (_seams.Above)
        {
            context.FillRectangle(rule, NativeTokens.HorizontalRule(this, 0, 0, bounds.Width));
        }

        if (_seams.Below)
        {
            context.FillRectangle(rule, NativeTokens.HorizontalRule(this, bounds.Height - NativeTokens.DevicePixel(this), 0, bounds.Width));
        }
    }

    public VitalsGauges()
    {
        // Never focusable (the keyboard stays on the command line), but named for a screen reader exploring the window.
        global::Avalonia.Automation.AutomationProperties.SetName(this, "Vitals");
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>The plane it paints, read off the cells under it. Internal for tests.</summary>
    internal Color Plane => _plane;

    /// <summary>What it is showing. Internal for tests.</summary>
    internal VitalsModel Model => _model;

    /// <summary>Whether the meters are stacked (true) or side by side. Internal for tests.</summary>
    internal bool Stacked => StackedFor(Bounds.Height > 0 ? Bounds.Height : Height, _model);

    /// <summary>
    /// What a bar is filled with: the warning when low, else the meter's own colour (<see cref="VitalsMeter.Colour"/>) held
    /// to the floor on the pane's plane — the cells' rule — else the panel's bar ink. Internal for tests.
    /// </summary>
    internal Color FillFor(string? colour, bool low)
    {
        if (low)
        {
            return Parse(_ink.Low);
        }

        if (colour is null || !Color.TryParse(colour, out var own))
        {
            return Parse(_ink.Bar);
        }

        var held = SharpMUTerm.Core.Text.Contrast.Legible(
            new SharpMUTerm.Core.Text.Rgb(own.R, own.G, own.B), new SharpMUTerm.Core.Text.Rgb(_plane.R, _plane.G, _plane.B));
        return Color.FromRgb(held.R, held.G, held.B);
    }

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

    /// <summary>The meter's label line in a stacked block: the name and the numbers over the bar.</summary>
    private static double LabelHeight(double cellHeight) => Math.Max(14, cellHeight * 0.85);

    /// <summary>A stacked meter's bar: ten pixels on an ordinary cell, as the mockups draw it.</summary>
    private static double BarHeight(double cellHeight) => cellHeight >= 15 ? 10 : 8;

    /// <summary>Height one stacked block takes: a line of text, the bar under it, and the gap to the next.</summary>
    private static double BlockHeight(double cellHeight) => LabelHeight(cellHeight) + NativeTokens.Space4 + BarHeight(cellHeight) + NativeTokens.Space12;

    /// <summary>The height every stacked block takes, the target's included (its rule sits in the gap above it).</summary>
    private double BlocksHeight(VitalsModel model) => BlocksHeight(model.Meters.Count, model.Target is not null, _cellHeight);

    private static double BlocksHeight(int meters, bool target, double cellHeight) =>
        (Math.Max(0, meters) + (target ? 1 : 0)) * BlockHeight(cellHeight);

    /// <summary>The mockup's padding under the last bar, to the card's edge.</summary>
    private const double BottomPad = NativeTokens.Space16;

    /// <summary>
    /// The height a headed stacked card needs: the heading, the gap under it, every block, and <see cref="BottomPad"/> under
    /// the last bar (a block's own trailing gap is <see cref="NativeTokens.Space12"/>, so four more).
    /// </summary>
    internal static double NeededHeight(int meters, bool target, double cellHeight) =>
        PanelText.HeadingHeight(cellHeight) + NativeTokens.Space12 + BlocksHeight(meters, target, cellHeight) + (BottomPad - NativeTokens.Space12);

    /// <summary>
    /// The whole rows a side-column card on cells of <paramref name="cellHeight"/> takes for these gauges: their needed
    /// height rounded up, so the card is never short and is less than a row too tall; that remainder is spread over the
    /// gaps between the blocks (<see cref="RenderStacked"/>), so the last bar still ends <see cref="BottomPad"/> above the
    /// map. What the window answers for <see cref="SharpMUTerm.Tui.IPointerPresenter.VitalsCardRows"/>.
    /// </summary>
    internal static int CardRows(int meters, bool target, double cellHeight) =>
        (int)Math.Ceiling((NeededHeight(meters, target, cellHeight) / cellHeight) - 0.001);

    private bool StackedFor(double height, VitalsModel model)
    {
        var blocks = model.Meters.Count + (model.Target is null ? 0 : 1);
        return blocks > 0 && height >= BlocksHeight(model) + 12;
    }

    /// <summary>Whether a stacked pane also has room for the panel heading over the blocks.</summary>
    private bool HeadedFor(double height, VitalsModel model) =>
        height >= NeededHeight(model.Meters.Count, model.Target is not null, _cellHeight) - 0.5;

    /// <summary>
    /// What each gap (under the heading, and between blocks) grows by in a headed card of <paramref name="height"/>: the
    /// part of a row the card has over what it needs, spread evenly so the last bar ends <see cref="BottomPad"/> above the
    /// card's edge, at most <see cref="NativeTokens.Space8"/> a gap (a taller card keeps the rest at its foot).
    /// </summary>
    private double GapGrowth(double height, VitalsModel model)
    {
        var target = model.Target is not null;
        var gaps = model.Meters.Count + (target ? 1 : 0);
        var extra = height - NeededHeight(model.Meters.Count, target, _cellHeight);
        return gaps <= 0 || extra <= 0 ? 0 : Math.Min(NativeTokens.Space8, Math.Floor(extra / gaps));
    }

    /// <summary>Where the last bar drawn ends, from the top of the card. Internal for tests.</summary>
    internal double LastBarBottom => _drawn.Count == 0 ? 0 : _drawn[^1].Track.Bottom;

    /// <summary>Whether the heading is drawn now. Internal for tests.</summary>
    internal bool Headed => Stacked && HeadedFor(Bounds.Height > 0 ? Bounds.Height : Height, _model);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        _drawn.Clear();

        // The plane covers the cells under it: the strip is drawn by this, not beside it.
        context.FillRectangle(new SolidColorBrush(_plane), bounds);
        DrawSeams(context, bounds, _text, _plane);
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

    /// <summary>
    /// The side column's card: the heading (Vitals and where the numbers come from) when there is room for it, then a
    /// block per meter — the name and the numbers over a ten-pixel rounded bar, a low meter's words and bar in the
    /// warning ink with <c>· low</c> — and the target under a rule, on a thinner bar.
    /// </summary>
    private void RenderStacked(DrawingContext context, Rect bounds)
    {
        var headed = HeadedFor(bounds.Height, _model);
        var grow = headed ? GapGrowth(bounds.Height, _model) : 0;
        var block = BlockHeight(_cellHeight) + grow;
        var label = LabelHeight(_cellHeight);
        var barHeight = BarHeight(_cellHeight);
        // The mockup's card: 12 px names, the numbers in the game text's mono at the same size.
        var size = Math.Min(NativeTokens.Caption, Math.Max(10, _cellHeight * 0.6));
        var top = headed
            ? PanelText.Heading(context, PanelHeading.For(_model), bounds.Width, _cellHeight, _plane, _text, right: NativeTokens.Space20, rule: NativeTokens.DevicePixel(this)) + 12 + grow
            : 8;
        var inner = new Rect(PanelText.Pad, top, Math.Max(1, bounds.Width - (PanelText.Pad * 2)), Math.Max(1, bounds.Height - top));
        var dim = PanelText.Dim(_text, _plane);
        var warn = Parse(_ink.Low);
        var y = inner.Top;
        foreach (var meter in _model.Meters)
        {
            var low = meter.Low;
            var middle = y + (label / 2);
            var title = low ? $"{meter.Title} · low" : meter.Title;
            PanelText.Draw(context, title, inner.Left, middle, size, low ? warn : _text, low, inner.Width * 0.6);
            PanelText.Draw(context, Numbers(meter), inner.Right, middle, size, low ? warn : _text, false, inner.Width * 0.4, alignRight: true, face: NativeTokens.MonoFont);
            DrawBar(context, new Rect(inner.Left, y + label + 4, inner.Width, barHeight), meter.HasMax ? meter.Share ?? 0 : 1, low, meter.Colour);
            y += block;
        }

        if (_model.Target is { } target)
        {
            context.FillRectangle(new SolidColorBrush(PanelText.Rule(_text, _plane)), new Rect(inner.Left, Math.Round(y - 4 - (grow / 2)), inner.Width, 1));
            y += 4;
            var middle = y + (label / 2);
            var leadWidth = 6 + PanelText.Draw(context, "Target", inner.Left, middle, size, dim, false, inner.Width * 0.3);
            PanelText.Draw(context, target.Name, inner.Left + leadWidth, middle, size, target.Low ? warn : _text, false, (inner.Width * 0.75) - leadWidth);
            if (target.Share is { } share)
            {
                PanelText.Draw(context, Percent(share), inner.Right, middle, size, _text, false, inner.Width * 0.25, alignRight: true, face: NativeTokens.MonoFont);
                DrawBar(context, new Rect(inner.Left, y + label + 4, inner.Width, Math.Max(4, barHeight - 4)), share, target.Low, target.Colour);
            }
        }
    }

    /// <summary>The narrowest a side-by-side bar is drawn; below it the words get shorter first.</summary>
    private const double MinimumBar = 24;

    private void RenderRow(DrawingContext context, Rect bounds)
    {
        var size = Math.Clamp(_cellHeight * 0.62, 9, 14);
        var barHeight = Math.Clamp(bounds.Height * 0.32, 4, 9);
        var inner = bounds.Deflate(new Thickness(PanelText.Pad, 0, NativeTokens.Space32, 0));
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
                DrawBar(context, new Rect(barLeft, middle - (barHeight / 2), barRight - barLeft, barHeight), meter.HasMax ? meter.Share ?? 0 : 1, low, meter.Colour);
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
                    DrawBar(context, new Rect(barLeft, middle - (barHeight / 2), barRight - barLeft, barHeight), target.Share!.Value, target.Low, target.Colour);
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

    private void DrawBar(DrawingContext context, Rect track, double share, bool low, string? colour)
    {
        // Flat fills on a track one step off the plane, rounded to the bar's height: the mockups' gauge.
        var radius = track.Height / 2;
        context.DrawRectangle(new SolidColorBrush(PanelText.Mix(_plane, _text, 0.12)), null, track, radius, radius);
        var fill = new Rect(track.X, track.Y, track.Width * Math.Clamp(share, 0, 1), track.Height);
        if (fill.Width > 0.5)
        {
            context.DrawRectangle(new SolidColorBrush(FillFor(colour, low)), null, fill, radius, radius);
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
}
