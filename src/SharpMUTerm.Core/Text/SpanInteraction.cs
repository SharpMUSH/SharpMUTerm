namespace SharpMUTerm.Core.Text;

/// <summary>What activating an interactive span does.</summary>
public enum InteractionKind
{
    /// <summary>Send a command to the server (MXP <c>&lt;SEND&gt;</c>, Pueblo <c>xch_cmd</c>).</summary>
    SendCommand,

    /// <summary>Open a hyperlink (MXP <c>&lt;A HREF&gt;</c>, Pueblo <c>&lt;A&gt;</c>).</summary>
    Hyperlink,
}

/// <summary>
/// Makes a <see cref="StyledSpan"/> clickable: a command to send or a URL to open, plus an
/// optional hint (tooltip). Produced by the MXP and Pueblo parsers and realised by the UI
/// (mouse/keyboard activation). UI-agnostic.
/// </summary>
public sealed record SpanInteraction(
    InteractionKind Kind,
    string Target,
    string? Hint = null,
    bool PromptOnly = false)
{
    /// <summary>
    /// A picture this span stands in for (MXP <c>&lt;IMAGE&gt;</c>), drawn under the line when the
    /// terminal can draw one. Independent of <see cref="Kind"/>: an image inside a <c>&lt;SEND&gt;</c>
    /// still sends its command when clicked, and a bare one opens the picture. Not persisted by
    /// <see cref="StyledLineCodec"/>, so a restored line comes back as its link and nothing more.
    /// </summary>
    public InlineImageRequest? Image { get; init; }

    /// <summary>A clickable command. <paramref name="promptOnly"/> puts it on the input line instead of sending.</summary>
    public static SpanInteraction Command(string command, string? hint = null, bool promptOnly = false) =>
        new(InteractionKind.SendCommand, command, hint, promptOnly);

    /// <summary>A hyperlink.</summary>
    public static SpanInteraction Link(string url, string? hint = null) =>
        new(InteractionKind.Hyperlink, url, hint);
}
