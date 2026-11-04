using System.Globalization;

namespace SharpMUTerm.Core.Configuration;

/// <summary>
/// Reads a host and port the way <c>/connect</c> takes them: <c>host port</c>, <c>host:port</c>, or a bare host. A
/// bracketed IPv6 address keeps its colons. One reader for every way in — the command line's <c>/connect</c>, the
/// welcome's form and a <c>telnet://</c> link (<see cref="Links.TelnetLink"/>) — so none of them can reach a host the
/// others would refuse.
/// </summary>
public static class ConnectEndpoint
{
    /// <summary>The port a host given without one is dialled on, the same default as the command line's.</summary>
    public const int DefaultPort = 4000;

    /// <summary>
    /// The host and port in <paramref name="words"/>, or null when they are not one host with at most one port, so a
    /// mistyped world name is refused rather than saved as a world that dials a host called "my world".
    /// </summary>
    /// <param name="words">One or two words.</param>
    /// <param name="defaultPort">The port when the words name none.</param>
    public static (string Host, int Port)? Parse(IReadOnlyList<string> words, int defaultPort = DefaultPort)
    {
        if (words.Count is 0 or > 2)
        {
            return null;
        }

        var host = words[0];
        var port = defaultPort;

        // An opening bracket is an IPv6 address, and is closed at the end or just before a port.
        if (host.StartsWith('[') && host.IndexOf(']') is var end
            && (end < 0 || host.IndexOf(']', end + 1) >= 0 || (end + 1 < host.Length && host[end + 1] != ':')))
        {
            return null;
        }

        if (words.Count == 2)
        {
            if (!TryPort(words[1], out port))
            {
                return null;
            }
        }
        else if (host.StartsWith('[') && host.IndexOf("]:", StringComparison.Ordinal) is var close and > 0)
        {
            if (!TryPort(host[(close + 2)..], out port))
            {
                return null;
            }

            host = host[..(close + 1)];
        }
        else if (host.Count(c => c == ':') == 1)
        {
            var colon = host.IndexOf(':');
            if (!TryPort(host[(colon + 1)..], out port))
            {
                return null;
            }

            host = host[..colon];
        }

        host = host.Trim('[', ']');
        return host.Length == 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown ? null : (host, port);
    }

    /// <summary>A port: digits only (no sign, no spaces), 1 to 65535.</summary>
    public static bool TryPort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;
}
