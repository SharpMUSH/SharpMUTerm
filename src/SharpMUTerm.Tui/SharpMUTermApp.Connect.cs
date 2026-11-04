using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// <c>/connect</c>: open a world from the command line. A configured world is reached by name; anything else
/// is read as a host (and port), saved as a new world and dialled. It exists because <c>--plain</c> has no
/// settings screens a screen reader can use, so a first run there had no way to add a world at all, and it is
/// the quickest way in everywhere else too.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The port a host given without one is dialled on, the same default as the command line's.</summary>
    internal const int DefaultConnectPort = 4000;

    /// <summary>What <c>/connect</c> with nothing after it says.</summary>
    internal const string ConnectUsage = "/connect <world>, or /connect <host> [port] [tls] to add one";

    /// <param name="shape">
    /// Finishes a world this adds before it is saved — the welcome's form adds the character it named. Not
    /// called when the words name a world that already exists.
    /// </param>
    private void ConnectCommand(string windowId, string arguments, Action<WorldDefinition>? shape = null)
    {
        var words = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tls = words.Any(IsTlsWord);
        words = words.Where(word => !IsTlsWord(word)).ToArray();

        if (words.Length == 0)
        {
            AppendWindowLine(windowId, $"[dim]*** {Escape(ConnectUsage)}[/]");
            if (_config.Worlds.Count > 0)
            {
                var names = string.Join(", ", _config.Worlds.Select(world => world.Name));
                AppendWindowLine(windowId, $"[dim]*** worlds: {Escape(names)}[/]");
            }

            return;
        }

        var world = _config.Worlds.FirstOrDefault(
            known => string.Equals(known.Name, string.Join(' ', words), StringComparison.OrdinalIgnoreCase));
        if (world is null)
        {
            if (ParseEndpoint(words) is not { } endpoint)
            {
                RefuseCommand($"no world called {Snippet(string.Join(' ', words))}, and that is not a host — {ConnectUsage}");
                return;
            }

            world = new WorldDefinition
            {
                Name = UniqueWorldName(endpoint.Host),
                Host = endpoint.Host,
                Port = endpoint.Port,
                UseTls = tls,
            };

            // A configuration's first world takes the layout the welcome was left at, whether it came from
            // the welcome's form or from a /connect typed under it.
            if (_config.Worlds.Count == 0)
            {
                ApplyWelcomeLayout(world);
            }

            shape?.Invoke(world);
            _config.Worlds.Add(world);
            SaveConfiguration();
            Notice($"added {world.Name} ({world.Host}:{world.Port}) — F5 edits it", MessageSeverity.Info, "F5");
        }

        var character = world.Characters.FirstOrDefault();
        var key = character is null ? world.Name : $"{world.Name}.{character.Name}";
        var session = _sessions.Find(key);
        if (session is null)
        {
            session = OpenSession(world, character);
            BindSession(session, OpenSessionWindow(session));
        }

        Activate(OpenSessionWindow(session));
        UpdateStatus();
        UpdateInputChrome();

        if (session.State is not (ConnectionState.Connected or ConnectionState.Connecting))
        {
            LastCommand = DialAtStartupAsync(session);
        }
    }

    private static bool IsTlsWord(string word) =>
        word.Equals("tls", StringComparison.OrdinalIgnoreCase)
        || word.Equals("--tls", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads <c>host port</c>, <c>host:port</c> or a bare host. A bracketed IPv6 address keeps its colons. Null
    /// when the words are not one host with at most one port, so a mistyped world name is refused rather than
    /// saved as a world that dials a host called "my world".
    /// </summary>
    internal static (string Host, int Port)? ParseEndpoint(IReadOnlyList<string> words)
    {
        if (words.Count is 0 or > 2)
        {
            return null;
        }

        var host = words[0];
        var port = DefaultConnectPort;
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
        return Uri.CheckHostName(host) == UriHostNameType.Unknown ? null : (host, port);
    }

    private static bool TryPort(string text, out int port) =>
        int.TryParse(text, out port) && port is > 0 and <= 65535;

    /// <summary>A world name not already taken: the host, then the host with a number after it.</summary>
    private string UniqueWorldName(string host)
    {
        bool Taken(string name) => _config.Worlds.Any(
            world => string.Equals(world.Name, name, StringComparison.OrdinalIgnoreCase));

        var name = host;
        for (var n = 2; Taken(name); n++)
        {
            name = $"{host} {n}";
        }

        return name;
    }
}
