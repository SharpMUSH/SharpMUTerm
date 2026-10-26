using SharpMUTerm.Core.Presence;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>
/// Discord presence: what the game in front says about itself (<c>External.Discord</c>), shown through the host's
/// <see cref="ClientHost.Presence"/>. Only the world whose window is in front is shown, only when that world
/// allows it (F5 ▸ <c>discord</c>), and only while it is connected; anything else clears it.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private IPresence? _presence;
    private bool _presenceOpened;
    private GamePresence? _shownPresence;

    /// <summary>What was last handed to the host, for the tests.</summary>
    internal GamePresence? ShownPresence => _shownPresence;

    private void WatchPresence(WorldSession session)
    {
        session.GamePresenceChanged += (_, _) => OnUi(UpdatePresence);
        session.StateChanged += (_, _) => OnUi(UpdatePresence);
    }

    /// <summary>
    /// Shows the focused window's game, or clears what is shown. The host is opened the first time there is
    /// something to show, so a client whose worlds never opt in never touches Discord.
    /// </summary>
    private void UpdatePresence()
    {
        var session = SendTarget();
        var wanted = session is { IsConnected: true, World.DiscordPresence: true } ? session.GamePresence : null;
        if (wanted == _shownPresence)
        {
            return;
        }

        if (wanted is not null && !_presenceOpened)
        {
            _presenceOpened = true;
            _presence = Host.Presence?.Invoke();
        }

        _shownPresence = wanted;
        _presence?.Show(wanted);
    }

    private void DisposePresence()
    {
        _presence?.Dispose();
        _presence = null;
    }
}
