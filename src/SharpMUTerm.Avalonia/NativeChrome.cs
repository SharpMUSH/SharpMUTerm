using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
/// <see cref="ChromeOverlay.Activate"/>, which acts on it exactly as a click on the cells under it would.
/// <para>
/// Never focusable: the keyboard stays with the client, as a click on the cells leaves it.
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

    /// <summary>What the client last said, or null.</summary>
    internal ChromeOverlay? Overlay { get; private set; }

    protected Color Plane { get; private set; } = Colors.Black;

    protected Color Ink { get; private set; } = Colors.White;

    /// <summary>The clickable areas and their targets, as last laid out. Internal for tests.</summary>
    internal IReadOnlyList<(Rect Area, string Target, string Tip)> Hits => _hits;

    /// <summary>Takes what the client said and the plane under the view, read off the frame, and redraws.</summary>
    public void Show(ChromeOverlay overlay, Color plane, Color ink)
    {
        Overlay = overlay;
        Plane = plane;
        Ink = ink;
        _hits.Clear();
        Layout(_hits);
        InvalidateVisual();
    }

    /// <summary>Works out where each clickable thing goes, for the size the view has been given.</summary>
    protected abstract void Layout(List<(Rect Area, string Target, string Tip)> hits);

    /// <summary>A left press on no clickable area. The header moves the window; the sidebar does nothing.</summary>
    protected virtual void PressedEmpty(PointerPressedEventArgs e)
    {
    }

    /// <summary>The area under <paramref name="point"/>, or null.</summary>
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
    protected bool Hovered(Rect area) => _hover == area;

    /// <summary>Whether <paramref name="area"/> is being pressed.</summary>
    protected bool Pressing(Rect area) => _pressed is not null && _hover == area;

    /// <summary>Acts on a target as a click would. Internal for tests.</summary>
    internal void Click(string target) => Overlay?.Activate(target);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitAt(e.GetPosition(this));
        var area = hit?.Area;
        if (area != _hover)
        {
            _hover = area;
            Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
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

    // Every pointer event over the view is the window's, as over a header button: nothing routes on to the cells,
    // which would read a press on whatever the client drew there.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            // The client's own menu for the place under the pointer, as over the cells (the map does the same).
            if (this.TranslatePoint(point.Position, View) is { } onView)
            {
                View.ForwardRightClick(onView, e.KeyModifiers);
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (HitAt(point.Position) is { } hit)
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

    protected Color Dim => Color.FromArgb(0xA0, Ink.R, Ink.G, Ink.B);

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

    protected static Color Parse(string? hex) => hex is not null && Color.TryParse(hex, out var colour) ? colour : Colors.Gray;

    private static Color ToColour(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);

    protected static FormattedText Format(string text, double size, Color colour, bool bold = false, double maxWidth = double.PositiveInfinity)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal),
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
        var width = Measure(text, size * 0.85, bold: true) + (size * 0.9);
        var height = Math.Max(size * 1.15, 10);
        var pill = new Rect(right - width, middle - (height / 2), width, height);
        context.DrawRectangle(new SolidColorBrush(fill), null, pill, height / 2, height / 2);
        Text(context, text, pill.X + ((width - Measure(text, size * 0.85, bold: true)) / 2), middle, size * 0.85, On(fill, Plane), bold: true);
        return width;
    }

    /// <summary>A count as the cells write it (<c>99+</c> past the cap), so the two views agree about one count.</summary>
    internal static string UnreadText(int unread) => unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The window's own header, drawn over the client's header row: the app's name, a search box that opens the command
/// surface (⌃P), the connected count and log state, and the character in front as a pill — a presence dot, the name,
/// and <c>world · TLS</c>. Everything it says is <see cref="HeaderModel"/>, the model the cell ribbon under it is
/// drawn from.
/// <para>
/// <b>Within the row, never taller.</b> The header is one cell row and stays one; a taller native header needs the
/// client's layout to leave the room (see CLAUDE.md, native chrome). The right end is left clear for the header
/// buttons (<see cref="DesktopLayer.ButtonCells"/>), which sit above this view. A press on empty space moves the
/// window and a double-click maximises, as a blank header cell does (<see cref="TerminalView.WindowGestureFromChrome"/>).
/// </para>
/// </summary>
internal sealed class NativeHeader(TerminalView view) : NativeChromeView(view)
{
    /// <summary>What the search box says. ⌃P lists commands, characters and windows, and settings screens.</summary>
    internal const string SearchText = "Search commands, windows, settings";

    /// <summary>Width kept clear at the right for the header buttons.</summary>
    public double ReserveRight { get; set; }

    private Rect _brand;
    private Rect _search;
    private Rect _pill;
    private Rect _counts;

    private double Size => Math.Clamp(Height * 0.6, 9, 14);

    protected override void Layout(List<(Rect Area, string Target, string Tip)> hits)
    {
        _brand = _search = _pill = _counts = default;
        if (Overlay is not { } overlay || double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }

        var model = overlay.HeaderModel;
        var size = Size;
        var h = Height;
        var plateTop = 1.0;
        var plateHeight = Math.Max(4, h - 2);
        var pad = Math.Round(size * 0.8);

        _brand = new Rect(6, plateTop, Measure("SharpMUTerm", size, bold: true) + (pad * 2), plateHeight);
        hits.Add((_brand, HeaderModel.PaletteTarget, $"Commands ({KeyGlyphs.Words(HeaderModel.PaletteKey)})"));

        var right = Width - ReserveRight - 6;
        if (model.Character is { } character && model.World is { } world)
        {
            var name = Measure(character.Name, size, bold: true);
            var detail = Measure(character.Detail(world.Name), size * 0.9);
            var width = Math.Min(pad + size + name + pad + detail + pad, Math.Max(0, right - _brand.Right - 24));
            _pill = new Rect(right - width, plateTop, width, plateHeight);
            hits.Add((_pill, character.Target, $"{character.Name} on {world.Name}"));
            right = _pill.Left - 12;
        }

        var counts = CountsText(model);
        var countsWidth = Measure(counts, size * 0.9);
        var searchStart = _brand.Right + 16;
        var keyWidth = Measure(KeyGlyphs.Words(HeaderModel.PaletteKey), size * 0.85) + 10;
        var searchFloor = size + Measure("Search", size) + keyWidth + (pad * 3);
        if (right - countsWidth - 16 - searchStart >= searchFloor)
        {
            _counts = new Rect(right - countsWidth, 0, countsWidth, h);
            right = _counts.Left - 16;
        }

        if (right - searchStart >= searchFloor)
        {
            var width = Math.Min(420, right - searchStart);
            _search = new Rect(searchStart, plateTop, width, plateHeight);
            hits.Add((_search, HeaderModel.PaletteTarget, $"Commands, characters, windows and settings ({KeyGlyphs.Words(HeaderModel.PaletteKey)})"));
        }
    }

    private static string CountsText(HeaderModel model) =>
        model.HasWorlds ? $"{model.ConnectedText}  ·  {model.LogText}" : model.LogText;

    protected override void PressedEmpty(PointerPressedEventArgs e) => View.WindowGestureFromChrome(e);

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Plane), null, new Rect(Bounds.Size));
        if (Overlay is not { } overlay)
        {
            return;
        }

        var model = overlay.HeaderModel;
        var size = Size;
        var middle = Bounds.Height / 2;
        var radius = Math.Min(6, _brand.Height / 2);

        // The brand: the ribbon's first segment, in its own fill.
        var brand = Parse(overlay.Palette.Brand);
        if (Pressing(_brand) || Hovered(_brand))
        {
            brand = Color.FromArgb(0xFF, (byte)Math.Min(255, brand.R + 18), (byte)Math.Min(255, brand.G + 18), (byte)Math.Min(255, brand.B + 18));
        }

        context.DrawRectangle(new SolidColorBrush(brand), null, _brand, radius, radius);
        Text(context, "SharpMUTerm", _brand.X + ((_brand.Width - Measure("SharpMUTerm", size, true)) / 2), middle, size, On(brand, Colors.Black), bold: true);

        if (_search.Width > 0)
        {
            var fill = Faint((byte)(Hovered(_search) ? 0x26 : 0x14));
            context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Faint(0x38)), 1), _search.Deflate(0.5), radius, radius);
            var pad = Math.Round(size * 0.8);

            // A magnifier: a ring and its handle.
            var ring = size * 0.32;
            var centre = new Point(_search.X + pad + ring, middle - (ring * 0.25));
            var pen = new Pen(new SolidColorBrush(Dim), 1.3);
            context.DrawEllipse(null, pen, centre, ring, ring);
            context.DrawLine(pen, centre + new Point(ring * 0.7, ring * 0.7), centre + new Point(ring * 1.5, ring * 1.5));

            var key = KeyGlyphs.Words(HeaderModel.PaletteKey);
            var keyWidth = Measure(key, size * 0.85) + 10;
            var cap = new Rect(_search.Right - pad - keyWidth, middle - (_search.Height * 0.36), keyWidth, _search.Height * 0.72);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Faint(0x50)), 1), cap.Deflate(0.5), 3, 3);
            Text(context, key, cap.X + 5, middle, size * 0.85, Dim);
            var textLeft = centre.X + (ring * 2) + 6;
            Text(context, SearchText, textLeft, middle, size, Dim, maxWidth: Math.Max(1, cap.Left - 8 - textLeft));
        }

        if (_counts.Width > 0)
        {
            Text(context, CountsText(model), _counts.X, middle, size * 0.9, Dim);
        }

        if (_pill.Width > 0 && model.Character is { } character && model.World is { } world)
        {
            var fill = Faint((byte)(Hovered(_pill) ? 0x2A : 0x18));
            context.DrawRectangle(new SolidColorBrush(fill), null, _pill, _pill.Height / 2, _pill.Height / 2);
            var pad = Math.Round(size * 0.8);
            var dot = size * 0.32;
            var dotCentre = new Point(_pill.X + pad + dot, middle);
            var accent = Accent(world.Accent);
            switch (character.Presence)
            {
                case CharacterPresence.Online:
                    context.DrawEllipse(new SolidColorBrush(accent), null, dotCentre, dot, dot);
                    break;
                case CharacterPresence.Connecting:
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(accent), 1.5), dotCentre, dot, dot);
                    break;
                default:
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(Dim), 1.2), dotCentre, dot, dot);
                    break;
            }

            var x = dotCentre.X + dot + 6;
            var room = _pill.Right - pad - x;
            var nameWidth = Text(context, character.Name, x, middle, size, Ink, bold: true, maxWidth: room);
            x += nameWidth + 8;
            if (_pill.Right - pad - x > 12)
            {
                Text(context, character.Detail(world.Name), x, middle, size * 0.9, Dim, maxWidth: _pill.Right - pad - x);
            }
        }
    }
}

/// <summary>
/// The window's own sidebar, drawn over the client's rail: <c>CONNECTIONS</c>, worlds as headings, characters with a
/// presence dot and an unread badge (offline ones greyed and saying so), the open character's windows indented with
/// the chord that reaches each and its unread count, and an "add a game" button at the foot. Everything it says is
/// <see cref="SidebarModel"/>, the model the cell rail under it is drawn from, and every click goes back as the
/// target that model gave — the rail's own handler acts on it.
/// <para>
/// It draws within the rail's cells and never wider, so no pane moves and no world is told a new size. While the
/// rail is collapsed (⌃B b) it draws nothing and the cells' narrow strip shows.
/// </para>
/// </summary>
internal sealed class NativeSidebar(TerminalView view) : NativeChromeView(view)
{
    /// <summary>What the button at the foot says.</summary>
    internal const string AddGameText = "+ Add a game or connect";

    /// <summary>One drawn row: what it is, where, and its node.</summary>
    internal sealed record Row(RailRowKind Kind, Rect Area, RailWorld World, RailCharacter? Character = null, RailWindow? Window = null);

    private readonly List<Row> _rows = [];
    private Rect _add;

    /// <summary>The rows as last laid out. Internal for tests.</summary>
    internal IReadOnlyList<Row> Rows => _rows;

    private double CellHeight { get; set; } = 16;

    /// <summary>The height of a cell, which the rows are spaced from.</summary>
    public void SetCellHeight(double height) => CellHeight = Math.Max(8, height);

    private double Size => Math.Clamp(CellHeight * 0.66, 9, 14);

    private double RowHeight => Math.Round(Math.Max(CellHeight * 1.3, Size * 1.9));

    protected override void Layout(List<(Rect Area, string Target, string Tip)> hits)
    {
        _rows.Clear();
        _add = default;
        if (Overlay is not { } overlay || double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }

        var row = RowHeight;
        var width = Width;
        _add = new Rect(8, Height - row - 8, Math.Max(0, width - 16), row);
        var bottom = _add.Top - 6;
        var y = row + 4; // under the CONNECTIONS title
        void Add(Row r, string target, string tip)
        {
            if (r.Area.Bottom > bottom)
            {
                return;
            }

            _rows.Add(r);
            hits.Add((r.Area, target, tip));
        }

        foreach (var world in overlay.Sidebar.Worlds)
        {
            y += 6;
            Add(new Row(RailRowKind.World, new Rect(4, y, width - 8, row), world), SidebarModel.TargetOf(world), world.Name);
            y += row;
            if (world.Characters.Count == 0)
            {
                Add(new Row(RailRowKind.Empty, new Rect(4, y, width - 8, row), world), SidebarModel.TargetOf(world), $"{world.Name} has no characters yet");
                y += row;
                continue;
            }

            foreach (var character in world.Characters)
            {
                Add(new Row(RailRowKind.Character, new Rect(4, y, width - 8, row), world, character), SidebarModel.TargetOf(character), $"Switch to {character.Name}");
                y += row;
                if (!character.Active)
                {
                    continue;
                }

                foreach (var window in character.Windows)
                {
                    Add(new Row(RailRowKind.Window, new Rect(4, y, width - 8, row), world, character, window), SidebarModel.TargetOf(window), window.Closed ? $"{window.Title} is closed" : $"Go to {window.Title}");
                    y += row;
                }
            }
        }

        if (_add.Width > 40 && _add.Top > row)
        {
            hits.Add((_add, SidebarModel.AddGameTarget, "Connect to a game, or add one"));
        }
        else
        {
            _add = default;
        }
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Plane), null, new Rect(Bounds.Size));
        if (Overlay is not { } overlay)
        {
            return;
        }

        var size = Size;
        var row = RowHeight;
        Text(context, "CONNECTIONS", 12, row / 2 + 4, size * 0.8, Dim, bold: true);

        foreach (var r in _rows)
        {
            var middle = r.Area.Center.Y;
            var hovered = Hovered(r.Area);
            var selected = r.Window is { } w && overlay.Sidebar.IsSelected(w) || r.Kind == RailRowKind.Character && r.Character!.Active;
            if (selected || hovered)
            {
                var plate = (byte)(selected && hovered ? 0x30 : selected ? 0x22 : 0x14);
                context.DrawRectangle(new SolidColorBrush(Faint(plate)), null, r.Area, 5, 5);
            }

            var accent = Accent(r.World.Accent);
            var right = r.Area.Right - 8;
            switch (r.Kind)
            {
                case RailRowKind.World:
                    context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(r.Area.X + 4, middle - (size * 0.55), 3, size * 1.1), 1.5, 1.5);
                    Text(context, r.World.Name, r.Area.X + 14, middle, size, Ink, bold: true, maxWidth: right - r.Area.X - 14);
                    break;

                case RailRowKind.Empty:
                    Text(context, "no characters", r.Area.X + 24, middle, size * 0.92, Dim, maxWidth: right - r.Area.X - 24);
                    break;

                case RailRowKind.Character:
                    DrawCharacter(context, r, r.Character!, accent, middle, right, size);
                    break;

                case RailRowKind.Window:
                    DrawWindow(context, r, r.Window!, accent, overlay.Sidebar.IsSelected(r.Window!), middle, right, size);
                    break;
            }
        }

        if (_add.Width > 0)
        {
            var fill = Faint((byte)(Hovered(_add) ? 0x22 : 0x0E));
            context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Faint(0x40)), 1), _add.Deflate(0.5), 6, 6);
            Text(context, AddGameText, _add.Center.X - (Math.Min(Measure(AddGameText, size), _add.Width - 12) / 2), _add.Center.Y, size, Ink, maxWidth: _add.Width - 12);
        }
    }

    private void DrawCharacter(DrawingContext context, Row r, RailCharacter character, Color accent, double middle, double right, double size)
    {
        var dot = size * 0.32;
        var centre = new Point(r.Area.X + 22, middle);
        var colour = character.Connected ? accent : Dim;
        if (character.InGroup)
        {
            // A member of the send group wears a diamond, as on the cells.
            var diamond = new StreamGeometry();
            using (var g = diamond.Open())
            {
                g.BeginFigure(centre + new Point(0, -dot * 1.2), true);
                g.LineTo(centre + new Point(dot * 1.2, 0));
                g.LineTo(centre + new Point(0, dot * 1.2));
                g.LineTo(centre + new Point(-dot * 1.2, 0));
                g.EndFigure(true);
            }

            context.DrawGeometry(character.Connected ? new SolidColorBrush(colour) : null, new Pen(new SolidColorBrush(colour), 1.2), diamond);
        }
        else if (character.Connected)
        {
            context.DrawEllipse(new SolidColorBrush(colour), null, centre, dot, dot);
        }
        else
        {
            context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 1.2), centre, dot, dot);
        }

        // Right to left: the unread badge or the offline word, then the cycle chord.
        if (character.Unread > 0)
        {
            right -= Badge(context, character.Unread, right, middle, size) + 6;
        }
        else if (!character.Connected)
        {
            right -= Text(context, "offline", right, middle, size * 0.85, Dim, alignRight: true) + 6;
        }

        if (character.Chord is { Length: > 0 } chord)
        {
            right -= Text(context, KeyGlyphs.Words(chord), right, middle, size * 0.85, Dim, alignRight: true) + 6;
        }

        var x = centre.X + dot + 7;
        Text(context, character.Name, x, middle, size, character.Connected ? Ink : Dim, bold: character.Active, maxWidth: Math.Max(1, right - x));
    }

    private void DrawWindow(DrawingContext context, Row r, RailWindow window, Color accent, bool selected, double middle, double right, double size)
    {
        var x = r.Area.X + 36;
        if (selected)
        {
            context.DrawRectangle(new SolidColorBrush(accent), null, new Rect(r.Area.X + 28, middle - (size * 0.5), 2.5, size), 1, 1);
        }

        if (window.Unread > 0)
        {
            right -= Badge(context, window.Unread, right, middle, size) + 6;
        }

        if (!window.Closed && window.Chord is { Length: > 0 } chord)
        {
            right -= Text(context, KeyGlyphs.Words(chord), right, middle, size * 0.85, Dim, alignRight: true) + 6;
        }

        if (window.Closed)
        {
            right -= Text(context, "closed", right, middle, size * 0.85, Dim, alignRight: true) + 6;
        }

        if (window.HasUnsent)
        {
            right -= Text(context, "✎", right, middle, size, Legible(Parse(Overlay?.Palette.Draft)), alignRight: true) + 4;
        }

        Text(context, window.Title, x, middle, size, window.Closed ? Dim : Ink, bold: selected, maxWidth: Math.Max(1, right - x));
    }
}
