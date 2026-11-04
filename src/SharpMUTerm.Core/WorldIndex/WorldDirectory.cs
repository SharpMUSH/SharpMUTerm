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
/// <param name="Id">The directory's durable key, for asking where the world is; <see cref="Guid.Empty"/> when the row gave none it could read.</param>
/// <param name="Name">Its name.</param>
/// <param name="Tagline">Its one-line description, or empty.</param>
/// <param name="Codebase">The server software family (<c>PennMUSH</c>), or empty when unknown.</param>
/// <param name="Players">Players now, as the directory says, or null.</param>
/// <param name="Count">How <paramref name="Players"/> was obtained, as the directory labels it.</param>
public sealed record WorldListing(Guid Id, string Name, string Tagline, string Codebase, int? Players, WorldPlayerCount Count);

/// <summary>
/// A page of the directory: the rows read, in the directory's order, how many it holds for the question, and how many rows
/// it sent that were not read — past <see cref="WorldDirectory.MaxRows"/>, or not a JSON object.
/// </summary>
public sealed record WorldListPage(IReadOnlyList<WorldListing> Rows, int Total, int Dropped)
{
    public bool Equals(WorldListPage? other) =>
        other is not null && Total == other.Total && Dropped == other.Dropped && Rows.SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(Total, Dropped, Rows.Count);
}

/// <summary>Where a world is: one telnet address the directory has seen it answer at, checked as <c>/connect</c> checks one.</summary>
/// <param name="Name">The world's name.</param>
/// <param name="Host">The host.</param>
/// <param name="Port">The port.</param>
/// <param name="Tls">Whether the address is a TLS one.</param>
/// <param name="Encoding">
/// The encoding a world added from here is set to (one of F5's names), or null to leave it <c>auto</c>: set only when the
/// directory names a charset that is not UTF-8 and not ASCII (<see cref="WorldDirectory.EncodingFor"/>).
/// </param>
public sealed record WorldAddress(string Name, string Host, int Port, bool Tls, string? Encoding = null);

/// <summary>
/// One address a game answers at, as the directory lists it: a <c>telnet</c> or <c>tls</c> endpoint it does not call gone.
/// </summary>
/// <param name="Host">The host: as <see cref="ConnectEndpoint.Parse"/> read it, or the directory's text, cleaned and bounded, when it could not.</param>
/// <param name="Port">The port, or 0 when the directory gave none.</param>
/// <param name="Tls">Whether it is a TLS address (the directory's <c>tls</c> kind, or TLS it measured there).</param>
/// <param name="Stale">Whether the directory calls it <c>stale</c>: listed, not reached lately.</param>
/// <param name="Connectable">
/// Whether <see cref="ConnectEndpoint.Parse"/> — the reader <c>/connect</c> uses — takes it. One it refuses is still listed,
/// marked not connectable, and is never put in the connect form.
/// </param>
public sealed record WorldEndpoint(string Host, int Port, bool Tls, bool Stale, bool Connectable = true)
{
    /// <summary><c>host:port</c>, an IPv6 host in brackets.</summary>
    public string Address => $"{(Host.Contains(':') ? $"[{Host}]" : Host)}:{Port.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// A game's page read down to what a connect needs: its telnet and TLS addresses in the order the directory lists them,
/// and the charset it names for the whole game, if any.
/// </summary>
/// <param name="Name">The game's name.</param>
/// <param name="Endpoints">At least one address, at most <see cref="WorldDirectory.MaxEndpoints"/>, in the directory's order.</param>
/// <param name="Charset">The game's charset as the directory names it (bounded, cleaned), or null when it names none.</param>
public sealed record WorldGame(string Name, IReadOnlyList<WorldEndpoint> Endpoints, string? Charset)
{
    /// <summary>The first address that can be dialled — the default — or -1 when none can.</summary>
    public int FirstConnectable
    {
        get
        {
            for (var i = 0; i < Endpoints.Count; i++)
            {
                if (Endpoints[i].Connectable)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>The encoding a world added from this game is set to, or null to leave it <c>auto</c>.</summary>
    public string? Encoding => WorldDirectory.EncodingFor(Charset);

    /// <summary>Endpoint <paramref name="index"/> as the address the connect form is filled in with.</summary>
    public WorldAddress Address(int index)
    {
        var endpoint = Endpoints[index];
        return new WorldAddress(Name, endpoint.Host, endpoint.Port, endpoint.Tls, Encoding);
    }

    public bool Equals(WorldGame? other) =>
        other is not null && Name == other.Name && Charset == other.Charset && Endpoints.SequenceEqual(other.Endpoints);

    public override int GetHashCode() => HashCode.Combine(Name, Charset, Endpoints.Count);
}

/// <summary>
/// The client side of mu*index (https://mu-index.com), the MU* directory Browse worlds lists: what to ask, how much of
/// an answer to read, and how to read it. <b>Everything that comes back is server text</b>: a name or a tagline is
/// bounded and stripped of control characters before anything draws it, and an address goes into the connect form only
/// if <see cref="ConnectEndpoint.Parse"/> (the reader <c>/connect</c> uses) takes it. Otherwise what the directory says
/// is shown as it says it — its rows, their order, its counts and their labels, its addresses in its order — because the
/// directory is the authority on its own data; the client caps how much it reads and asks for nothing at start. The
/// listing carries no address, so it is asked for one game at a time, when the player picks it (<see cref="ParseGame"/>).
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

    /// <summary>The longest host shown for an address the client could not read as one.</summary>
    public const int MaxHostShown = 64;

    /// <summary>The longest charset name kept.</summary>
    public const int MaxCharset = 32;

    /// <summary>The most addresses one game offers.</summary>
    public const int MaxEndpoints = 8;

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

        // unnamed=!yes: the directory leaves out games it lists only by host:port (MUIndex's `unnamed` facet).
        var text = new StringBuilder(root).Append("/games?limit=").Append(MaxRows).Append("&sort=players&unnamed=!yes");
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
            var dropped = 0;
            foreach (var game in games.EnumerateArray())
            {
                if (rows.Count >= MaxRows || game.ValueKind != JsonValueKind.Object)
                {
                    dropped++;
                    continue;
                }

                rows.Add(ReadListing(game));
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

    private static WorldListing ReadListing(JsonElement game)
    {
        var id = Text(game, "id") is { } idText && Guid.TryParse(idText, out var parsed) ? parsed : Guid.Empty;
        int? players = game.TryGetProperty("playersNow", out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out var count) && count is >= 0 and < 1_000_000
                ? count
                : null;
        var how = Text(game, "playersNowState") switch
        {
            "measured" => WorldPlayerCount.Measured,
            "declared" => WorldPlayerCount.Declared,
            _ => WorldPlayerCount.Unknown,
        };

        return new WorldListing(
            id, Clean(Text(game, "name"), MaxName), Clean(Text(game, "tagline"), MaxTagline), Clean(Text(game, "codebase"), MaxCodebase), players, how);
    }

    /// <summary>
    /// Reads a <c>GET /games/{id}</c> answer (<c>GameView</c>) down to its addresses, in the order the directory lists
    /// them: <c>telnet</c> and <c>tls</c> endpoints (a websocket or http one is not a telnet address) that it does not call
    /// <c>gone</c>, at most <see cref="MaxEndpoints"/>. An address <see cref="ConnectEndpoint.Parse"/> refuses is listed and
    /// marked not connectable. The charset is the game's <c>fields.CHARSET</c> value — the directory's one charset field,
    /// which names no address.
    /// </summary>
    public static (WorldGame? Game, string? Problem) ParseGame(ReadOnlySpan<byte> json)
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

            var listed = new List<WorldEndpoint>();
            foreach (var endpoint in endpoints.EnumerateArray().Take(64))
            {
                if (listed.Count >= MaxEndpoints)
                {
                    break;
                }

                var kind = Text(endpoint, "kind");
                var state = Text(endpoint, "state");
                if (endpoint.ValueKind != JsonValueKind.Object || kind is not ("telnet" or "tls") || state == "gone")
                {
                    continue;
                }

                var host = Text(endpoint, "host") ?? string.Empty;
                var port = endpoint.TryGetProperty("port", out var portValue) && portValue.ValueKind == JsonValueKind.Number
                    && portValue.TryGetInt32(out var number) ? number : 0;
                var tls = kind == "tls" || (endpoint.TryGetProperty("tlsMeasured", out var measured) && measured.ValueKind == JsonValueKind.True);
                var parsed = host.Length is > 0 and <= 253 && !host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) && port is > 0 and <= 65535
                    ? ConnectEndpoint.Parse([host, port.ToString(CultureInfo.InvariantCulture)])
                    : null;
                listed.Add(parsed is { } ok
                    ? new WorldEndpoint(ok.Host, ok.Port, tls, state == "stale")
                    : new WorldEndpoint(Clean(host, MaxHostShown), Math.Clamp(port, 0, 65535), tls, state == "stale", Connectable: false));
            }

            if (listed.Count == 0)
            {
                return (null, "the directory lists no telnet address for it");
            }

            return (new WorldGame(name.Length == 0 ? listed[0].Host : name, listed, Charset(root)), null);
        }
        catch (JsonException)
        {
            return (null, "the answer is not JSON the client can read");
        }
    }

    /// <summary>The game's <c>fields.CHARSET</c> value, cleaned and bounded; null when there is none.</summary>
    private static string? Charset(JsonElement root)
    {
        if (!root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object
            || !fields.TryGetProperty("CHARSET", out var field) || field.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var value = Clean(Text(field, "value"), MaxCharset);
        return value.Length > 0 ? value : null;
    }

    /// <summary>
    /// The F5 encoding a world is set to for a directory's <paramref name="charset"/>, or null to leave it <c>auto</c>:
    /// UTF-8, ASCII (which UTF-8 reads) and any name the client does not offer stay <c>auto</c>, so CHARSET still decides.
    /// </summary>
    public static string? EncodingFor(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        var key = new string(charset.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return key switch
        {
            "ISO88591" or "LATIN1" or "ISOLATIN1" or "L1" or "ISOIR100" or "CP819" or "IBM819" => "ISO-8859-1",
            "CP1252" or "WINDOWS1252" => "CP1252",
            "CP437" or "IBM437" or "437" => "CP437",
            _ => null,
        };
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
