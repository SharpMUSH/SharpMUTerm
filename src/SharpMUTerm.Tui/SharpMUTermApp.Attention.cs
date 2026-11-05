using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>
/// Telling the desktop that something wants the player while they are in another window: a trigger marked
/// <c>alert</c>, or — on a world that opts in (F5 ▸ <c>alert on</c>) — a line that names the character or pages them
/// (<see cref="WorldSession.AttentionRequested"/>). The host says it (<see cref="ClientHost.Attention"/>); the client
/// decides whether to ask:
/// <list type="bullet">
/// <item>F7 ▸ <c>alert when away</c> is on;</item>
/// <item>the player is not looking: the host says it is not in front, or — where the host cannot tell and does not
/// filter for itself (a terminal other than kitty) — nothing has been typed or clicked for <see cref="AwayAfter"/>;</item>
/// <item>the rate limit has room (<see cref="AttentionThrottle"/>, one per ten seconds).</item>
/// </list>
/// <para>
/// While a host that knows its focus is behind another window, the title carries a count of the lines that arrived
/// (<c>(3) Corvid · Aetherfall — SharpMUTerm</c>), the passive half of the same cue; it clears when the window comes
/// forward.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>With no focus to read, how long without input before the player counts as away.</summary>
    internal static readonly TimeSpan AwayAfter = TimeSpan.FromSeconds(30);

    private AttentionThrottle? _attentionThrottle;
    private bool? _hostFocused;
    private int _unseenWhileAway;
    private DateTimeOffset _lastAttendedAt;

    /// <summary>Every request handed to the host, for the tests.</summary>
    internal List<AttentionRequest> AttentionSent { get; } = [];

    /// <summary>How many lines arrived while the host was behind another window.</summary>
    internal int UnseenWhileAway => _unseenWhileAway;

    /// <summary>Called by the constructor: listens to the host's focus.</summary>
    private void WireAttention()
    {
        _lastAttendedAt = _time.GetUtcNow();
        if (Host.Attention is not { } attention)
        {
            return;
        }

        _hostFocused = attention.Focused;
        attention.FocusChanged += focused => OnUiThread(() => OnHostFocusChanged(focused));
    }

    private void OnHostFocusChanged(bool focused)
    {
        _hostFocused = focused;
        if (focused)
        {
            _lastAttendedAt = _time.GetUtcNow();
            if (_unseenWhileAway > 0)
            {
                _unseenWhileAway = 0;
                RepublishTitle();
            }
        }
    }

    /// <summary>The player did something: they are here.</summary>
    private void NoteAttended() => _lastAttendedAt = _time.GetUtcNow();

    /// <summary>A line landed in a pane; counted for the title while the host is behind another window.</summary>
    private void NoteUnseenLine()
    {
        if (_hostFocused == false && _config.Text.AlertWhenAway)
        {
            _unseenWhileAway++;
            RepublishTitle();
        }
    }

    private void OnAttentionRequested(WorldSession session, AttentionEventArgs e)
    {
        KeepAlert(session.SessionKey, e.Reason, e.Text);
        if (!_config.Text.AlertWhenAway || Host.Attention is not { } host)
        {
            return;
        }

        var focused = host.Focused ?? _hostFocused;
        if (focused == true)
        {
            return;
        }

        if (focused is null && !host.FiltersByFocus && _time.GetUtcNow() - _lastAttendedAt < AwayAfter)
        {
            return;
        }

        _attentionThrottle ??= new AttentionThrottle(_time);
        if (!_attentionThrottle.TryTake())
        {
            return;
        }

        var who = SharpMUTerm.Client.Chrome.WindowTitle.For(session.Character?.Name, session.World.Name);
        var name = who.EndsWith(" — " + SharpMUTerm.Client.Chrome.WindowTitle.Product, StringComparison.Ordinal)
            ? who[..^(SharpMUTerm.Client.Chrome.WindowTitle.Product.Length + 3)]
            : who;
        var why = e.Reason switch
        {
            AttentionReason.Page => "page",
            AttentionReason.Name => "mentioned you",
            _ => "alert",
        };
        var request = new AttentionRequest($"{name} — {why}", e.Text);
        AttentionSent.Add(request);
        host.Request(request);
    }
}
