namespace SharpMUTerm.Tui;

/// <summary>
/// One key a footer names: the key, what it does, a shorter way to say it, and when it is given up.
/// </summary>
/// <param name="Key">The key in glyphs (<c>⌃S</c>, <c>Esc</c>).</param>
/// <param name="Label">What it does, in full.</param>
/// <param name="Short">A shorter label for a narrow row, or null to use <paramref name="Label"/> with any parenthetical cut.</param>
/// <param name="Drop">
/// When this key leaves a row that cannot hold every key: lowest first. <see cref="Keep"/> is never dropped — the key
/// that closes the surface, and the one that accepts it.
/// </param>
internal readonly record struct LadderKey(string Key, string Label, string? Short = null, int Drop = LadderKey.Keep)
{
    /// <summary>A key a row never gives up.</summary>
    public const int Keep = int.MaxValue;

    /// <summary>The label at a rung: 0 full, 1 any parenthetical cut, 2 the short label.</summary>
    internal string LabelAt(int rung) => rung switch
    {
        0 => Label,
        1 => KeyLadder.CutParenthetical(Label),
        _ => Short ?? KeyLadder.CutParenthetical(Label),
    };
}

/// <summary>
/// A footer's key row fitted to one row, never wrapped (the D1 lesson, and the reason a modal's way out cannot be
/// scrolled off it). Rungs, in order: every key in full; parentheticals cut (<c>close (keeps the draft)</c> becomes
/// <c>close</c>); short labels; then keys given up lowest <see cref="LadderKey.Drop"/> first, in reading order otherwise
/// — the <see cref="ModeHints"/>/<c>FocusHints</c> rule that reading order and drop order are separate. A key marked
/// <see cref="LadderKey.Keep"/> is never dropped; if even those do not fit, the row is cut at the edge.
/// </summary>
internal static class KeyLadder
{
    /// <summary>The cells between two keys: <c> · </c>.</summary>
    internal const int Gap = 3;

    /// <summary>
    /// The row for <paramref name="keys"/> in at most <paramref name="width"/> cells (0: no limit, every key in full).
    /// <paramref name="key"/> and <paramref name="label"/> style the two halves of a key; <paramref name="separator"/> is
    /// the markup between keys (three visible cells). <paramref name="more"/>, when given, is appended once anything was
    /// dropped — the key that lists the rest.
    /// </summary>
    internal static string Fit(
        IReadOnlyList<LadderKey> keys,
        int width,
        Func<string, string> key,
        Func<string, string> label,
        string separator,
        LadderKey? more = null) =>
        Join(Choose(keys, width, more), key, label, separator);

    /// <summary>The keys and labels <see cref="Fit"/> draws, for a caller that styles them itself or a test.</summary>
    internal static IReadOnlyList<(string Key, string Label)> Choose(IReadOnlyList<LadderKey> keys, int width, LadderKey? more = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (width <= 0)
        {
            return keys.Select(k => (k.Key, k.Label)).ToList();
        }

        for (var rung = 0; rung <= 2; rung++)
        {
            var all = keys.Select(k => (k.Key, k.LabelAt(rung))).ToList();
            if (Length(all) <= width)
            {
                return all;
            }
        }

        // Give keys up, lowest Drop first; ties go from the end of the row, so the row keeps its first words longest.
        var order = keys
            .Select((k, i) => (k, i))
            .Where(p => p.k.Drop != LadderKey.Keep)
            .OrderBy(p => p.k.Drop)
            .ThenByDescending(p => p.i)
            .Select(p => p.i)
            .ToList();
        var dropped = new HashSet<int>();
        foreach (var index in order)
        {
            dropped.Add(index);
            var kept = keys.Where((_, i) => !dropped.Contains(i)).Select(k => (k.Key, k.LabelAt(2))).ToList();
            if (more is { } extra)
            {
                kept.Add((extra.Key, extra.LabelAt(2)));
            }

            if (Length(kept) <= width)
            {
                return kept;
            }
        }

        var last = keys.Where((_, i) => !dropped.Contains(i)).Select(k => (k.Key, k.LabelAt(2))).ToList();
        if (more is { } tail)
        {
            last.Add((tail.Key, tail.LabelAt(2)));
        }

        return last;
    }

    /// <summary>The visible cells a row of <paramref name="keys"/> takes.</summary>
    internal static int Length(IReadOnlyList<(string Key, string Label)> keys) =>
        keys.Count == 0 ? 0 : keys.Sum(k => k.Key.Length + (k.Label.Length == 0 ? 0 : (k.Key.Length == 0 ? 0 : 1) + k.Label.Length)) + (Gap * (keys.Count - 1));

    /// <summary><paramref name="text"/> with a trailing <c>(…)</c> removed: <c>close (keeps the draft)</c> → <c>close</c>.</summary>
    internal static string CutParenthetical(string text)
    {
        var open = text.IndexOf(" (", StringComparison.Ordinal);
        return open > 0 && text.EndsWith(')') ? text[..open] : text;
    }

    private static string Join(IReadOnlyList<(string Key, string Label)> keys, Func<string, string> key, Func<string, string> label, string separator) =>
        string.Join(separator, keys.Select(k =>
            k.Key.Length == 0 ? label(k.Label)
            : k.Label.Length == 0 ? key(k.Key)
            : key(k.Key) + label(" " + k.Label)));
}
