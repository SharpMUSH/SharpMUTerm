using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Client.Palette;

/// <summary>A run of characters in a string: where it starts and how long it is.</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>Which field of an entry a query matched, best first.</summary>
public enum PaletteMatchField
{
    /// <summary>The query is empty; nothing was matched.</summary>
    None,

    /// <summary>The title.</summary>
    Title,

    /// <summary>The detail, the key or the group's name — listed below every title match.</summary>
    Other,
}

/// <summary>One line of the palette: a group header or an entry.</summary>
public abstract record PaletteRow;

/// <summary>A group's header.</summary>
/// <param name="Label">The header as drawn, in capitals (<c>RECENT</c>, <c>LAYOUT</c>).</param>
public sealed record PaletteHeader(string Label) : PaletteRow;

/// <summary>
/// An entry the palette lists: the command, its key as this host spells it (in the one glyph vocabulary,
/// <see cref="KeyGlyphs"/>), the words beside it, and where the query matched its title.
/// </summary>
/// <param name="Item">The catalog entry; <see cref="CommandItem.Id"/> is what a surface dispatches.</param>
/// <param name="Key">The chord, or null. A surface draws it in a column of its own and never elides it.</param>
/// <param name="Detail">The words beside the title, or null.</param>
/// <param name="TitleMatches">The runs of the title the query matched, ascending, for highlighting.</param>
/// <param name="Field">Which field the query matched.</param>
/// <param name="Score">The match's score within its field; zero for an empty query.</param>
public sealed record PaletteEntry(
    CommandItem Item,
    string? Key,
    string? Detail,
    IReadOnlyList<TextRange> TitleMatches,
    PaletteMatchField Field,
    int Score) : PaletteRow;

/// <summary>
/// What the palette shows for one query: the rows in display order (headers and entries interleaved), the
/// entries alone in the same order (what ↑↓ walk and ⏎ dispatches), and how many entries the catalog held.
/// </summary>
public sealed record PaletteView(IReadOnlyList<PaletteRow> Rows, IReadOnlyList<PaletteEntry> Entries, int Total)
{
    /// <summary>The widest key any entry carries, in characters; the width a surface reserves for the key column.</summary>
    public int KeyWidth => Entries.Count == 0 ? 0 : Entries.Max(e => e.Key?.Length ?? 0);
}

/// <summary>
/// The command surface's view-model, shared by every host's palette: a query and a catalog in, ordered rows
/// with group headers out. UI-agnostic — the terminal draws it as markup and the window will draw it with
/// its own controls, and both list the same rows in the same order.
/// <para>
/// <b>An empty query</b> lists <c>RECENT</c> first (what was last run from the surface, newest first), then
/// <c>SUGGESTED</c> (what the state the client is in makes likely: copy and make-a-trigger while output is
/// selected, <em>Back to live output</em> while scrolled back, and so on — <see cref="Suggested"/>), then every
/// group in its fixed order. An entry is listed once, in the first of those it belongs to.
/// </para>
/// <para>
/// <b>A query</b> is matched by <see cref="FuzzyMatch"/> against the title; an entry whose title does not match
/// is tried against its detail, its key and its group's name, and listed below every title match. Groups keep
/// their headers but are ordered by their best entry, so the best match is always the first row; within a group
/// entries are ordered by score, and ties keep catalog order.
/// </para>
/// </summary>
public static class CommandPaletteModel
{
    /// <summary>The header over recently run commands.</summary>
    public const string RecentLabel = "RECENT";

    /// <summary>The header over the entries the current state suggests.</summary>
    public const string SuggestedLabel = "SUGGESTED";

    private static readonly CommandGroup[] GroupOrder =
    {
        CommandGroup.GoTo,
        CommandGroup.World,
        CommandGroup.Terminal,
        CommandGroup.Compose,
        CommandGroup.Map,
        CommandGroup.Panels,
        CommandGroup.Layout,
        CommandGroup.Help,
        CommandGroup.Settings,
    };

    /// <summary>The header a group is listed under.</summary>
    public static string GroupLabel(CommandGroup group) => group switch
    {
        CommandGroup.GoTo => "GO TO",
        CommandGroup.World => "CONNECTION",
        CommandGroup.Terminal => "OUTPUT",
        CommandGroup.Compose => "WRITING",
        CommandGroup.Map => "MAP",
        CommandGroup.Panels => "PANELS",
        CommandGroup.Layout => "WINDOWS & PANES",
        CommandGroup.Help => "HELP & SETUP",
        CommandGroup.Settings => "SETTINGS",
        _ => group.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// The ids the client's state makes likely, most specific first. Kept to what the state says outright —
    /// a selection, a scrolled or frozen pane, a walk, a clash — rather than guessed from habit, which is what
    /// <c>RECENT</c> is for.
    /// </summary>
    public static IReadOnlyList<string> Suggested(CommandContext state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var ids = new List<string>();

        // A configuration that is new (no world yet, or the key tour still owed): where to start, before anything else.
        if (state.NewConfiguration)
        {
            ids.Add(BuiltInCommands.HelpCommand);
            ids.Add(BuiltInCommands.WelcomeCommand);
            ids.Add(BuiltInCommands.ImportCommand);
        }

        if (state.OutputSelected)
        {
            ids.Add("term:copy");
            ids.Add("term:trigger-from-selection");
        }

        if (state.Frozen)
        {
            ids.Add("term:unfreeze");
        }

        if (state.ScrolledBack)
        {
            ids.Add("term:scroll-live");
        }

        if (state.Walking)
        {
            ids.Add("term:walk-stop");
        }

        if (state.Zoomed)
        {
            ids.Add("layout:unzoom");
        }

        if (state.KeyClashes > 0)
        {
            ids.Add("settings:key-clashes");
        }

        // A pane narrower than its game wraps: every hard-wrapped row leaves a stray word. Suggested while it holds,
        // rather than said once, because ragged lines are the look of every room until it is fixed.
        foreach (var ragged in state.RaggedWraps ?? [])
        {
            ids.Add(BuiltInCommands.JoinWraps(ragged.World));
        }

        return ids;
    }

    /// <summary>Builds the view for <paramref name="query"/> over <paramref name="catalog"/>.</summary>
    /// <param name="query">What has been typed; blank lists everything.</param>
    /// <param name="catalog">Every entry offered right now, in registration order.</param>
    /// <param name="recent">Ids run from the surface, newest first; ids no longer offered are skipped.</param>
    /// <param name="suggested">Ids the current state makes likely (<see cref="Suggested"/>).</param>
    public static PaletteView Build(
        string query,
        IReadOnlyList<CommandItem> catalog,
        IReadOnlyList<string>? recent = null,
        IReadOnlyList<string>? suggested = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(catalog);

        return string.IsNullOrWhiteSpace(query)
            ? BuildEmpty(catalog, recent ?? Array.Empty<string>(), suggested ?? Array.Empty<string>())
            : BuildMatched(query, catalog);
    }

    private static PaletteView BuildEmpty(
        IReadOnlyList<CommandItem> catalog, IReadOnlyList<string> recent, IReadOnlyList<string> suggested)
    {
        var rows = new List<PaletteRow>();
        var entries = new List<PaletteEntry>();
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, CommandItem>(StringComparer.Ordinal);
        foreach (var item in catalog)
        {
            byId.TryAdd(item.Id, item);
        }

        void Section(string label, IEnumerable<CommandItem> items)
        {
            var list = items.Where(i => !shown.Contains(i.Id)).ToList();
            if (list.Count == 0)
            {
                return;
            }

            rows.Add(new PaletteHeader(label));
            foreach (var item in list)
            {
                shown.Add(item.Id);
                var entry = Entry(item, Array.Empty<TextRange>(), PaletteMatchField.None, 0);
                rows.Add(entry);
                entries.Add(entry);
            }
        }

        Section(RecentLabel, recent.Where(byId.ContainsKey).Select(id => byId[id]));
        Section(SuggestedLabel, suggested.Where(byId.ContainsKey).Select(id => byId[id]));
        foreach (var group in GroupOrder)
        {
            Section(GroupLabel(group), catalog.Where(i => i.Group == group));
        }

        // A group a package or a later build adds is listed after the known ones rather than dropped.
        foreach (var group in catalog.Select(i => i.Group).Distinct().Where(g => Array.IndexOf(GroupOrder, g) < 0))
        {
            Section(GroupLabel(group), catalog.Where(i => i.Group == group));
        }

        return new PaletteView(rows, entries, catalog.Count);
    }

    private static PaletteView BuildMatched(string query, IReadOnlyList<CommandItem> catalog)
    {
        var matched = new List<(PaletteEntry Entry, int Index)>();
        for (var index = 0; index < catalog.Count; index++)
        {
            var item = catalog[index];
            if (FuzzyMatch.Match(query, item.Title) is { } title)
            {
                matched.Add((Entry(item, Ranges(title.Positions), PaletteMatchField.Title, title.Score), index));
                continue;
            }

            var other = new[] { DetailOf(item), KeyOf(item), GroupLabel(item.Group) }
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => FuzzyMatch.Match(query, t!))
                .Where(r => r is not null)
                .Select(r => r!.Score)
                .DefaultIfEmpty(int.MinValue)
                .Max();
            if (other != int.MinValue)
            {
                matched.Add((Entry(item, Array.Empty<TextRange>(), PaletteMatchField.Other, other), index));
            }
        }

        // Best first: a title match above any other, then by score, then catalog order.
        static int Compare((PaletteEntry Entry, int Index) a, (PaletteEntry Entry, int Index) b)
        {
            var byField = a.Entry.Field.CompareTo(b.Entry.Field);
            if (byField != 0)
            {
                return byField;
            }

            var byScore = b.Entry.Score.CompareTo(a.Entry.Score);
            return byScore != 0 ? byScore : a.Index.CompareTo(b.Index);
        }

        matched.Sort(Compare);

        // Groups in the order of their best entry; the sort above already put each group's best first.
        var rows = new List<PaletteRow>();
        var entries = new List<PaletteEntry>();
        foreach (var group in matched.Select(m => m.Entry.Item.Group).Distinct())
        {
            rows.Add(new PaletteHeader(GroupLabel(group)));
            foreach (var (entry, _) in matched.Where(m => m.Entry.Item.Group == group))
            {
                rows.Add(entry);
                entries.Add(entry);
            }
        }

        return new PaletteView(rows, entries, catalog.Count);
    }

    private static PaletteEntry Entry(CommandItem item, IReadOnlyList<TextRange> matches, PaletteMatchField field, int score) =>
        new(item, KeyOf(item), DetailOf(item), matches, field, score);

    private static string? KeyOf(CommandItem item) =>
        string.IsNullOrWhiteSpace(item.Key) ? null : KeyGlyphs.Normalize(item.Key);

    // An entry that set only a subtitle (a package's, or a family that has no key) shows it as its detail.
    private static string? DetailOf(CommandItem item) =>
        item.Detail ?? (item.Key is null ? item.Subtitle : null);

    /// <summary>Collapses ascending positions into runs.</summary>
    public static IReadOnlyList<TextRange> Ranges(IReadOnlyList<int> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var ranges = new List<TextRange>();
        var i = 0;
        while (i < positions.Count)
        {
            var start = positions[i];
            var length = 1;
            while (i + length < positions.Count && positions[i + length] == start + length)
            {
                length++;
            }

            ranges.Add(new TextRange(start, length));
            i += length;
        }

        return ranges;
    }
}
