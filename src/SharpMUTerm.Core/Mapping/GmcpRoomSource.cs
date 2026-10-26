using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Telnet;
using TelnetNegotiationCore.Gmcp;

namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// Rooms from GMCP <c>Room.Info</c>. Servers disagree on its shape, so the shape is read off what is present:
/// <list type="bullet">
/// <item><description><b>Iron Realms and Aardwolf</b>: <c>num</c>, <c>name</c>, <c>area</c>/<c>zone</c>,
/// <c>environment</c>/<c>terrain</c>, <c>coords</c> as <c>area,x,y,z</c> (IRE) or <c>coord</c> as an object
/// (Aardwolf), and <c>exits</c> as direction → room number. Read through TelnetNegotiationCore's typed
/// <see cref="RoomInfo"/>.</description></item>
/// <item><description><b>MUME and its relatives</b>: <c>id</c> instead of <c>num</c>, exits as objects carrying
/// <c>id</c>, and <c>Room.UpdateExits</c> to change the exits of the room the character is in.</description></item>
/// </list>
/// The package name is matched without regard to case, because Aardwolf sends <c>room.info</c>.
/// </summary>
public sealed class GmcpRoomSource : IRoomSource
{
    private readonly WorldSession _session;

    public GmcpRoomSource(WorldSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.GmcpReceived += OnGmcp;
    }

    public event EventHandler<RoomObservation>? RoomEntered;

    public void Dispose() => _session.GmcpReceived -= OnGmcp;

    private void OnGmcp(object? sender, GmcpMessageEventArgs e)
    {
        if (Read(e.Package, e.Json) is { } room)
        {
            RoomEntered?.Invoke(this, room);
        }
    }

    /// <summary>The room a GMCP message describes, or null when it is not a room message or not one this reads.</summary>
    public static RoomObservation? Read(string package, string json)
    {
        var isInfo = string.Equals(package, GmcpPackages.RoomInfo, StringComparison.OrdinalIgnoreCase);
        var isUpdate = string.Equals(package, "Room.UpdateExits", StringComparison.OrdinalIgnoreCase);
        if (!isInfo && !isUpdate)
        {
            return null;
        }

        JsonObject? body;
        try
        {
            body = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (body is null)
        {
            return null;
        }

        if (isUpdate)
        {
            // MUME sends the exits object itself, or wrapped in "exits".
            var exits = Exits(body["exits"] as JsonObject ?? body);
            return new RoomObservation(Number(body["id"]), string.Empty, exits) { ExitsOnly = true };
        }

        if (body["num"] is not null && RoomInfo.TryParse(json, out var ire))
        {
            return new RoomObservation(ire.Num, ire.Name, Exits(body["exits"] as JsonObject))
            {
                Area = ire.Area,
                Environment = ire.Environment,
                Coordinates = IreCoordinates(ire.Coords) ?? AardwolfCoordinates(body["coord"] as JsonObject),
            };
        }

        if (Number(body["id"]) is { } id)
        {
            return new RoomObservation(id, Text(body["name"]) ?? string.Empty, Exits(body["exits"] as JsonObject))
            {
                Area = Text(body["area"]) ?? Text(body["zone"]),
                Environment = Text(body["terrain"]) ?? Text(body["environment"]),
                Coordinates = Number(body["x"]) is { } x && Number(body["y"]) is { } y
                    ? ((int)x, (int)y, (int)(Number(body["z"]) ?? 0))
                    : null,
            };
        }

        return null;
    }

    /// <summary>
    /// Exits by direction or command, where each value is a room number, a numeric string, or an object
    /// carrying one under <c>id</c> (MUME). A direction is stored by its long name; anything else is a command.
    /// </summary>
    private static List<ObservedExit> Exits(JsonObject? exits)
    {
        var observed = new List<ObservedExit>();
        if (exits is null)
        {
            return observed;
        }

        foreach (var (key, value) in exits)
        {
            var to = value is JsonObject target ? Number(target["id"]) : Number(value);
            observed.Add(new ObservedExit(MapDirections.Normalise(key) ?? key, to));
        }

        return observed;
    }

    /// <summary>IRE's <c>coords</c>: <c>area,x,y,z</c>, or <c>x,y,z</c>.</summary>
    private static (int, int, int)? IreCoordinates(string? coords)
    {
        var parts = (coords ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        var numbers = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            {
                return null;
            }

            numbers.Add(n);
        }

        return numbers.Count switch
        {
            >= 4 => (numbers[1], numbers[2], numbers[3]),
            3 => (numbers[0], numbers[1], numbers[2]),
            _ => null,
        };
    }

    /// <summary>Aardwolf's <c>coord</c>: <c>{"id":…,"x":…,"y":…,"cont":…}</c>, flat on one level.</summary>
    private static (int, int, int)? AardwolfCoordinates(JsonObject? coord) =>
        coord is not null && Number(coord["x"]) is { } x && Number(coord["y"]) is { } y && (x >= 0 || y >= 0)
            ? ((int)x, (int)y, 0)
            : null;

    private static long? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var n))
        {
            return n;
        }

        if (value.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < long.MaxValue)
        {
            return (long)d;
        }

        return value.TryGetValue<string>(out var s)
            && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
