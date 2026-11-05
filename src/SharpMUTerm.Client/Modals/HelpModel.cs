using SharpMUTerm.Client.Help;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Client.Modals;

/// <summary>What a help row is: a how-to page, a slash command, or a key.</summary>
public enum HelpKind
{
    /// <summary>A short page (<see cref="HelpTopic"/>); choosing it shows the page.</summary>
    Topic,

    /// <summary>A slash command (<see cref="SlashCommands"/>); choosing it puts the command on the command line, unsent.</summary>
    Command,

    /// <summary>A key, from the command registry or the host's own keys; choosing one with a command runs it.</summary>
    Key,
}

/// <summary>One row of help.</summary>
/// <param name="Kind">What choosing it does.</param>
/// <param name="Section">The heading it is listed under.</param>
/// <param name="Title">What it is called: a page's title, a command's usage, a key's action.</param>
/// <param name="Key">The key, in glyphs (<c>⌃P</c>), or empty.</param>
/// <param name="Detail">A few words more, or empty.</param>
/// <param name="Target">A page's id, the text a command places, or the command a key runs (empty for none).</param>
public sealed record HelpEntry(HelpKind Kind, string Section, string Title, string Key, string Detail, string Target);

/// <summary>A short how-to page.</summary>
public sealed record HelpTopic(string Id, string Title, string Summary, IReadOnlyList<string> Lines)
{
    public bool Equals(HelpTopic? other) =>
        other is not null && Id == other.Id && Title == other.Title && Summary == other.Summary && ModelLists.Same(Lines, other.Lines);

    public override int GetHashCode() => HashCode.Combine(Id, Title, Summary, ModelLists.Hash(Lines));

    /// <summary>
    /// The page as paragraphs: consecutive lines joined with a space, a blank line between paragraphs kept as an empty
    /// one. The lines are broken for reading in source; each host wraps the paragraphs to the width it has.
    /// </summary>
    public IReadOnlyList<string> Paragraphs
    {
        get
        {
            var paragraphs = new List<string>();
            var current = new List<string>();
            foreach (var line in Lines)
            {
                if (line.Length == 0)
                {
                    if (current.Count > 0)
                    {
                        paragraphs.Add(string.Join(' ', current));
                        current.Clear();
                    }

                    paragraphs.Add(string.Empty);
                    continue;
                }

                current.Add(line);
            }

            if (current.Count > 0)
            {
                paragraphs.Add(string.Join(' ', current));
            }

            return paragraphs;
        }
    }
}

/// <summary>
/// Help: the slash commands (from <see cref="SlashCommands"/>, the table <c>--help</c> prints), every key the command
/// registry names for this host plus the keys the host keeps, and a few short how-to pages. Drawn by the cell surface
/// (<c>HelpSurface.Render</c>) and by the window's native one; both filter it with <see cref="View"/>, so a query lists
/// the same rows.
/// </summary>
/// <param name="Entries">Every row, in the order they are listed.</param>
/// <param name="Topics">The pages the topic rows open.</param>
public sealed record HelpModel(IReadOnlyList<HelpEntry> Entries, IReadOnlyList<HelpTopic> Topics)
{
    /// <summary>The heading over the pages.</summary>
    public const string TopicsSection = "HOW-TO";

    /// <summary>The heading over the slash commands.</summary>
    public const string CommandsSection = "SLASH COMMANDS";

    /// <summary>The heading over the keys the host keeps for itself and its newline.</summary>
    public const string HostSection = "KEYS · THIS HOST";

    /// <summary>What the list says when nothing matches.</summary>
    public const string NoMatches = "nothing matches — ⌫ widens the search";

    /// <summary>
    /// The rows for <paramref name="query"/>, in list order: every row with each word of the query somewhere in its
    /// title, key, detail or heading — or, for a page, in its text. A blank query lists everything.
    /// </summary>
    public IReadOnlyList<HelpEntry> View(string? query)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return Entries;
        }

        return Entries.Where(e => words.All(w => Mentions(e, w))).ToArray();
    }

    /// <summary>The page with <paramref name="id"/>, or null.</summary>
    public HelpTopic? Topic(string? id) => Topics.FirstOrDefault(t => t.Id == id);

    private bool Mentions(HelpEntry entry, string word)
    {
        static bool In(string text, string w) => text.Contains(w, StringComparison.OrdinalIgnoreCase);
        if (In(entry.Title, word) || In(entry.Key, word) || In(entry.Detail, word) || In(entry.Section, word))
        {
            return true;
        }

        return entry.Kind == HelpKind.Topic && Topic(entry.Target) is { } topic && topic.Lines.Any(l => In(l, word));
    }

    /// <summary>
    /// Help for this host: the pages, the slash commands, every key <paramref name="catalog"/> names (⌃P's entries, so
    /// a key is listed as the host spells it), then the host's newline and the chords it keeps.
    /// </summary>
    /// <param name="catalog">The command registry's entries for this host and state.</param>
    /// <param name="newline">The host's newline chord, in glyphs (<c>⌥⏎</c>, <c>⌃⏎</c>).</param>
    /// <param name="hostKeeps">The chords the host keeps for itself, with what each does there.</param>
    public static HelpModel Build(
        IReadOnlyList<CommandItem> catalog,
        string newline,
        IReadOnlyList<(string Chord, string Action)> hostKeeps)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(hostKeeps);
        var topics = HelpTopics.For(KeyGlyphs.Normalize(newline));
        var entries = new List<HelpEntry>();
        entries.AddRange(topics.Select(t => new HelpEntry(HelpKind.Topic, TopicsSection, t.Title, string.Empty, t.Summary, t.Id)));
        entries.AddRange(SlashCommands.All.Select(c => new HelpEntry(HelpKind.Command, CommandsSection, c.Usage, string.Empty, c.Summary, c.Placed)));

        // The registry's keys, by its own groups. Go-to rows are left out: they are per character and per window, and
        // the keys page says how those numberings work.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in catalog.Where(i => i.Group != CommandGroup.GoTo && !string.IsNullOrWhiteSpace(i.Key)))
        {
            if (!seen.Add(item.Id))
            {
                continue;
            }

            entries.Add(new HelpEntry(
                HelpKind.Key,
                "KEYS · " + CommandPaletteModel.GroupLabel(item.Group),
                item.Title,
                KeyGlyphs.Normalize(item.Key!),
                item.Detail ?? string.Empty,
                item.Id));
        }

        entries.Add(new HelpEntry(HelpKind.Key, HostSection, "Insert a newline in the command line", KeyGlyphs.Normalize(newline), string.Empty, string.Empty));
        foreach (var (chord, action) in hostKeeps)
        {
            entries.Add(new HelpEntry(HelpKind.Key, HostSection, action, KeyGlyphs.Normalize(chord), "kept by this host", string.Empty));
        }

        return new HelpModel(entries, topics);
    }

    public bool Equals(HelpModel? other) =>
        other is not null && ModelLists.Same(Entries, other.Entries) && ModelLists.Same(Topics, other.Topics);

    public override int GetHashCode() => HashCode.Combine(ModelLists.Hash(Entries), ModelLists.Hash(Topics));
}

/// <summary>
/// One opening of help in a host's own controls. Choosing a page shows it (the host's own business, nothing comes back);
/// choosing a slash command or a key comes back through <see cref="Choose"/>, after help has closed, to the client
/// method the cell surface's ⏎ ends in.
/// </summary>
public sealed class HelpSession : ModalSession<HelpModel>
{
    private readonly Action<int> _choose;

    /// <param name="model">What help lists.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="choose">Acts on entry N of <see cref="HelpModel.Entries"/>, after help has closed.</param>
    /// <param name="closed">Told help closed.</param>
    public HelpSession(HelpModel model, bool isDark, Action<int> choose, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(choose);
        _choose = choose;
    }

    public override string Surface => "help";

    /// <summary>What the search box opens with typed in (<c>/help words</c>).</summary>
    public string InitialQuery { get; init; } = string.Empty;

    /// <summary>The page help opens on, or null for the list.</summary>
    public string? InitialTopic { get; init; }

    /// <summary>
    /// The host's: ⏎ on entry <paramref name="entryIndex"/> of <see cref="HelpModel.Entries"/>. A page is the host's to
    /// show and returns false; a command or a key closes help and is acted on (true). A key with no command does nothing.
    /// </summary>
    public bool Choose(int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= Model.Entries.Count)
        {
            return false;
        }

        var entry = Model.Entries[entryIndex];
        if (entry.Kind == HelpKind.Topic || entry.Target.Length == 0)
        {
            return false;
        }

        return CloseThen(() => _choose(entryIndex));
    }
}
