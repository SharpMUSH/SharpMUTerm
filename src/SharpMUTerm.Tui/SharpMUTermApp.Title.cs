using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Tui;

/// <summary>
/// The window's title: <c>Character@World — SharpMUTerm</c> for what is in front (<see cref="WindowTitle"/>), handed
/// to the host's <see cref="ClientHost.Title"/> — the desktop window's title bar, the terminal's OSC 2. Worked out
/// whenever the header is, from the header's own model, so the title and the header always name the same character;
/// the header is rebuilt on every activation, connect and disconnect.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private string? _shownTitle;

    // Nothing is written to the host before Run: a terminal is not ours to write to until the loop has it.
    private bool _titleLive;

    /// <summary>What was last handed to the host, for the tests; null before the first header.</summary>
    internal string? ShownTitle => _shownTitle;

    // The title for what is in front, before the unread count (SharpMUTermApp.Attention.cs) is put in front of it.
    private string? _baseTitle;

    private void PublishTitle(HeaderModel header)
    {
        _baseTitle = SharpMUTerm.Client.Chrome.WindowTitle.For(header);
        RepublishTitle();
    }

    /// <summary>Hands the host the title as it stands: the header's, led by the unread count while away.</summary>
    private void RepublishTitle()
    {
        if (_baseTitle is not { } baseTitle)
        {
            return;
        }

        var title = SharpMUTerm.Client.Chrome.WindowTitle.WithUnread(baseTitle, _unseenWhileAway);
        if (title == _shownTitle)
        {
            return;
        }

        _shownTitle = title;
        if (_titleLive)
        {
            Host.Title?.Show(title);
        }
    }

    /// <summary>Called by <see cref="Run"/>: from now on the title goes to the host, starting with the one already worked out.</summary>
    internal void StartTitle()
    {
        _titleLive = true;
        if (_shownTitle is { } title)
        {
            Host.Title?.Show(title);
        }
    }
}
