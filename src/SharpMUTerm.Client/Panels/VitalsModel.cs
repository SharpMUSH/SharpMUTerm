using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Client.Panels;

/// <summary>
/// One meter of the vitals panel, as every view of it draws it: the terminal's cell strip and the window's native
/// gauges are both drawn from this, so the two cannot disagree about a value or about what "low" is.
/// </summary>
/// <param name="Label">The label the player gave the meter on F7 (<c>hp</c>).</param>
/// <param name="Value">The value as the game sent it.</param>
/// <param name="Max">The maximum as the game sent it, or null when it has not sent one.</param>
/// <param name="HasMax">Whether the meter is configured with a maximum at all, sent yet or not.</param>
/// <param name="Share">Value over maximum, when both are numbers and the maximum is positive.</param>
/// <param name="Low">Whether <paramref name="Share"/> is under <see cref="VitalsModel.LowShare"/>.</param>
public sealed record VitalsMeter(string Label, string Value, string? Max, bool HasMax, double? Share, bool Low)
{
    /// <summary>
    /// The label spelt out where a view has the room: <c>hp</c> reads as <c>Health</c>, <c>mp</c> as <c>Mana</c>.
    /// Only the abbreviations every MUD shares; anything else is the player's own label, unchanged.
    /// </summary>
    public string Title => VitalsModel.TitleFor(Label);
}

/// <summary>What the character is fighting, when the game says.</summary>
/// <param name="Name">The target's name, bounded by <see cref="CombatTarget.MaxNameLength"/>.</param>
/// <param name="Share">Its health as a share of its maximum, when the game sends one.</param>
public sealed record VitalsTarget(string Name, double? Share)
{
    /// <summary>Whether the target is below <see cref="VitalsModel.LowShare"/>.</summary>
    public bool Low => Share < VitalsModel.LowShare;
}

/// <summary>
/// The vitals panel's content for one character, with no presentation in it: the meters that have a value, in
/// their configured order, and the target. The terminal draws it as a row of cells
/// (<c>VitalsStripRenderer</c>); the window draws native gauges over that row from the same instance.
/// </summary>
public sealed record VitalsModel(IReadOnlyList<VitalsMeter> Meters, VitalsTarget? Target)
{
    /// <summary>The share under which a meter is low. One number for every view, the status row's included.</summary>
    public const double LowShare = 0.25;

    /// <summary>No meter and no target: nothing for any view to draw.</summary>
    public static VitalsModel Empty { get; } = new([], null);

    /// <summary>Whether there is nothing to draw.</summary>
    public bool IsEmpty => Meters.Count == 0 && Target is null;

    /// <summary>The model for a Core target, or null.</summary>
    public static VitalsTarget? TargetFrom(CombatTarget? target) => target is null ? null : new VitalsTarget(target.Name, target.Share);

    /// <summary>Value equality over the meters, which a record's list member does not give.</summary>
    public bool Equals(VitalsModel? other) =>
        other is not null && Equals(Target, other.Target) && Meters.SequenceEqual(other.Meters);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Target);
        foreach (var meter in Meters)
        {
            hash.Add(meter);
        }

        return hash.ToHashCode();
    }

    /// <summary>See <see cref="VitalsMeter.Title"/>.</summary>
    public static string TitleFor(string label) => label.Trim().ToLowerInvariant() switch
    {
        "hp" or "health" => "Health",
        "mp" or "sp" or "mana" => "Mana",
        "mv" or "moves" or "move" or "movement" => "Movement",
        "ep" or "en" or "endurance" => "Endurance",
        "wp" or "willpower" => "Willpower",
        "st" or "sta" or "stamina" => "Stamina",
        "xp" or "exp" => "Experience",
        _ => label,
    };
}
