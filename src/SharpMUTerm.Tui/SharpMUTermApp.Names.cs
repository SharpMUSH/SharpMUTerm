using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Tui;

internal sealed partial class SharpMUTermApp
{
    /// <summary>
    /// A session key named for a person (<see cref="CharacterNames"/>): <c>Corvid · Aetherfall</c>. An open session
    /// answers first, so a world dialled from the command line that is not in the configuration is still named;
    /// then the configuration. The dotted key is an address and is never shown.
    /// </summary>
    internal string DisplayName(string sessionKey)
    {
        var open = _sessions.Sessions
            .Select(s => new CharacterRef(s.World.Name, s.Character?.Name ?? string.Empty, s.SessionKey, s.IsConnected));
        foreach (var session in open)
        {
            if (string.Equals(session.SessionKey, sessionKey, StringComparison.Ordinal))
            {
                return CharacterNames.Display(session);
            }
        }

        return CharacterNames.ForKey(sessionKey, _config);
    }
}
