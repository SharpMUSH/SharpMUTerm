using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Client.Panels;

/// <summary>Which run of the quick bar a binding sits in. Every view keeps the runs apart and in this order.</summary>
public enum QuickBarGroup
{
    /// <summary>A function key, plain or modified (<c>F11</c>, <c>⌃F1</c>).</summary>
    Function,

    /// <summary>Any other chord (<c>⌃E</c>, <c>⌥K</c>).</summary>
    Chord,

    /// <summary>A numeric keypad key (<c>Num8</c>), in the pad's own order: 7 8 9, 4 5 6, 1 2 3, 0.</summary>
    Keypad,
}

/// <summary>One binding on the quick bar, as every view of it draws it.</summary>
/// <param name="Key">The key as printed: <c>F1</c>, <c>⌃F2</c>, <c>⌃E</c>, <c>Num8</c>.</param>
/// <param name="Descriptor">The descriptor the key fires (<c>Ctrl+F2</c>), which the client runs on a click.</param>
/// <param name="Title">The binding's name, on one line.</param>
/// <param name="Fires">Whether pressing the key reaches the binding on this host; one that does not is still
/// clickable and drawn dim.</param>
/// <param name="Danger">Whether the binding is flagged dangerous on F4 (<c>Macro.Danger</c>): every view draws it in the
/// warning colour.</param>
/// <param name="Group">The run it is listed in.</param>
/// <param name="Short">The shorter wording a view uses when the whole bar does not fit at <paramref name="Title"/>: a compass
/// direction (the title's, else the command's) as its abbreviation (<c>northwest</c> → <c>nw</c>), anything else as the title. Null means the title.</param>
public sealed record QuickBarItem(
    string Key, string Descriptor, string Title, bool Fires, bool Danger = false,
    QuickBarGroup Group = QuickBarGroup.Function, string? Short = null)
{
    /// <summary>The wording to draw: <see cref="Short"/> when <paramref name="shortForm"/>, else the title.</summary>
    public string Label(bool shortForm) => shortForm ? Short ?? Title : Title;

    /// <summary>
    /// The key as drawn once its group has said "Num": a keypad key's digit (<c>Num8</c> → <c>8</c>), anything else as it is.
    /// </summary>
    public string ShortKey => Group == QuickBarGroup.Keypad && Key.StartsWith("Num", StringComparison.Ordinal) ? Key[3..] : Key;

    /// <summary>The walking directions and their usual short forms, which a MUD reads as the same command.</summary>
    private static readonly Dictionary<string, string> Directions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["north"] = "n", ["south"] = "s", ["east"] = "e", ["west"] = "w",
        ["northeast"] = "ne", ["northwest"] = "nw", ["southeast"] = "se", ["southwest"] = "sw",
        ["up"] = "u", ["down"] = "d",
    };

    /// <summary>The short wording for <paramref name="title"/>: a compass direction's abbreviation, else null (the title).</summary>
    public static string? ShortFor(string title) => Directions.TryGetValue(title.Trim(), out var abbreviation) ? abbreviation : null;
}

/// <summary>
/// The quick bar's content for one character: every key it has bound, in the order every view lists them — function keys
/// (plain before modified, then by number), then other chords, then the numeric keypad in the pad's order. The terminal draws them as a row of cells (<c>QuickBarRenderer</c>);
/// the window draws native buttons over that row from the same instance, so an item index names the same binding
/// in both. A binding is dangerous only where F4 says so (<see cref="QuickBarItem.Danger"/>); no view guesses it from a name.
/// </summary>
public sealed record QuickBarModel(IReadOnlyList<QuickBarItem> Items)
{
    /// <summary>The key that opens the bindings, as the hint prints it.</summary>
    public const string EditKey = "F4";

    /// <summary>The hint naming where the keys are edited.</summary>
    public const string EditHint = EditKey + " edits keys";

    /// <summary>The command a click on the hint dispatches: the F4 screen, through the registry.</summary>
    public const string EditCommand = CommandIds.ScreenPrefix + "keypad";

    /// <summary>What every view says when there are no bindings to show.</summary>
    public const string EmptyText = "No keys bound";

    /// <summary>
    /// The window's button for <see cref="EditCommand"/>, where it has the room for a button rather than the cells' link:
    /// the same door, with <see cref="EditKey"/> in its tooltip.
    /// </summary>
    public const string BindLabel = "+ Bind a key";

    /// <summary>No bindings.</summary>
    public static QuickBarModel Empty { get; } = new([]);

    /// <summary>Value equality over the items, which a record's list member does not give.</summary>
    public bool Equals(QuickBarModel? other) => other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
