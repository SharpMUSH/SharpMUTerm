using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The client's notices as toasts: stacked at the bottom right of the cell area, just above the command line and the
/// status row, newest at the bottom. Each is the very <see cref="NoticeModel"/> the status row's cells were drawn from
/// (which still carry it underneath — the toast is an overlay, so the cells are what they are in a terminal), and the
/// message log stays the history: a toast fades after <see cref="NoticeModel.Duration"/> and is not kept.
/// <para>
/// <b>Input.</b> Only a toast is hit: the control paints nothing between its toasts, and Avalonia hits only what was
/// painted, so everywhere else in the control the pointer goes through to the cells. While the pointer is over a toast it stays, and for a moment after it leaves. A left click on
/// a toast with an action runs it — through <see cref="NoticeOverlay.Run"/>, the registry, never a command the window
/// built — and a click without one, or a right click, dismisses it. Nothing here is focusable, so a toast never takes
/// the keyboard from the command line.
/// </para>
/// </summary>
internal sealed class NoticeToasts : Control
{
    /// <summary>How many toasts are stacked at once; an older one is retired when a fifth arrives.</summary>
    internal const int MaxShown = 4;

    /// <summary>How long a toast takes to fade once its time is up.</summary>
    internal static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(450);

    /// <summary>How long a toast stays after the pointer leaves it.</summary>
    internal static readonly TimeSpan HoverGrace = TimeSpan.FromSeconds(2);

    private const double Inset = 12, Gap = 8, Padding = 10, EdgeWidth = 4, CardWidth = 380, TextSize = 13, ChipSize = 11;

    private readonly List<Toast> _toasts = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private Point? _pointer;

    public NoticeToasts()
    {
        Focusable = false;
        IsTabStop = false;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Tick());
        AutomationProperties.SetName(this, "Notices");
    }

    /// <summary>The toasts on screen, oldest first. Internal for tests.</summary>
    internal IReadOnlyList<NoticeModel> Shown => _toasts.Select(t => t.Overlay.Notice).ToList();

    /// <summary>The bottom of the stack, in this control's coordinates: just above the pane area's last row.</summary>
    internal double Bottom { get; set; }

    /// <summary>Shows a notice the client said. A toast for the same notice (by sequence) is not shown twice.</summary>
    public void Show(NoticeOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (_toasts.Any(t => t.Overlay.Notice.Sequence == overlay.Notice.Sequence))
        {
            return;
        }

        _toasts.Add(new Toast(overlay, _clock.Elapsed));
        while (_toasts.Count > MaxShown)
        {
            _toasts.RemoveAt(0);
        }

        _timer.Start();
        InvalidateVisual();
    }

    /// <summary>
    /// Runs or dismisses the toast showing notice <paramref name="sequence"/>, as a left click on it does. Internal for
    /// tests; a real click comes through <see cref="OnPointerPressed"/>.
    /// </summary>
    internal void Click(long sequence)
    {
        var toast = _toasts.FirstOrDefault(t => t.Overlay.Notice.Sequence == sequence);
        if (toast is null)
        {
            return;
        }

        _toasts.Remove(toast);
        if (toast.Overlay.Notice.Action is { } action)
        {
            toast.Overlay.Run(action.Command);
        }

        InvalidateVisual();
    }

    /// <summary>Moves every toast's clock on by <paramref name="by"/>, as time passing would. Internal for tests.</summary>
    internal void Advance(TimeSpan by)
    {
        foreach (var toast in _toasts)
        {
            toast.Shown -= by;
            toast.HeldUntil -= by;
        }

        Tick();
    }

    /// <summary>Where each toast is drawn, oldest first, in this control's coordinates. Internal for tests.</summary>
    internal IReadOnlyList<Rect> Rects() => Layout().Select(l => l.Rect).ToList();

    private void Tick()
    {
        var now = _clock.Elapsed;
        var hovered = HoveredToast();
        foreach (var toast in _toasts)
        {
            if (toast == hovered)
            {
                toast.HeldUntil = now + HoverGrace;
            }
        }

        var before = _toasts.Count;
        _toasts.RemoveAll(t => FadeOf(t, now) <= 0);
        if (_toasts.Count == 0)
        {
            _timer.Stop();
        }

        if (before != _toasts.Count || _toasts.Any(t => Fading(t, now)))
        {
            InvalidateVisual();
        }
    }

    private static TimeSpan Due(Toast toast) => toast.Shown + NoticeModel.Duration > toast.HeldUntil ? toast.Shown + NoticeModel.Duration : toast.HeldUntil;

    private static bool Fading(Toast toast, TimeSpan now) => now > Due(toast);

    private static double FadeOf(Toast toast, TimeSpan now)
    {
        var past = (now - Due(toast)).TotalMilliseconds;
        return past <= 0 ? 1 : Math.Max(0, 1 - (past / FadeTime.TotalMilliseconds));
    }

    private Toast? HoveredToast() =>
        _pointer is { } at ? Layout().LastOrDefault(l => l.Rect.Contains(at)).Toast : null;

    // ── Layout ───────────────────────────────────────────────────────────────────────────────

    private readonly record struct Placed(Toast Toast, Rect Rect, FormattedText Text, FormattedText? Chip, FormattedText? Action);

    private List<Placed> Layout()
    {
        var placed = new List<Placed>();
        var width = Math.Min(CardWidth, Bounds.Width - (2 * Inset));
        if (width < 120 || _toasts.Count == 0)
        {
            return placed;
        }

        var bottom = (Bottom > 0 ? Math.Min(Bottom, Bounds.Height) : Bounds.Height) - Gap;
        var x = Bounds.Width - Inset - width;

        // Newest at the bottom: lay out from the newest upwards, then hand them back oldest first.
        for (var i = _toasts.Count - 1; i >= 0; i--)
        {
            var toast = _toasts[i];
            var palette = toast.Overlay.Palette;
            var ink = PanelText.Parse(palette.Text);
            var inner = width - EdgeWidth - (2 * Padding);
            var chip = toast.Overlay.Notice.Key is { } key ? Format(key, ChipSize, PanelText.Parse(palette.Edge(toast.Overlay.Notice.Severity)), bold: true, inner) : null;
            var textWidth = inner - (chip is null ? 0 : chip.Width + 8);
            var text = Format(toast.Overlay.Notice.Text, TextSize, ink, bold: false, Math.Max(40, textWidth), lines: 3);
            var action = toast.Overlay.Notice.Action is { } a ? Format($"{a.Label}  ›", ChipSize, PanelText.Parse(palette.Edge(toast.Overlay.Notice.Severity)), bold: true, inner) : null;
            var height = (2 * Padding) + text.Height + (action is null ? 0 : action.Height + 6);
            var top = bottom - height;
            if (top < Inset)
            {
                break;
            }

            placed.Insert(0, new Placed(toast, new Rect(x, top, width, height), text, chip, action));
            bottom = top - Gap;
        }

        return placed;
    }

    private static FormattedText Format(string text, double size, Color colour, bool bold, double maxWidth, int lines = 1) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, new SolidColorBrush(colour))
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = lines,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    // ── Input ────────────────────────────────────────────────────────────────────────────────

    internal bool Hits(Point point) => Layout().Any(l => l.Rect.Contains(point));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer = e.GetPosition(this);
        var over = HoveredToast();
        Cursor = new Cursor(over?.Overlay.Notice.Action is not null ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ToolTip.SetTip(this, over?.Overlay.Notice.Action is { } action ? $"{action.Label} — click" : over is null ? null : "Click to dismiss");
        e.Handled = true;
        Tick();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        var at = e.GetPosition(this);
        var hit = Layout().LastOrDefault(l => l.Rect.Contains(at));
        if (hit.Toast is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            _toasts.Remove(hit.Toast);
            InvalidateVisual();
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            Click(hit.Toast.Overlay.Notice.Sequence);
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

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var now = _clock.Elapsed;
        var hovered = HoveredToast();
        foreach (var (toast, rect, text, chip, action) in Layout())
        {
            var palette = toast.Overlay.Palette;
            var plane = PanelText.Parse(palette.Plane);
            var edge = PanelText.Parse(palette.Edge(toast.Overlay.Notice.Severity));
            using var _ = context.PushOpacity(FadeOf(toast, now));

            // A soft shadow, the card, and the severity edge down its left side.
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), null, rect.Translate(new Vector(0, 2)).Inflate(1), 7, 7);
            var lift = toast == hovered ? 0.08 : 0;
            context.DrawRectangle(
                new SolidColorBrush(Mix(plane, PanelText.Parse(palette.Text), lift)),
                new Pen(new SolidColorBrush(Color.FromArgb(0x60, edge.R, edge.G, edge.B)), 1),
                rect,
                6,
                6);
            using (context.PushClip(new RoundedRect(rect, 6)))
            {
                context.FillRectangle(new SolidColorBrush(edge), new Rect(rect.X, rect.Y, EdgeWidth, rect.Height));
            }

            var x = rect.X + EdgeWidth + Padding;
            var y = rect.Y + Padding;
            if (chip is not null)
            {
                var chipRect = new Rect(x - 3, y + 1, chip.Width + 6, chip.Height);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, edge.R, edge.G, edge.B)), null, chipRect, 3, 3);
                context.DrawText(chip, new Point(x, y + 1));
                x += chip.Width + 8;
            }

            context.DrawText(text, new Point(x, y));
            if (action is not null)
            {
                context.DrawText(action, new Point(rect.X + EdgeWidth + Padding, y + text.Height + 6));
            }
        }
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));

    private sealed class Toast(NoticeOverlay overlay, TimeSpan shown)
    {
        public NoticeOverlay Overlay { get; } = overlay;

        public TimeSpan Shown { get; set; } = shown;

        public TimeSpan HeldUntil { get; set; }
    }
}
