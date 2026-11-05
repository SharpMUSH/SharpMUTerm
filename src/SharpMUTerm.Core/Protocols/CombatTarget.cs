using System.Globalization;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// What the character is fighting, as the game reports it: a name and, when the game says, how much of its health
/// is left (0–1). Read from <see cref="GameDataStore"/> by the vitals strip.
/// </summary>
/// <param name="Name">The target's name as the game spells it.</param>
/// <param name="Share">Its health left, 0–1, or null when the game names a target and not its health.</param>
public sealed record CombatTarget(string Name, double? Share)
{
    /// <summary>
    /// Where a target's name is, in the order tried: Aardwolf's <c>char.status</c> <c>enemy</c>, IRE's
    /// <c>IRE.Target.Info</c> <c>short_desc</c>, and the MSDP convention's <c>OPPONENT_NAME</c>.
    /// </summary>
    public const string NamePaths = "Char.Status.enemy|IRE.Target.Info.short_desc|MSDP.OPPONENT_NAME";

    /// <summary>
    /// Where its health is: Aardwolf's <c>enemypct</c> (0–100), IRE's <c>hpperc</c> (<c>"40%"</c>), and MSDP's
    /// <c>OPPONENT_HEALTH</c> — out of <c>OPPONENT_HEALTH_MAX</c> when the game sends one, else a percentage.
    /// </summary>
    public const string SharePaths = "Char.Status.enemypct|IRE.Target.Info.hpperc|MSDP.OPPONENT_HEALTH";

    /// <summary>The longest name kept; a longer one is cut, because the name arrives from the wire.</summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// The target the store describes, or null when it names none. An empty name is no target: Aardwolf sends
    /// <c>"enemy": ""</c> once a fight is over.
    /// </summary>
    public static CombatTarget? Read(GameDataStore data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var name = StatusMeter.Read(data, NamePaths)?.Trim();
        if (string.IsNullOrEmpty(name) || name.All(char.IsControl))
        {
            return null;
        }

        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength];
        }

        return new CombatTarget(name, ShareOf(data));
    }

    private static double? ShareOf(GameDataStore data)
    {
        foreach (var path in StatusMeter.Alternatives(SharePaths))
        {
            if (data.GetText(path) is not { } text)
            {
                continue;
            }

            if (!double.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value))
            {
                return null;
            }

            if (path.StartsWith(GameDataStore.MsdpPrefix + ".", StringComparison.OrdinalIgnoreCase)
                && data.GetNumber("MSDP.OPPONENT_HEALTH_MAX") is { } max and > 0)
            {
                return Math.Clamp(value / max, 0, 1);
            }

            return Math.Clamp(value / 100, 0, 1);
        }

        return null;
    }
}
