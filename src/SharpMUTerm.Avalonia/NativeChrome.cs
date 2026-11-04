using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// What the native header and sidebar share: a clickable area naming the target the client put in its model, and the
/// pointer handling that turns a press and release on one area into that target. Nothing here spells a target: every
/// one comes out of <see cref="HeaderModel"/> or <see cref="SidebarModel"/>, and goes back through
/// <see cref="ChromeOverlay.Activate"/>, which acts on it exactly as a click on the cell rail or header would.
/// <para>
/// Both sit beside the cells, never over them (<see cref="NativeLayout"/>): the client leaves no row or column for
/// them. The window's resize band runs along their outer edges as it does along the cells'. Never focusable: the
/// keyboard stays with the client. While a modal of the client's is in front (<see cref="ChromeOverlay.Interactive"/>
/// false) they stay drawn, under a veil, and take no click.
/// </para>
/// </summary>
internal abstract class NativeChromeView : Control
{
    private readonly List<(Rect Area, string Target, string Tip)> _hits = [];
    private string? _pressed;
    private Rect? _hover;

    protected NativeChromeView(TerminalView view)
    {
        View = view;
        Focusable = false;
        ClipToBounds = true;
    }

    protected TerminalView View { get; }

    /// <summary>What the client last said, or null before it has said anything.</summary>
    internal ChromeOverlay? Overlay { get; private set; }

    protected Color Plane { get; private set; } = Colors.Black;

    protected Color Ink { get; private set; } = Colors.White;

    /// <summary>Whether the chrome is drawn but takes no click, because a modal of the client's is in front.</summary>
    internal bool Inert => Overlay is { Interactive: false };

    /// <summary>The clickable areas and their targets, as last laid out. Internal for tests.</summary>
    internal IReadOnlyList<(Rect Area, string Target, string Tip)> Hits => _hits;

    /// <summary>Takes what the client said and the plane and ink to draw with, and redraws.</summary>
    public void Show(ChromeOverlay? overlay, Color plane, Color ink)
    {
        Overlay = overlay;
        Plane = plane;
        Ink = ink;
        _hits.Clear();
        if (overlay is not null && !double.IsNaN(Width) && !double.IsNaN(Height))
        {
            Layout(overlay, _hits);

            // Every tooltip in the window's words, whatever the client's model said it in.
            for (var i = 0; i < _hits.Count; i++)
            {
                _hits[i] = _hits[i] with { Tip = NativeKeys.Words(_hits[i].Tip) };
            }
        }

        InvalidateVisual();
    }

    /// <summary>Works out where each clickable thing goes, for the size the view has been given.</summary>
    protected abstract void Layout(ChromeOverlay overlay, List<(Rect Area, string Target, string Tip)> hits);

    /// <summary>A left press on no clickable area. The header moves the window; the sidebar does nothing.</summary>
    protected virtual void PressedEmpty(PointerPressedEventArgs e)
    {
    }

    /// <summary>The area under <paramref name="point"/>, or null. An area with no target only carries a tooltip.</summary>
    protected (Rect Area, string Target, string Tip)? HitAt(Point point)
    {
        foreach (var hit in _hits)
        {
            if (hit.Area.Contains(point))
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>Whether the pointer is over <paramref name="area"/>, for a hover plate.</summary>
    protected bool Hovered(Rect area) => !Inert && _hover == area;

    /// <summary>Whether <paramref name="area"/> is being pressed.</summary>
    protected bool Pressing(Rect area) => _pressed is not null && _hover == area;

    /// <summary>Acts on a target as a click would. Internal for tests.</summary>
    internal void Click(string target)
    {
        if (!Inert && target.Length > 0)
        {
            Overlay?.Activate(target);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitAt(e.GetPosition(this));
        var area = hit?.Area;
        var edge = View.EdgeCursorFromChrome(e);
        Cursor = edge is { } resize ? new Cursor(resize)
            : hit is { Target.Length: > 0 } && !Inert ? new Cursor(StandardCursorType.Hand)
            : Cursor.Default;
        if (area != _hover)
        {
            _hover = area;
            ToolTip.SetTip(this, hit?.Tip);
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

    // Every pointer event over the view is the window's: there is no cell under it to reach, and the client's own
    // cells beside it are a different place.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (View.ResizeFromChrome(e))
        {
            return;
        }

        if (!Inert && HitAt(point.Position) is { Target.Length: > 0 } hit)
        {
            _pressed = hit.Target;
            _hover = hit.Area;
            InvalidateVisual();
            return;
        }

        PressedEmpty(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        var pressed = _pressed;
        _pressed = null;
        InvalidateVisual();
        if (pressed is not null && e.InitialPressMouseButton == MouseButton.Left
            && HitAt(e.GetPosition(this)) is { } hit && hit.Target == pressed)
        {
            Click(pressed);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    // ── Drawing ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The UI font size the chrome's text is set in.</summary>
    protected const double FontSize = NativeTokens.Body + 1;

    /// <summary>
    /// Secondary text (<c>offline</c>, a chord, <c>CONNECTIONS</c>): the ink two thirds of the way from the plane,
    /// opaque and held to <see cref="NativeTokens.TextFloor"/> on it. It is small text; a translucent ink was
    /// 3.1–3.4:1 on Light's sidebar.
    /// </summary>
    protected Color Dim => SecondaryInk(Ink, Plane);

    /// <summary><see cref="Dim"/> for an ink on a plane. Internal for tests.</summary>
    internal static Color SecondaryInk(Color ink, Color plane)
    {
        const double share = 0xA8 / 255.0;
        byte Mix(byte a, byte b) => (byte)Math.Round((a * share) + (b * (1 - share)));
        var mixed = new Rgb(Mix(ink.R, plane.R), Mix(ink.G, plane.G), Mix(ink.B, plane.B));
        return ToColour(Contrast.Legible(mixed, new Rgb(plane.R, plane.G, plane.B), NativeTokens.TextFloor));
    }

    protected Color Faint(byte alpha) => Color.FromArgb(alpha, Ink.R, Ink.G, Ink.B);

    /// <summary>A colour the client handed over, held to the legibility floor against the view's plane.</summary>
    protected Color Legible(Color colour) =>
        ToColour(Contrast.Legible(new Rgb(colour.R, colour.G, colour.B), new Rgb(Plane.R, Plane.G, Plane.B)));

    /// <summary>A world's accent, legible on the plane; the client's accent when the world has none of its own.</summary>
    protected Color Accent(TerminalColor accent) =>
        accent.Kind == TerminalColorKind.Rgb ? Legible(Color.FromRgb(accent.R, accent.G, accent.B)) : Legible(Parse(Overlay?.Palette.Accent));

    /// <summary>Ink on a fill, held to the floor against that fill.</summary>
    protected static Color On(Color fill, Color ink) =>
        ToColour(Contrast.Legible(new Rgb(ink.R, ink.G, ink.B), new Rgb(fill.R, fill.G, fill.B)));

    /// <summary>A <c>#rrggbb</c> the client handed over, or grey.</summary>
    internal static Color Parse(string? hex) => hex is not null && Color.TryParse(hex, out var colour) ? colour : Colors.Gray;

    private static Color ToColour(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);

    /// <summary>The plane lifted (on a dark theme) or lowered (on a light one) by <paramref name="amount"/>: a raised surface.</summary>
    protected Color Raised(double amount)
    {
        var luma = (0.2126 * Plane.R) + (0.7152 * Plane.G) + (0.0722 * Plane.B);
        var target = luma < 128 ? Colors.White : Colors.Black;
        byte Mix(byte a, byte b) => (byte)Math.Round(a + ((b - a) * amount));
        return Color.FromRgb(Mix(Plane.R, target.R), Mix(Plane.G, target.G), Mix(Plane.B, target.B));
    }

    /// <summary>The veil a modal in front lays over the chrome: drawn, readable, and plainly not the thing in use.</summary>
    protected void DrawVeil(DrawingContext context)
    {
        if (Inert)
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x70, Plane.R, Plane.G, Plane.B)), null, new Rect(Bounds.Size));
        }
    }

    protected static FormattedText Format(string text, double size, Color colour, bool bold = false, double maxWidth = double.PositiveInfinity)
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

    protected static double Measure(string text, double size, bool bold = false) => Format(text, size, Colors.White, bold).Width;

    /// <summary>Draws one line of text vertically centred on <paramref name="middle"/>; returns its width.</summary>
    protected static double Text(
        DrawingContext context, string text, double x, double middle, double size, Color colour, bool bold = false,
        double maxWidth = double.PositiveInfinity, bool alignRight = false)
    {
        var formatted = Format(text, size, colour, bold, maxWidth);
        var left = alignRight ? x - formatted.Width : x;
        context.DrawText(formatted, new Point(Math.Round(left), Math.Round(middle - (formatted.Height / 2))));
        return formatted.Width;
    }

    /// <summary>An unread count as a pill, right-aligned at <paramref name="right"/>; returns its width.</summary>
    protected double Badge(DrawingContext context, int unread, double right, double middle, double size)
    {
        var text = UnreadText(unread);
        var fill = Legible(Parse(Overlay?.Palette.Unread));
        var width = Math.Max(Measure(text, size * 0.85, bold: true) + (size * 0.9), size * 1.4);
        var height = Math.Max(size * 1.3, 10);
        var pill = new Rect(right - width, middle - (height / 2), width, height);
        context.DrawRectangle(new SolidColorBrush(fill), null, pill, height / 2, height / 2);
        Text(context, text, pill.X + ((width - Measure(text, size * 0.85, bold: true)) / 2), middle, size * 0.85, On(fill, Plane), bold: true);
        return width;
    }

    /// <summary>A presence dot: filled while connected, a ring while being dialled or offline.</summary>
    protected static void Dot(DrawingContext context, Point centre, double radius, Color colour, bool filled, bool diamond = false)
    {
        if (diamond)
        {
            // A member of the send group wears a diamond, as on the cells.
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                g.BeginFigure(centre + new Point(0, -radius * 1.25), true);
                g.LineTo(centre + new Point(radius * 1.25, 0));
                g.LineTo(centre + new Point(0, radius * 1.25));
                g.LineTo(centre + new Point(-radius * 1.25, 0));
                g.EndFigure(true);
            }

            context.DrawGeometry(filled ? new SolidColorBrush(colour) : null, new Pen(new SolidColorBrush(colour), 1.3), shape);
            return;
        }

        if (filled)
        {
            context.DrawEllipse(new SolidColorBrush(colour), null, centre, radius, radius);
        }
        else
        {
            context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 1.3), centre, radius, radius);
        }
    }

    /// <summary>A chevron pointing left (<paramref name="left"/>) or right, centred in <paramref name="area"/>.</summary>
    protected static void Chevron(DrawingContext context, Rect area, bool left, Color colour)
    {
        var pen = new Pen(new SolidColorBrush(colour), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var c = area.Center;
        var s = Math.Min(area.Width, area.Height) * 0.18;
        foreach (var dx in new[] { -s * 0.9, s * 0.9 })
        {
            var tip = left ? c.X + dx - s : c.X + dx + s;
            var back = left ? c.X + dx + s : c.X + dx - s;
            context.DrawLine(pen, new Point(back, c.Y - (s * 1.6)), new Point(tip, c.Y));
            context.DrawLine(pen, new Point(tip, c.Y), new Point(back, c.Y + (s * 1.6)));
        }
    }

    /// <summary>A count as the cells write it (<c>99+</c> past the cap), so the two views agree about one count.</summary>
    internal static string UnreadText(int unread) => unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The window's own header band, across the top of the window above the cells: the app's name, a search box that
/// opens the command surface (⌃P), the connected count and log state, and the character in front as a pill — a
/// presence dot, the name, and <c>world · TLS</c>. Everything it says is <see cref="HeaderModel"/>, the model the cell
/// ribbon would be drawn from; the client draws no header row in this host.
/// <para>
/// While ⌃B is armed the search box and the counts give way to the keymap (<see cref="HeaderModel.Prefix"/>): the
/// chord, each key it answers (a key that cannot run now is dimmed, and its tooltip says why), and the way out — what
/// the cell header's strip says, with the room to say all of it. The right end is left clear for the header buttons
/// (<see cref="NativeLayout.ButtonSize"/>), which sit above this view. A press on empty space moves the window and a
/// double-click maximises (<see cref="TerminalView.WindowGestureFromChrome"/>).
/// </para>
/// </summary>
internal sealed class NativeHeader(TerminalView view) : NativeChromeView(view)
{
    /// <summary>What the search box says. ⌃P lists commands, characters and windows, and settings screens.</summary>
    internal const string SearchText = CommandPalette.Title;

    /// <summary>The prefix's leading word, beside its chord.</summary>
    internal const string PrefixWord = "awaiting a pane key";

    /// <summary>Width kept clear at the right for the header buttons.</summary>
    public double ReserveRight { get; set; }

    private Rect _brand;
    private Rect _search;
    private Rect _pill;
    private Rect _counts;
    private Rect _prefix;
    private readonly List<(Rect Area, HeaderPrefixKey Key)> _keys = [];
    private Rect _exit;

    /// <summary>The ⌃B keys as last laid out. Internal for tests.</summary>
    internal IReadOnlyList<(Rect Area, HeaderPrefixKey Key)> PrefixKeys => _keys;

    private const double PlateHeight = 30;

    protected override void Layout(ChromeOverlay overlay, List<(Rect Area, string Target, string Tip)> hits)
    {
        _brand = _search = _pill = _counts = _prefix = _exit = default;
        _keys.Clear();
        var model = overlay.HeaderModel;
        const double size = FontSize;
        var h = Height;
        var plateHeight = Math.Min(PlateHeight, Math.Max(4, h - 6));
        var plateTop = Math.Round((h - plateHeight) / 2);
        const double pad = NativeTokens.Space12;

        // The brand is the name in plain text, as a desktop app's title reads (the mockup's): still the palette's door,
        // shown by a plate only under the pointer. The ribbon's filled segment is the cells' way of saying it.
        _brand = new Rect(NativeTokens.PanelInset - NativeTokens.Space8, plateTop, Measure("SharpMUTerm", size + 2, bold: true) + (2 * NativeTokens.Space8), plateHeight);
        hits.Add((_brand, HeaderModel.PaletteTarget, $"Commands ({KeyGlyphs.Words(HeaderModel.PaletteKey)})"));

        var right = Width - ReserveRight - NativeTokens.Space8;
        if (model.Character is { } character && model.World is { } world)
        {
            var name = Measure(character.Name, size, bold: true);
            var detail = Measure(character.Detail(world.Name), size - 1);
            var width = Math.Min(pad + 10 + 8 + name + 10 + detail + pad, Math.Max(0, right - _brand.Right - 24));
            _pill = new Rect(right - width, plateTop, width, plateHeight);
            hits.Add((_pill, character.Target, $"{character.Name} on {world.Name} — {character.Host}:{character.Port}"));
            right = _pill.Left - 16;
        }

        var start = _brand.Right + 20;
        if (model.Prefix is { } prefix)
        {
            LayoutPrefix(prefix, start, right, plateTop, plateHeight, hits);
            return;
        }

        var counts = CountsText(model);
        var countsWidth = Measure(counts, size - 1);
        var keyWidth = Measure(KeyGlyphs.Words(HeaderModel.PaletteKey), size - 1.5) + 12;
        var searchFloor = 18 + Measure("Search", size) + keyWidth + (pad * 3);
        if (right - countsWidth - 20 - start >= searchFloor)
        {
            // The counts go to the left of the search box, which keeps its place beside the pill.
            var searchWidth = Math.Min(360, right - countsWidth - 20 - start);
            _counts = new Rect(right - searchWidth - 20 - countsWidth, 0, countsWidth, h);
        }

        if (right - start >= searchFloor)
        {
            // Beside the character's pill at the right, as the mockup sets it, rather than against the brand.
            var width = Math.Min(360, right - start);
            _search = new Rect(right - width, plateTop, width, plateHeight);
            hits.Add((_search, HeaderModel.PaletteTarget, $"Commands, characters, windows and settings ({KeyGlyphs.Words(HeaderModel.PaletteKey)})"));
        }
    }

    private void LayoutPrefix(HeaderPrefix prefix, double start, double right, double plateTop, double plateHeight, List<(Rect Area, string Target, string Tip)> hits)
    {
        const double size = FontSize;
        var chipHeight = plateHeight - 6;
        var chipTop = plateTop + 3;
        var lead = Measure(KeyGlyphs.Words(HeaderPrefix.Chord), size, bold: true) + Measure(PrefixWord, size - 1) + 34;
        _prefix = new Rect(start, plateTop, Math.Min(lead, Math.Max(0, right - start)), plateHeight);
        hits.Add((_prefix, string.Empty, $"{KeyGlyphs.Words(HeaderPrefix.Chord)} is armed: the next key acts on the workspace. {prefix.Exit}."));

        var exitWidth = Measure(NativeKeys.Words(prefix.Exit), size - 1) + 8;
        var x = _prefix.Right + 12;
        var limit = right - exitWidth - 12;
        foreach (var key in prefix.Keys)
        {
            var width = Measure(NativeKeys.Words(key.Keys), size, bold: true) + 16;
            if (x + width > limit)
            {
                break;
            }

            var area = new Rect(x, chipTop, width, chipHeight);
            _keys.Add((area, key));
            hits.Add((area, string.Empty, key.Available ? $"{key.Keys}  {key.Title}" : $"{key.Keys}  {key.Title} — {key.Blocked}"));
            x += width + 5;
        }

        if (right - x >= exitWidth)
        {
            _exit = new Rect(right - exitWidth, 0, exitWidth, Height);
        }
    }

    private static string CountsText(HeaderModel model) =>
        (model.Recording ? $"{HeaderModel.RecordingText}  ·  " : string.Empty)
        + (model.HasWorlds ? $"{model.ConnectedText}  ·  {model.LogText}" : model.LogText);

    protected override void PressedEmpty(PointerPressedEventArgs e) => View.WindowGestureFromChrome(e);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Plane), null, bounds);

        // A hairline under the band, where a desktop app's toolbar meets its content: one device pixel, the band's last.
        context.DrawRectangle(new SolidColorBrush(Faint(0x24)), null,
            NativeTokens.HorizontalRule(this, bounds.Height - NativeTokens.DevicePixel(this), 0, bounds.Width));
        if (Overlay is not { } overlay)
        {
            return;
        }

        var model = overlay.HeaderModel;
        const double size = FontSize;
        var middle = Bounds.Height / 2;
        const double radius = NativeTokens.RadiusM;

        // The brand: the name in the header's own ink, a plate under it only while it is pointed at or pressed.
        if (Pressing(_brand) || Hovered(_brand))
        {
            context.DrawRectangle(new SolidColorBrush(Pressing(_brand) ? Raised(0.13) : Raised(0.08)), null, _brand, radius, radius);
        }

        Text(context, "SharpMUTerm", NativeTokens.PanelInset, middle, size + 2, Ink, bold: true);

        if (_search.Width > 0)
        {
            DrawSearch(context, middle, radius);
        }

        if (_counts.Width > 0)
        {
            Text(context, CountsText(model), _counts.X, middle, size - 1, Parse(overlay.Palette.HeaderMuted));
        }

        if (model.Prefix is { } prefix)
        {
            DrawPrefix(context, prefix, overlay, middle);
        }

        if (_pill.Width > 0 && model.Character is { } character && model.World is { } world)
        {
            var fill = Hovered(_pill) ? Raised(0.16) : Raised(0.09);
            context.DrawRectangle(new SolidColorBrush(fill), null, _pill, _pill.Height / 2, _pill.Height / 2);
            var accent = Accent(world.Accent);
            var dotCentre = new Point(_pill.X + 12 + 4.5, middle);
            Dot(context, dotCentre, 4.5, character.Presence == CharacterPresence.Offline ? Dim : accent, character.Presence == CharacterPresence.Online);
            var x = dotCentre.X + 4.5 + 8;
            var room = _pill.Right - 12 - x;
            var nameWidth = Text(context, character.Name, x, middle, size, Ink, bold: true, maxWidth: room);
            x += nameWidth + 10;
            if (_pill.Right - 12 - x > 12)
            {
                Text(context, character.Detail(world.Name), x, middle, size - 1, Parse(overlay.Palette.HeaderMuted), maxWidth: _pill.Right - 12 - x);
            }
        }

        DrawVeil(context);
    }

    private void DrawSearch(DrawingContext context, double middle, double radius)
    {
        const double size = FontSize;
        var fill = Hovered(_search) ? Raised(0.13) : Raised(0.07);
        context.DrawRectangle(new SolidColorBrush(fill), NativeTokens.HairlinePen(this, Faint(0x30)), NativeTokens.StrokeRect(this, _search), radius, radius);

        // A magnifier: a ring and its handle.
        const double ring = 5;
        var centre = new Point(_search.X + 14 + ring, middle - 1);
        var pen = new Pen(new SolidColorBrush(Dim), 1.4, lineCap: PenLineCap.Round);
        context.DrawEllipse(null, pen, centre, ring, ring);
        context.DrawLine(pen, centre + new Point(ring * 0.72, ring * 0.72), centre + new Point(ring * 1.55, ring * 1.55));

        var key = KeyGlyphs.Words(HeaderModel.PaletteKey);
        var keyWidth = Measure(key, size - 1.5) + 12;
        var cap = new Rect(_search.Right - 8 - keyWidth, middle - 10, keyWidth, 20);
        context.DrawRectangle(null, NativeTokens.HairlinePen(this, Faint(0x50)), NativeTokens.StrokeRect(this, cap), NativeTokens.RadiusS, NativeTokens.RadiusS);
        Text(context, key, cap.X + 6, middle, size - 1.5, Dim);
        var textLeft = centre.X + (ring * 2) + 8;
        Text(context, SearchText, textLeft, middle, size, Dim, maxWidth: Math.Max(1, cap.Left - 10 - textLeft));
    }

    private void DrawPrefix(DrawingContext context, HeaderPrefix prefix, ChromeOverlay overlay, double middle)
    {
        const double size = FontSize;
        var notice = Legible(Parse(overlay.Palette.Notice));
        if (_prefix.Width > 0)
        {
            var plate = Color.FromArgb(0x2A, notice.R, notice.G, notice.B);
            context.DrawRectangle(new SolidColorBrush(plate), NativeTokens.HairlinePen(this, Color.FromArgb(0x90, notice.R, notice.G, notice.B)), NativeTokens.StrokeRect(this, _prefix), NativeTokens.RadiusM, NativeTokens.RadiusM);
            var x = _prefix.X + 12;
            x += Text(context, KeyGlyphs.Words(HeaderPrefix.Chord), x, middle, size, notice, bold: true) + 8;
            Text(context, PrefixWord, x, middle, size - 1, Ink, maxWidth: Math.Max(1, _prefix.Right - 10 - x));
        }

        foreach (var (area, key) in _keys)
        {
            var colour = key.Available ? Ink : Faint(0x60);
            context.DrawRectangle(new SolidColorBrush(Raised(key.Available ? 0.12 : 0.04)), NativeTokens.HairlinePen(this, Faint(key.Available ? (byte)0x40 : (byte)0x20)), NativeTokens.StrokeRect(this, area), NativeTokens.RadiusControl, NativeTokens.RadiusControl);
            Text(context, NativeKeys.Words(key.Keys), area.X + 8, middle, size, colour, bold: true);
        }

        if (_exit.Width > 0)
        {
            Text(context, NativeKeys.Words(prefix.Exit), _exit.Right, middle, size - 1, Dim, alignRight: true);
        }
    }
}

/// <summary>
/// The window's own sidebar, down the left of the window beside the cells: <c>CONNECTIONS</c>, worlds as headings,
/// characters with a presence dot and an unread badge (offline ones greyed and saying so), the open character's
/// windows indented with the chord that reaches each and its unread count, and an "add a game" button at the foot.
/// Everything it says is <see cref="SidebarModel"/>, the model the cell rail would be drawn from, and every click goes
/// back as the target that model gave — the rail's own handler acts on it. The client draws no rail column here.
/// <para>
/// <b>Folded</b> (⌃B b, or the fold button beside the title) it is a slim strip, the cell rail's collapsed form at a
/// desktop app's size: an unfold button, an accent mark per world, and a dot with an initial per character (filled
/// while connected, with an unread mark), each still a click on its target, with the name in its tooltip.
/// </para>
/// <para>
/// <b>More rows than fit scroll</b> under the title and above the foot button, with the wheel or by dragging an overlay
/// scrollbar drawn like the panes' (thin, widening under the pointer). A right click on a row opens a native menu of the
/// client's entries for that row (<see cref="ChromeOverlay.RowMenu"/>, the registry's commands with their keys); and
/// while the keyboard is on the sidebar (⌥C, <see cref="SidebarModel.Cursor"/>) its row wears a focus ring and is
/// scrolled into view. The keys themselves are the client's: nothing here is focusable.
/// </para>
/// </summary>
internal sealed class NativeSidebar(TerminalView view) : NativeChromeView(view)
{
    /// <summary>What the button at the foot says.</summary>
    internal const string AddGameText = "+ Add a world or connect";

    /// <summary>The fold button's tooltip, open and folded.</summary>
    internal const string FoldTip = "Fold the sidebar (Ctrl+B b)", UnfoldTip = "Open the sidebar (Ctrl+B b)";

    /// <summary>One drawn row: what it is, where, and its node.</summary>
    internal sealed record Row(RailRowKind Kind, Rect Area, RailWorld World, RailCharacter? Character = null, RailWindow? Window = null);

    private readonly List<Row> _rows = [];
    private Rect _add;
    private Rect _fold;

    /// <summary>How far the rows are scrolled, in pixels, and the list area they scroll in.</summary>
    private double _scroll;
    private double _content;
    private Rect _list;
    private bool _barHovered;
    private double? _barGrab;

    /// <summary>How far the rows are scrolled, in pixels. Internal for tests.</summary>
    internal double ScrollOffset => _scroll;

    /// <summary>The most the rows can be scrolled: zero when they all fit. Internal for tests.</summary>
    internal double ScrollRange => Math.Max(0, _content - _list.Height);

    /// <summary>The row menu last opened, for a test.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    /// <summary>The rows as last laid out. Internal for tests.</summary>
    internal IReadOnlyList<Row> Rows => _rows;

    private const double RowHeight = 28;

    /// <summary>
    /// A row's own left edge: half the chrome's inset, so a row's plate starts there and its text — a world's name, the
    /// title, the foot button's words — on <see cref="NativeTokens.PanelInset"/>, the header brand's edge.
    /// </summary>
    private const double RowInset = NativeTokens.PanelInset - NativeTokens.Space8;

    /// <summary>A character row's name: the mockup's indent under its world, past the presence dot.</summary>
    private const double CharacterIndent = NativeTokens.Space32;

    /// <summary>A window row's name, indented under its character's (the mockup's 46 px from the sidebar's edge).</summary>
    private const double WindowIndent = CharacterIndent + NativeTokens.Space4;

    private const double TitleHeight = 40;

    private bool Folded => Overlay?.Sidebar.Collapsed ?? false;

    protected override void Layout(ChromeOverlay overlay, List<(Rect Area, string Target, string Tip)> hits)
    {
        _rows.Clear();
        _add = default;
        var width = Width - NativeLayout.SidebarGutter; // the gutter is plane, never a row
        var folded = overlay.Sidebar.Collapsed;
        _fold = folded ? new Rect((width - 32) / 2, 6, 32, 28) : new Rect(width - 38, 6, 30, 28);
        hits.Add((_fold, SidebarModel.CollapseTarget, folded ? UnfoldTip : FoldTip));

        var row = folded ? 32 : RowHeight;
        _add = folded
            ? new Rect((width - 32) / 2, Height - 32 - NativeTokens.Space12, 32, 32)
            : new Rect(RowInset, Height - NativeTokens.ControlHeight - NativeTokens.Space12, Math.Max(0, width - (2 * RowInset)), NativeTokens.ControlHeight);
        var bottom = _add.Top - NativeTokens.Space8;
        _list = new Rect(0, TitleHeight, width, Math.Max(0, bottom - TitleHeight));
        var y = TitleHeight;

        // Laid out unscrolled first: the scroll is clamped to what the rows need, and moved to keep the keyboard's row
        // in view, before anything is placed.
        var laid = new List<(Row Row, string Target, string Tip)>();
        void Add(Row r, string target, string tip) => laid.Add((r, target, tip));

        foreach (var world in overlay.Sidebar.Worlds)
        {
            y += folded ? 4 : 8;
            if (folded)
            {
                // The world's accent mark, one short rule: where its characters begin.
                Add(new Row(RailRowKind.World, new Rect(RowInset, y, width - (2 * RowInset), 10), world), SidebarModel.TargetOf(world), world.Name);
                y += 10;
            }
            else
            {
                Add(new Row(RailRowKind.World, new Rect(RowInset, y, width - (2 * RowInset), row), world), SidebarModel.TargetOf(world), $"{world.Name} — {world.Host}:{world.Port}");
                y += row;
            }

            if (world.Characters.Count == 0)
            {
                if (!folded)
                {
                    Add(new Row(RailRowKind.Empty, new Rect(RowInset, y, width - (2 * RowInset), row), world), SidebarModel.TargetOf(world), $"{world.Name} has no characters yet");
                    y += row;
                }

                continue;
            }

            foreach (var character in world.Characters)
            {
                var area = folded ? new Rect((width - 36) / 2, y, 36, row) : new Rect(RowInset, y, width - (2 * RowInset), row);
                var tip = folded
                    ? $"{character.Name} on {world.Name}{(character.Connected ? string.Empty : " (offline)")}{(character.Unread > 0 ? $", {UnreadText(character.Unread)} unread" : string.Empty)}"
                    : $"Switch to {character.Name}";
                Add(new Row(RailRowKind.Character, area, world, character), SidebarModel.TargetOf(character), tip);
                y += row;
                if (!character.Active || folded)
                {
                    continue;
                }

                foreach (var window in character.Windows)
                {
                    Add(new Row(RailRowKind.Window, new Rect(RowInset, y, width - (2 * RowInset), row), world, character, window), SidebarModel.TargetOf(window), window.Closed ? $"{window.Title} is closed" : $"Go to {window.Title}");
                    y += row;
                }
            }
        }

        _content = y + 4 - TitleHeight;
        if (overlay.Sidebar.Cursor is { } cursor && laid.FirstOrDefault(l => KeyOf(l.Row) == cursor) is { Row: { } focused })
        {
            if (focused.Area.Top - _scroll < _list.Top)
            {
                _scroll = focused.Area.Top - _list.Top - 4;
            }
            else if (focused.Area.Bottom - _scroll > _list.Bottom)
            {
                _scroll = focused.Area.Bottom - _list.Bottom + 4;
            }
        }

        _scroll = Math.Clamp(_scroll, 0, ScrollRange);
        foreach (var (r, target, tip) in laid)
        {
            var area = r.Area.Translate(new Vector(0, -_scroll));
            if (area.Bottom <= _list.Top || area.Top >= _list.Bottom)
            {
                continue;
            }

            _rows.Add(r with { Area = area });
            hits.Add((area.Intersect(_list), target, tip));
        }

        if (_add.Width > 20 && _add.Top > TitleHeight)
        {
            hits.Add((_add, SidebarModel.AddGameTarget, "Connect to a game, or add one"));
        }
        else
        {
            _add = default;
        }
    }

    /// <summary>A drawn row's identity, as the client keys it (<see cref="SidebarModel.KeyOf(RailRowKind, string?)"/>).</summary>
    internal static string KeyOf(Row row) => SidebarModel.KeyOf(row.Kind, TargetOf(row));

    /// <summary>The target the cell rail gives the same row.</summary>
    internal static string TargetOf(Row row) => row.Kind switch
    {
        RailRowKind.Window => SidebarModel.TargetOf(row.Window!),
        RailRowKind.Character => SidebarModel.TargetOf(row.Character!),
        RailRowKind.Empty => RailModel.NoCharactersTarget(row.World.Name),
        _ => SidebarModel.TargetOf(row.World),
    };

    /// <summary>The row under <paramref name="point"/>, inside the list area, or null.</summary>
    private Row? RowAt(Point point) =>
        _list.Contains(point) ? _rows.FirstOrDefault(r => r.Area.Contains(point)) : null;

    /// <summary>Scrolls the rows by <paramref name="pixels"/> (positive is down the list) and lays them out again.</summary>
    internal void ScrollBy(double pixels)
    {
        var before = _scroll;
        _scroll = Math.Clamp(_scroll + pixels, 0, ScrollRange);
        if (_scroll != before)
        {
            Show(Overlay, Plane, Ink);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollBy(-e.Delta.Y * RowHeight * 3);
    }

    /// <summary>
    /// Asks the client for <paramref name="rowKey"/>'s menu and opens it at the pointer. The client builds it on its own
    /// thread and answers there; the menu is opened on this one. Internal for tests.
    /// </summary>
    internal void OpenRowMenu(string rowKey)
    {
        if (Overlay is not { } overlay || Inert)
        {
            return;
        }

        overlay.RowMenu(rowKey, model => Dispatcher.UIThread.Post(() =>
        {
            if (model.Entries.Count == 0)
            {
                return;
            }

            var menu = NativeMenu.Build(model, overlay.RunRowMenu, PlacementMode.Pointer, this);
            OpenedMenu = menu;
            menu.Open(this);
        }));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!Inert && point.Properties.IsRightButtonPressed && RowAt(point.Position) is { } row)
        {
            e.Handled = true;
            OpenRowMenu(KeyOf(row));
            return;
        }

        if (!Inert && point.Properties.IsLeftButtonPressed && Bar() is { } bar && bar.Zone.Contains(point.Position))
        {
            e.Handled = true;
            _barGrab = bar.Thumb.Contains(point.Position) ? point.Position.Y - bar.Thumb.Top : bar.Thumb.Height / 2;
            e.Pointer.Capture(this);
            DragBar(point.Position.Y);
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_barGrab is not null)
        {
            DragBar(position.Y);
            e.Handled = true;
            return;
        }

        var hovered = Bar() is { } bar && bar.Zone.Contains(position);
        if (hovered != _barHovered)
        {
            _barHovered = hovered;
            InvalidateVisual();
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_barGrab is not null)
        {
            _barGrab = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _barHovered = false;
        base.OnPointerExited(e);
    }

    /// <summary>The panes' bar, at the panes' sizes: over the list's right edge, never in a column of it.</summary>
    private const double BarWidth = 4, BarWidthHovered = 8, BarZone = 12, MinimumThumb = 18;

    /// <summary>The scrollbar's zone and thumb, or null when every row fits. Internal for tests.</summary>
    internal (Rect Zone, Rect Thumb, double Travel)? Bar()
    {
        if (ScrollRange <= 0 || _list.Height <= 0)
        {
            return null;
        }

        var thumb = Math.Min(_list.Height, Math.Max(MinimumThumb, _list.Height * _list.Height / _content));
        var travel = _list.Height - thumb;
        var y = _list.Top + (travel * _scroll / ScrollRange);
        var width = _barHovered || _barGrab is not null ? BarWidthHovered : BarWidth;
        return (new Rect(_list.Right - BarZone, _list.Top, BarZone, _list.Height), new Rect(_list.Right - width - 2, y, width, thumb), travel);
    }

    private void DragBar(double y)
    {
        if (Bar() is not { } bar || bar.Travel <= 0 || _barGrab is not { } grab)
        {
            return;
        }

        var fraction = Math.Clamp((y - grab - bar.Zone.Top) / bar.Travel, 0, 1);
        ScrollBy((fraction * ScrollRange) - _scroll);
    }

    private void DrawBar(DrawingContext context)
    {
        if (Bar() is not { } bar)
        {
            return;
        }

        var active = _barHovered || _barGrab is not null;
        if (active)
        {
            var track = new Rect(bar.Thumb.X, bar.Zone.Y, bar.Thumb.Width, bar.Zone.Height);
            context.DrawRectangle(new SolidColorBrush(Faint(0x22)), null, track, bar.Thumb.Width / 2, bar.Thumb.Width / 2);
        }

        context.DrawRectangle(new SolidColorBrush(Faint(active ? (byte)0xB0 : (byte)0x60)), null, bar.Thumb, bar.Thumb.Width / 2, bar.Thumb.Width / 2);
    }

    /// <summary>The keyboard's row: a ring in the client's accent, over a lifted plate, so it reads apart from a hover.</summary>
    private void DrawCursor(DrawingContext context, Row row)
    {
        var accent = Legible(Parse(Overlay?.Palette.Accent));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(accent), 1.6), row.Area.Deflate(1), 6, 6);
    }

    public override void Render(DrawingContext context)
    {
        // The plane runs on through the gutter to the panes (NativeLayout.SidebarGutter): the pane's own raised surface
        // against it is the edge, as the cell rail's backdrop is.
        context.DrawRectangle(new SolidColorBrush(Plane), null, new Rect(Bounds.Size));
        if (Overlay is not { } overlay)
        {
            return;
        }

        const double size = FontSize;
        if (Hovered(_fold))
        {
            context.DrawRectangle(new SolidColorBrush(Raised(0.12)), null, _fold, 6, 6);
        }

        Chevron(context, _fold, left: !Folded, Dim);
        if (!Folded)
        {
            Text(context, "CONNECTIONS", NativeTokens.PanelInset, _fold.Center.Y, size - 2.5, Dim, bold: true);
        }

        using (context.PushClip(_list))
        {
            foreach (var r in _rows)
            {
                var focused = overlay.Sidebar.Cursor is { } cursor && KeyOf(r) == cursor;
                DrawRow(context, r, overlay, focused, size);
                if (focused)
                {
                    DrawCursor(context, r);
                }
            }
        }

        DrawBar(context);
        DrawFoot(context, size);
        DrawVeil(context);
    }

    private void DrawRow(DrawingContext context, Row r, ChromeOverlay overlay, bool focused, double size)
    {
            if (Folded)
            {
                DrawFolded(context, r, overlay);
                return;
            }

            var middle = r.Area.Center.Y;
            var hovered = Hovered(r.Area.Intersect(_list));
            // Only the character in front wears the selected plate: its window in front is said by the accent mark beside
            // the window's name, so the two rows do not read as two selections.
            var selected = r.Kind == RailRowKind.Character && r.Character!.Active;
            if (focused)
            {
                context.DrawRectangle(new SolidColorBrush(Raised(0.14)), null, r.Area, 6, 6);
            }
            else if (selected || hovered)
            {
                context.DrawRectangle(new SolidColorBrush(Raised(selected && hovered ? 0.16 : selected ? 0.11 : 0.07)), null, r.Area, 6, 6);
            }

            var accent = Accent(r.World.Accent);
            var right = r.Area.Right - 10;
            switch (r.Kind)
            {
                case RailRowKind.World:
                    context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(r.Area.X, middle - 7, NativeTokens.MarkWidth, 14), 1.5, 1.5);
                    Text(context, r.World.Name, NativeTokens.PanelInset, middle, size, Ink, bold: true, maxWidth: right - NativeTokens.PanelInset);
                    break;

                case RailRowKind.Empty:
                    Text(context, "no characters", RowInset + CharacterIndent, middle, size - 1, Dim, maxWidth: right - RowInset - CharacterIndent);
                    break;

                case RailRowKind.Character:
                    DrawCharacter(context, r, r.Character!, accent, middle, right, size);
                    break;

                case RailRowKind.Window:
                    DrawWindow(context, r, r.Window!, accent, overlay.Sidebar.IsSelected(r.Window!), middle, right, size);
                    break;
            }
    }

    private void DrawFoot(DrawingContext context, double size)
    {
        if (_add.Width > 0)
        {
            var fill = Hovered(_add) ? Raised(0.12) : Raised(0.05);
            context.DrawRectangle(new SolidColorBrush(fill), NativeTokens.HairlinePen(this, Faint(0x40)), NativeTokens.StrokeRect(this, _add), NativeTokens.RadiusM, NativeTokens.RadiusM);
            if (Folded)
            {
                var pen = new Pen(new SolidColorBrush(Ink), 1.6, lineCap: PenLineCap.Round);
                var c = _add.Center;
                context.DrawLine(pen, c + new Point(-6, 0), c + new Point(6, 0));
                context.DrawLine(pen, c + new Point(0, -6), c + new Point(0, 6));
            }
            else
            {
                // Left-aligned on the sidebar's text edge, as the mockup's button reads, not centred.
                var x = NativeTokens.PanelInset;
                Text(context, AddGameText, x, _add.Center.Y, size, Ink, maxWidth: Math.Max(1, _add.Right - NativeTokens.Space8 - x));
            }
        }
    }

    private void DrawFolded(DrawingContext context, Row r, ChromeOverlay overlay)
    {
        var accent = Accent(r.World.Accent);
        if (r.Kind == RailRowKind.World)
        {
            context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(r.Area.Center.X - 9, r.Area.Center.Y - 1.5, 18, 3), 1.5, 1.5);
            return;
        }

        if (r.Character is not { } character)
        {
            return;
        }

        // A disc with the character's initial: filled in the world's colour while connected, a ring while not.
        var centre = r.Area.Center;
        const double radius = 12;
        if (character.Active || Hovered(r.Area))
        {
            context.DrawRectangle(new SolidColorBrush(Raised(character.Active ? 0.12 : 0.07)), null, r.Area, 7, 7);
        }

        var colour = character.Connected ? accent : Dim;
        if (character.Connected)
        {
            context.DrawEllipse(new SolidColorBrush(colour), null, centre, radius, radius);
        }
        else
        {
            context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 1.4), centre, radius, radius);
        }

        var initial = character.Name.Length > 0 ? char.ToUpperInvariant(character.Name[0]).ToString() : "?";
        var ink = character.Connected ? On(colour, Plane) : Dim;
        Text(context, initial, centre.X - (Measure(initial, FontSize, bold: true) / 2), centre.Y, FontSize, ink, bold: true);
        if (character.Unread > 0)
        {
            var mark = Legible(Parse(overlay.Palette.Unread));
            context.DrawEllipse(new SolidColorBrush(mark), new Pen(new SolidColorBrush(Plane), 1.5), centre + new Point(radius * 0.75, -radius * 0.75), 4, 4);
        }
    }

    private void DrawCharacter(DrawingContext context, Row r, RailCharacter character, Color accent, double middle, double right, double size)
    {
        var centre = new Point(r.Area.X + CharacterIndent - NativeTokens.Space12, middle);
        var colour = character.Connected ? accent : Dim;
        Dot(context, centre, 4.5, colour, character.Connected, diamond: character.InGroup);

        // Right to left: the unread badge or the offline word, then the cycle chord.
        if (character.Unread > 0)
        {
            right -= Badge(context, character.Unread, right, middle, size) + 6;
        }
        else if (!character.Connected)
        {
            right -= Text(context, "offline", right, middle, size - 1.5, Dim, alignRight: true) + 6;
        }

        if (character.Chord is { Length: > 0 } chord)
        {
            right -= Text(context, KeyGlyphs.Words(chord), right, middle, size - 1.5, Dim, alignRight: true) + 6;
        }

        var x = r.Area.X + CharacterIndent;
        Text(context, character.Name, x, middle, size, character.Connected ? Ink : Dim, bold: character.Active, maxWidth: Math.Max(1, right - x));
    }

    private void DrawWindow(DrawingContext context, Row r, RailWindow window, Color accent, bool selected, double middle, double right, double size)
    {
        var x = r.Area.X + WindowIndent;
        if (selected)
        {
            context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(x - NativeTokens.Space8 - NativeTokens.MarkWidth, middle - 7, NativeTokens.MarkWidth, 14), 1.5, 1.5);
        }

        if (window.Unread > 0)
        {
            right -= Badge(context, window.Unread, right, middle, size) + 6;
        }

        if (!window.Closed && window.Chord is { Length: > 0 } chord)
        {
            right -= Text(context, KeyGlyphs.Words(chord), right, middle, size - 1.5, Dim, alignRight: true) + 6;
        }

        if (window.Closed)
        {
            right -= Text(context, "closed", right, middle, size - 1.5, Dim, alignRight: true) + 6;
        }

        if (window.HasUnsent)
        {
            right -= Text(context, "✎", right, middle, size, Legible(Parse(Overlay?.Palette.Draft)), alignRight: true) + 4;
        }

        Text(context, window.Title, x, middle, size, window.Closed ? Dim : Ink, bold: selected, maxWidth: Math.Max(1, right - x));
    }
}
