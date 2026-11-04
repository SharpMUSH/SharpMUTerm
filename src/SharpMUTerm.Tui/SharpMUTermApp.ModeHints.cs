using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Tui;

/// <summary>
/// The status row's mode-aware key hints. While the focused pane is scrolled back, split, frozen, carrying the ⌃F
/// result bar, or a docked composer holds the keyboard, the row's hint names that mode's keys instead of the pane
/// and tab chords — the way out of where you are outranks the way around. The keys come from
/// <see cref="ModeHints"/> (Client), resolved through this host's spellings, so the row cannot name a chord the host
/// keeps for itself; the hint goes in only where it fits, like every other droppable cell on the row.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The mode the status row last named the keys of, so a change of mode alone repaints it.</summary>
    private StatusMode _statusModeShown;

    /// <summary>
    /// The mode the keyboard is in, as far as the status row is concerned, the highest first: an armed composer, the
    /// focused window carrying the search bar, the focused pane frozen, split, or scrolled back. Internal for tests.
    /// </summary>
    internal StatusMode CurrentStatusMode()
    {
        if (ComposerArmed)
        {
            return StatusMode.Composer;
        }

        var pane = _workspace.Layout.FocusedPane;
        var windowId = pane.ActiveTab ?? MainWindowId;
        if (_searchMark is { } mark && string.Equals(mark.WindowId, windowId, StringComparison.Ordinal))
        {
            return StatusMode.SearchResult;
        }

        if (pane.Frozen)
        {
            return StatusMode.Frozen;
        }

        if (IsScrollSplit(windowId))
        {
            return StatusMode.SplitScrollback;
        }

        return ScrollTarget() is { CanScrollDown: true } ? StatusMode.ScrolledBack : StatusMode.None;
    }

    /// <summary>
    /// The row's candidates for <paramref name="mode"/>, longest first, as markup: each key in the client's notice
    /// voice and what it does dimmed, so the way out reads at a glance. Every candidate is exactly as wide as the
    /// plain text <see cref="ModeHints.Candidates"/> gives, which is what the row measures.
    /// </summary>
    private List<string> ModeHintMarkup(StatusMode mode)
    {
        var keys = HostKeyGlyphs();
        var hints = ModeHints.For(mode)
            .Select(h => $"[{_ink.Notice}]{MarkupText.Escape(Client.Palette.KeyGlyphs.Normalize(h.Key.Resolve(keys)))}[/] [dim]{MarkupText.Escape(h.Does)}[/]")
            .ToList();
        var candidates = new List<string>(hints.Count);
        for (var n = hints.Count; n > 0; n--)
        {
            candidates.Add(string.Join("[dim] · [/]", hints.Take(n)));
        }

        return candidates;
    }

    /// <summary>
    /// Repaints the status row when the mode has changed since it was drawn. Rides the frame as well as the places
    /// that change a mode, because a mode can begin with no key of ours (a click in a composer, the wheel). Never
    /// displaces a notice: <see cref="SetStatus"/> keeps it and the resting row comes back with the hint.
    /// </summary>
    private void SyncStatusMode()
    {
        if (CurrentStatusMode() != _statusModeShown)
        {
            RefreshStatusRow();
        }
    }
}
