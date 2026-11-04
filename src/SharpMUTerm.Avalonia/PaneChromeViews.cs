using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A pane's tab strip, drawn by the window over the strip's row, which the client paints blank where the window draws
/// the strips (<see cref="HostChrome.Tabs"/>). The model is the one the cells' titles are written from
/// (<see cref="TabStripModel"/>): the selected tab is a card joined to the pane, on the pane's own plane (its character's
/// tint and focus lift come with it), the others plain text in the quiet ink — an idle tab of another character's on its
/// own plane, as in the cells — an unread count in the accent, a close on hover where the tab offers one, and
/// <c>+N ▾</c> where the cells' list of hidden tabs would open.
/// <para>
/// <b>Input.</b> Only the tabs and the <c>+N ▾</c> button are hit (<see cref="ICustomHitTest"/>): a press on the strip's
/// empty run goes on to the cells, where the client reads it as the divider or a window drag, exactly as the empty run
/// of a cell strip is read. Every gesture is handed back as a pane and window id, never a command; it takes no focus.
/// </para>
/// </summary>
internal sealed class NativeTabStrip : Control, ICustomHitTest
{
    private readonly List<(Rect Area, Rect? Close, TabChipModel Tab, Color Plane)> _tabs = [];

    private readonly List<double> _names = [];

    private string[] _labels = [];

    /// <summary>The narrowest a name is cut to while sharing room; the tab in front keeps more (the cells' floors).</summary>
    private const double NameFloor = 36, SelectedNameFloor = 72, NameCap = 220;
    private Rect? _overflow;
    private string? _hoverTab;
    private bool _hoverClose;
    private bool _hoverOverflow;
    private (string? Tab, bool Close, bool Overflow) _pressed;

    public NativeTabStrip()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>What the client last said about this strip. Internal for tests.</summary>
    internal TabStripOverlay? Overlay { get; private set; }

    private NativeTokens Tokens { get; set; } = NativeTokens.Dark;

    private bool Interactive { get; set; } = true;

    /// <summary>Each tab's area, as last laid out. Internal for tests.</summary>
    internal IReadOnlyList<(Rect Area, Rect? Close, TabChipModel Tab, Color Plane)> Tabs => _tabs;

    /// <summary>The <c>+N ▾</c> button's area, or null when every tab fits. Internal for tests.</summary>
    internal Rect? OverflowArea => _overflow;

    /// <summary>The type size for the strip's depth: one cell row, which is the window's cell height.</summary>
    private double Size => (double.IsNaN(Height) ? Bounds.Height : Height) switch
    {
        >= 24 => NativeTokens.Title,
        >= 18 => NativeTokens.Body,
        _ => NativeTokens.Small,
    };

    /// <summary>Takes the client's strip and the window's tokens, lays the tabs out and redraws.</summary>
    public void Show(TabStripOverlay overlay, NativeTokens tokens, bool interactive)
    {
        Overlay = overlay;
        Tokens = tokens;
        Interactive = interactive;
        Lay();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Lay();
    }

    private static Color Parse(string? hex, Color fallback) => hex is not null && Color.TryParse(hex, out var c) ? c : fallback;

    private static Rgb Rgb(Color c) => new(c.R, c.G, c.B);

    private static Color Colour(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>An ink held to the window's text floor on the plane it is drawn on.</summary>
    internal static Color Held(Color ink, Color plane) => Colour(Contrast.Legible(Rgb(ink), Rgb(plane), NativeTokens.TextFloor));

    private void Lay()
    {
        _tabs.Clear();
        _names.Clear();
        _overflow = null;
        if (Overlay is not { } overlay)
        {
            return;
        }

        var width = double.IsNaN(Width) ? Bounds.Width : Width;
        var height = double.IsNaN(Height) ? Bounds.Height : Height;
        var strip = Parse(overlay.StripPlane, Tokens.Card);
        var plane = Parse(overlay.Plane, Tokens.Card);
        var right = width - 6;
        if (overlay.Overflow is { Entries.Count: > 0 } menu)
        {
            var label = $"+{menu.Entries.Count} ▾";
            var w = PanelText.Measure(label, Size, bold: true) + 16;
            _overflow = new Rect(Math.Max(0, width - w - 6), 3, w, Math.Max(4, height - 6));
            right = _overflow.Value.X - 6;
        }

        var x = 8.0;
        var top = Math.Min(4, Math.Max(1, height / 6));
        var names = Fit(overlay.Model.Tabs, right - x);
        for (var i = 0; i < overlay.Model.Tabs.Count; i++)
        {
            var tab = overlay.Model.Tabs[i];
            var name = names[i];
            var extra = Extra(tab);
            var close = tab.Closable ? 16 : 0;
            var w = 14 + name + extra + close + 14;
            var area = new Rect(x, top, w, Math.Max(4, height - top));
            var chip = overlay.Chips.Count > i && overlay.Chips[i] is { } hex ? Parse(hex, strip) : strip;
            Rect? closeArea = tab.Closable ? new Rect(area.Right - 14 - close, top + ((area.Height - 16) / 2), 16, 16) : null;
            _tabs.Add((area, closeArea, tab, tab.Selected ? plane : chip));
            _names.Add(name);
            x += w + 2;
            if (x > right)
            {
                break;
            }
        }
    }

    private double Extra(TabChipModel tab) =>
        (tab.Unread > 0 ? PanelText.Measure(" " + UnreadBadge.Format(tab.Unread), Size, true) : 0)
        + (Marks(tab).Length > 0 ? PanelText.Measure(Marks(tab), Size, false) : 0);

    /// <summary>
    /// Each name's width so the tabs fit <paramref name="room"/>, the way the cells' strip shares its row
    /// (<c>TabStripFit</c>): a cap is lowered until the row fits and every longer name is cut to it, so short names stay
    /// whole; the tab in front keeps a floor of its own. Past the floors the row clips, as the cells' does.
    /// </summary>
    private double[] Fit(IReadOnlyList<TabChipModel> tabs, double room)
    {
        var fixedCost = tabs.Sum(t => 28 + Extra(t) + (t.Closable ? 16 : 0) + 2);
        double Measure(string label, TabChipModel t) => Math.Min(NameCap, PanelText.Measure(label, Size, t.Selected));

        // The owner prefix goes first, and for the whole strip, as in the cells: the chip and the other character's
        // mark still say whose a tab is.
        _labels = tabs.Select(t => t.Name).ToArray();
        if (fixedCost + tabs.Sum(t => Measure(t.Name, t)) > room)
        {
            _labels = tabs.Select(t => t.Title).ToArray();
        }

        var full = tabs.Select((t, i) => Measure(_labels[i], t)).ToArray();
        double Width(double cap) => full.Select((w, i) => Math.Min(w, Math.Max(cap, tabs[i].Selected ? Math.Min(w, SelectedNameFloor) : 0))).Sum();
        if (fixedCost + full.Sum() <= room || full.Length == 0)
        {
            return full;
        }

        var cap = full.Max();
        while (cap > NameFloor && fixedCost + Width(cap) > room)
        {
            cap -= 2;
        }

        return full.Select((w, i) => Math.Min(w, Math.Max(cap, tabs[i].Selected ? Math.Min(w, SelectedNameFloor) : 0))).ToArray();
    }

    /// <summary>The quiet marks after a name: the draft pen and the other character's sign, as the cells write them.</summary>
    private static string Marks(TabChipModel tab) => (tab.Draft ? " ✎" : string.Empty) + (tab.Foreign ? " ⌁" : string.Empty);

    public bool HitTest(Point point) =>
        Interactive && (_tabs.Any(t => t.Area.Contains(point)) || _overflow?.Contains(point) == true);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var hit = _tabs.FirstOrDefault(t => t.Area.Contains(p));
        var tab = hit.Tab?.WindowId;
        var close = hit.Close?.Contains(p) == true;
        var overflow = _overflow?.Contains(p) == true;
        if (tab != _hoverTab || close != _hoverClose || overflow != _hoverOverflow)
        {
            _hoverTab = tab;
            _hoverClose = close;
            _hoverOverflow = overflow;
            Cursor = tab is not null || overflow ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            ToolTip.SetTip(this, hit.Tab is { } t ? Tip(t, close) : overflow ? $"{Overlay?.Overflow?.Entries.Count} more tabs" : null);
            InvalidateVisual();
        }
    }

    private static string Tip(TabChipModel tab, bool close) =>
        NativeKeys.Words(close
            ? "Close this tab (Ctrl+W)"
            : tab.Name + (tab.Unread > 0 ? $"\n{UnreadBadge.Format(tab.Unread)} new" : string.Empty)
              + (tab.Draft ? "\nan unsent line on its command line" : string.Empty));

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverTab = null;
        _hoverClose = false;
        _hoverOverflow = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        var p = e.GetPosition(this);
        var hit = _tabs.FirstOrDefault(t => t.Area.Contains(p));
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // The menu takes the pointer, so the release would never arrive: a right press is the whole click.
            if (Overlay is { } overlay && hit.Tab is { } tab)
            {
                overlay.Menu(overlay.Model.PaneId, tab.WindowId);
            }

            return;
        }

        _pressed = (hit.Tab?.WindowId, hit.Close?.Contains(p) == true, _overflow?.Contains(p) == true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        var pressed = _pressed;
        _pressed = default;
        if (e.InitialPressMouseButton != MouseButton.Left || Overlay is not { } overlay)
        {
            return;
        }

        var p = e.GetPosition(this);
        var hit = _tabs.FirstOrDefault(t => t.Area.Contains(p));
        if (pressed.Overflow && _overflow?.Contains(p) == true)
        {
            overlay.OpenOverflow(overlay.Model.PaneId);
        }
        else if (pressed.Tab is { } id && hit.Tab?.WindowId == id)
        {
            if (pressed.Close && hit.Close?.Contains(p) == true)
            {
                overlay.Close(overlay.Model.PaneId, id);
            }
            else
            {
                overlay.Activate(overlay.Model.PaneId, id);
            }
        }
    }

    /// <summary>Clicks a tab as a left press and release on it would. Internal for tests.</summary>
    internal void PerformClick(string windowId, bool close = false)
    {
        if (Overlay is { } overlay)
        {
            if (close)
            {
                overlay.Close(overlay.Model.PaneId, windowId);
            }
            else
            {
                overlay.Activate(overlay.Model.PaneId, windowId);
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Overlay is not { } overlay)
        {
            return;
        }

        var bounds = new Rect(Bounds.Size);
        var strip = Parse(overlay.StripPlane, Tokens.Card);
        context.FillRectangle(new SolidColorBrush(strip), bounds);
        var rule = new Pen(new SolidColorBrush(Tokens.Rule), 1);
        var bottom = Math.Floor(bounds.Height) - 0.5;
        context.DrawLine(rule, new Point(0, bottom), new Point(bounds.Width, bottom));

        var size = Size;
        var middle = (bounds.Height / 2) + 1;
        var index = -1;
        foreach (var (area, close, tab, plane) in _tabs)
        {
            index++;
            var hovered = tab.WindowId == _hoverTab;
            if (tab.Selected)
            {
                // A card joined to the pane: rounded at the top, ruled on three sides, open at the bottom onto the pane.
                var r = Math.Min(NativeTokens.RadiusM, area.Height / 2);
                var card = CardGeometry(area, r, bounds.Height);
                context.DrawGeometry(new SolidColorBrush(plane), null, card);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Tokens.Rule), 1), CardOutline(area, r, bounds.Height));

                // The focused pane's front tab carries the focus cue: an accent bar along its top, between the corners.
                if (overlay.Model.Focused)
                {
                    context.DrawRectangle(new SolidColorBrush(Tokens.Accent), null, new Rect(area.X + r, area.Y, Math.Max(0, area.Width - (2 * r)), 2), 1, 1);
                }
            }
            else
            {
                if (plane != strip)
                {
                    context.DrawRectangle(new SolidColorBrush(plane), null, area.Deflate(new Thickness(0, 1, 0, 3)), NativeTokens.RadiusS, NativeTokens.RadiusS);
                }

                if (hovered && Interactive)
                {
                    context.DrawRectangle(new SolidColorBrush(PanelText.Alpha(Tokens.Text, 0x14)), null, area.Deflate(new Thickness(0, 1, 0, 3)), NativeTokens.RadiusS, NativeTokens.RadiusS);
                }
            }

            var ink = Held(tab.Selected ? Tokens.Text : Tokens.Dim, plane);
            var x = area.X + 14;
            var limit = area.Right - 14 - (close is null ? 0 : 16) - x;
            var drawn = PanelText.Draw(context, _labels.Length > index ? _labels[index] : tab.Name, x, middle, size, ink, tab.Selected, Math.Min(_names.Count > index ? _names[index] : NameCap, limit));
            x += drawn;
            if (tab.Unread > 0)
            {
                x += PanelText.Draw(context, " " + UnreadBadge.Format(tab.Unread), x, middle, size, Held(Tokens.Accent, plane), true, area.Right - x);
            }

            if (Marks(tab) is { Length: > 0 } marks)
            {
                PanelText.Draw(context, marks, x, middle, size, Held(Tokens.Dim, plane), false, area.Right - x);
            }

            if (close is { } c && hovered && Interactive)
            {
                var shade = _hoverClose && hovered ? 0x30 : 0;
                if (shade > 0)
                {
                    context.DrawRectangle(new SolidColorBrush(PanelText.Alpha(Tokens.Text, (byte)shade)), null, c, NativeTokens.RadiusS, NativeTokens.RadiusS);
                }

                var pen = new Pen(new SolidColorBrush(PanelText.Alpha(ink, 0xe0)), 1.25);
                var m = c.Center;
                context.DrawLine(pen, new Point(m.X - 3.5, m.Y - 3.5), new Point(m.X + 3.5, m.Y + 3.5));
                context.DrawLine(pen, new Point(m.X + 3.5, m.Y - 3.5), new Point(m.X - 3.5, m.Y + 3.5));
            }
        }

        if (_overflow is { } o && overlay.Overflow is { } menu)
        {
            var fill = _hoverOverflow ? Tokens.Selected : Tokens.Raised;
            context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Tokens.Edge), 1), o, NativeTokens.RadiusS, NativeTokens.RadiusS);
            PanelText.Draw(context, $"+{menu.Entries.Count} ▾", o.X + 8, o.Center.Y, size, Held(Tokens.Accent, fill), true, o.Width - 10);
        }

        if (!Interactive)
        {
            context.FillRectangle(new SolidColorBrush(PanelText.Alpha(strip, 0x70)), bounds);
        }
    }

    /// <summary>The selected tab's fill: rounded top corners, square bottom running down over the strip's rule.</summary>
    private static Geometry CardGeometry(Rect area, double r, double height)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        var bottom = height;
        c.BeginFigure(new Point(area.X, bottom), true);
        c.LineTo(new Point(area.X, area.Y + r));
        c.ArcTo(new Point(area.X + r, area.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(area.Right - r, area.Y));
        c.ArcTo(new Point(area.Right, area.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(area.Right, bottom));
        c.EndFigure(true);
        return g;
    }

    /// <summary>The selected tab's edge: three sides, none along the bottom, which is where it joins its pane.</summary>
    private static Geometry CardOutline(Rect area, double r, double height)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        var left = area.X + 0.5;
        var right = area.Right - 0.5;
        var top = area.Y + 0.5;
        c.BeginFigure(new Point(left, height), false);
        c.LineTo(new Point(left, top + r));
        c.ArcTo(new Point(left + r, top), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(right - r, top));
        c.ArcTo(new Point(right, top + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
        c.LineTo(new Point(right, height));
        c.EndFigure(false);
        return g;
    }
}

/// <summary>
/// The band across a pane split by scrolling back, drawn by the window over the band's row (its cells stay drawn under
/// it): how far back the history is, that new lines keep arriving below, and a <c>Jump to latest</c> button that ends
/// where End does (<see cref="ScrollBandModel"/>, the model the cells' rule is drawn from). Only the button is hit; the
/// rest of the band passes the pointer on to the cells. It takes no focus.
/// </summary>
internal sealed class ScrollBandView : Control, ICustomHitTest
{
    private Rect _button;
    private bool _hover;
    private bool _pressed;

    public ScrollBandView()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>What the client last said about this band. Internal for tests.</summary>
    internal ScrollBandOverlay? Overlay { get; private set; }

    private NativeTokens Tokens { get; set; } = NativeTokens.Dark;

    private bool Interactive { get; set; } = true;

    /// <summary>The button's area, as last laid out. Internal for tests.</summary>
    internal Rect Button => _button;

    private double Size => (double.IsNaN(Height) ? Bounds.Height : Height) >= 20 ? NativeTokens.Body : NativeTokens.Small;

    private string ButtonText => $"{ScrollBandModel.Action}  {NativeKeys.Words(ScrollBandModel.ReturnKey)}";

    /// <summary>Takes the client's band and the window's tokens and redraws.</summary>
    public void Show(ScrollBandOverlay band, NativeTokens tokens, bool interactive)
    {
        Overlay = band;
        Tokens = tokens;
        Interactive = interactive;
        Lay();
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Lay();
    }

    private void Lay()
    {
        var width = double.IsNaN(Width) ? Bounds.Width : Width;
        var height = double.IsNaN(Height) ? Bounds.Height : Height;
        var w = PanelText.Measure(ButtonText, Size, bold: true) + 20;
        var h = Math.Max(4, Math.Min(height - 4, 24));
        _button = new Rect(Math.Max(0, width - w - 12), (height - h) / 2, w, h);
    }

    public bool HitTest(Point point) => Interactive && _button.Contains(point);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Back to the live output (End, Ctrl+End)");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        var click = _pressed && e.InitialPressMouseButton == MouseButton.Left && _button.Contains(e.GetPosition(this));
        _pressed = false;
        InvalidateVisual();
        if (click)
        {
            PerformClick();
        }
    }

    /// <summary>Clicks the button as a left press and release would. Internal for tests.</summary>
    internal void PerformClick()
    {
        if (Overlay is { } band)
        {
            band.Jump(band.Model.WindowId);
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Overlay is not { } band)
        {
            return;
        }

        var bounds = new Rect(Bounds.Size);
        var plane = Tokens.Raised;
        context.FillRectangle(new SolidColorBrush(plane), bounds);
        var rule = new Pen(new SolidColorBrush(Tokens.Rule), 1);
        context.DrawLine(rule, new Point(0, 0.5), new Point(bounds.Width, 0.5));
        context.DrawLine(rule, new Point(0, Math.Floor(bounds.Height) - 0.5), new Point(bounds.Width, Math.Floor(bounds.Height) - 0.5));

        var size = Size;
        var middle = bounds.Height / 2;
        var x = 12.0;
        var room = _button.X - 12;
        x += PanelText.Draw(context, band.Model.Lead(band.Count), x, middle, size, NativeTabStrip.Held(Tokens.Text, plane), true, room - x);
        if (room - x > 40)
        {
            PanelText.Draw(context, ScrollBandModel.Detail, x + 12, middle, size, NativeTabStrip.Held(Tokens.Dim, plane), false, room - x - 12);
        }

        var fill = Tokens.Accent;
        var shade = _hover && Interactive ? 0xe8 : 0xff;
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)shade, fill.R, fill.G, fill.B)), null, _button, NativeTokens.RadiusS + 2, NativeTokens.RadiusS + 2);
        PanelText.Draw(context, ButtonText, _button.X + 10, _button.Center.Y, size, Tokens.AccentInk, true, _button.Width - 12);
    }
}
