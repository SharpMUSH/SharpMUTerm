using System.Globalization;

namespace SharpMUTerm.Core.Transport;

/// <summary>
/// A world reached over a WebSocket: what a path may hold, how the address is written, and how a <c>ws://</c> or
/// <c>wss://</c> address typed on the command line is read. The address is the player's or a directory's text, so it is
/// refused rather than tidied, as <see cref="Links.TelnetLink"/> refuses a link.
/// </summary>
public static class WebSocketAddress
{
    /// <summary>The longest path kept.</summary>
    public const int MaxPathLength = 256;

    /// <summary>
    /// Whether <paramref name="path"/> is a path a world may dial: it starts with <c>/</c>, is at most
    /// <see cref="MaxPathLength"/> characters, and holds only the characters an address's path and query may (no
    /// spaces, no control characters, no <c>#</c>, nothing past ASCII).
    /// </summary>
    public static bool IsPath(string? path) =>
        path is { Length: > 0 and <= MaxPathLength } && path[0] == '/' && path.All(IsPathCharacter);

    private static bool IsPathCharacter(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
            or '-' or '.' or '_' or '~' or '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '='
            or ':' or '@' or '/' or '?' or '%';

    /// <summary>The address dialled: <c>ws://host:port/path</c>, <c>wss://</c> with TLS, an IPv6 host bracketed.</summary>
    public static string Format(string host, int port, bool tls, string path)
    {
        var shownHost = host.Contains(':') ? $"[{host}]" : host;
        return string.Create(CultureInfo.InvariantCulture, $"{(tls ? "wss" : "ws")}://{shownHost}:{port}{path}");
    }

    /// <summary>Whether <paramref name="text"/> starts with <c>ws://</c> or <c>wss://</c>, in any case.</summary>
    public static bool LooksLikeOne(string text) =>
        text.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a <c>ws://</c> or <c>wss://</c> address into a host, port (80 or 443 when none is given), TLS and path
    /// (<c>/</c> when none is given), or null with the reason in <paramref name="refusal"/>. Refused: user information,
    /// a fragment, a port outside 1–65535, a path <see cref="IsPath"/> refuses, and anything over 512 characters.
    /// </summary>
    public static (string Host, int Port, bool Tls, string Path)? TryParse(string text, out string? refusal)
    {
        refusal = null;
        if (text.Length > 512)
        {
            refusal = "the address is too long";
            return null;
        }

        if (!LooksLikeOne(text))
        {
            refusal = "it is not a ws:// or wss:// address";
            return null;
        }

        if (text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c > '~' || c == '\\'))
        {
            refusal = "the address holds a character an address cannot";
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.HostNameType is UriHostNameType.Unknown)
        {
            refusal = "the address does not read";
            return null;
        }

        if (uri.UserInfo.Length > 0)
        {
            refusal = "the address carries a user name";
            return null;
        }

        if (uri.Fragment.Length > 0 || text.Contains('#'))
        {
            refusal = "the address carries a fragment";
            return null;
        }

        var path = uri.PathAndQuery;
        if (!IsPath(path))
        {
            refusal = "the address's path holds characters this client does not send";
            return null;
        }

        var tls = uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
        var port = uri.IsDefaultPort ? (tls ? 443 : 80) : uri.Port;
        if (port is <= 0 or > 65535)
        {
            refusal = "the port is out of range";
            return null;
        }

        return (uri.IdnHost.Trim('[', ']'), port, tls, path);
    }
}
