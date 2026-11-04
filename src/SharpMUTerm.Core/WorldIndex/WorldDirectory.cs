using System.Globalization;
using System.Text;
using System.Text.Json;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.WorldIndex;

/// <summary>How Browse worlds orders the directory's rows.</summary>
public enum WorldDirectorySort
{
    /// <summary>Most players counted now first, the directory's own default; games it could not count follow.</summary>
    Players,

    /// <summary>Alphabetical: the one order that ranks nobody.</summary>
    Name,
}

/// <summary>How a row's player count was obtained, as the directory labels it.</summary>
public enum WorldPlayerCount
{
    /// <summary>No count, or none the directory could say it measured.</summary>
    Unknown,

    /// <summary>Counted by the directory's own probe (a <c>WHO</c>, a connect screen).</summary>
    Measured,

    /// <summary>A number the game reports about itself (MSSP <c>PLAYERS</c>).</summary>
    Declared,
}

/// <summary>One world as the directory lists it, every text already bounded and stripped of control characters.</summary>
/// <param name="Id">The directory's durable key, for asking where the world is.</param>
/// <param name="Name">Its name.</param>
/// <param name="Tagline">Its one-line description, or empty.</param>
/// <param name="Codebase">The server software family (<c>PennMUSH</c>), or empty when unknown.</param>
/// <param name="Players">Players counted now, or null.</param>
/// <param name="Count">How <paramref name="Players"/> was obtained.</param>
public sealed record WorldListing(Guid Id, string Name, string Tagline, string Codebase, int? Players, WorldPlayerCount Count);

/// <summary>A page of the directory: the rows read, how many the directory holds for the question, and how many rows it sent that were not read.</summary>
public sealed record WorldListPage(IReadOnlyList<WorldListing> Rows, int Total, int Dropped)
{
    public bool Equals(WorldListPage? other) =>
        other is not null && Total == other.Total && Dropped == other.Dropped && Rows.SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(Total, Dropped, Rows.Count);
}

/// <summary>Where a world is: one telnet address the directory has seen it answer at, checked as <c>/connect</c> checks one.</summary>
public sealed record WorldAddress(string Name, string Host, int Port, bool Tls);

/// <summary>
/// The client side of mu*index (https://mu-index.com), the MU* directory Browse worlds lists: what to ask, how much of
/// an answer to read, and how to read it. <b>Everything that comes back is server text</b>: a name or a tagline is
/// bounded and stripped of control characters before anything draws it, an address is read by
/// <see cref="ConnectEndpoint.Parse"/> (the reader <c>/connect</c> uses) or not at all, and a row that breaks a rule is
/// dropped and counted rather than failing the list. The listing carries no address, so it is asked for one game at a
/// time, when the player picks it (<see cref="ParseGame"/>).
/// </summary>
public static class WorldDirectory
{
    /// <summary>Rows asked for in one listing: the directory's most played, then the player types to narrow them.</summary>
    public const int MaxRows = 200;

    /// <summary>The most bytes one answer may be, counted while it streams.</summary>
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>How long one request may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>The longest a world's name is kept.</summary>
    public const int MaxName = 64;

    /// <summary>The longest a tagline is kept.</summary>
    public const int MaxTagline = 160;

    /// <summary>The longest a codebase is kept.</summary>
    public const int MaxCodebase = 32;

    /// <summary>The longest query sent.</summary>
    public const int MaxQuery = 64;

    /// <summary>What the dataset's licence asks every listing to say (CC BY 4.0).</summary>
    public const string Attribution = "MUIndex (https://github.com/SharpMUSH/MUIndex)";

    /// <summary>The licence the directory publishes its data under.</summary>
    public const string Licence = "CC BY 4.0";

    /// <summary>The directory's name as the client says it.</summary>
    public const string Name = "mu*index";

    /// <summary>The address of the listing for <paramref name="query"/>, or null when the base is not an https link.</summary>
    public static Uri? ListUri(string? baseUrl, string? query = null)
    {
        if (Base(baseUrl) is not { } root)
        {
            return null;
        }

        var text = new StringBuilder(root).Append("/games?limit=").Append(MaxRows).Append("&sort=players");
        var q = Clean(query, MaxQuery);
        if (q.Length > 0)
        {
            text.Append("&q=").Append(Uri.EscapeDataString(q));
        }

        return new Uri(text.ToString());
    }

    /// <summary>The address of one game's page, which names its addresses; null when the base is not an https link.</summary>
    public static Uri? GameUri(string? baseUrl, Guid id) =>
        Base(baseUrl) is { } root ? new Uri($"{root}/games/{id:D}") : null;

    /// <summary>Why <paramref name="baseUrl"/> cannot be asked, or null when it can.</summary>
    public static string? Problem(string? baseUrl) =>
        Base(baseUrl) is not null ? null
            : string.IsNullOrWhiteSpace(baseUrl) ? "no world directory is set (worldDirectoryUrl in config.json)"
            : "the world directory is not an https link (worldDirectoryUrl in config.json)";

    private static string? Base(string? baseUrl) =>
        Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : null;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 32, AllowTrailingCommas = false };

    /// <summary>Reads a <c>GET /games</c> answer (<c>GameListView</c>).</summary>
    public static (WorldListPage? Page, string? Problem) ParseList(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaxResponseBytes)
        {
            return (null, "the answer was too large");
        }

        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), Options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
            {
                return (null, "the answer is not a list of worlds");
            }

            var rows = new List<WorldListing>();
            var seen = new HashSet<Guid>();
            var dropped = 0;
            foreach (var game in games.EnumerateArray())
            {
                if (rows.Count >= MaxRows || ReadListing(game) is not { } row || !seen.Add(row.Id))
                {
                    dropped++;
                    continue;
                }

                rows.Add(row);
            }

            var total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var n) && n >= 0
                ? Math.Max(n, rows.Count)
                : rows.Count;
            return (new WorldListPage(rows, total, dropped), null);
        }
        catch (JsonException)
        {
            return (null, "the answer is not JSON the client can read");
        }
    }

    private static WorldListing? ReadListing(JsonElement game)
    {
        if (game.ValueKind != JsonValueKind.Object
            || Text(game, "id") is not { } idText || !Guid.TryParse(idText, out var id) || id == Guid.Empty)
        {
            return null;
        }

        var name = Clean(Text(game, "name"), MaxName);
        if (name.Length == 0 || (game.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True))
        {
            return null;
        }

        int? players = game.TryGetProperty("playersNow", out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out var count) && count is >= 0 and < 1_000_000
                ? count
                : null;
        var how = players is null ? WorldPlayerCount.Unknown : Text(game, "playersNowState") switch
        {
            "measured" => WorldPlayerCount.Measured,
            "declared" => WorldPlayerCount.Declared,
            _ => WorldPlayerCount.Unknown,
        };

        // A number the directory will not say it measured is not shown as one.
        if (how == WorldPlayerCount.Unknown)
        {
            players = null;
        }

        return new WorldListing(id, name, Clean(Text(game, "tagline"), MaxTagline), Clean(Text(game, "codebase"), MaxCodebase), players, how);
    }

    /// <summary>
    /// Reads a <c>GET /games/{id}</c> answer (<c>GameView</c>) down to the one address to offer: a <c>telnet</c> or
    /// <c>tls</c> endpoint, never one the directory calls <c>gone</c>; <c>active</c> before <c>stale</c>, then the one
    /// seen most recently. An address <see cref="ConnectEndpoint.Parse"/> refuses is skipped.
    /// </summary>
    public static (WorldAddress? Address, string? Problem) ParseGame(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaxResponseBytes)
        {
            return (null, "the answer was too large");
        }

        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), Options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "the answer is not a world");
            }

            var name = Clean(Text(root, "name"), MaxName);
            if (!root.TryGetProperty("endpoints", out var endpoints) || endpoints.ValueKind != JsonValueKind.Array)
            {
                return (null, "the directory lists no address for it");
            }

            var candidates = new List<(WorldAddress Address, int Rank, DateTimeOffset Seen)>();
            foreach (var endpoint in endpoints.EnumerateArray().Take(64))
            {
                if (endpoint.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var kind = Text(endpoint, "kind");
                var rank = Text(endpoint, "state") switch
                {
                    "active" => 0,
                    "stale" => 1,
                    _ => -1,
                };
                if (kind is not ("telnet" or "tls") || rank < 0
                    || Text(endpoint, "host") is not { Length: > 0 and <= 253 } host
                    || host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
                    || !endpoint.TryGetProperty("port", out var portValue) || portValue.ValueKind != JsonValueKind.Number
                    || !portValue.TryGetInt32(out var port)
                    || ConnectEndpoint.Parse([host, port.ToString(CultureInfo.InvariantCulture)]) is not { } parsed)
                {
                    continue;
                }

                var tls = kind == "tls" || (endpoint.TryGetProperty("tlsMeasured", out var measured) && measured.ValueKind == JsonValueKind.True);
                var seen = Text(endpoint, "lastSeenAt") is { } at
                    && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
                        ? when
                        : DateTimeOffset.MinValue;
                candidates.Add((new WorldAddress(name.Length == 0 ? parsed.Host : name, parsed.Host, parsed.Port, tls), rank, seen));
            }

            if (candidates.Count == 0)
            {
                return (null, "the directory lists no telnet address for it that answers");
            }

            var best = candidates.OrderBy(c => c.Rank).ThenByDescending(c => c.Seen).First();
            return (best.Address, null);
        }
        catch (JsonException)
        {
            return (null, "the answer is not JSON the client can read");
        }
    }

    /// <summary>
    /// Server text made safe to draw: control, format (bidirectional overrides included), private-use and astral characters and line breaks
    /// become spaces, runs of space collapse, and what is left is cut to <paramref name="max"/> with an ellipsis.
    /// </summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || max <= 0)
        {
            return string.Empty;
        }

        var built = new StringBuilder(Math.Min(text.Length, max + 1));
        var space = false;
        foreach (var c in text)
        {
            if (built.Length > max * 2)
            {
                break;
            }

            var category = char.GetUnicodeCategory(c);
            var blank = char.IsWhiteSpace(c) || char.IsControl(c)
                || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                    or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned;
            if (blank)
            {
                space = built.Length > 0;
                continue;
            }

            if (space)
            {
                built.Append(' ');
                space = false;
            }

            built.Append(c);
        }

        var cleaned = built.ToString();
        return cleaned.Length <= max ? cleaned : cleaned[..(max - 1)].TrimEnd() + "…";
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static readonly HttpClient Client = new() { Timeout = Timeout };

    /// <summary>
    /// Downloads <paramref name="uri"/>: https only, JSON asked for, the size counted while it streams. Null when it
    /// is not https, did not succeed, or was larger than <see cref="MaxResponseBytes"/>.
    /// </summary>
    public static async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
