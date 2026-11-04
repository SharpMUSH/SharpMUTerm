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
}
