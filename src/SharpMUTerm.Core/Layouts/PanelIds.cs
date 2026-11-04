namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// The panels a layout can place, by id. An id is a plain string so a layout written by a newer client (or by
/// hand) still loads: a placement naming an id outside <see cref="Known"/> is kept in the layout and skipped by
/// everything that reads it (<see cref="LayoutDefinition.KnownPlacements"/>).
/// </summary>
public static class PanelIds
{
    /// <summary>The output pane: the game's text.</summary>
    public const string Output = "output";

    /// <summary>The docked composer, a multi-row editor for posts.</summary>
    public const string Composer = "composer";

    /// <summary>Who is in the room with the character (GMCP <c>Room.Players</c>).</summary>
    public const string WhoHere = "who-here";

    /// <summary>Health, mana and the other meters a game sends.</summary>
    public const string Vitals = "vitals";

    /// <summary>The map drawn from the rooms the mapper recorded.</summary>
    public const string Map = "map";

    /// <summary>The capture windows trigger rules send lines to (channels, pages, tells).</summary>
    public const string Chat = "chat";

    /// <summary>A row of buttons for the commands a player uses most.</summary>
    public const string QuickBar = "quick-bar";

    /// <summary>Every GMCP, MSDP and MXP value the session holds.</summary>
    public const string GameData = "game-data";

    /// <summary>Every id this client knows how to draw.</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Output, Composer, WhoHere, Vitals, Map, Chat, QuickBar, GameData,
    };

    /// <summary>Whether <paramref name="panelId"/> names a panel this client can draw.</summary>
    public static bool IsKnown(string? panelId) => panelId is not null && Known.Contains(panelId);
}
