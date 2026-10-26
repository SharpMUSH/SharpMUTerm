using SharpConsoleUI.Parsing;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>What activating an output-pane link does — decided by the payload's scheme, never by the
/// text a server put in it.</summary>
internal enum LinkAction
{
    /// <summary>
    /// A scheme <see cref="LinkPayload.For"/> never writes. Refused out loud, never guessed at: the
    /// fallback that treated an unrecognised payload as a URL is how a forged <c>mux:send:</c> got to
    /// be sendable in the first place.
    /// </summary>
    Unknown,

    /// <summary>Send <see cref="LinkPayload.Target"/> to the world the clicked window belongs to.</summary>
    Send,

    /// <summary>Put <see cref="LinkPayload.Target"/> on that window's command line without sending it.</summary>
    Prompt,

    /// <summary>Open <see cref="LinkPayload.Target"/> in the web view.</summary>
    Web,
}

/// <summary>
/// The <c>[link=…]</c> payloads the output panes carry, in both directions: <see cref="For"/> writes one
/// from a parsed <see cref="SpanInteraction"/>, and <see cref="Parse"/> reads it back when the click
/// arrives. Generation and interpretation live in one file on purpose — they are a single round trip,
/// and a change to one that is not matched in the other is a security defect rather than a cosmetic
/// one.
/// <para>
/// <strong>Every payload is tagged with a scheme chosen from the interaction's
/// <see cref="InteractionKind"/>, and no branch passes server-supplied text through bare.</strong>
/// That is the whole security property, and it does <em>not</em> come from the prefixes being unusual
/// or hard to guess — a world can put any characters it likes in an <c>&lt;A HREF&gt;</c>, including
/// these. What a world cannot choose is which <see cref="InteractionKind"/> its markup parses to:
/// <c>&lt;SEND&gt;</c> becomes <see cref="InteractionKind.SendCommand"/> and <c>&lt;A HREF&gt;</c>
/// becomes <see cref="InteractionKind.Hyperlink"/>, decided by <c>MxpParser</c>/<c>PuebloParser</c>.
/// Wrapping the hyperlink case in a scheme of its own is what makes the namespaces disjoint.
/// </para>
/// <para>
/// It was one layer short. The hyperlink case used to emit the server's <c>href</c> verbatim, so
/// <c>&lt;A HREF="mux:send:@shutdown"&gt;</c> produced a payload byte-identical to a genuine
/// <c>&lt;SEND&gt;</c> and the click handler wrote it to the wire — defeating exactly the distinction
/// MXP keeps <c>&lt;A&gt;</c> and <c>&lt;SEND&gt;</c> apart for, and <c>PROMPT</c> with it. A legitimate
/// <c>&lt;SEND "look"&gt;</c> still produces a sendable link; that is MXP working, and is not what this
/// guards. <strong>Do not re-introduce a bare passthrough for any kind added later.</strong>
/// </para>
/// <para>
/// The escaping is the framework's <see cref="LinkUrl"/>, whose <c>Escape</c>/<c>Unescape</c> are exact
/// inverses — and <c>MarkupParser</c> already runs <c>Unescape</c> over a <c>[link=…]</c> payload before
/// raising <c>LinkClicked</c>, so a target reaches <see cref="Parse"/> byte-for-byte: query strings,
/// <c>&amp;</c>, <c>=</c>, <c>#</c>, a literal <c>%</c>, an already-percent-encoded <c>%20</c>, Unicode,
/// and any length. It escapes <c>[</c> and <c>]</c> too, which is not cosmetic: both that parser and our
/// own <c>MarkupWidth</c> read a tag by scanning to the next <c>]</c>, so an unescaped one in an
/// <c>href</c> would close the tag early and let a server inject markup of its own.
/// </para>
/// </summary>
internal readonly record struct LinkPayload(LinkAction Action, string Target)
{
    /// <summary>
    /// An MXP <c>&lt;SEND&gt;</c> menu's entries, first one first; null for a link with one target. The
    /// <see cref="Action"/> and <see cref="Target"/> are the first entry's, so a click is unchanged.
    /// </summary>
    internal IReadOnlyList<SendChoice>? Choices { get; init; }

    /// <summary>A <c>&lt;SEND&gt;</c> menu; the menu's own entries are percent-encoded inside it.</summary>
    internal const string MenuScheme = "mux:menu:";

    /// <summary>The same with <c>PROMPT</c>.</summary>
    internal const string MenuPromptScheme = "mux:menu-prompt:";

    /// <summary>MXP <c>&lt;SEND&gt;</c> / Pueblo <c>xch_cmd</c>: a command to write to the wire.</summary>
    internal const string SendScheme = "mux:send:";

    /// <summary>The same with MXP's <c>PROMPT</c> keyword: onto the command line, not to the wire.</summary>
    internal const string PromptScheme = "mux:prompt:";

    /// <summary>
    /// MXP <c>&lt;A HREF&gt;</c>, Pueblo's <c>&lt;A HREF&gt;</c> without <c>xch_cmd</c>, and the web
    /// view's own anchors — everything whose target is a URL to fetch rather than text to send.
    /// </summary>
    internal const string WebScheme = "mux:web:";

    /// <summary>A payload nothing here wrote, and so nothing here acts on.</summary>
    internal static LinkPayload None => new(LinkAction.Unknown, string.Empty);

    /// <summary>
    /// The payload for a span's interaction, or null when the span is not clickable. See the type's own
    /// remarks for why each kind gets a scheme and none is passed through bare.
    /// </summary>
    internal static string? For(SpanInteraction? interaction) => interaction?.Kind switch
    {
        InteractionKind.SendCommand when interaction.Choices is { Count: > 1 } choices =>
            (interaction.PromptOnly ? MenuPromptScheme : MenuScheme) + LinkUrl.Escape(EncodeChoices(choices)),
        InteractionKind.SendCommand when interaction.PromptOnly => PromptScheme + LinkUrl.Escape(interaction.Target),
        InteractionKind.SendCommand => SendScheme + LinkUrl.Escape(interaction.Target),
        InteractionKind.Hyperlink => WebScheme + LinkUrl.Escape(interaction.Target),
        _ => null,
    };

    /// <summary>
    /// Reads a clicked link back into an action and its target. The <paramref name="url"/> is what a
    /// <c>LinkClicked</c> handler is given, i.e. already through <see cref="LinkUrl.Unescape"/> —
    /// <see cref="Delivered"/> is the same thing starting from what <see cref="For"/> wrote.
    /// <para>
    /// Anything not carrying one of the three schemes is <see cref="LinkAction.Unknown"/>. There is no
    /// "probably a URL" arm, deliberately: an untagged payload cannot have come from
    /// <see cref="For"/>, so acting on it means acting on text of unknown provenance.
    /// </para>
    /// </summary>
    internal static LinkPayload Parse(string? url) => url switch
    {
        not null when url.StartsWith(MenuScheme, StringComparison.Ordinal) => Menu(LinkAction.Send, url[MenuScheme.Length..]),
        not null when url.StartsWith(MenuPromptScheme, StringComparison.Ordinal) => Menu(LinkAction.Prompt, url[MenuPromptScheme.Length..]),
        not null when url.StartsWith(SendScheme, StringComparison.Ordinal) =>
            new(LinkAction.Send, url[SendScheme.Length..]),
        not null when url.StartsWith(PromptScheme, StringComparison.Ordinal) =>
            new(LinkAction.Prompt, url[PromptScheme.Length..]),
        not null when url.StartsWith(WebScheme, StringComparison.Ordinal) =>
            new(LinkAction.Web, url[WebScheme.Length..]),
        _ => None,
    };

    /// <summary>
    /// What <see cref="For"/> wrote, as a click delivers it: the framework's own unescape and then
    /// <see cref="Parse"/>. It exists so the round trip can be asserted without rendering a frame —
    /// the step in the middle is <c>MarkupParser</c>'s, not ours, and a test that skipped it would be
    /// checking a chain the app does not use.
    /// </summary>
    internal static LinkPayload Delivered(string emitted) => Parse(LinkUrl.Unescape(emitted));

    /// <summary>
    /// <c>label,command|label,command</c>, each part through <see cref="Uri.EscapeDataString"/>, which leaves
    /// neither <c>,</c> nor <c>|</c> unescaped — so the separators cannot come from a server's text.
    /// </summary>
    private static string EncodeChoices(IReadOnlyList<SendChoice> choices) =>
        string.Join('|', choices.Select(c => Uri.EscapeDataString(c.Label) + "," + Uri.EscapeDataString(c.Command)));

    private static LinkPayload Menu(LinkAction action, string encoded)
    {
        var choices = new List<SendChoice>();
        foreach (var part in encoded.Split('|'))
        {
            var comma = part.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0)
            {
                return None;
            }

            choices.Add(new SendChoice(Uri.UnescapeDataString(part[..comma]), Uri.UnescapeDataString(part[(comma + 1)..])));
        }

        return choices.Count == 0 ? None : new LinkPayload(action, choices[0].Command) { Choices = choices };
    }
}
