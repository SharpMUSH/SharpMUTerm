using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's status footer, across the bottom of the window under the cells and the sidebar (<see cref="HostChrome.Status"/>):
/// the status row the client would have drawn in its last row, from the same <see cref="StatusModel"/> — the connection,
/// the mode's keys, meters, reconnect, <c>● REC</c>, <c>♪ muted</c>, the scrollback segment, a notice — in the UI face,
/// keys in words. The left cluster reads from the left edge and the right one ends at the right edge.
/// <para>
/// A run the client marked (a notice's action, the palette hint) is a link: a click hands its target back through
/// <see cref="StatusOverlay.Activate"/>, which runs the client's own method or refuses. Never focusable; every pointer
/// event over it is the window's. While a modal of the client's is in front it is drawn and takes no click. The toasts
/// sit above it, inside the cell area.
/// </para>
/// </summary>
internal sealed class NativeFooter : Control
{
    private readonly TerminalView _view;
    private readonly List<(Rect Area, string Target)> _hits = [];
    private string? _hover;

    public NativeFooter(TerminalView view)
    {
        _view = view;
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>What the client last said.</summary>
    internal StatusOverlay? Overlay { get; private set; }

    /// <summary>The clickable runs as last drawn. Internal for tests.</summary>
    internal IReadOnlyList<(Rect Area, string Target)> Hits => _hits;

    /// <summary>The footer's text as drawn, keys in words. Internal for tests.</summary>
    internal string Text => Overlay is { } o ? NativeKeys.Words(o.Model.Text) : string.Empty;

    /// <summary>Takes what the client said and redraws.</summary>
    public void Show(StatusOverlay? overlay)
    {
        if (overlay == Overlay)
        {
            return;
        }

        Overlay = overlay;
        InvalidateVisual();
    }

    /// <summary>Acts on a target as a click would. Internal for tests.</summary>
    internal void Click(string target)
    {
        if (Overlay is { Interactive: true } overlay && target.Length > 0)
        {
            overlay.Activate(target);
        }
    }

    public override void Render(DrawingContext context)
    {
        var plane = NativeChromeView.Parse(Overlay?.Model.Plane ?? HexOf(_view.Renderer.DefaultBackground));
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(plane), bounds);
        var ink = _view.Renderer.DefaultForeground;
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(0x1f, ink.Red, ink.Green, ink.Blue)), new Rect(0, 0, bounds.Width, 1));
        _hits.Clear();
        if (Overlay is not { } overlay)
        {
            return;
        }

        var middle = bounds.Height / 2;
        var right = bounds.Width - NativeLayout.FooterPadX - WindowChrome.EdgeBand;
        var rightWidth = overlay.Model.Right.Sum(r => Format(r, plane).WidthIncludingTrailingWhitespace);
        var x = NativeLayout.FooterPadX;
        foreach (var run in overlay.Model.Left)
        {
            x += Draw(context, run, x, middle, plane, Math.Max(0, right - rightWidth - 16 - x));
        }

        x = Math.Max(x + 16, right - rightWidth);
        foreach (var run in overlay.Model.Right)
        {
            x += Draw(context, run, x, middle, plane, Math.Max(0, right - x));
        }

        if (!overlay.Interactive)
        {
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(0x70, plane.R, plane.G, plane.B)), bounds);
        }
    }

    private double Draw(DrawingContext context, StatusRun run, double x, double middle, Color plane, double room)
    {
        if (room <= 0)
        {
            return 0;
        }

        var formatted = Format(run, plane);
        formatted.MaxTextWidth = Math.Max(1, room);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        var top = Math.Round(middle - (formatted.Height / 2));
        context.DrawText(formatted, new Point(Math.Round(x), top));
        var width = Math.Min(formatted.WidthIncludingTrailingWhitespace, room);
        if (run.Target is { } target && run.Text.Trim().Length > 0)
        {
            var area = new Rect(x, 0, width, Bounds.Height);
            _hits.Add((area, target));
            if (_hover == target && Overlay is { Interactive: true })
            {
                var colour = InkOf(run);
                context.FillRectangle(new SolidColorBrush(colour), new Rect(x, top + formatted.Baseline + 2, width, 1));
            }
        }

        return width;
    }

    /// <summary>A run as text in the UI face: keys in words, padding runs of spaces shortened to a gap.</summary>
    private FormattedText Format(StatusRun run, Color plane) => new(
        Gaps.Replace(NativeKeys.Words(run.Text), "   "),
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        new Typeface(NativeTokens.UiFont, FontStyle.Normal, run.Bold ? FontWeight.SemiBold : FontWeight.Normal),
        NativeTokens.Body,
        new SolidColorBrush(InkOf(run)))
    {
        MaxLineCount = 1,
    };

    private static readonly Regex Gaps = new(" {3,}", RegexOptions.CultureInvariant);

    /// <summary>A run's ink: its own, else the row's (a run the cells write with no colour).</summary>
    private Color InkOf(StatusRun run) => NativeChromeView.Parse(run.Ink ?? Overlay?.Model.Ink);

    private static string HexOf(SkiaSharp.SKColor colour) => $"#{colour.Red:x2}{colour.Green:x2}{colour.Blue:x2}";

    private string? TargetAt(Point point) => _hits.FirstOrDefault(h => h.Area.Contains(point)).Target;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var target = Overlay is { Interactive: true } ? TargetAt(e.GetPosition(this)) : null;
        var edge = _view.EdgeCursorFromChrome(e);
        Cursor = edge is { } resize ? new Cursor(resize) : target is not null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        if (target != _hover)
        {
            _hover = target;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || _view.ResizeFromChrome(e))
        {
            return;
        }

        if (TargetAt(point.Position) is { } target)
        {
            Click(target);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }
}
