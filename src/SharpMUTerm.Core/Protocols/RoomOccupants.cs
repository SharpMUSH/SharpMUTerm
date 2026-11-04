using System.Globalization;
using System.Text.Json;

namespace SharpMUTerm.Core.Protocols;

/// <summary>One person or creature in the room with the character.</summary>
/// <param name="Name">What a command names them by (<c>look Name</c>): a player's <c>name</c>, a creature's own.</param>
/// <param name="Display">What the panel shows: a player's <c>fullname</c> when the game sends one, else the name.</param>
/// <param name="IsPlayer">A player, from <c>Room.Players</c>; false for a creature from <c>Char.Items</c>.</param>
/// <param name="IdleSeconds">How long the player has been idle, when the game says (no standard field does).</param>
/// <param name="Id">A creature's item id, which is how <c>Char.Items.Remove</c> names it; null for a player.</param>
public sealed record RoomOccupant(string Name, string Display, bool IsPlayer, int? IdleSeconds = null, string? Id = null);

/// <summary>
/// Who is in the room with one character, kept from the GMCP messages that say so: <c>Room.Players</c> (the whole
/// list), <c>Room.AddPlayer</c> and <c>Room.RemovePlayer</c> (one each), and the room's creatures from
/// <c>Char.Items.List</c>/<c>Add</c>/<c>Remove</c> with <c>location: "room"</c> and an <c>m</c> attribute — the
/// IRE and Aardwolf conventions. Read by the who-here panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a roster rather than a read of <see cref="GameDataStore"/></b>, because the store keeps the
/// <em>latest</em> message per package: <c>Room.AddPlayer</c> there is whoever arrived last, and a
/// <c>Room.RemovePlayer</c> is a name with nothing to subtract it from.
/// </para>
/// <para>
/// <b>It is bounded at the door</b> — <see cref="MaxOccupants"/> people, names of <see cref="MaxNameLength"/> — and a
/// name with a control character or (for a player, whose name is a command argument) any white space is refused.
/// Writes come from the telnet read loop and reads from the UI thread; one lock covers both.
/// </para>
/// </remarks>
public sealed class RoomOccupants
{
    /// <summary>The most people held; more are dropped.</summary>
    public const int MaxOccupants = 200;

    /// <summary>The longest name or display name kept.</summary>
    public const int MaxNameLength = 64;

    private readonly Lock _gate = new();
    private readonly List<RoomOccupant> _players = [];
    private readonly List<RoomOccupant> _creatures = [];
    private bool _known;

    /// <summary>Raised after the roster changed. On the writer's thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Whether the game has said who is here at all — a <c>Room.Players</c> or <c>Room.AddPlayer</c> arrived. An
    /// empty room the game reported is known; a game that never reports is not.
    /// </summary>
    public bool Known
    {
        get
        {
            lock (_gate)
            {
                return _known;
            }
        }
    }

    /// <summary>Players in the order the game listed them, then creatures.</summary>
    public IReadOnlyList<RoomOccupant> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return [.. _players, .. _creatures];
            }
        }
    }

    /// <summary>
    /// Takes one GMCP message. Packages this roster does not read, and malformed bodies, are ignored. Returns
    /// whether the roster changed.
    /// </summary>
    public bool Apply(string package, string? json)
    {
        if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(json) || json.Length > GameDataStore.MaxPayloadBytes)
        {
            return false;
        }

        var name = package.Trim().ToLowerInvariant();
        if (name is not ("room.players" or "room.addplayer" or "room.removeplayer"
            or "char.items.list" or "char.items.add" or "char.items.remove"))
        {
            return false;
        }

        bool changed;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            lock (_gate)
            {
                changed = name switch
                {
                    "room.players" => ReplacePlayers(root),
                    "room.addplayer" => AddPlayer(root),
                    "room.removeplayer" => RemovePlayer(root),
                    "char.items.list" => ReplaceCreatures(root),
                    "char.items.add" => AddCreature(root),
                    _ => RemoveCreature(root),
                };
            }
        }
        catch (JsonException)
        {
            return false;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    /// <summary>Forgets everyone, as a new connection does.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _players.Clear();
            _creatures.Clear();
            _known = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool ReplacePlayers(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        _known = true;
        _players.Clear();
        foreach (var item in root.EnumerateArray())
        {
            if (Player(item) is { } player && Room() && !_players.Exists(p => SameName(p, player.Name)))
            {
                _players.Add(player);
            }
        }

        return true;
    }

    private bool AddPlayer(JsonElement root)
    {
        if (Player(root) is not { } player)
        {
            return false;
        }

        _known = true;
        var at = _players.FindIndex(p => SameName(p, player.Name));
        if (at >= 0)
        {
            _players[at] = player;
        }
        else if (Room())
        {
            _players.Add(player);
        }

        return true;
    }

    private bool RemovePlayer(JsonElement root)
    {
        var name = root.ValueKind == JsonValueKind.String ? root.GetString() : Text(root, "name");
        return name is not null && _players.RemoveAll(p => SameName(p, name.Trim())) > 0;
    }

    private bool ReplaceCreatures(JsonElement root)
    {
        if (!InRoom(root) || !TryProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        _creatures.Clear();
        foreach (var item in items.EnumerateArray())
        {
            if (Creature(item) is { } creature && Room())
            {
                _creatures.Add(creature);
            }
        }

        return true;
    }

    private bool AddCreature(JsonElement root)
    {
        if (!InRoom(root) || !TryProperty(root, "item", out var item) || Creature(item) is not { } creature)
        {
            return false;
        }

        _creatures.RemoveAll(c => c.Id == creature.Id);
        if (!Room())
        {
            return false;
        }

        _creatures.Add(creature);
        return true;
    }

    private bool RemoveCreature(JsonElement root)
    {
        if (!InRoom(root) || !TryProperty(root, "item", out var item))
        {
            return false;
        }

        var id = item.ValueKind == JsonValueKind.Object ? Id(item) : Scalar(item);
        return id is not null && _creatures.RemoveAll(c => c.Id == id) > 0;
    }

    /// <summary>Whether one more fits under <see cref="MaxOccupants"/>.</summary>
    private bool Room() => _players.Count + _creatures.Count < MaxOccupants;

    private static RoomOccupant? Player(JsonElement item)
    {
        var name = item.ValueKind == JsonValueKind.String ? item.GetString() : Text(item, "name");
        name = name?.Trim();
        if (!Acceptable(name) || name!.Any(char.IsWhiteSpace))
        {
            return null;
        }

        var full = Text(item, "fullname")?.Trim();
        return new RoomOccupant(name!, Acceptable(full) ? full! : name!, true, Idle(item));
    }

    private static RoomOccupant? Creature(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || Text(item, "attrib") is not { } attrib
            || !attrib.Contains('m', StringComparison.Ordinal)
            || Id(item) is not { } id)
        {
            return null;
        }

        var name = Text(item, "name")?.Trim();
        return Acceptable(name) ? new RoomOccupant(name!, name!, false, Id: id) : null;
    }

    private static bool InRoom(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && string.Equals(Text(root, "location"), "room", StringComparison.OrdinalIgnoreCase);

    private static bool Acceptable(string? name) =>
        name is { Length: > 0 and <= MaxNameLength } && !name.Any(char.IsControl);

    private static bool SameName(RoomOccupant player, string name) =>
        string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase);

    private static string? Id(JsonElement item) =>
        TryProperty(item, "id", out var id) ? Scalar(id) : null;

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() is { Length: > 0 and <= MaxNameLength } s ? s : null,
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };

    private static bool TryProperty(JsonElement item, string property, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in item.EnumerateObject())
            {
                if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? Text(JsonElement item, string property) =>
        TryProperty(item, property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Idle(JsonElement item)
    {
        if (!TryProperty(item, "idle", out var value))
        {
            return null;
        }

        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) => n,
            _ => (int?)null,
        };
        return seconds is >= 0 ? seconds : null;
    }
}
