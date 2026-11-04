using System.Globalization;
using SharpConsoleUI.Controls;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using static SharpMUTerm.Tui.MarkupText;
using static SharpMUTerm.Tui.ScreenPalette;

namespace SharpMUTerm.Tui;

/// <summary>
/// What the settings search needs from the app: the declared sections it searches, the configuration their
/// values are read from, and a way to take the reader to a match. <see cref="Go"/> returns false when it could
/// not, and the search stays up.
/// </summary>
internal sealed record SettingsSearchHost(
    Func<IReadOnlyList<SettingsSectionContribution>> Sections,
    Func<AppConfiguration> Config,
    Func<SettingsMatch, bool> Go);

/// <summary>What a key did to the settings search.</summary>
internal enum SettingsSearchAction
{
    /// <summary>Not a key the search uses.</summary>
    None,

    /// <summary>The query or the selection moved.</summary>
    Redraw,

    /// <summary>Back to the screen the search was opened over.</summary>
    Close,

    /// <summary>Go to the selected match.</summary>
    Go,
}

/// <summary>
/// The settings search's keyboard state: the query being typed and which match is selected. It owns no
/// controls — <see cref="SettingsOverlay"/> hands it keys and draws <see cref="SettingsSearchView"/> from it —
/// so the whole contract is testable without a terminal. Typing narrows, Backspace widens, ↑↓ walk the matches,
/// ⏎ goes to the selected one, Esc goes back to the screen it was opened over.
/// </summary>
internal sealed class SettingsSearchState
{
    private readonly SettingsSearchHost _host;

    internal SettingsSearchState(SettingsSearchHost host, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Origin = origin;
    }

    /// <summary>The title of the screen the search was opened over, which Esc goes back to.</summary>
    internal string? Origin { get; }

    /// <summary>What has been typed.</summary>
    internal string Query { get; private set; } = string.Empty;

    /// <summary>Which match is selected, clamped to the matches there are.</summary>
    internal int Selected { get; private set; }

    /// <summary>The sections searched, in their order.</summary>
    internal IReadOnlyList<SettingsSectionContribution> Sections => _host.Sections();

    /// <summary>The configuration a match's value is read from.</summary>
    internal AppConfiguration Config => _host.Config();

    /// <summary>Every match for the query, in section order.</summary>
    internal IReadOnlyList<SettingsMatch> Matches => SettingsSearch.Find(Sections, Query);

    /// <summary>The selected match, or null when nothing matches.</summary>
    internal SettingsMatch? Current
    {
        get
        {
            var matches = Matches;
            return matches.Count == 0 ? null : matches[Math.Clamp(Selected, 0, matches.Count - 1)];
        }
    }

    /// <summary>Interprets one key.</summary>
    internal SettingsSearchAction Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return SettingsSearchAction.Close;

            case ConsoleKey.Enter:
                return Current is null ? SettingsSearchAction.Redraw : SettingsSearchAction.Go;

            case ConsoleKey.UpArrow:
                return Move(-1);

            case ConsoleKey.DownArrow:
                return Move(1);

            case ConsoleKey.Backspace:
                if (Query.Length == 0)
                {
                    return SettingsSearchAction.Redraw;
                }

                Query = Query[..^1];
                Selected = 0;
                return SettingsSearchAction.Redraw;

            default:
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar)
                    && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
                {
                    Type(key.KeyChar.ToString());
                    return SettingsSearchAction.Redraw;
                }

                // Every other key is swallowed rather than passed on: the screen under the search is not
                // the one the reader is looking at, and a key reaching it would change something unseen.
                return SettingsSearchAction.Redraw;
        }
    }

    /// <summary>Appends text to the query — a key, or a paste flattened to one line.</summary>
    internal void Type(string text)
    {
        var line = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
        Query = (Query + line).Length > MaxQuery ? (Query + line)[..MaxQuery] : Query + line;
        Selected = 0;
    }

    /// <summary>The longest query kept; a field label is never near it.</summary>
    internal const int MaxQuery = 64;

    private SettingsSearchAction Move(int delta)
    {
        var count = Matches.Count;
        if (count == 0)
        {
            return SettingsSearchAction.Redraw;
        }

        Selected = ((Math.Clamp(Selected, 0, count - 1) + delta) % count + count) % count;
        return SettingsSearchAction.Redraw;
    }
}

/// <summary>
/// Draws the settings search: a header carrying the query, a list of every settings section with how many
/// matches each holds, and beside it the matches grouped by section, the matched text marked, each field's
/// value now and <c>● changed</c> where it differs from its default. Below
/// <see cref="ScreenChrome.TooNarrowForTwo"/> the section list is left out and the matches take the width.
/// Pure markup apart from <see cref="Build"/>.
/// </summary>
internal static class SettingsSearchView
{
    /// <summary>The section list's width when the screen can afford it.</summary>
    internal const int SectionsWidth = 35;

    /// <summary>The fewest cells the section list is drawn in.</summary>
    internal const int SectionsMinimum = 24;

    /// <summary>The fewest cells the matches are readable in.</summary>
    internal const int MatchesMinimum = 44;

    /// <summary>The header's hints. Every key named here is one <see cref="SettingsSearchState"/> answers.</summary>
    internal const string Hints = "type to filter · ↑↓ select · ⏎ go · Esc back";

    /// <summary>The composed screen.</summary>
    internal static IWindowControl Build(SettingsSearchState state, int width, int height, ScreenTheme scheme)
    {
        ArgumentNullException.ThrowIfNull(state);

        var matches = state.Matches;
        var rows = ScreenChrome.Rows(height);
        var header = ScreenChrome.Band(HeaderLine(state, matches, width), HeaderBg, scheme);
        var footer = ScreenChrome.Band(FooterLine(state, matches, width), FooterBg, scheme);

        if (ScreenChrome.TooNarrowForTwo(width, SectionsMinimum, MatchesMinimum))
        {
            var only = ScreenChrome.Window(MatchColumn(state, matches, width), rows);
            return ScreenChrome.Single(
                header, footer, ScreenChrome.Stretch(ScreenChrome.Panel(only, scheme)), only.Count, rows);
        }

        var left = ScreenChrome.SplitWidth(width, SectionsWidth, SectionsMinimum, MatchesMinimum);
        var sections = SectionColumn(state, matches, left);
        var right = ScreenChrome.Window(MatchColumn(state, matches, width - left - ScreenChrome.ColumnDivider), rows);
        return ScreenChrome.Split(
            header,
            footer,
            ScreenChrome.Stretch(ScreenChrome.Panel(sections, scheme)),
            ScreenChrome.Stretch(ScreenChrome.Panel(right, scheme)),
            left,
            Math.Max(sections.Count, right.Count),
            rows,
            scheme);
    }

    /// <summary>
    /// <c>Settings  / meter▌   3 matches</c> on the left, the keys on the right. The block after the query is
    /// where the next character lands; it is drawn rather than placed with the terminal's caret because the
    /// screens draw every caret they have.
    /// </summary>
    internal static string HeaderLine(SettingsSearchState state, IReadOnlyList<SettingsMatch> matches, int width)
    {
        var count = state.Query.Trim().Length == 0
            ? string.Empty
            : matches.Count == 1 ? "   1 match" : $"   {matches.Count.ToString(CultureInfo.InvariantCulture)} matches";
        var left = $" [bold {Value}]Settings[/]  [{Accent}]/[/] [{Value} on {FieldBg}]{Escape(state.Query)}[/]"
            + $"[{Ink} on {Accent}] [/][{Label}]{count}[/]";
        return SpreadTitle(left, $"[{Label}]{Hints} [/]", width);
    }

    /// <summary>Where the selection is, and the actions: <c>meter 1/3 · Text &amp; display   [Esc] Back  [⏎] Go</c>.</summary>
    internal static string FooterLine(SettingsSearchState state, IReadOnlyList<SettingsMatch> matches, int width)
    {
        var context = state.Current is { } current
            ? ScreenChrome.Context(
                Escape(ScreenChrome.Position(state.Query.Trim(), Math.Clamp(state.Selected, 0, matches.Count - 1), matches.Count)),
                Escape(current.Section.Title))
            : string.Empty;
        var actions = $"[{Label}] [[Esc]] Back [/]  "
            + (state.Current is null ? string.Empty : $"[{Ink} on {Accent}] [[⏎]] Go [/] ");
        return SpreadLR(" " + context, actions, width);
    }

    /// <summary>
    /// Every section, with its key and how many matches it holds. The section the selected match is in wears
    /// the cursor band; a section with none is dimmed while a query is typed, so the list reads as where the
    /// query was found.
    /// </summary>
    internal static List<string> SectionColumn(SettingsSearchState state, IReadOnlyList<SettingsMatch> matches, int width)
    {
        var counts = SettingsSearch.CountBySection(matches);
        var selected = state.Current?.Section.Id;
        var typed = state.Query.Trim().Length > 0;
        var lines = new List<string> { string.Empty };
        foreach (var section in state.Sections)
        {
            var count = counts.GetValueOrDefault(section.Id);
            var key = section.Key ?? string.Empty;
            var tally = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
            var title = ElideVisible(Escape(section.Title), Math.Max(4, width - 11));
            var ink = typed && count == 0 ? Muted : Value;
            var row = $" [{ink}]{PadVisible(title, Math.Max(4, width - 11))}[/] [{Label}]{key,-4}[/][{Accent}]{tally,3}[/]";
            lines.Add(ScreenChrome.Cursor(row, section.Id == selected, width));
        }

        return lines;
    }

    /// <summary>
    /// The matches, grouped under their section's title and key. A field match reads its label with the
    /// matched run marked, its value now, and <c>● changed</c> when it differs from its default; a section
    /// match reads <c>open …</c>. The selected one wears the cursor band and has its help line under it.
    /// </summary>
    internal static List<string> MatchColumn(SettingsSearchState state, IReadOnlyList<SettingsMatch> matches, int width)
    {
        var lines = new List<string> { string.Empty };
        if (state.Query.Trim().Length == 0)
        {
            lines.Add($" [{Label}]Type to search every setting by name, by what it does, or by screen.[/]");
            if (state.Origin is { } origin)
            {
                lines.Add($" [{Label}]Esc goes back to {Escape(origin)}.[/]");
            }

            return lines;
        }

        if (matches.Count == 0)
        {
            lines.Add($" [{Label}]Nothing matches “{Escape(state.Query.Trim())}”.[/]");
            return lines;
        }

        var config = state.Config;
        var selected = Math.Clamp(state.Selected, 0, matches.Count - 1);
        string? section = null;
        string? group = null;
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (!string.Equals(match.Section.Id, section, StringComparison.Ordinal))
            {
                if (section is not null)
                {
                    lines.Add(string.Empty);
                }

                section = match.Section.Id;
                group = null;
                lines.Add($" [bold {Value}]{Escape(match.Section.Title)}[/]  [{Label}]{Escape(match.Section.Key ?? string.Empty)}[/]");
            }

            if (match.Field is { Group: { } fieldGroup } && !string.Equals(fieldGroup, group, StringComparison.Ordinal))
            {
                group = fieldGroup;
                lines.Add($" [dim]├ {Escape(fieldGroup.ToUpperInvariant())}[/]");
            }

            lines.Add(ScreenChrome.Cursor(Row(match, config, width), i == selected, width));
            if (i == selected && match.Field?.Help is { } help)
            {
                lines.Add($"     [{Label}]{ElideVisible(Escape(help), Math.Max(8, width - 6))}[/]");
            }
        }

        return lines;
    }

    /// <summary>One match: its marked text, then (for a field) its value and whether it has changed.</summary>
    private static string Row(SettingsMatch match, AppConfiguration config, int width)
    {
        var text = Marked(match);
        if (match.Field is not { } field)
        {
            return $"   [{Label}]open[/] {text}";
        }

        var changed = field.Binding is { } binding && !binding.IsDefault(config);
        var mark = changed ? $"  [{Accent}]{OptionsScreenRenderer.ChangedMark}[/]" : string.Empty;
        var value = field.Binding?.Text(config) ?? string.Empty;
        var room = Math.Max(0, width - 3 - 30 - VisibleLength(mark) - 2);
        var shown = room > 3 && value.Length > 0 ? $"[{Muted}]{ElideVisible(Escape(value), room)}[/]" : string.Empty;
        return $"   {PadVisible(text, 30)} {shown}{mark}";
    }

    /// <summary>The match's text with the matched run in the accent, bold.</summary>
    private static string Marked(SettingsMatch match)
    {
        if (match.Start < 0 || match.Length == 0 || match.Start + match.Length > match.Text.Length)
        {
            return $"[{Value}]{Escape(match.Text)}[/]";
        }

        var before = match.Text[..match.Start];
        var run = match.Text.Substring(match.Start, match.Length);
        var after = match.Text[(match.Start + match.Length)..];
        return $"[{Value}]{Escape(before)}[/][bold {Accent}]{Escape(run)}[/][{Value}]{Escape(after)}[/]";
    }
}
