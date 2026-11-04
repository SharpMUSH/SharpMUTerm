using System.Globalization;
using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Client.Panels;

/// <summary>Whether a person in the room is at the keyboard, as far as the game says.</summary>
public enum WhoPresence
{
    /// <summary>Active, or the game sends no idle time for them.</summary>
    Active,

    /// <summary>Idle for at least <see cref="WhoHereModel.ActiveSeconds"/>.</summary>
    Idle,
}

/// <summary>One person or creature in the room, as every view of the who-here panel draws it.</summary>
/// <param name="Name">The name a <c>look</c> uses: bounded and free of whitespace by <see cref="RoomOccupants"/>.</param>
/// <param name="Display">What the panel shows: a player's full name when the game sends one, else the name.</param>
/// <param name="State">Active or idle; a creature is always <see cref="WhoPresence.Active"/>.</param>
/// <param name="Idle">How long the player has been idle as it reads (<c>active</c>, <c>idle 4m</c>), or null when the
/// game does not say.</param>
/// <param name="IsNpc">A creature from <c>Char.Items</c> rather than a player.</param>
public sealed record WhoHereRow(string Name, string Display, WhoPresence State, string? Idle, bool IsNpc);

/// <summary>
/// The who-here panel's content for one character, with no presentation in it: the room's name and who is in it,
/// in the roster's order. The terminal draws it as rows of cells (<c>WhoHereRenderer</c>); the window draws a
/// native list over those cells from the same instance, so a row index names the same person in both.
/// </summary>
/// <param name="Known">Whether the game has said who is here at all.</param>
/// <param name="Room">The room's name, when the game sends one.</param>
/// <param name="Rows">Who is here.</param>
public sealed record WhoHereModel(bool Known, string? Room, IReadOnlyList<WhoHereRow> Rows)
{
    /// <summary>Under this many seconds idle a player reads <c>active</c>.</summary>
    public const int ActiveSeconds = 60;

    /// <summary>What every view says before the game has said who is here.</summary>
    public const string UnknownText = "The game has not said who is here.";

    /// <summary>What every view says in a room with nobody else in it.</summary>
    public const string EmptyText = "Nobody else is here.";

    /// <summary>The game has not said who is here.</summary>
    public static WhoHereModel Unknown { get; } = new(false, null, []);

    /// <summary>Whether any row carries an idle time, so a view knows whether to give the column room.</summary>
    public bool ShowsIdle => Rows.Any(r => r.Idle is not null);

    /// <summary>The model for a room name and a roster snapshot.</summary>
    public static WhoHereModel From(string? room, IReadOnlyList<RoomOccupant> occupants) =>
        new(
            true,
            string.IsNullOrWhiteSpace(room) ? null : room.Trim(),
            occupants.Select(o => new WhoHereRow(
                o.Name,
                o.Display,
                o.IsPlayer && o.IdleSeconds is >= ActiveSeconds ? WhoPresence.Idle : WhoPresence.Active,
                o.IsPlayer && o.IdleSeconds is { } seconds ? IdleText(seconds) : null,
                !o.IsPlayer)).ToList());

    /// <summary>How an idle time reads: <c>active</c>, <c>idle 4m</c>, <c>idle 2h</c>, <c>idle 3d</c>.</summary>
    public static string IdleText(int seconds) => seconds switch
    {
        < ActiveSeconds => "active",
        < 3600 => $"idle {(seconds / 60).ToString(CultureInfo.InvariantCulture)}m",
        < 86400 => $"idle {(seconds / 3600).ToString(CultureInfo.InvariantCulture)}h",
        _ => $"idle {Math.Min(99, seconds / 86400).ToString(CultureInfo.InvariantCulture)}d",
    };

    /// <summary>Value equality over the rows, which a record's list member does not give.</summary>
    public bool Equals(WhoHereModel? other) =>
        other is not null && Known == other.Known && Room == other.Room && Rows.SequenceEqual(other.Rows);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Known);
        hash.Add(Room);
        foreach (var row in Rows)
        {
            hash.Add(row);
        }

        return hash.ToHashCode();
    }
}
