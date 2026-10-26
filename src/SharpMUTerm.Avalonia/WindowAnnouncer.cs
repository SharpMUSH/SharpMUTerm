using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using SharpMUTerm.Core.Accessibility;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's half of <see cref="IAnnouncer"/>: a live region a screen reader watches (UI Automation on Windows,
/// AT-SPI on Linux), which takes each announcement as its text. Output is polite, so a reader finishes what it was
/// saying; the client's own messages are assertive, because they are usually about the key just pressed.
/// </summary>
/// <remarks>
/// The client calls <see cref="Announce"/> on its own thread; the region is Avalonia's, so the text is posted to
/// Avalonia's thread. Nothing is kept: a reader that wants history has the transcript and the scrollback.
/// </remarks>
internal sealed class WindowAnnouncer : IAnnouncer
{
    private TextBlock? _output;
    private TextBlock? _notices;

    /// <summary>The two regions, built once for the window that shows them.</summary>
    public IReadOnlyList<Control> Regions()
    {
        _output = Region("Output", AutomationLiveSetting.Polite);
        _notices = Region("Client messages", AutomationLiveSetting.Assertive);
        return [_output, _notices];
    }

    public void Announce(string text, AnnouncementKind kind)
    {
        var target = kind == AnnouncementKind.Output ? _output : _notices;
        if (target is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            // The same text twice in a row is not a change, so a repeated line would go unspoken without this.
            target.Text = target.Text == text ? text + " " : text;
        });
    }

    /// <summary>A region a reader finds and a sighted user does not see: no size, no hit testing, never painted.</summary>
    private static TextBlock Region(string name, AutomationLiveSetting live)
    {
        var block = new TextBlock
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
        };
        AutomationProperties.SetName(block, name);
        AutomationProperties.SetLiveSetting(block, live);
        return block;
    }
}
