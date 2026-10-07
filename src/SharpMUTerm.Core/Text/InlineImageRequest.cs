using System.Globalization;

namespace SharpMUTerm.Core.Text;

/// <summary>The unit an MXP <c>&lt;IMAGE&gt;</c> size attribute is written in.</summary>
public enum ImageExtentUnit
{
    /// <summary>A bare number: pixels of the source picture.</summary>
    Pixels,

    /// <summary>A number suffixed <c>c</c>: character cells.</summary>
    Characters,

    /// <summary>A number suffixed <c>%</c>: a share of the room the pane has.</summary>
    Percent,
}

/// <summary>
/// One of an image's two size hints, as the spec writes them: "in pixels, character heights, or
/// percentage".
/// </summary>
public readonly record struct ImageExtent(int Value, ImageExtentUnit Unit)
{
    /// <summary>The largest value accepted in any unit; anything past it is a typo or an attack.</summary>
    public const int MaxValue = 10_000;

    /// <summary>
    /// Reads <c>200</c>, <c>20c</c> or <c>50%</c>. Zero, negative, unparseable and absurd values are
    /// refused rather than clamped: a hint the server got wrong is better ignored than obeyed.
    /// </summary>
    public static bool TryParse(string? text, out ImageExtent extent)
    {
        extent = default;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        var unit = ImageExtentUnit.Pixels;
        if (s.EndsWith('%'))
        {
            unit = ImageExtentUnit.Percent;
            s = s[..^1];
        }
        else if (s.EndsWith('c') || s.EndsWith('C'))
        {
            unit = ImageExtentUnit.Characters;
            s = s[..^1];
        }
        else if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^2];
        }

        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value <= 0 || value > MaxValue || (unit == ImageExtentUnit.Percent && value > 100))
        {
            return false;
        }

        extent = new ImageExtent(value, unit);
        return true;
    }
}

/// <summary>
/// A picture a world asked to have drawn inline — MXP's <c>&lt;IMAGE&gt;</c>. It rides on the
/// <see cref="SpanInteraction"/> of the span standing in for it, so it travels wherever the line does
/// (scrollback, spawn windows, trigger rewrites) and the text-only rendering of that span is the
/// fallback a terminal without graphics shows.
/// </summary>
/// <param name="Url">The absolute <c>http</c>, <c>https</c> or <c>data</c> URL to fetch.</param>
/// <param name="Name">What the placeholder calls it: the file name.</param>
/// <param name="Width">The <c>W=</c> hint, if any.</param>
/// <param name="Height">The <c>H=</c> hint, if any.</param>
public sealed record InlineImageRequest(
    string Url,
    string Name,
    ImageExtent? Width = null,
    ImageExtent? Height = null);
