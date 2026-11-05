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
    /// Installed Nerd Font families, asked by name for an icon in the Private Use Area the bundled symbols lack. No
    /// longer the text face: the bundled JetBrains Mono is the default on every OS, so the window looks the same on
    /// Windows, Linux and macOS; an installed face draws text only when <c>--font</c> names it.
    /// </summary>
    public static readonly string[] PreferredFamilies =
    [
        "CaskaydiaMono Nerd Font", "CaskaydiaCove Nerd Font Mono", "CaskaydiaCove Nerd Font",
        "JetBrainsMono Nerd Font Mono", "JetBrainsMono Nerd Font", "FiraCode Nerd Font Mono",
    ];

    /// <summary>
    /// Installed faces tried only when the bundled JetBrains Mono is missing from the build (a packaging slip).
    /// </summary>
    private static readonly string[] LastResortFamilies =
    [
        "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Courier New",
    ];

    /// <summary>The bundled faces (<c>Fonts/</c>), loaded once and never disposed: every grid shares them.</summary>
    private static readonly Lazy<SKTypeface?> BundledRegular = new(() => Bundled("JetBrainsMono-Regular.ttf"));
    private static readonly Lazy<SKTypeface?> BundledBold = new(() => Bundled("JetBrainsMono-Bold.ttf"));
    private static readonly Lazy<SKTypeface?> BundledItalic = new(() => Bundled("JetBrainsMono-Italic.ttf"));
    private static readonly Lazy<SKTypeface?> BundledBoldItalic = new(() => Bundled("JetBrainsMono-BoldItalic.ttf"));
    private static readonly Lazy<SKTypeface?> BundledSymbols = new(() => Bundled("SymbolsNerdFontMono-Regular.ttf"));

    /// <summary>The family name the bundled text face reports.</summary>
    internal const string BundledFamily = "JetBrains Mono";

    private readonly ConcurrentDictionary<(int CodePoint, bool Bold, bool Italic), SKFont> _fonts = new();
    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly SKTypeface _italic;
    private readonly SKTypeface _boldItalic;

    /// <param name="sizePixels">The text size, in device pixels.</param>
    /// <param name="family">An installed family <c>--font</c> named, or null for the bundled face.</param>
    /// <param name="lineHeight">
    /// The row height as a multiple of the face's own ascent-to-descent height (<c>--line-height</c>, at least 1). The
    /// extra is split above and below the text, so a box or a picture still meets the row above and below it: the
    /// cell is taller, never gapped.
    /// </param>
    public CellFonts(float sizePixels, string? family = null, float lineHeight = 1f)
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
        var text = metrics.Descent - metrics.Ascent;
        CellHeight = Math.Max(1, (int)Math.Ceiling(text * Math.Max(1f, lineHeight)));
        Baseline = (float)Math.Round(-metrics.Ascent + ((CellHeight - text) / 2f));
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

        // The bundled symbols first, so every OS draws the same icon; an installed Nerd Font only for one they lack.
        if (BundledSymbols.Value is { } bundled && bundled.ContainsGlyph(codePoint))
        {
            return bundled;
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

    /// <summary>Whether the bundled faces are in this build, for a test to hold the packaging to.</summary>
    internal static bool HasBundledFaces => BundledRegular.Value is not null && BundledSymbols.Value is not null;

    private static SKTypeface? Bundled(string file)
    {
        using var stream = typeof(CellFonts).Assembly.GetManifestResourceStream($"Fonts.{file}");
        if (stream is null)
        {
            return null;
        }

        using var data = SKData.Create(stream);
        return data is null ? null : SKTypeface.FromData(data);
    }

    private static SKTypeface? BundledText(SKFontStyle style) => (style.Weight >= (int)SKFontStyleWeight.SemiBold, style.Slant != SKFontStyleSlant.Upright) switch
    {
        (true, true) => BundledBoldItalic.Value,
        (true, false) => BundledBold.Value,
        (false, true) => BundledItalic.Value,
        _ => BundledRegular.Value,
    };

    private static readonly string[] SymbolFamilies =
    [
        "Symbols Nerd Font Mono", "Symbols Nerd Font", .. PreferredFamilies,
    ];

    private static SKTypeface? Match(string? family, SKFontStyle style)
    {
        // A family --font named, if it is installed and is not the bundled face itself.
        if (family is not null && !string.Equals(family, BundledFamily, StringComparison.OrdinalIgnoreCase))
        {
            var named = SKFontManager.Default.MatchFamily(family, style);
            if (named is not null && string.Equals(named.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                return named;
            }

            named?.Dispose();
        }

        // The bundled face is asked for by its own name, so a family that resolves to it (the regular face's,
        // when bold is matched after it) gets the bundled weights rather than a system fallback.
        if (BundledText(style) is { } bundled)
        {
            return bundled;
        }

        foreach (var name in LastResortFamilies)
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
