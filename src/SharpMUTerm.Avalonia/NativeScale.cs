using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// How much larger than designed each native area of the window is drawn: the Text size page's sizes with the zoom
/// multiplied in (<see cref="DisplaySettings"/>). <see cref="Composer"/> is against the cells' own text size, which
/// already carries the zoom; the rest are against the design's own sizes.
/// </summary>
internal readonly record struct NativeScales(double Interface, double Panels, double Dialogs, double Composer)
{
    /// <summary>Everything as designed.</summary>
    public static NativeScales One { get; } = new(1, 1, 1, 1);

    /// <summary>The scales the page's <paramref name="sizes"/> ask for.</summary>
    public static NativeScales From(DisplaySettings sizes)
    {
        var held = sizes.Clamped();
        var zoom = held.Zoom / 100.0;
        return new(zoom * held.InterfaceText / 100.0, zoom * held.PanelText / 100.0, zoom * held.DialogText / 100.0, held.ComposerText / 100.0);
    }
}

/// <summary>
/// Draws a native view larger or smaller than designed, as a browser's zoom does: the view is laid out in a box
/// <c>1 / scale</c> the size of the area it covers and drawn <c>scale</c> times larger, so its text, padding, rules and
/// hit areas grow together and it still covers exactly its area. Every view keeps its own layout rules; nothing in them
/// knows the scale. At 1 nothing is set, so a window at 100% is laid out exactly as it was.
/// </summary>
internal static class NativeScale
{
    /// <summary>Sizes <paramref name="control"/> to cover <paramref name="width"/> × <paramref name="height"/> at <paramref name="scale"/>.</summary>
    public static void Cover(Control control, double width, double height, double scale)
    {
        if (Math.Abs(scale - 1) < 0.0005)
        {
            if (control.RenderTransform is ScaleTransform)
            {
                control.RenderTransform = null;
            }

            control.UseLayoutRounding = true;
            control.Width = width;
            control.Height = height;
            return;
        }

        // Not rounded: a box 1/scale the area's size is seldom whole pixels, and rounding it would draw the view up to half
        // a pixel past its area (64 px at 1.5 is 42.67, which rounds to 43 and draws at 64.5).
        control.UseLayoutRounding = false;
        control.Width = width / scale;
        control.Height = height / scale;
        control.RenderTransformOrigin = RelativePoint.TopLeft;
        if (control.RenderTransform is not ScaleTransform current || Math.Abs(current.ScaleX - scale) > 0.0005)
        {
            control.RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    /// <summary>The scale <paramref name="control"/> is drawn at (1 when it is drawn as laid out).</summary>
    public static double Of(Visual control) => control.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;

    /// <summary>The area <paramref name="control"/> covers on screen, in its parent's units.</summary>
    public static Size Extent(Control control)
    {
        var scale = Of(control);
        return new Size(control.Width * scale, control.Height * scale);
    }

    /// <summary><paramref name="rect"/>, in a scaled view's own units, in the units of the area it covers.</summary>
    public static Rect Out(Rect rect, double scale) =>
        Math.Abs(scale - 1) < 0.0005 ? rect : new Rect(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);

    /// <summary>
    /// How many device pixels one of <paramref name="visual"/>'s own units is: the screen's scaling times every zoom applied
    /// to it and its ancestors, so a hairline under a zoomed view is still one device pixel.
    /// </summary>
    public static double DeviceScale(Visual visual)
    {
        var top = TopLevel.GetTopLevel(visual);
        var scale = top?.RenderScaling ?? 1;
        if (scale <= 0)
        {
            scale = 1;
        }

        for (Visual? at = visual; at is not null && at != top; at = at.GetVisualParent())
        {
            // A ScaleTransform of ours (Cover) or the matrix a LayoutTransformControl lays on its child (Wrap).
            if (at.RenderTransform is { } transform)
            {
                scale *= Math.Abs(transform.Value.M11);
            }
        }

        return scale;
    }

    /// <summary>
    /// <paramref name="child"/> laid out and drawn at <paramref name="scale"/> inside whatever holds the result: for a
    /// dialog over the whole window, which centres itself in the room it is given rather than covering a fixed area.
    /// </summary>
    public static LayoutTransformControl Wrap(Control child, double scale) => new()
    {
        LayoutTransform = Math.Abs(scale - 1) < 0.0005 ? null : new ScaleTransform(scale, scale),
        Child = child,
    };

    /// <summary>The scale a <see cref="Wrap"/> lays its child out at.</summary>
    public static double WrapScale(LayoutTransformControl wrap) => wrap.LayoutTransform is { } transform ? Math.Abs(transform.Value.M11) : 1;
}
