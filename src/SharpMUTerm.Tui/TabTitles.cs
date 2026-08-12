using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// Formats a window's tab-strip label from its <see cref="WorkspaceWindow"/> state: the title, an
/// unread badge like <c>(3)</c> while the tab is in the background, a <c>✎</c> pen when it holds an
/// unsent input draft, and a <c>⌁</c> when the window belongs to a <em>different</em> character than
/// the one currently focused. Pure so it can be unit-tested without a terminal.
/// </summary>
/// <remarks>
/// <para><b>A tab title is markup.</b> Every width the strip is measured by — the header paint, its
/// desired width, and the click hit test that picks the tab and its <c>×</c> — is
/// <c>MarkupParser.StripLength</c>, so a tag here costs no cells and moves no hit test. It also means
/// configured and world-supplied text must be escaped on the way in: a window titled <c>[Chat]</c>
/// would otherwise be eaten as a tag by the parser and by the hit test alike.</para>
/// <para>The close affordance is <em>not</em> here. A <c>✕</c> in the label is just text the hit test
/// reads as part of the title; the real button is <c>TabPage.IsClosable</c>.</para>
/// </remarks>
/// <summary>
/// The plane an idle tab's chip is painted on and the ink that lands on it, as <c>#rrggbb</c> markup
/// tokens. It exists because <c>TabControl</c>'s four chip colours are properties of the <em>control</em>
/// — one answer for every unselected tab in a strip — so a tab that wants to say whose window it is has
/// to say it in the only per-tab channel there is: its title, which is markup.
/// </summary>
internal readonly record struct TabChip(string Plane, string Ink);

internal static class TabTitles
{
    /// <param name="window">The window the tab stands for.</param>
    /// <param name="focusedCharacterKey">
    /// The session key of the character in focus, so a window belonging to another one can be marked.
    /// </param>
    /// <param name="focusedPane">
    /// Whether this is the focused pane's tab, which earns a leading <c>▌</c> — the shape half of the
    /// focus cue, for a terminal where the lit plane behind it flattens.
    /// </param>
    /// <param name="selected">
    /// Whether this is the tab its pane is showing, emboldened in <em>every</em> pane so the selection
    /// survives a flattened palette. Independent of <paramref name="focusedPane"/>: an unfocused pane
    /// still has a tab in front of it.
    /// </param>
    /// <param name="chip">
    /// The plane this tab is drawn on when it is <em>not</em> the one its pane is showing, so a pane
    /// holding two characters' windows says whose each background tab is. Ignored on the selected tab,
    /// which the strip paints in its page's own plane.
    /// </param>
    public static string For(
        WorkspaceWindow window,
        string? focusedCharacterKey = null,
        bool focusedPane = false,
        bool selected = false,
        ChromeInk? ink = null,
        TabChip? chip = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        // A child window carries its owner as a prefix so it stays traceable once dragged into another
        // pane; a character's own main window needs none, the focused-character context names it.
        var owner = window.Kind != WindowKind.Main && !string.IsNullOrEmpty(window.OwnerLabel)
            ? MarkupText.Escape(window.OwnerLabel) + " - "
            : string.Empty;

        // Capped through the sidebar's own formatter, so the two surfaces reading one number cannot print
        // different answers and a count from the wire cannot push a narrow strip's later tabs off the end.
        var unread = window.Unread > 0 ? $" ({UnreadBadge.Format(window.Unread)})" : string.Empty;
        var pen = window.HasUnsentInput ? $" {Glyphs.Draft}" : string.Empty;

        // ⌁ marks a window owned by a character other than the focused one.
        var cross = focusedCharacterKey is not null
                    && window.SessionKey is not null
                    && !string.Equals(window.SessionKey, focusedCharacterKey, StringComparison.Ordinal)
            ? " ⌁"
            : string.Empty;

        var focus = focusedPane ? Glyphs.FocusedPane + " " : string.Empty;

        // Tint and weight cover the name and count only: the ▌ ahead and the ✎ / ⌁ behind are other
        // facts. One tag rather than two nested, because a selected tab can also be unread.
        var named = owner + MarkupText.Escape(window.Title) + unread;
        var style = (selected, window.Unread > 0) switch
        {
            (true, true) => $"bold {UnreadBadge.TintFor(ink)}",
            (true, false) => "bold",
            (false, true) => UnreadBadge.TintFor(ink),
            _ => null,
        };

        // The chip is the tab's own plane, which only an unselected tab carries — the selected one is
        // painted by the strip, in the plane its page is on. Foreground first so an unread tab keeps its
        // accent: the plane says whose window this is, the accent says it has something new.
        if (chip is { } tile && !selected)
        {
            style = $"{style ?? tile.Ink} on {tile.Plane}";
        }
        var body = style is null ? named : $"[{style}]{named}[/]";

        return focus + body + pen + cross;
    }
}
