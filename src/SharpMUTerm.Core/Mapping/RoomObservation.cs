namespace SharpMUTerm.Core.Mapping;

/// <summary>One exit as a source saw it: a direction's long name or a command, and the room it leads to when known.</summary>
public readonly record struct ObservedExit(string Key, long? To);

/// <summary>
/// "The character is in this room, and these are its exits", as a <see cref="IRoomSource"/> reports it.
/// <see cref="Id"/> is the game's own number, or null for a source that has none (the text source), in which
/// case the <see cref="MapRecorder"/> works out which room it is.
/// </summary>
public sealed record RoomObservation(long? Id, string Name, IReadOnlyList<ObservedExit> Exits)
{
    public string? Area { get; init; }

    public string? Environment { get; init; }

    /// <summary>Coordinates the server gave, when it gave any; otherwise the recorder places the room.</summary>
    public (int X, int Y, int Z)? Coordinates { get; init; }

    /// <summary>
    /// True when this only updates the exits of the room the character is already in (MUME's
    /// <c>Room.UpdateExits</c>) rather than reporting a move.
    /// </summary>
    public bool ExitsOnly { get; init; }
}

/// <summary>Something that can tell where the character is.</summary>
public interface IRoomSource : IDisposable
{
    event EventHandler<RoomObservation>? RoomEntered;
}
