using SharpMUTerm.Core.Automation;

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

    /// <summary>
    /// Whether the host reports every chord as the key and the modifiers that were pressed. A window does. A
    /// terminal does not: it writes Ctrl+letter as a control byte (losing Shift, and spelling ⌃H/⌃I/⌃J/⌃M as
    /// Backspace, Tab and Enter), Ctrl+digit as whatever byte the ASCII table gives it, Ctrl+Tab as a bare Tab, and
    /// its emulator and the framework's parser spend chords of their own (kitty's ⌃⇧←/→, the parser's ⌥O). F4 and
    /// the quick bar read this through <see cref="MacroKeys.Verdict"/>, so a binding is drawn live on the host
    /// where it fires and dead where it never arrives.
    /// </summary>
    public required bool ReportsChordsWhole { get; init; }

    /// <summary>
    /// Whether a numpad digit reaches the client as itself (<c>Num7</c>) rather than as the main row's <c>7</c>. A
    /// window reads the physical key; a terminal is never put in application-keypad mode, so its numpad types digits
    /// and a <c>Num</c> binding never fires there. The numpad is the combat player's walking keys, which is why F4
    /// says which it is per host.
    /// </summary>
    public required bool DeliversNumpad { get; init; }

    public bool Reserves(ConsoleKeyInfo key) => Reserved.Any(r => r.Chord.Matches(key));

    /// <summary>The reserved chord a descriptor names, or null.</summary>
    internal ReservedChord? ReservedFor(MacroKeyParts parts) =>
        Reserved.FirstOrDefault(r => Same(r.Chord, parts));

    /// <summary>Whether a descriptor names the newline chord.</summary>
    internal bool IsNewline(MacroKeyParts parts) => Same(Newline, parts);

    private static bool Same(HostChord chord, MacroKeyParts parts) =>
        MacroKeys.NameOf(chord.Key) is { } name
        && string.Equals(name, parts.Key, StringComparison.OrdinalIgnoreCase)
        && chord.Modifiers.HasFlag(ConsoleModifiers.Control) == parts.Ctrl
        && chord.Modifiers.HasFlag(ConsoleModifiers.Alt) == parts.Alt
        && chord.Modifiers.HasFlag(ConsoleModifiers.Shift) == parts.Shift;

    /// <summary>
    /// A terminal: ⌥⏎ is the newline, reassembled from the Escape and Enter it arrives as
    /// (<c>SharpMUTermApp.TryAltEnter</c>). ⇧⏎ and ⌃⏎ arrive as a bare ⏎ and are named nowhere. The emulator's
    /// own bindings are its configuration, not ours, and are reported by <see cref="MacroKeys.Verdict"/>.
    /// </summary>
    public static HostKeys Terminal { get; } = new()
    {
        Newline = new HostChord(ConsoleKey.Enter, ConsoleModifiers.Alt, "Alt+⏎", "Alt+Enter"),
        Reserved = [],
        ReportsChordsWhole = false,
        DeliversNumpad = false,
    };
}
