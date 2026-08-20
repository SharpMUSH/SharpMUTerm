using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// Renders the row that carries the server's own prompt, directly above the command line.
/// </summary>
/// <remarks>
/// <para>
/// A prompt is not a line of output and is deliberately not routed to one: a game that ends every
/// turn with <c>HP:100 MP:50 &gt;</c> would double its own output into the scrollback and the
/// transcript, which is why <c>WorldSession.OnOutputReceived</c> hands prompts to
/// <c>CurrentPrompt</c> instead. But nothing read that property, so a prompt this client received
/// perfectly was displayed nowhere — a login screen that ends its question with <c>IAC GA</c> and
/// waits looked like a server that had stopped answering. This is the missing reader.
/// </para>
/// <para>
/// It is <b>always exactly one row</b>, elided rather than wrapped, and that is a layout property
/// rather than a cosmetic one. Every sticky row is reserved before the workspace is measured, so a
/// band whose height followed the length of whatever the server last sent would take rows off every
/// pane — and per-pane NAWS is derived from the pane rectangles, so each such change re-announces a
/// new terminal size to every connected game. This is the same rule the rail's reserved badge fields
/// and the status row's capped scrollback count follow: <b>chrome measured from wire data must not
/// change size</b>. The row appearing at all still costs one row once, when the first prompt of a
/// session arrives; after that it only ever changes text.
/// </para>
/// <para>
/// The band is the <em>idle</em> input band, not the armed one. The armed band means "⏎ sends from
/// here", the prompt row is not somewhere you can type, and painting it in the armed tone would put
/// a second meaning on the one cue that answers which bar has the keyboard. It is the
/// <em>untinted</em> idle band: the bar immediately below already wears the focused character's hue,
/// and a second tinted band stacked on it would say the same thing twice.
/// </para>
/// </remarks>
internal static class PromptRowRenderer
{
    /// <summary>What the row shows before a prompt has ever arrived, and after a disconnect.</summary>
    internal const string Empty = "";

    /// <summary>
    /// Builds the row's markup.
    /// </summary>
    /// <param name="prompt">The server's prompt, or null when there is none to show.</param>
    /// <param name="formatter">
    /// Turns the styled line into markup. It must be one whose plane is <paramref name="bandHex"/>,
    /// or the game's colours are held to a contrast floor against a fill they are never read on.
    /// </param>
    /// <param name="width">The row's width in cells. Anything longer is elided to fit.</param>
    /// <param name="bandHex">The band the row is painted on, as <c>#rrggbb</c>.</param>
    /// <returns>Markup for exactly one row, or <see cref="Empty"/> when there is nothing to show.</returns>
    public static string Row(StyledLine? prompt, MarkupFormatter formatter, int width, string bandHex)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentException.ThrowIfNullOrEmpty(bandHex);

        if (prompt is null || prompt.IsEmpty || width <= 0)
        {
            return Empty;
        }

        // A prompt may legitimately contain a newline — a server is free to send one inside what it
        // then terminates with GA — and a row that is one row by construction cannot show the second.
        // Take the last segment: what a prompt asks is at its end, and the earlier part was already
        // delivered as output.
        var text = prompt.Text;
        var lastBreak = text.LastIndexOfAny(['\n', '\r']);
        var single = lastBreak >= 0
            ? StyledText.Truncate(Skip(prompt, lastBreak + 1), width)
            : StyledText.Truncate(prompt, width);

        if (single.IsEmpty)
        {
            return Empty;
        }

        // Padded to the full width inside the background tag rather than left ragged: the band has to
        // reach the right edge or the row reads as a stray coloured word floating on the backdrop
        // instead of as part of the input area. The pad is plain spaces, so it costs no cells beyond
        // the width already reserved.
        var pad = new string(' ', Math.Max(0, width - single.Text.Length));
        return $"[on {bandHex}]{formatter.ToMarkup(single)}{pad}[/]";
    }

    /// <summary>
    /// The line from <paramref name="start"/> onwards, spans and styles intact.
    /// </summary>
    /// <remarks>
    /// <see cref="StyledText"/> has no drop-the-head helper and this is the only caller that wants
    /// one, so it stays here rather than widening a Core API for a single use.
    /// </remarks>
    private static StyledLine Skip(StyledLine line, int start)
    {
        if (start <= 0)
        {
            return line;
        }

        var spans = new List<StyledSpan>();
        var seen = 0;

        foreach (var span in line.Spans)
        {
            var end = seen + span.Length;
            if (end > start)
            {
                var from = Math.Max(0, start - seen);
                spans.Add(new StyledSpan(span.Text[from..], span.Style, span.Interaction));
            }

            seen = end;
        }

        return new StyledLine(spans, line.RuleColor);
    }
}
