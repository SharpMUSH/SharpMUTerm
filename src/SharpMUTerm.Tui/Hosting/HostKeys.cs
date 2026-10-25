namespace SharpMUTerm.Tui;

/// <summary>A chord as a host spells it: the key, its exact modifiers, and the two ways a surface names it.</summary>
/// <param name="Glyph">How an in-app surface names it (<c>Alt+⏎</c>).</param>
/// <param name="Text">How <c>--help</c> names it, in ASCII (<c>Alt+Enter</c>): that page may land in a pipe.</param>
internal sealed record HostChord(ConsoleKey Key, ConsoleModifiers Modifiers, string Glyph, string Text)
{
    public bool Matches(ConsoleKeyInfo key) => key.Key == Key && key.Modifiers == Modifiers;
}

/// <summary>A chord the host takes before the client sees it, and what the host does with it.</summary>
internal sealed record ReservedChord(HostChord Chord, string Action);

/// <summary>
/// The keys that differ between hosts. Every surface that names one of them — the ⌃P catalog, <c>--help</c>,
/// F4 — reads it from here, so a host that delivers a different newline chord, or keeps a chord for itself,
/// cannot leave a surface naming the other host's key. <c>AdvertisedKeyHonestyTests</c> runs once per host.
/// </summary>
internal sealed record HostKeys
{
    /// <summary>The chord that inserts a newline in the command line on this host.</summary>
    public required HostChord Newline { get; init; }

    /// <summary>Chords the host acts on itself; the client never receives them, so no surface may offer them.</summary>
    public required IReadOnlyList<ReservedChord> Reserved { get; init; }

    public bool Reserves(ConsoleKeyInfo key) => Reserved.Any(r => r.Chord.Matches(key));

    /// <summary>
    /// A terminal: ⌥⏎ is the newline, reassembled from the Escape and Enter it arrives as
    /// (<c>SharpMUTermApp.TryAltEnter</c>). ⇧⏎ and ⌃⏎ arrive as a bare ⏎ and are named nowhere. The emulator's
    /// own bindings are its configuration, not ours, and are reported by <see cref="MacroKeys.Verdict"/>.
    /// </summary>
    public static HostKeys Terminal { get; } = new()
    {
        Newline = new HostChord(ConsoleKey.Enter, ConsoleModifiers.Alt, "Alt+⏎", "Alt+Enter"),
        Reserved = [],
    };
}
