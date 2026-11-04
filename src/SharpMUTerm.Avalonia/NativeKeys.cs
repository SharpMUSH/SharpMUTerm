using SharpMUTerm.Client.Palette;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// How every native surface in the window spells a key: in words (<c>Alt+2</c>, <c>Ctrl+W</c>, <c>Ctrl+B b</c>), the way a
/// desktop application labels one, never the terminal's glyphs (<c>⌥2</c>, <c>⌃W</c>). The client writes its keys and its
/// sentences about keys in glyphs, because the terminal draws them in a cell font where they read well; the window draws
/// in a UI font, where they read smaller than the words around them, and a menu saying <c>⌥2</c> beside a sidebar saying
/// <c>Alt+2</c> reads as two different modifiers. So whatever the client hands a native view to show — a menu's keys, a
/// notice and its key chip, a palette entry's detail, the composer's key row, a modal's sentences, a tooltip — passes
/// through <see cref="Words"/> on its way to the screen. <c>NativeKeySpellingTests</c> scans the native surfaces for a
/// glyph that got through. The cells keep their glyphs.
/// </summary>
internal static class NativeKeys
{
    /// <summary>The key glyphs the window never shows: the three modifiers and the Enter key.</summary>
    internal static readonly char[] Glyphs = ['⌃', '⌥', '⇧', '⏎'];

    /// <summary>
    /// <paramref name="text"/> with every key glyph spelt as a word — <c>⌃⏎</c> is <c>Ctrl+Enter</c>, as the modal footers
    /// and the window's own chords (<c>HostChord</c>'s words) say it; null is empty.
    /// </summary>
    public static string Words(string? text) =>
        string.IsNullOrEmpty(text) || text.IndexOfAny(Glyphs) < 0
            ? text ?? string.Empty
            : KeyGlyphs.Words(text).Replace("⏎", "Enter", StringComparison.Ordinal);

    /// <summary>Whether <paramref name="text"/> still holds a modifier glyph.</summary>
    public static bool HasGlyph(string? text) => text is not null && text.IndexOfAny(Glyphs) >= 0;
}
