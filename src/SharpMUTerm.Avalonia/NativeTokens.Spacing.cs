using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The spacing scale and the device-pixel rules every native view lays out with, so the header, the sidebar, the tab
/// strips, the panels, the cards, the buttons, the inputs, the footer and the modals share one set of insets and line up.
/// <para>
/// <b>The scale is a 4 px grid with the mockups' control half-steps.</b> Regions, cards and groups are spaced on
/// 4/8/12/16/20/24; a control's own padding may take the 2/6/10/14 the mockups' buttons and rows use
/// (<c>padding: 6px 10px</c>, <c>8px 14px</c>). Nothing odd: an odd logical pixel is a half device pixel at 150%.
/// <c>NativeTokenTests</c> fails on a numeric literal in a <c>new Thickness(…)</c> or a <c>Spacing =</c> outside the
/// tokens, apart from the few files it lists with their reasons.
/// </para>
/// <para>
/// <b>Chrome sizes are multiples of 4</b> (<see cref="NativeLayout"/>: the header, the sidebar, the footer, the
/// command-line strip), because 4 logical pixels are a whole number of device pixels at 100, 125, 150 and 200% — so the
/// cells start on a device pixel at every scale Windows offers, and a native view placed from a cell origin meets the
/// cells without a blended row.
/// </para>
/// </summary>
internal sealed partial record NativeTokens
{
    /// <summary>A rule, a border, a divider: one line. Drawn as one device pixel (<see cref="DevicePixel"/>).</summary>
    public const double Hairline = 1;

    /// <summary>A chosen card's or a focused control's border: twice a hairline.</summary>
    public const double BorderStrong = 2;

    /// <summary>A map exit: the mockups' <c>stroke-width="2"</c> line between rooms, drawn in whole device pixels (<see cref="LineWidth"/>).</summary>
    public const double MapExit = 2;

    /// <summary>The scale: a control half-step, a hairline gap.</summary>
    public const double Space2 = 2;

    /// <summary>The scale: the smallest gap between related things (an icon and its label).</summary>
    public const double Space4 = 4;

    /// <summary>The scale: a control half-step (the mockups' <c>padding: 6px 10px</c>).</summary>
    public const double Space6 = 6;

    /// <summary>The scale: the gap between controls in a row, a row's vertical padding.</summary>
    public const double Space8 = 8;

    /// <summary>The scale: a control half-step (a row's or a button's side padding).</summary>
    public const double Space10 = 10;

    /// <summary>The scale: a card's or a list's inner padding.</summary>
    public const double Space12 = 12;

    /// <summary>The scale: a control half-step (a large button's side padding).</summary>
    public const double Space14 = 14;

    /// <summary>The scale: a panel's and the chrome's text inset (<see cref="PanelInset"/>).</summary>
    public const double Space16 = 16;

    /// <summary>The scale: a section's inset, the command line's field inset.</summary>
    public const double Space20 = 20;

    /// <summary>The scale: a modal's inset (<see cref="ModalInset"/>), the gap between groups.</summary>
    public const double Space24 = 24;

    /// <summary>The scale: the gap above a page's closing row.</summary>
    public const double Space28 = 28;

    /// <summary>The scale: a page's outer margin on a narrow page.</summary>
    public const double Space32 = 32;

    /// <summary>
    /// The one left inset of the window's chrome: the header's brand, the sidebar's title and world names, a panel's
    /// heading, its rows and its footer, the status footer's text. The mockups' 16 px.
    /// </summary>
    public const double PanelInset = Space16;

    /// <summary>A modal's inset from its card's edge: heading, body and footer.</summary>
    public const double ModalInset = Space24;

    /// <summary>A settings page's side margin.</summary>
    public const double PageInset = 40;

    /// <summary>A button's, an input's or a combo's height on a page or a modal (the mockups' 32 px controls).</summary>
    public const double ControlHeight = 32;

    /// <summary>A panel's button height (the composer's Preview and Send, the mockups' 28 px).</summary>
    public const double ControlHeightSmall = 28;

    /// <summary>The command line's field: the mockup's 38 px, one row of text and its padding.</summary>
    public const double FieldHeight = 38;

    /// <summary>
    /// One device pixel in logical units for <paramref name="visual"/>'s screen: what a hairline is drawn at, so a rule
    /// at 125% or 150% is one crisp pixel rather than 1.25 blended across two.
    /// </summary>
    public static double DevicePixel(Visual visual) => 1 / Scaling(visual);

    /// <summary><paramref name="value"/> moved to the nearest device pixel on <paramref name="visual"/>'s screen.</summary>
    public static double Snap(Visual visual, double value)
    {
        var scale = Scaling(visual);
        return Math.Round(value * scale) / scale;
    }

    /// <summary>
    /// A horizontal hairline across <paramref name="x0"/>..<paramref name="x1"/> whose top edge is the device pixel
    /// nearest <paramref name="y"/>: one device pixel tall, filled rather than stroked, so nothing is blended.
    /// </summary>
    public static Rect HorizontalRule(Visual visual, double y, double x0, double x1)
    {
        var px = DevicePixel(visual);
        return new Rect(Snap(visual, x0), Snap(visual, y), Math.Max(0, Snap(visual, x1) - Snap(visual, x0)), px);
    }

    /// <summary>A vertical hairline down <paramref name="y0"/>..<paramref name="y1"/>, its left edge on a device pixel.</summary>
    public static Rect VerticalRule(Visual visual, double x, double y0, double y1)
    {
        var px = DevicePixel(visual);
        return new Rect(Snap(visual, x), Snap(visual, y0), px, Math.Max(0, Snap(visual, y1) - Snap(visual, y0)));
    }

    /// <summary>
    /// The rectangle a one-device-pixel stroke of <paramref name="area"/> is drawn along: its edges snapped to device
    /// pixels and moved in by half of one, so the stroke covers exactly one row of device pixels on each side.
    /// </summary>
    public static Rect StrokeRect(Visual visual, Rect area)
    {
        var half = DevicePixel(visual) / 2;
        var left = Snap(visual, area.Left);
        var top = Snap(visual, area.Top);
        var right = Snap(visual, area.Right);
        var bottom = Snap(visual, area.Bottom);
        return new Rect(left + half, top + half, Math.Max(0, right - left - (2 * half)), Math.Max(0, bottom - top - (2 * half)));
    }

    /// <summary>
    /// <paramref name="width"/> rounded to whole device pixels on <paramref name="visual"/>'s screen, never under one: a
    /// line drawn at it covers whole pixels rather than blending into a fainter, wider one.
    /// </summary>
    public static double LineWidth(Visual visual, double width)
    {
        var scale = Scaling(visual);
        return Math.Max(1, Math.Round(width * scale)) / scale;
    }

    /// <summary>
    /// Where to put the centre of a straight line <paramref name="width"/> wide (already <see cref="LineWidth"/>) near
    /// <paramref name="centre"/> so its edges fall on device pixels: on a pixel edge for an even number of pixels, in the
    /// middle of one for an odd number. Moves it half a device pixel at most.
    /// </summary>
    public static double LineCentre(Visual visual, double centre, double width)
    {
        var scale = Scaling(visual);
        var odd = ((int)Math.Round(width * scale) & 1) == 1;
        return odd ? (Math.Floor(centre * scale) + 0.5) / scale : Math.Round(centre * scale) / scale;
    }

    /// <summary>A one-device-pixel pen in <paramref name="colour"/>, for <see cref="StrokeRect"/>.</summary>
    public static Pen HairlinePen(Visual visual, Color colour) => new(new SolidColorBrush(colour), DevicePixel(visual));

    /// <summary>
    /// Sets the window's text to light hinting, as a browser draws it (the mockups were drawn in one). Avalonia's default
    /// hinting snaps Inter's x-height and capitals up a whole pixel at 12–14 px, so every native line read a size
    /// heavier than <c>Main.png</c> and sat about 1.5 px higher in its box: a heading's capitals rose above its middle.
    /// Inherited by everything in the window; the cells draw their own glyphs and are not touched.
    /// </summary>
    public static void ApplyTextRendering(Visual window) => TextOptions.SetTextHintingMode(window, TextHintingMode.Light);

    private static double Scaling(Visual visual)
    {
        var scale = TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1;
        return scale > 0 ? scale : 1;
    }
}
