using SharpMUTerm.Core.Automation;

namespace SharpMUTerm.Tui;

/// <summary>How a binding collides with something else that wants its chord.</summary>
internal enum KeyClashKind
{
    /// <summary>A global shortcut of the client's holds the chord, and runs first: the binding never fires.</summary>
    ClientWins,

    /// <summary>Another binding earlier in the list holds the chord: this one never fires.</summary>
    Shadowed,

    /// <summary>The binding runs, and the client's own action on that chord does not (a warning, not a fault).</summary>
    BindingWins,

    /// <summary>The binding runs, and a later binding on the same chord never does.</summary>
    Shadows,
}

/// <summary>One clash: the binding, what kind, and the sentence F4 prints for it.</summary>
/// <param name="Macro">The binding.</param>
/// <param name="Kind">Which of the four collisions it is.</param>
/// <param name="Reason">What happens, naming which side wins.</param>
internal readonly record struct KeyClash(Macro Macro, KeyClashKind Kind, string Reason)
{
    /// <summary>Whether the binding still runs: a warning rather than a fault.</summary>
    internal bool BindingRuns => Kind is KeyClashKind.BindingWins or KeyClashKind.Shadows;
}

/// <summary>
/// Which F4 bindings share a chord with the client or with one another, and which side wins. The client's
/// side is read from <see cref="MacroKeys"/> and nowhere else, so this cannot disagree with the dispatcher:
/// <see cref="MacroKeys.ClaimedBy"/> is a global shortcut (tried before any window, so the client wins and
/// <see cref="MacroKeys.Verdict"/> already reports the binding as taken), and <see cref="MacroKeys.YieldedBy"/>
/// is an action the main window takes only after <c>DispatchMacro</c> has declined the key (so the binding
/// wins). Two bindings on one chord resolve as <see cref="MacroEngine.Resolve"/> does: the first in the list
/// answers, even when it is disabled, and the second never runs.
/// <para>
/// The bindings are compared across every set, which is F4's own stricter reading (<c>AlreadyBound</c>): a
/// session flattens its character's sets into one engine, so two sets ever enabled together collide.
/// </para>
/// </summary>
internal static class KeyClashes
{
    /// <summary>Every clash among <paramref name="macros"/>, in list order.</summary>
    internal static IReadOnlyList<KeyClash> Find(IReadOnlyList<Macro> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);
        var clashes = new List<KeyClash>();
        foreach (var macro in macros)
        {
            if (For(macro, macros) is { } clash)
            {
                clashes.Add(clash);
            }
        }

        return clashes;
    }

    /// <summary>
    /// How many clashes a reader needs to look at: every binding that never fires because of another claim
    /// on its chord, and every binding that hides one of the client's own keys. A binding that shadows a later
    /// one is not counted again — the later one already is.
    /// </summary>
    internal static int Count(IReadOnlyList<Macro> macros) =>
        Find(macros).Count(c => c.Kind != KeyClashKind.Shadows);

    /// <summary>The clash <paramref name="macro"/> is part of, or null.</summary>
    internal static KeyClash? For(Macro macro, IReadOnlyList<Macro> macros)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(macros);
        if (MacroKey.Canonicalise(macro.Key) is not { } chord || !Live(macro))
        {
            return null;
        }

        if (MacroKeys.ClaimedBy(chord) is { } claimed)
        {
            return new KeyClash(macro, KeyClashKind.ClientWins, $"the client wins: {chord} {claimed}");
        }

        var holders = macros
            .Where(m => string.Equals(MacroKey.Canonicalise(m.Key), chord, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var at = holders.FindIndex(m => ReferenceEquals(m, macro));
        if (at > 0)
        {
            return new KeyClash(macro, KeyClashKind.Shadowed, $"{Name(holders[0])} holds {chord} first; this never runs");
        }

        if (!MacroKeys.Verdict(chord).Fires)
        {
            return null;
        }

        if (MacroKeys.YieldedBy(chord) is { } yielded)
        {
            return new KeyClash(macro, KeyClashKind.BindingWins, $"this wins: {chord} no longer {yielded}");
        }

        return holders.Count > 1
            ? new KeyClash(macro, KeyClashKind.Shadows, $"this wins: {Name(holders[1])} on {chord} never runs")
            : null;
    }

    // A disabled binding, or one with nothing to send or call, leaves its key alone (DispatchMacro), so it
    // clashes with nothing itself — though as the first holder of a chord it still blocks a later one.
    private static bool Live(Macro macro) =>
        macro.Enabled && (macro.Command.Length > 0 || !string.IsNullOrEmpty(macro.ScriptCallback));

    private static string Name(Macro macro) =>
        string.IsNullOrWhiteSpace(macro.Name) ? $"→ {macro.Command}" : macro.Name;
}
