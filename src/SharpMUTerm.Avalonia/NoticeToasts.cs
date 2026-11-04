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
/// painted, so everywhere else in the control the pointer goes through to the cells. While the pointer is over a toast it stays, and for a moment after it leaves.
/// And while the pointer is over any toast the stack does not move: every toast keeps the place it had when the pointer
/// arrived (<see cref="Frozen"/>), so an older one fading out leaves a gap instead of letting the stack fall and carry the
/// hovered toast out from under the pointer; a notice arriving meanwhile goes above the stack. The stack closes up once
/// the pointer leaves. A left click on
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

    // Where each toast was when the pointer came onto the stack, and the bounds that was measured in; null while the
    // pointer is off it. While set, the stack holds still under the pointer.
    private Dictionary<Toast, Placed>? _frozen;
    private Rect _frozenBounds;

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

    /// <summary>
    /// What a toast must not cover, in this control's coordinates — the native map's legend. A toast that would land on
    /// one is lifted to sit above it, and the rest of the stack goes up with it.
    /// </summary>
    internal IReadOnlyList<Rect> Avoid
    {
        get => _avoid;
        set
        {
            if (!_avoid.SequenceEqual(value))
            {
                _avoid = value;
                InvalidateVisual();
            }
        }
    }

    private IReadOnlyList<Rect> _avoid = [];

    /// <summary>Whether the stack is holding still because the pointer is over a toast. Internal for tests.</summary>
    internal bool Frozen => _frozen is not null;

    /// <summary>Shows a notice the client said. A toast for the same notice (by sequence) is not shown twice.</summary>
    public void Show(NoticeOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (_toasts.Any(t => t.Overlay.Notice.Sequence == overlay.Notice.Sequence))
        {
            return;
        }

        _toasts.Add(new Toast(overlay, _clock.Elapsed));
        var hovered = HoveredToast();
        while (_toasts.Count > MaxShown)
        {
            // The oldest goes, unless the pointer is on it: then the next oldest.
            _toasts.Remove(_toasts.First(t => t != hovered));
        }

        _timer.Start();
        InvalidateVisual();
    }

    /// <summary>
    /// Runs or dismisses the toast showing notice <paramref name="sequence"/>, as a left click on it does. Internal for
    /// tests; a real click comes through <see cref="OnPointerPressed"/>.
    /// </summary>
    internal void Click(long sequence, bool second = false)
    {
        var toast = _toasts.FirstOrDefault(t => t.Overlay.Notice.Sequence == sequence);
        if (toast is null)
        {
            return;
        }

        _toasts.Remove(toast);
        if ((second ? toast.Overlay.Notice.Second : toast.Overlay.Notice.Action) is { } action)
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

    /// <summary>The words each toast draws — its key chip, its text and its action — oldest first. Internal for tests.</summary>
    internal IReadOnlyList<string> DrawnText() =>
        Layout().SelectMany(l => new[] { SourceText(l.Toast), ChipText(l.Toast), BodyText(l.Toast), ActionText(l.Toast), SecondText(l.Toast) }).OfType<string>().ToList();

    /// <summary>Whether each toast's label is a key chip (true) or a source caption (false), oldest first; null for none. Internal for tests.</summary>
    internal IReadOnlyList<bool?> LabelIsChip() =>
        Layout().Select(l => l.Toast.Overlay.Notice.Source is not null ? false : l.Toast.Overlay.Notice.Key is not null ? true : (bool?)null).ToList();

    // A source (Packages, Map, Sound) is a caption over the text, never a chip: in a chip a word reads as a key to press.
    private static string? SourceText(Toast toast) => toast.Overlay.Notice.Source;

    // What a toast says, in the window's words (NativeKeys): the key chip, the text, the action line.
    private static string? ChipText(Toast toast) =>
        toast.Overlay.Notice.Source is null && toast.Overlay.Notice.Key is { } key ? NativeKeys.Words(key) : null;

    private static string BodyText(Toast toast) => NativeKeys.Words(toast.Overlay.Notice.Text);

    private static string? ActionText(Toast toast) => toast.Overlay.Notice.Action is { } a ? $"{NativeKeys.Words(a.Label)}  ›" : null;

    // A second action (a saved scene's Copy path beside Open) sits on the same line, after the first, as its own click.
    private static string? SecondText(Toast toast) =>
        toast.Overlay.Notice.Action is not null && toast.Overlay.Notice.Second is { } a ? $"{NativeKeys.Words(a.Label)}  ›" : null;

    /// <summary>Where a toast's second action is drawn, or an empty rect when it has none.</summary>
    private static Rect SecondArea(Placed placed)
    {
        if (placed.Action is not { } action || placed.Second is not { } second)
        {
            return default;
        }

        var y = placed.Rect.Y + Padding + (placed.Source is null ? 0 : placed.Source.Height + 2) + placed.Text.Height + 6;
        var x = placed.Rect.X + EdgeWidth + Padding + action.Width + SecondGap;
        return new Rect(x - (SecondGap / 2), y - 3, second.Width + SecondGap, second.Height + 6);
    }

    /// <summary>The gap between a toast's two actions, in pixels.</summary>
    private const double SecondGap = 20;

    /// <summary>Where each toast's second action is, oldest first (empty for none). Internal for tests.</summary>
    internal IReadOnlyList<Rect> SecondAreas() => Layout().Select(SecondArea).ToList();

    /// <summary>Where each toast is drawn, oldest first, in this control's coordinates. Internal for tests.</summary>
    internal IReadOnlyList<Rect> Rects() => Layout().Select(l => l.Rect).ToList();

    private void Tick()
    {
        var now = _clock.Elapsed;
        SyncFreeze();
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

    /// <summary>
    /// Freezes the stack where it is when the pointer comes onto a toast, and lets it close up when the pointer leaves
    /// every toast (or the control is resized, which re-measures everything anyway).
    /// </summary>
    private void SyncFreeze()
    {
        if (_frozen is not null && (_pointer is null || _frozenBounds != Bounds || HoveredToast() is null))
        {
            _frozen = null;
            InvalidateVisual();
        }

        if (_frozen is null && _pointer is { } at)
        {
            var placed = Layout();
            if (placed.Any(l => l.Rect.Contains(at)))
            {
                _frozen = placed.ToDictionary(l => l.Toast);
                _frozenBounds = Bounds;
            }
        }
    }

    // ── Layout ───────────────────────────────────────────────────────────────────────────────

    private readonly record struct Placed(Toast Toast, Rect Rect, FormattedText Text, FormattedText? Chip, FormattedText? Action, FormattedText? Source = null, FormattedText? Second = null);

    private List<Placed> Layout()
    {
        if (_frozen is not null)
        {
            return FrozenLayout(_frozen);
        }

        var placed = new List<Placed>();
        var width = Math.Min(CardWidth, Bounds.Width - (2 * Inset));
        if (width < 120 || _toasts.Count == 0)
        {
            return placed;
        }

        var bottom = (Bottom > 0 ? Math.Min(Bottom, Bounds.Height) : Bounds.Height) - Gap;

        // Newest at the bottom: lay out from the newest upwards, then hand them back oldest first.
        for (var i = _toasts.Count - 1; i >= 0; i--)
        {
            var one = Measure(_toasts[i], width, bottom);
            while (one is { } landed && _avoid.FirstOrDefault(a => a.Intersects(landed.Rect.Inflate(Gap / 2))) is { Height: > 0 } obstacle)
            {
                one = Measure(_toasts[i], width, obstacle.Top - Gap);
            }

            if (one is null)
            {
                break;
            }

            placed.Insert(0, one.Value);
            bottom = one.Value.Rect.Top - Gap;
        }

        return placed;
    }

    /// <summary>
    /// The stack while the pointer is on it: every toast that was there keeps its place (a gap where one has gone), and a
    /// toast that has arrived since is stacked above the topmost, newest nearest the stack, while there is room.
    /// </summary>
    private List<Placed> FrozenLayout(Dictionary<Toast, Placed> frozen)
    {
        var kept = _toasts.Where(frozen.ContainsKey).Select(t => frozen[t]).ToList();
        var arrived = _toasts.Where(t => !frozen.ContainsKey(t)).ToList();
        if (arrived.Count == 0)
        {
            return kept;
        }

        var top = frozen.Values.Min(p => p.Rect.Top) - Gap;
        var above = new List<Placed>();
        for (var i = arrived.Count - 1; i >= 0; i--)
        {
            if (Measure(arrived[i], frozen.Values.First().Rect.Width, top) is not { } placed)
            {
                break;
            }

            above.Insert(0, placed);
            top = placed.Rect.Top - Gap;
        }

        // Oldest first, as the open layout hands them back: everything above the frozen block is newer than it.
        return [.. kept, .. above];
    }

    /// <summary>One toast placed with its bottom at <paramref name="bottom"/>, or null when it would not fit below the inset.</summary>
    private Placed? Measure(Toast toast, double width, double bottom)
    {
        var x = Bounds.Width - Inset - width;
        var palette = toast.Overlay.Palette;
        var ink = PanelText.Parse(palette.Text);
        var inner = width - EdgeWidth - (2 * Padding);
        var chip = ChipText(toast) is { } key ? Format(key, ChipSize, PanelText.Parse(palette.Edge(toast.Overlay.Notice.Severity)), bold: true, inner) : null;
        var textWidth = inner - (chip is null ? 0 : chip.Width + 8);
        var text = Format(BodyText(toast), TextSize, ink, bold: false, Math.Max(40, textWidth), lines: 3);
        var action = ActionText(toast) is { } a ? Format(a, ChipSize, PanelText.Parse(palette.Edge(toast.Overlay.Notice.Severity)), bold: true, inner) : null;
        var second = action is not null && SecondText(toast) is { } b
            ? Format(b, ChipSize, PanelText.Parse(palette.Edge(toast.Overlay.Notice.Severity)), bold: true, Math.Max(1, inner - action.Width - SecondGap))
            : null;
        var source = SourceText(toast) is { } said ? Format(said.ToUpperInvariant(), ChipSize - 1, PanelText.Parse(palette.Edge(toast.Overlay.Notice.Severity)), bold: true, inner) : null;
        var height = (2 * Padding) + text.Height + (action is null ? 0 : action.Height + 6) + (source is null ? 0 : source.Height + 2);
        var top = bottom - height;
        return top < Inset ? null : new Placed(toast, new Rect(x, top, width, height), text, chip, action, source, second);
    }

    private static FormattedText Format(string text, double size, Color colour, bool bold, double maxWidth, int lines = 1) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(NativeTokens.UiFont, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, new SolidColorBrush(colour))
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
        SyncFreeze();
        var over = HoveredToast();
        Cursor = new Cursor(over?.Overlay.Notice.Action is not null ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ToolTip.SetTip(this, over?.Overlay.Notice.Action is { } action ? $"{NativeKeys.Words(action.Label)} — click" : over is null ? null : "Click to dismiss");
        e.Handled = true;
        Tick();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        SyncFreeze();
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
            Click(hit.Toast.Overlay.Notice.Sequence, SecondArea(hit).Contains(at));
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
        foreach (var placed in Layout())
        {
            var (toast, rect, text, chip, action, source, second) = placed;
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
            if (source is not null)
            {
                // The part of the client that spoke, as a small caption above the text: a label, not a key.
                context.DrawText(source, new Point(x, y));
                y += source.Height + 2;
            }

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
                if (second is not null)
                {
                    context.DrawText(second, new Point(rect.X + EdgeWidth + Padding + action.Width + SecondGap, y + text.Height + 6));
                }
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
