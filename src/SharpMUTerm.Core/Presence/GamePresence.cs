using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUTerm.Core.Presence;

/// <summary>
/// What a game says about itself for a Discord profile, over GMCP's <c>External.Discord</c> (Mudlet's
/// convention): <c>Info</c> names the game's Discord application, <c>Status</c> what the player is doing.
/// </summary>
/// <param name="ApplicationId">The game's Discord application id, from <c>Info</c>; nothing is shown without it.</param>
/// <param name="Game">The game's name.</param>
/// <param name="Details">The first line under the game's name.</param>
/// <param name="State">The second line.</param>
/// <param name="SmallImage">An image key the game's application defines.</param>
/// <param name="SmallImageText">The tooltip on that image.</param>
/// <param name="StartTime">When the session started, for the elapsed clock.</param>
/// <param name="PartySize">How many are in the player's party.</param>
/// <param name="PartyMax">How many the party can hold.</param>
public sealed record GamePresence(
    string ApplicationId,
    string? Game = null,
    string? Details = null,
    string? State = null,
    string? SmallImage = null,
    string? SmallImageText = null,
    DateTimeOffset? StartTime = null,
    int? PartySize = null,
    int? PartyMax = null)
{
    /// <summary>The module a world lists in <c>Core.Supports.Set</c> when it may set the presence.</summary>
    public const string GmcpModule = "External.Discord 1";

    public const string InfoPackage = "External.Discord.Info";
    public const string StatusPackage = "External.Discord.Status";

    /// <summary>Discord's limit on a presence field.</summary>
    public const int MaxField = 128;

    /// <summary>
    /// The presence after <paramref name="package"/> arrives, from <paramref name="current"/> (null for none yet).
    /// Returns <paramref name="current"/> for any other package or a body that does not parse.
    /// </summary>
    public static GamePresence? Apply(GamePresence? current, string package, string json)
    {
        JsonObject? body;
        try
        {
            body = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return current;
        }

        if (body is null)
        {
            return current;
        }

        if (string.Equals(package, InfoPackage, StringComparison.OrdinalIgnoreCase))
        {
            var id = Text(body, "applicationid");
            if (id is null || !id.All(char.IsAsciiDigit) || id.Length > 32)
            {
                return current;
            }

            return current is null ? new GamePresence(id) : current with { ApplicationId = id };
        }

        if (!string.Equals(package, StatusPackage, StringComparison.OrdinalIgnoreCase) || current is null)
        {
            return current;
        }

        return current with
        {
            Game = Text(body, "game") ?? current.Game,
            Details = Text(body, "details") ?? current.Details,
            State = Text(body, "state") ?? current.State,
            SmallImage = Text(body, "smallimage") ?? current.SmallImage,
            SmallImageText = Text(body, "smallimagetext") ?? current.SmallImageText,
            StartTime = Number(body, "starttime") is { } start and > 0
                ? DateTimeOffset.FromUnixTimeSeconds(Math.Min(start, 253402300799))
                : current.StartTime,
            PartySize = (int?)Number(body, "partysize") ?? current.PartySize,
            PartyMax = (int?)Number(body, "partymax") ?? current.PartyMax,
        };
    }

    /// <summary>A string field, trimmed to one line and Discord's length, or null when absent or empty.</summary>
    private static string? Text(JsonObject body, string name)
    {
        if (body[name] is not JsonValue value)
        {
            return null;
        }

        var text = value.TryGetValue(out string? s) ? s
            : value.TryGetValue(out long n) ? n.ToString(CultureInfo.InvariantCulture)
            : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return text.Length <= MaxField ? text : text[..MaxField];
    }

    private static long? Number(JsonObject body, string name) =>
        body[name] is JsonValue value
            ? value.TryGetValue(out long n) ? Math.Clamp(n, 0, int.MaxValue)
            : value.TryGetValue(out string? s) && long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? Math.Clamp(p, 0, int.MaxValue)
            : null
            : null;
}

/// <summary>
/// Where a game's presence is shown. A host supplies one or none; the client calls it on its own thread, and an
/// implementation must not block.
/// </summary>
public interface IPresence : IDisposable
{
    /// <summary>Shows <paramref name="presence"/>, or clears what is shown when it is null.</summary>
    void Show(GamePresence? presence);
}
