using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Palette;

namespace SharpMUTerm.Client.Chrome;

/// <summary>
/// A state the focused pane (or the keyboard) is in that changes what the keys do, and so changes which keys the
/// status row names. Zellij's idea: the row that otherwise teaches the pane chords teaches the way out of whatever
/// you are in. Listed in priority order, lowest first; when two hold at once the higher one is named.
/// </summary>
public enum StatusMode
{
    /// <summary>Nothing special: the row names the pane, tab and command-line chords (<c>FocusHints</c>).</summary>
    None,

    /// <summary>The focused pane is scrolled back with the split off: the history fills the pane.</summary>
    ScrolledBack,

    /// <summary>The focused pane is scrolled back and split, the live tail under a band.</summary>
    SplitScrollback,

    /// <summary>The focused pane is frozen (⌥F).</summary>
    Frozen,

    /// <summary>The focused window carries the ⌃F search's result bar.</summary>
    SearchResult,

    /// <summary>A docked composer holds the keyboard.</summary>
    Composer,
}

/// <summary>One key a mode answers, and what it does: <c>⌥F thaws</c>.</summary>
/// <param name="Key">The chord, as the ⌃P catalog writes keys.</param>
/// <param name="Does">What it does, as a verb the row reads after the key.</param>
public sealed record ModeHint(KeyHint Key, string Does)
{
    /// <summary>The hint as the row writes it on the host whose spellings are <paramref name="keys"/>.</summary>
    public string Text(HostKeyGlyphs keys) => $"{KeyGlyphs.Normalize(Key.Resolve(keys))} {Does}";
}

/// <summary>
/// The keys the status row names while a <see cref="StatusMode"/> holds. Pure, so both hosts — and the tests — read
/// one list, and a key is resolved through the host's own spellings (<see cref="HostKeyGlyphs"/>) and written in the
/// palette's one vocabulary (<see cref="KeyGlyphs"/>), never typed out a second time by whoever draws the row.
/// <para>
/// <b>Most important first, and the row takes the longest prefix that fits</b>: a narrow terminal keeps the way out
/// and loses the extras, rather than losing both because the pair no longer fitted — <c>FocusHints</c>' rule.
/// </para>
/// </summary>
public static class ModeHints
{
    /// <summary>What <paramref name="mode"/>'s keys are, most important first; empty for <see cref="StatusMode.None"/>.</summary>
    public static IReadOnlyList<ModeHint> For(StatusMode mode) => mode switch
    {
        // The scrollback segment beside it already says ⌃End live, so this row adds what that one does not.
        StatusMode.ScrolledBack => [new("⌥F", "freezes"), new("PgDn", "pages down")],

        // Bare End is claimed only while a split is open (and the caret is at the end of the line, where it rests).
        StatusMode.SplitScrollback => [new("End", "returns"), new("⌥F", "freezes")],
        StatusMode.Frozen => [new("⌥F", "thaws")],
        StatusMode.SearchResult => [new("⌥G", "next hit"), new("⌃F", "searches again")],
        StatusMode.Composer => [new("⌃S", "sends"), new("Esc", "releases"), new("F1", "full screen")],
        _ => [],
    };

    /// <summary>
    /// The row's candidates for <paramref name="mode"/> on the host whose spellings are <paramref name="keys"/>: every
    /// hint, then one fewer, down to the first alone. Joined with the row's own <c> · </c>.
    /// </summary>
    public static IReadOnlyList<string> Candidates(StatusMode mode, HostKeyGlyphs keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var hints = For(mode).Select(h => h.Text(keys)).ToList();
        var candidates = new List<string>(hints.Count);
        for (var n = hints.Count; n > 0; n--)
        {
            candidates.Add(string.Join(" · ", hints.Take(n)));
        }

        return candidates;
    }
}
