using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// Marks the words the composer's dictionary does not know, through the editor's syntax-highlighting hook.
/// </summary>
/// <remarks>
/// A colour and not an underline: the hook carries a foreground and nothing else, and an underline needs a
/// styling hook in <see cref="MultilineEditControl"/> upstream. <see cref="ScreenPalette.Warn"/> on
/// <see cref="ScreenPalette.EditBg"/> is the pair the settings screens already use for a refused value.
/// </remarks>
internal sealed class SpellingHighlighter(SpellChecker checker) : ISyntaxHighlighter
{
    private static readonly Color Marked = new(ScreenPalette.Warn);

    public (IReadOnlyList<SyntaxToken> Tokens, SyntaxLineState EndState) Tokenize(string line, int lineIndex, SyntaxLineState startState) =>
        (checker.Misspellings(line).Select(m => new SyntaxToken(m.Start, m.Length, Marked)).ToArray(), startState);
}
