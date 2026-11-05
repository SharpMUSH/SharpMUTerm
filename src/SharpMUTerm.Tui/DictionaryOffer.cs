using SharpMUTerm.Client.Modals;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// The dictionary offer in cells: the question, what would be fetched from where and saved where, and the two keys.
/// Drawn from <see cref="DictionaryOfferModel"/>, the model the window's native offer draws.
/// </summary>
internal static class DictionaryOffer
{
    /// <summary>The width the offer asks for.</summary>
    internal const int PreferredWidth = 72;

    internal static List<string> Render(DictionaryOfferModel model, int width)
    {
        width = Math.Max(30, width);
        var lines = new List<string>
        {
            $"[bold {ScreenPalette.Value}]{Escape(model.Question)}[/]",
            string.Empty,
        };
        foreach (var sentence in model.Lines)
        {
            lines.AddRange(HelpSurface.Wrap(sentence, width - 1).Select(l => $"[{ScreenPalette.Label}]{Escape(l)}[/]"));
        }

        lines.Add(string.Empty);
        lines.Add($"[{ScreenPalette.Accent}]⏎[/] [{ScreenPalette.Value}]download[/]   [{ScreenPalette.Accent}]Esc[/] [{ScreenPalette.Label}]not now[/]");
        return lines;
    }
}
