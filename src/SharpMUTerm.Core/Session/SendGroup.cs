namespace SharpMUTerm.Core.Session;

/// <summary>
/// The characters one line can be sent to at once (⌃P ▸ <c>Send the command line to the group</c>), and how a
/// script names another character (<c>world.sendTo</c>). What BeipMU calls puppets: a MUSH player's alts, moved
/// together.
/// </summary>
/// <remarks>
/// <para>
/// <b>Held by session key, in memory, for the life of the run.</b> A group is a thing a player assembles for a
/// scene, and a saved one would be a standing instruction to send one line to several worlds that nobody
/// remembers making.
/// </para>
/// <para>
/// <b>Each member gets the line once, and a disconnected one gets nothing</b> and is named in the result, so a
/// send that reached two of three says which one it missed rather than looking as if it reached everybody.
/// </para>
/// </remarks>
public sealed class SendGroup
{
    private readonly object _gate = new();
    private readonly List<string> _keys = [];

    /// <summary>The members, in the order they were added.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return _keys.ToArray();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _keys.Count;
            }
        }
    }

    public bool Contains(string sessionKey)
    {
        lock (_gate)
        {
            return _keys.Contains(sessionKey, StringComparer.Ordinal);
        }
    }

    /// <summary>Adds the character, or removes it when it is already a member. Returns whether it is now one.</summary>
    public bool Toggle(string sessionKey)
    {
        lock (_gate)
        {
            if (_keys.Remove(sessionKey))
            {
                return false;
            }

            _keys.Add(sessionKey);
            return true;
        }
    }

    /// <summary>Takes a character out of the group, if it was in it.</summary>
    public bool Remove(string sessionKey)
    {
        lock (_gate)
        {
            return _keys.Remove(sessionKey);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _keys.Clear();
        }
    }

    /// <summary>
    /// The members a line can go to now, once each and in group order, and the names of those it cannot.
    /// </summary>
    public (IReadOnlyList<WorldSession> Reachable, IReadOnlyList<string> Missed) Resolve(IReadOnlyList<WorldSession> sessions)
    {
        var reachable = new List<WorldSession>();
        var missed = new List<string>();
        foreach (var key in Keys)
        {
            var session = sessions.FirstOrDefault(s => string.Equals(s.SessionKey, key, StringComparison.Ordinal));
            if (session is { IsConnected: true } && !reachable.Contains(session))
            {
                reachable.Add(session);
            }
            else
            {
                missed.Add(session?.Character?.Name ?? key);
            }
        }

        return (reachable, missed);
    }

    /// <summary>
    /// The open session a script means by <paramref name="name"/>: a <c>World.Character</c> key exactly, else a
    /// character name, ignoring case. Returns null and why when nothing, or more than one, matches, or when the
    /// one it names is not connected.
    /// </summary>
    public static (WorldSession? Session, string? Refusal) Find(IReadOnlyList<WorldSession> sessions, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "a character name is required");
        }

        var byKey = sessions.FirstOrDefault(s => string.Equals(s.SessionKey, name, StringComparison.OrdinalIgnoreCase));
        var matches = byKey is not null
            ? [byKey]
            : sessions.Where(s => string.Equals(s.Character?.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            0 => (null, $"no open character is called {name}"),
            > 1 => (null, $"{matches.Count} open characters are called {name}; name one as World.Character"),
            _ when !matches[0].IsConnected => (null, $"{matches[0].SessionKey} is not connected"),
            _ => (matches[0], null),
        };
    }
}
