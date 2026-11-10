using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;

namespace SharpMUTerm.Tui;

/// <summary>
/// Text size and zoom, where the host draws its own text (<see cref="ClientHost.TextSize"/>: the desktop window). The
/// sizes are the configuration's <see cref="AppConfiguration.Display"/>: the Text size page edits them through the
/// settings funnel like any setting, the window's Ctrl+= / Ctrl+- / Ctrl+0 and Ctrl+wheel and ⌃P's zoom entries step the
/// zoom here, and the host is told the result. A terminal has none of it: its font size is its emulator's, and nothing an
/// application writes changes it (xterm's OSC 50 picks a font, not a size, and is off in every terminal this client
/// targets), so the client lists nothing that would do nothing and the help topic says to use the terminal's own zoom.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Whether the host draws its own text and so can size it.</summary>
    internal bool CanSizeText => Host.TextSize is not null;

    /// <summary>The sizes last said to the host; null before the first, so the host is always told once.</summary>
    private DisplaySettings? _textSizesShown;

    /// <summary>The sizes in force, held to their ranges.</summary>
    internal DisplaySettings TextSizes => _config.Display.Clamped();

    /// <summary>
    /// Whether the Text size &amp; zoom page is offered: the host sizes its own text and draws the page natively. The
    /// window's <c>--emulated</c> mode draws everything in cells and has no native settings window, so it keeps the zoom
    /// (keys, wheel, right-click menu, ⌃P) and takes the output's size from <c>--font-size</c>, with no page.
    /// </summary>
    internal bool HasTextSizePage => Host.TextSize is { SizesAreas: true } && Host.NativeSettings is not null;

    /// <summary>Registers the Text size page and listens for the host's zoom keys, where the host can size text.</summary>
    private void RegisterTextSize()
    {
        if (Host.TextSize is not { } host)
        {
            return;
        }

        if (HasTextSizePage)
        {
            _contributions.SettingsSections.Add(BuiltInSettings.TextSize);
        }

        host.ZoomRequested += step => OnUiThread(() => StepZoom(step));
    }

    /// <summary>Opens the Text size &amp; zoom page in the host's settings window (⌃P's entry).</summary>
    private bool OpenTextSizePage()
    {
        if (!HasTextSizePage)
        {
            Notice("text size is your terminal's own here: use its zoom (often Ctrl+= and Ctrl+-)", toast: false);
            return false;
        }

        return TryOpenNativeSettings(Section(BuiltInSettings.TextSizeSectionId));
    }

    /// <summary>
    /// Steps the zoom (in for a positive <paramref name="step"/>, out for a negative one, back to 100% for zero), saves it
    /// and tells the host. Saved through <see cref="PersistConfiguration"/>, the view-preference path, because the settings
    /// funnel re-periodises every timer and a zoom is pressed in runs. Refused out loud where the host draws no text.
    /// </summary>
    internal bool StepZoom(int step)
    {
        if (Host.TextSize is null)
        {
            Notice("text size is your terminal's own here: use its zoom (often Ctrl+= and Ctrl+-)", toast: false);
            return false;
        }

        var before = ZoomLevels.Clamp(_config.Display.Zoom);
        var after = ZoomLevels.Stepped(before, step);
        if (after == before && _config.Display.Zoom == before)
        {
            Notice(step > 0 ? $"zoom is at its largest, {ZoomLevels.Label(after)}"
                : step < 0 ? $"zoom is at its smallest, {ZoomLevels.Label(after)}"
                : "zoom is already 100%", MessageSeverity.Info, toast: false);
            SyncTextSizes();
            return true;
        }

        _config.Display.Zoom = after;
        PersistConfiguration();
        SyncTextSizes();
        Notice($"zoom {ZoomLevels.Label(after)}", MessageSeverity.Info, key: step == 0 ? "⌃0" : step > 0 ? "⌃=" : "⌃-", toast: false);
        return true;
    }

    /// <summary>
    /// Tells the host the sizes when they differ from what it was last told: after every settings commit, every zoom step
    /// and every frame (a frame is cheap to check and covers a change made any other way, such as an undo).
    /// </summary>
    private void SyncTextSizes()
    {
        if (Host.TextSize is not { } host)
        {
            return;
        }

        var sizes = TextSizes;
        if (_textSizesShown is { } shown && Same(shown, sizes))
        {
            return;
        }

        _textSizesShown = sizes;
        host.Show(sizes);

        // A vitals card is as tall as its gauges, which are drawn at the panel size: a change there can need other rows.
        foreach (var windowId in _vitalsCardRows.Keys.ToArray())
        {
            ResizeVitalsCard(windowId);
        }
    }

    private static bool Same(DisplaySettings a, DisplaySettings b) =>
        a.Zoom == b.Zoom && a.OutputPoints == b.OutputPoints && a.ComposerText == b.ComposerText
        && a.InterfaceText == b.InterfaceText && a.PanelText == b.PanelText && a.DialogText == b.DialogText;
}
