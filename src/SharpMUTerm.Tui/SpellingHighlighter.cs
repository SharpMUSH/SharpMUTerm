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
/// <see cref="ScreenPalette.EditBg"/> is the pair the settings screens already use for a refused value;
/// <paramref name="marked"/> is that warning resolved for the app's theme (<see cref="ScreenTheme"/>).
/// </remarks>
internal sealed class SpellingHighlighter(SpellChecker checker, Color marked) : ISyntaxHighlighter
{
    public (IReadOnlyList<SyntaxToken> Tokens, SyntaxLineState EndState) Tokenize(string line, int lineIndex, SyntaxLineState startState) =>
        (checker.Misspellings(line).Select(m => new SyntaxToken(m.Start, m.Length, marked)).ToArray(), startState);
}
