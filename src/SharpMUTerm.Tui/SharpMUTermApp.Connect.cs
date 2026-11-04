using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Links;
using SharpMUTerm.Core.Session;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// <c>/connect</c>: open a world from the command line. A configured world is reached by name; anything else
/// is read as a host (and port), saved as a new world and dialled. It exists because <c>--plain</c> has no
/// settings screens a screen reader can use, so a first run there had no way to add a world at all, and it is
/// the quickest way in everywhere else too.
/// <para>
/// A <c>telnet://</c> link (<see cref="OpenLink"/>) ends in the same place, through the same reader, so a link can do
/// nothing <c>/connect</c> cannot.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The port a host given without one is dialled on, the same default as the command line's.</summary>
    internal const int DefaultConnectPort = ConnectEndpoint.DefaultPort;

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
        if (world is not null)
        {
            DialWorld(world);
            return;
        }

        if (ParseEndpoint(words) is not { } endpoint)
        {
            RefuseCommand($"no world called {Snippet(string.Join(' ', words))}, and that is not a host — {ConnectUsage}");
            return;
        }

        ConnectToEndpoint(endpoint.Host, endpoint.Port, tls, shape);
    }

    /// <summary>
    /// Follows a <c>telnet://</c> or <c>telnets://</c> link: the link from the command line a desktop launched the
    /// client with, or one a second launch handed to this one. The link is server text, read by
    /// <see cref="TelnetLink"/>; all it can do is what <c>/connect host port</c> does. A world already configured for
    /// the same host, port and TLS is dialled rather than added again — matched on the endpoint and never on a name,
    /// so a link cannot reach a world by naming it. Nothing is sent: a world a link adds has no character, and an
    /// existing world logs in only as its own configuration says.
    /// </summary>
    internal void OpenLink(string text)
    {
        if (TelnetLink.TryParse(text, out var refusal) is not { } link)
        {
            RefuseCommand($"not following that link: {refusal}");
            return;
        }

        OpenLink(link);
    }

    /// <inheritdoc cref="OpenLink(string)"/>
    internal void OpenLink(TelnetLink link)
    {
        var world = _config.Worlds.FirstOrDefault(known =>
            string.Equals(known.Host, link.Host, StringComparison.OrdinalIgnoreCase)
            && known.Port == link.Port
            && known.UseTls == link.Tls);
        if (world is not null)
        {
            DialWorld(world);
            return;
        }

        ConnectToEndpoint(link.Host, link.Port, link.Tls);
    }

    /// <summary>
    /// Adds a world for <paramref name="host"/> and dials it: what <c>/connect host port</c> does once the words are
    /// read, and all a <c>telnet://</c> link can do.
    /// </summary>
    private void ConnectToEndpoint(string host, int port, bool tls, Action<WorldDefinition>? shape = null)
    {
        var world = new WorldDefinition
        {
            Name = UniqueWorldName(host),
            Host = host,
            Port = port,
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
        DialWorld(world);
    }

    /// <summary>Opens <paramref name="world"/>'s first character (or the world alone), brings it forward and dials it.</summary>
    private void DialWorld(WorldDefinition world)
    {
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
    /// Reads <c>host port</c>, <c>host:port</c> or a bare host (<see cref="ConnectEndpoint"/>, the one reader
    /// <c>/connect</c>, the welcome and a <c>telnet://</c> link share).
    /// </summary>
    internal static (string Host, int Port)? ParseEndpoint(IReadOnlyList<string> words) => ConnectEndpoint.Parse(words);

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
