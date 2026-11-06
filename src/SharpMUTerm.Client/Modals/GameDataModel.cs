using System.Globalization;
using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Client.Modals;

/// <summary>
/// The game data inspector (⌃P ▸ Show game data): every GMCP package and MSDP variable a connection holds, as it was
/// when the inspector opened. Both hosts draw this one record: a list with the selected package's payload under it.
/// </summary>
/// <param name="Owner">Whose data it is, as the header names them; empty for none.</param>
/// <param name="Rows">One row per package, in the store's order.</param>
public sealed record GameDataModel(string Owner, IReadOnlyList<GameDataRow> Rows)
{
    /// <summary>The surface's title, as both hosts word it.</summary>
    public const string Title = "Game data";

    /// <summary>What an inspector with no data says.</summary>
    public const string Empty = "nothing yet — this connection has received no GMCP or MSDP data.";

    /// <summary>The keys, as the cells' header and the window's footer say them.</summary>
    public const string Keys = "↑↓ select · Esc close";

    /// <summary>How many packages, in words.</summary>
    public string Count => Rows.Count == 1 ? "1 package" : $"{Rows.Count.ToString(CultureInfo.InvariantCulture)} packages";

    /// <summary>The store's packages, aged against <paramref name="now"/>.</summary>
    public static GameDataModel From(IReadOnlyList<GameDataEntry> entries, string owner, DateTimeOffset now) =>
        new(owner, [.. entries.Select(e => new GameDataRow(
            e.Package, Age(now - e.UpdatedAt), e.Data?.ToJsonString() ?? "(no data)", GameDataStore.Pretty(e.Data)))]);

    /// <summary>How long ago a package last changed, in the fewest characters.</summary>
    public static string Age(TimeSpan age) => age.TotalSeconds switch
    {
        < 1 => "now",
        < 60 => $"{(int)age.TotalSeconds}s ago",
        < 3600 => $"{(int)age.TotalMinutes}m ago",
        _ => $"{(int)age.TotalHours}h ago",
    };

    public bool Equals(GameDataModel? other) =>
        other is not null && Owner == other.Owner && ModelLists.Same(Rows, other.Rows);

    public override int GetHashCode() => HashCode.Combine(Owner, ModelLists.Hash(Rows));
}

/// <summary>One package of game data.</summary>
/// <param name="Package">Its name (<c>Char.Vitals</c>, <c>MSDP.HEALTH</c>).</param>
/// <param name="Age">How long ago it last changed.</param>
/// <param name="Preview">Its payload on one line.</param>
/// <param name="Payload">Its payload, indented.</param>
public sealed record GameDataRow(string Package, string Age, string Preview, string Payload);

/// <summary>The game data inspector open in a host's own controls. Choosing a row is the view's; nothing comes back.</summary>
public sealed class GameDataSession(GameDataModel model, bool isDark, Action closed)
    : ModalSession<GameDataModel>(model, isDark, closed)
{
    public override string Surface => Surfaces.SurfaceCatalog.GameData;
}
