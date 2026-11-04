using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// Whether a panel has anything to show for one session yet. A layout says where a panel goes; this says whether
/// it is drawn, so a world that never sends a map never gets an empty map pane.
/// </summary>
public interface IPanelAvailability
{
    /// <summary>Whether <paramref name="panelId"/> has data. False for an id outside <see cref="PanelIds.Known"/>.</summary>
    bool HasData(string panelId);
}

/// <summary>
/// The facts about one session that decide which panels have data, as plain values, and the rule that reads them.
/// A host builds one per session (<see cref="From"/>) whenever one of the inputs changes; the rule is pure.
/// </summary>
/// <param name="RoomsRecorded">Rooms in the session's map (<c>WorldMap.Rooms.Count</c>).</param>
/// <param name="MeterValueArrived">Whether any status meter has read a value (<see cref="StatusMeter.Read"/>).</param>
/// <param name="WhoHereKnown">Whether the game has said who is in the room (GMCP <c>Room.Players</c>).</param>
/// <param name="SpawnWindows">Capture windows the session's trigger rules have opened.</param>
/// <param name="GameDataEntries">Values the session's <see cref="GameDataStore"/> holds.</param>
public sealed record PanelAvailability(
    int RoomsRecorded = 0,
    bool MeterValueArrived = false,
    bool WhoHereKnown = false,
    int SpawnWindows = 0,
    int GameDataEntries = 0) : IPanelAvailability
{
    /// <summary>A session that has shown nothing yet: only the panels that need no data.</summary>
    public static PanelAvailability Nothing { get; } = new();

    /// <summary>The GMCP packages that say who is in the room.</summary>
    public static IReadOnlyList<string> WhoHerePackages { get; } = ["Room.Players", "Room.AddPlayer"];

    /// <inheritdoc />
    public bool HasData(string panelId) => panelId switch
    {
        PanelIds.Output or PanelIds.Composer or PanelIds.QuickBar => true,
        PanelIds.Map => RoomsRecorded > 0,
        PanelIds.Vitals => MeterValueArrived,
        PanelIds.WhoHere => WhoHereKnown,
        PanelIds.Chat => SpawnWindows > 0,
        PanelIds.GameData => GameDataEntries > 0,
        _ => false,
    };

    /// <summary>
    /// Reads the facts off a session's stores. <paramref name="meters"/> defaults to
    /// <see cref="StatusMeter.Defaults"/>; the map and the capture windows are counted by the caller, because they
    /// live in the host.
    /// </summary>
    public static PanelAvailability From(
        GameDataStore? gameData,
        int roomsRecorded,
        int spawnWindows,
        IEnumerable<StatusMeter>? meters = null)
    {
        var meterArrived = gameData is not null
            && (meters ?? StatusMeter.Defaults()).Any(m => StatusMeter.Read(gameData, m.Value) is not null);
        // By package name, not value: an empty Room.Players (nobody else here) is still knowing who is here.
        var whoHere = gameData is not null
            && gameData.Entries.Any(e => WhoHerePackages.Contains(e.Package, StringComparer.OrdinalIgnoreCase));
        return new PanelAvailability(roomsRecorded, meterArrived, whoHere, spawnWindows, gameData?.Count ?? 0);
    }
}
