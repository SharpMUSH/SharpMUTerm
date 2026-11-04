using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Links;

/// <summary>
/// A <c>telnet://</c> or <c>telnets://</c> link, read into the one thing it may ask for: a connection to a host and
/// port, over TLS for <c>telnets</c>.
/// <para>
/// <b>A link is server text.</b> It arrives from a web page, a chat message, another program: anybody can write one,
/// and the desktop hands it to this client when it is clicked. So it is read strictly and can do exactly what
/// <c>/connect host port</c> can (the host and port go through <see cref="ConnectEndpoint.Parse"/>, the same reader)
/// and nothing more. It never names a world, a character or a command, and it never sends anything: a world it adds
/// has no character, so nothing logs in.
/// </para>
/// <para>Refused rather than tidied, each for a reason:</para>
/// <list type="bullet">
/// <item><b>User information</b> (<c>telnet://user:password@host</c>). RFC 4248 allows it; this client would have to
/// send it as a login, which is a command the link would be choosing.</item>
/// <item><b>A path, query or fragment</b> beyond a lone <c>/</c>. A telnet URL has nothing to say there, and text that
/// means nothing to a reader is somewhere for a payload to ride.</item>
/// <item><b>Any control character, space, non-ASCII character or percent-escape</b>, anywhere. A CR or LF in a host
/// would be a second line on any wire it reached, and an escape is a second reading of the same bytes.</item>
/// <item><b>A port that is not 1–65535 in plain digits</b>, and an unbracketed IPv6 address (its colons would be read
/// as a port).</item>
/// </list>
/// </summary>
/// <param name="Host">The host, without brackets.</param>
/// <param name="Port">The port.</param>
/// <param name="Tls">Whether the link was <c>telnets://</c>.</param>
public sealed record TelnetLink(string Host, int Port, bool Tls)
{
    /// <summary>The plain scheme.</summary>
    public const string Scheme = "telnet";

    /// <summary>The TLS scheme.</summary>
    public const string SecureScheme = "telnets";

    /// <summary>Telnet's registered port, for a <c>telnet://</c> link that names none.</summary>
    public const int DefaultPort = 23;

    /// <summary>Telnet-over-TLS's registered port, for a <c>telnets://</c> link that names none.</summary>
    public const int DefaultSecurePort = 992;

    /// <summary>The longest link read at all; anything longer is refused before it is looked at.</summary>
    public const int MaxLength = 512;

    /// <summary>Whether <paramref name="text"/> is meant as a link: it starts with one of the two schemes.</summary>
    public static bool LooksLikeLink(string? text) =>
        text is not null
        && (text.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(SecureScheme + "://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads <paramref name="text"/>, or says in a few words why it was refused.</summary>
    public static TelnetLink? TryParse(string? text, out string? refusal)
    {
        refusal = Refuse(text, out var link);
        return link;
    }

    /// <summary>Reads <paramref name="text"/>, or null when it is not a link this client will follow.</summary>
    public static TelnetLink? TryParse(string? text) => TryParse(text, out _);

    private static string? Refuse(string? text, out TelnetLink? link)
    {
        link = null;
        if (string.IsNullOrEmpty(text))
        {
            return "the link is empty";
        }

        if (text.Length > MaxLength)
        {
            return "the link is too long";
        }

        foreach (var c in text)
        {
            if (c <= ' ' || c > '~')
            {
                return "the link holds a space, a line break or another character a link cannot hold";
            }
        }

        bool tls;
        string rest;
        if (text.StartsWith(SecureScheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            tls = true;
            rest = text[(SecureScheme.Length + 3)..];
        }
        else if (text.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            tls = false;
            rest = text[(Scheme.Length + 3)..];
        }
        else
        {
            return "only telnet:// and telnets:// links are followed";
        }

        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? rest : rest[..end];
        var tail = end < 0 ? string.Empty : rest[end..];
        if (tail.Length > 0 && tail != "/")
        {
            return "the link has a path, query or fragment; a telnet link names only a host and port";
        }

        if (authority.Contains('@'))
        {
            return "the link carries a user name or password, which this client will not send";
        }

        if (authority.Contains('%') || authority.Contains('\\'))
        {
            return "the link's host is escaped";
        }

        if (authority.Length == 0)
        {
            return "the link names no host";
        }

        if (!authority.StartsWith('[') && authority.Count(c => c == ':') > 1)
        {
            return "an IPv6 address in a link must be in brackets";
        }

        if (authority.EndsWith(':'))
        {
            return "the link's port is empty";
        }

        var defaultPort = tls ? DefaultSecurePort : DefaultPort;
        if (ConnectEndpoint.Parse([authority], defaultPort) is not { } endpoint)
        {
            return "the link's host or port is not valid (a port is 1 to 65535)";
        }

        link = new TelnetLink(endpoint.Host, endpoint.Port, tls);
        return null;
    }

    /// <summary>The host as <c>/connect</c> takes it: an IPv6 address in brackets.</summary>
    public string HostForCommand => Host.Contains(':') ? $"[{Host}]" : Host;

    /// <summary>The link back as text, for a message: <c>telnets://host:992</c>.</summary>
    public override string ToString() => $"{(Tls ? SecureScheme : Scheme)}://{HostForCommand}:{Port}";
}
