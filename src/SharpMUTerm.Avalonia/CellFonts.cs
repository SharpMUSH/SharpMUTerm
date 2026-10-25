using System.Collections.Concurrent;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The monospace face the grid is drawn in, the cell it implies, and a per-character fallback for whatever
/// that face lacks — this client prints Nerd Font icons, box drawing and whatever a world sends, and no one
/// font has all of it.
/// <para>
/// The cell is whole pixels on both axes. A fractional advance would put every cell boundary at a different
/// sub-pixel offset, and a background run, a box-drawing line or a picture slice drawn to such a boundary
/// leaves a hairline seam where two meet.
/// </para>
/// </summary>
internal sealed class CellFonts : IDisposable
{
    /// <summary>
    /// Families tried in order when none is named. Nerd Font builds first, because the client's chrome is drawn
    /// in Nerd Font icons and a face that has them needs no fallback for a single cell of the header.
    /// </summary>
    public static readonly string[] PreferredFamilies =
    [
        "CaskaydiaMono Nerd Font", "CaskaydiaCove Nerd Font Mono", "CaskaydiaCove Nerd Font",
        "JetBrainsMono Nerd Font Mono", "JetBrainsMono Nerd Font", "FiraCode Nerd Font Mono",
        "Cascadia Mono", "Cascadia Code", "JetBrains Mono", "Consolas", "SF Mono", "Menlo",
        "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Courier New",
    ];

    private readonly ConcurrentDictionary<(int CodePoint, bool Bold, bool Italic), SKFont> _fonts = new();
    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly SKTypeface _italic;
    private readonly SKTypeface _boldItalic;

    public CellFonts(float sizePixels, string? family = null)
    {
        Size = sizePixels;
        _regular = Match(family, SKFontStyle.Normal) ?? SKTypeface.Default;
        _bold = Match(_regular.FamilyName, SKFontStyle.Bold) ?? _regular;
        _italic = Match(_regular.FamilyName, SKFontStyle.Italic) ?? _regular;
        _boldItalic = Match(_regular.FamilyName, SKFontStyle.BoldItalic) ?? _bold;

        using var probe = new SKFont(_regular, sizePixels);
        var metrics = probe.Metrics;
        CellWidth = Math.Max(1, (int)Math.Ceiling(probe.MeasureText("M")));
        // Ascent to descent, without the face's line gap: a terminal's rows sit flush, and a box or a picture
        // drawn down a column of cells must not be striped by the gap between them.
        CellHeight = Math.Max(1, (int)Math.Ceiling(metrics.Descent - metrics.Ascent));
        Baseline = (float)Math.Round(-metrics.Ascent);
        UnderlineOffset = metrics.UnderlinePosition is { } u and > 0 ? u : sizePixels / 10f;
        StrokeWidth = Math.Max(1f, metrics.UnderlineThickness is { } t and > 0 ? t : sizePixels / 14f);
    }

    public float Size { get; }

    public string FamilyName => _regular.FamilyName;

    public int CellWidth { get; }

    public int CellHeight { get; }

    /// <summary>Distance from a cell's top to the text baseline.</summary>
    public float Baseline { get; }

    public float UnderlineOffset { get; }

    public float StrokeWidth { get; }

    /// <summary>A font that can draw <paramref name="codePoint"/>, in the requested weight and slant where it can.</summary>
    public SKFont For(int codePoint, bool bold, bool italic) =>
        _fonts.GetOrAdd((codePoint, bold, italic), key => Create(key.CodePoint, key.Bold, key.Italic));

    public void Dispose()
    {
        foreach (var font in _fonts.Values.Distinct())
        {
            font.Dispose();
        }
    }

    private SKFont Create(int codePoint, bool bold, bool italic)
    {
        var face = (bold, italic) switch
        {
            (true, true) => _boldItalic,
            (true, false) => _bold,
            (false, true) => _italic,
            _ => _regular,
        };

        if (!face.ContainsGlyph(codePoint))
        {
            var style = bold ? (italic ? SKFontStyle.BoldItalic : SKFontStyle.Bold) : (italic ? SKFontStyle.Italic : SKFontStyle.Normal);
            face = Symbols(codePoint)
                ?? SKFontManager.Default.MatchCharacter(_regular.FamilyName, style, null, codePoint)
                ?? SKFontManager.Default.MatchCharacter(codePoint)
                ?? face;
        }

        var font = new SKFont(face, Size)
        {
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
            Hinting = SKFontHinting.Slight,
        };

        // Synthesise what the family does not ship, rather than drawing bold text in the regular weight.
        if (bold && face.FontStyle.Weight < (int)SKFontStyleWeight.SemiBold)
        {
            font.Embolden = true;
        }

        if (italic && face.FontStyle.Slant == SKFontStyleSlant.Upright)
        {
            font.SkewX = -0.2f;
        }

        return font;
    }

    /// <summary>
    /// A Nerd Font for an icon in the Private Use Area, asked for by name. The platform's own fallback is no help
    /// there: the PUA has no agreed meaning, so it returns whichever installed face claims the code point — on
    /// one Linux box tried, a Unifont sample that draws every one as a hex box — rather than the Nerd Font sitting
    /// beside it.
    /// </summary>
    private static SKTypeface? Symbols(int codePoint)
    {
        if (codePoint is not ((>= 0xE000 and <= 0xF8FF) or >= 0xF0000))
        {
            return null;
        }

        foreach (var name in SymbolFamilies)
        {
            var face = SKFontManager.Default.MatchFamily(name);
            if (face is not null && string.Equals(face.FamilyName, name, StringComparison.OrdinalIgnoreCase) && face.ContainsGlyph(codePoint))
            {
                return face;
            }
        }

        return null;
    }

    private static readonly string[] SymbolFamilies =
    [
        "Symbols Nerd Font Mono", "Symbols Nerd Font", .. PreferredFamilies.Where(f => f.Contains("Nerd Font", StringComparison.Ordinal)),
    ];

    private static SKTypeface? Match(string? family, SKFontStyle style)
    {
        foreach (var name in family is null ? PreferredFamilies : [family, .. PreferredFamilies])
        {
            var face = SKFontManager.Default.MatchFamily(name, style);
            if (face is not null && string.Equals(face.FamilyName, name, StringComparison.OrdinalIgnoreCase))
            {
                return face;
            }

            face?.Dispose();
        }

        return SKFontManager.Default.MatchFamily("monospace", style);
    }
}
