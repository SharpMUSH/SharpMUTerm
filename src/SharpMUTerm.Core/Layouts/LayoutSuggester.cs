using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Telnet.Mssp;

namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// What a connection has shown the client, as plain values, so <see cref="LayoutSuggester"/> can be tested
/// without a session. <see cref="From"/> reads it off the live stores.
/// </summary>
/// <param name="GameDataPackages">
/// The package names <see cref="GameDataStore"/> holds: GMCP packages by name (<c>Char.Vitals</c>), MSDP variables
/// as <c>MSDP.NAME</c>.
/// </param>
/// <param name="MsspCodebase">MSSP <c>CODEBASE</c>, or null when the server sent none.</param>
/// <param name="MsspFamily">MSSP <c>FAMILY</c>, or null when the server sent none.</param>
public sealed record LayoutSignals(
    IReadOnlyCollection<string> GameDataPackages,
    string? MsspCodebase = null,
    string? MsspFamily = null)
{
    /// <summary>Nothing seen yet.</summary>
    public static LayoutSignals None { get; } = new([]);

    /// <summary>Reads the signals off a session's game data and its server's MSSP report; either may be null.</summary>
    public static LayoutSignals From(GameDataStore? gameData, MsspData? mssp) => new(
        gameData?.Entries.Select(e => e.Package).ToArray() ?? [],
        mssp?.Codebase,
        mssp?.Family);
}

/// <summary>Why <see cref="LayoutSuggester"/> suggested what it did — what the offer can say to the player.</summary>
public enum LayoutSuggestionReason
{
    /// <summary>The game sends vitals (GMCP <c>Char.Vitals</c>, MSDP <c>HEALTH</c>…): a game with fighting in it.</summary>
    Vitals,

    /// <summary>The server's MSSP names a MUSH-family codebase (PennMUSH, TinyMUX, a MUCK, a MOO…).</summary>
    RoleplayServer,

    /// <summary>Nothing pointed either way.</summary>
    Default,
}

/// <summary>A suggested layout and the reason for it.</summary>
public sealed record LayoutSuggestion(string LayoutId, LayoutSuggestionReason Reason);

/// <summary>
/// Suggests a built-in layout for a world from what its connection has shown: <see cref="BuiltInLayouts.Combat"/>
/// once the game sends vitals, <see cref="BuiltInLayouts.Roleplay"/> otherwise — with a reason that says whether
/// the server's MSSP named a roleplay-leaning codebase or nothing pointed either way. Vitals win over the codebase:
/// a MUSH that sends <c>Char.Vitals</c> has a combat system someone wanted shown.
/// </summary>
public static class LayoutSuggester
{
    /// <summary>The game-data packages that mean the game sends vitals (compared ignoring case).</summary>
    public static IReadOnlySet<string> VitalsPackages { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Char.Vitals",
        $"{GameDataStore.MsdpPrefix}.HEALTH",
        $"{GameDataStore.MsdpPrefix}.HEALTH_MAX",
        $"{GameDataStore.MsdpPrefix}.MANA",
        $"{GameDataStore.MsdpPrefix}.MOVEMENT",
    };

    /// <summary>
    /// Codebase name endings that lean towards roleplay. A word of the codebase ending in one counts, so
    /// <c>PennMUSH 1.8.8</c>, <c>TinyMUX 2.13</c>, <c>RhostMUSH</c>, <c>ProtoMUCK</c> and <c>LambdaMOO</c> all do.
    /// </summary>
    private static readonly string[] RoleplayEndings = ["MUSH", "MUX", "MUCK", "MOO"];

    /// <summary>The MSSP <c>FAMILY</c> values that lean towards roleplay.</summary>
    private static readonly string[] RoleplayFamilies = ["TinyMUD"];

    /// <summary>Suggests a layout for what <paramref name="signals"/> says the connection has seen.</summary>
    public static LayoutSuggestion Suggest(LayoutSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (signals.GameDataPackages.Any(VitalsPackages.Contains))
        {
            return new LayoutSuggestion(BuiltInLayouts.CombatId, LayoutSuggestionReason.Vitals);
        }

        if (IsRoleplayCodebase(signals.MsspCodebase) || IsRoleplayFamily(signals.MsspFamily))
        {
            return new LayoutSuggestion(BuiltInLayouts.RoleplayId, LayoutSuggestionReason.RoleplayServer);
        }

        return new LayoutSuggestion(BuiltInLayouts.RoleplayId, LayoutSuggestionReason.Default);
    }

    /// <summary>
    /// Whether an MSSP <c>CODEBASE</c> names a MUSH-family server. Read word by word (runs of letters), so a version
    /// number or punctuation does not matter and a word that merely contains <c>MOO</c> mid-way does not count.
    /// </summary>
    public static bool IsRoleplayCodebase(string? codebase)
    {
        if (string.IsNullOrWhiteSpace(codebase))
        {
            return false;
        }

        foreach (var word in Words(codebase))
        {
            if (RoleplayEndings.Any(e => word.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRoleplayFamily(string? family) =>
        family is not null && RoleplayFamilies.Any(f => string.Equals(f, family.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Words(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var letter = i < text.Length && char.IsAsciiLetter(text[i]);
            if (letter && start < 0)
            {
                start = i;
            }
            else if (!letter && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }
}
