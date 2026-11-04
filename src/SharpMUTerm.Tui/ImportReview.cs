using SharpMUTerm.Client.Modals;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// The import review in cells: what can act as the player, what will be added, what was left out and why, the passwords
/// question, and the keys. Drawn from <see cref="ImportReviewModel"/>, the model the window's native review draws. The
/// number of lines does not change when the passwords are switched, so the modal sized when it opened still fits.
/// </summary>
internal static class ImportReview
{
    /// <summary>The width the review asks for.</summary>
    internal const int PreferredWidth = 84;

    internal static List<string> Render(ImportReviewModel model, int width)
    {
        ArgumentNullException.ThrowIfNull(model);
        width = Math.Max(30, width);
        var lines = new List<string> { $"[bold {ScreenPalette.Value}]{Escape(model.Title)}[/]" };
        lines.AddRange(HelpSurface.Wrap(model.Intro, width - 1).Select(Label));

        if (model.Empty)
        {
            lines.Add(string.Empty);
            lines.Add(Label("There is nothing in it this client can import."));
        }
        else
        {
            lines.Add(string.Empty);
            lines.Add(Heading(ImportReviewModel.ActsHeading));
            if (model.ActsAsYou.Count == 0)
            {
                lines.Add("  " + Label("nothing: no rule in it sends a command"));
            }

            foreach (var act in model.ActsAsYou)
            {
                lines.AddRange(Indented($"[{ScreenPalette.Caution}]•[/] ", act, width, Value));
            }

            lines.Add(string.Empty);
            lines.Add(Heading(ImportReviewModel.AddsHeading));
            foreach (var row in model.Adds)
            {
                lines.AddRange(Indented($"[{ScreenPalette.Accent}]{Escape(row.Kind.PadRight(5))}[/] [bold {ScreenPalette.Value}]{Escape(Clip(row.Name))}[/]  ", row.Detail, width, Label));
            }
        }

        if (model.LeftOutCount > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Heading($"{ImportReviewModel.LeftOutHeading} ({model.LeftOutCount})"));
            foreach (var row in model.LeftOut)
            {
                lines.AddRange(Indented($"[{ScreenPalette.Muted}]{Escape(row.Kind.PadRight(7))}[/] {Value(Clip(row.Name))} ", "— " + row.Detail, width, Label));
            }

            if (model.MoreLeftOut is { } more)
            {
                lines.Add("  " + Label(more));
            }
        }

        if (model.PasswordLine is { } passwords)
        {
            lines.Add(string.Empty);
            var mark = model.StorePasswords ? $"[{ScreenPalette.Accent}]●[/] " : $"[{ScreenPalette.Label}]○[/] ";
            lines.AddRange(Indented(mark, passwords, width, Value, indent: 0));
        }

        lines.Add(string.Empty);
        lines.Add(Keys(model.Empty ? "Esc close" : model.Keys));
        return lines;
    }

    private static string Clip(string text) => text.Length > 32 ? text[..31] + "…" : text;

    private static string Heading(string text) => $"[bold {ScreenPalette.Accent}]{Escape(text)}[/]";

    private static string Label(string text) => $"[{ScreenPalette.Label}]{Escape(text)}[/]";

    private static string Value(string text) => $"[{ScreenPalette.Value}]{Escape(text)}[/]";

    /// <summary>A lead and text wrapped under it, the continuation rows indented past the lead.</summary>
    private static IEnumerable<string> Indented(string lead, string text, int width, Func<string, string> style, int indent = 2)
    {
        var leadWidth = VisibleLength(lead) + indent;
        var room = Math.Max(10, width - leadWidth - 1);
        var wrapped = HelpSurface.Wrap(text, room).ToList();
        var pad = new string(' ', indent);
        for (var i = 0; i < wrapped.Count; i++)
        {
            yield return i == 0
                ? pad + lead + style(wrapped[i])
                : new string(' ', leadWidth) + style(wrapped[i]);
        }
    }

    private static string Keys(string keys) =>
        string.Join("   ", keys.Split(" · ").Select(part =>
        {
            var space = part.IndexOf(' ', StringComparison.Ordinal);
            return space < 0
                ? Label(part)
                : $"[{ScreenPalette.Accent}]{Escape(part[..space])}[/] {Label(part[(space + 1)..])}";
        }));
}
