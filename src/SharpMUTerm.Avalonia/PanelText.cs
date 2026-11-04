using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// Text and colour for the native list panels (<see cref="WhoHereList"/>, <see cref="QuickBarButtons"/>): one line of
/// the window's UI font, trimmed with an ellipsis, measured and drawn the same way, so the two panels read as one
/// family with the vitals gauges.
/// </summary>
internal static class PanelText
{
    /// <summary>One line of text, trimmed to <paramref name="maxWidth"/> with an ellipsis.</summary>
    public static FormattedText Format(string text, double size, Color colour, bool bold, double maxWidth = double.PositiveInfinity)
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

    /// <summary>How wide a line is drawn, untrimmed.</summary>
    public static double Measure(string text, double size, bool bold) => Format(text, size, Colors.White, bold).Width;

    /// <summary>
    /// Draws a line with its left edge at <paramref name="x"/> (its right edge there when <paramref name="alignRight"/>),
    /// centred on <paramref name="middle"/>, and returns the width it took.
    /// </summary>
    public static double Draw(DrawingContext context, string text, double x, double middle, double size, Color colour, bool bold, double maxWidth, bool alignRight = false)
    {
        if (maxWidth < 2 || text.Length == 0)
        {
            return 0;
        }

        var formatted = Format(text, size, colour, bold, maxWidth);
        var width = Math.Min(formatted.Width, maxWidth);
        context.DrawText(formatted, new Point(alignRight ? x - width : x, middle - (formatted.Height / 2)));
        return width;
    }

    /// <summary>The ink at an alpha: the dim and hover shades are the text colour, never a theme's grey.</summary>
    public static Color Alpha(Color colour, byte alpha) => Color.FromArgb(alpha, colour.R, colour.G, colour.B);

    /// <summary>A <c>#rrggbb</c> from the client, or grey when it is not one.</summary>
    public static Color Parse(string hex) => Color.TryParse(hex, out var colour) ? colour : Colors.Gray;

    // ── The panel heading: one row every docked panel draws over its content ────────────────────────────────────

    /// <summary>The panels' side padding, and the heading's.</summary>
    public const double Pad = 16;

    /// <summary>The panels' text size: the window's body size, one step up, which is what the mockups' side column reads at.</summary>
    public const double Size = NativeTokens.Body + 1;

    /// <summary>How tall the heading is, its rule included, for the cell height the panel sits on: 42 px at a 20 px cell.</summary>
    public static double HeadingHeight(double cellHeight) => Math.Clamp(cellHeight * 2.1, 32, 44);

    /// <summary>The secondary ink: <paramref name="text"/> mixed toward <paramref name="plane"/>, opaque so it can be floor-checked.</summary>
    public static Color Dim(Color text, Color plane) => Mix(text, plane, 0.38);

    /// <summary>The heading's rule, and every rule in a panel: a step from the plane toward the text.</summary>
    public static Color Rule(Color text, Color plane) => Mix(plane, text, 0.14);

    /// <summary>Mixes <paramref name="a"/> toward <paramref name="b"/> by <paramref name="amount"/> (0 is a, 1 is b), opaque.</summary>
    public static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + ((b.R - a.R) * amount)),
        (byte)Math.Round(a.G + ((b.G - a.G) * amount)),
        (byte)Math.Round(a.B + ((b.B - a.B) * amount)));

    /// <summary>
    /// Draws a panel heading across the top of <paramref name="width"/> — the title semibold, the context dim beside it,
    /// a rule under both — and returns the height it took (<see cref="HeadingHeight"/>). <paramref name="right"/> is the
    /// room kept clear at the right edge (a chip, the pop-out button).
    /// </summary>
    public static double Heading(DrawingContext context, SharpMUTerm.Client.Panels.PanelHeading heading, double width, double cellHeight, Color plane, Color text, double right = 0)
    {
        var height = HeadingHeight(cellHeight);
        context.FillRectangle(new SolidColorBrush(plane), new Rect(0, 0, width, height));
        var middle = (height - 1) / 2;
        var limit = width - Pad - right;
        var x = Pad;
        x += Draw(context, heading.Title, x, middle, Size, text, true, limit - x) + 8;
        if (heading.Context is { Length: > 0 } words)
        {
            Draw(context, words, x, middle, Size, Dim(text, plane), false, limit - x);
        }

        context.FillRectangle(new SolidColorBrush(Rule(text, plane)), new Rect(0, height - 1, width, 1));
        return height;
    }
}
