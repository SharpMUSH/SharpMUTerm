namespace SharpMUTerm.Client.Palette;

/// <summary>
/// One spelling for every chord a list of keys shows. The catalog's keys are written in the symbol vocabulary
/// (<c>⌃R</c>, <c>⌥⇧→</c>, <c>⌃B |</c>) except where a host names a chord in words — the newline is <c>Alt+⏎</c>
/// in a terminal and <c>Ctrl+⏎</c> in the window, because <c>--help</c> and the composer's footer read those
/// strings too. A column of keys mixing <c>⌥R</c> with <c>Alt+⏎</c> reads as two different modifiers, so the
/// palette spells them all the one way: <c>Ctrl</c> ⌃, <c>Alt</c> ⌥, <c>Shift</c> ⇧, <c>Enter</c> ⏎,
/// <c>Tab</c> ⇥, <c>Esc</c> stays a word.
/// </summary>
public static class KeyGlyphs
{
    private static readonly (string Word, string Glyph)[] Modifiers =
    {
        ("Ctrl+", "⌃"),
        ("Control+", "⌃"),
        ("Alt+", "⌥"),
        ("Option+", "⌥"),
        ("Shift+", "⇧"),
    };

    private static readonly (string Word, string Glyph)[] Keys =
    {
        ("Enter", "⏎"),
        ("Return", "⏎"),
        ("Tab", "⇥"),
    };

    /// <summary>
    /// <paramref name="chord"/> in the symbol vocabulary: <c>Alt+⏎</c> → <c>⌥⏎</c>, <c>Ctrl+Enter</c> → <c>⌃⏎</c>.
    /// A chord already in symbols, or a plain key name (<c>F4</c>, <c>PgUp</c>), is returned unchanged.
    /// </summary>
    public static string Normalize(string chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        var result = chord;
        foreach (var (word, glyph) in Modifiers)
        {
            result = result.Replace(word, glyph, StringComparison.OrdinalIgnoreCase);
        }

        // A bare key name only after a modifier glyph, so "Tab" in prose (or F-keys) is left alone.
        foreach (var (word, glyph) in Keys)
        {
            foreach (var modifier in new[] { "⌃", "⌥", "⇧" })
            {
                result = result.Replace(modifier + word, modifier + glyph, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="chord"/> in words, the way a desktop application labels a key: <c>⌥1</c> → <c>Alt+1</c>,
    /// <c>⌃P</c> → <c>Ctrl+P</c>, <c>⌥⇧→</c> → <c>Alt+Shift+→</c>. The inverse of <see cref="Normalize"/> for the
    /// modifiers; a chord with no modifier glyph is returned unchanged. For a view drawn in a UI font, where the
    /// glyphs read smaller than the words around them.
    /// </summary>
    public static string Words(string chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        return chord.Replace("⌃", "Ctrl+", StringComparison.Ordinal)
            .Replace("⌥", "Alt+", StringComparison.Ordinal)
            .Replace("⇧", "Shift+", StringComparison.Ordinal);
    }
}
