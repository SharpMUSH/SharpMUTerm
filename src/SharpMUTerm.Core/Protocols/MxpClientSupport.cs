using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// What this client says about its MXP: its <c>&lt;VERSION&gt;</c> and the <c>&lt;SUPPORT&gt;</c> answer. The telnet
/// layer answers both itself, because the library takes those lines out of the stream before the reader sees them.
/// </summary>
/// <remarks>
/// <b>An honest list, and deliberately not an aspirational one:</b> a SUPPORTS answer is a claim a server acts on, so
/// naming a tag nothing here acts on makes it send markup that shows as nothing. A tag is listed when MarkupString
/// reads it <em>and</em> <see cref="SharpMUTerm.Core.Text.MarkupLineParser"/> does something with what it becomes.
/// </remarks>
public static class MxpClientSupport
{
    /// <summary>What this client answers a VERSION request with.</summary>
    public const string ClientName = GmcpHandshake.ClientName;

    /// <summary>The MXP revision a VERSION reply names.</summary>
    public const string MxpRevision = "1.0";

    /// <summary>The style-sheet version a VERSION reply names: none is ever in effect.</summary>
    public const string StyleVersion = "0";

    /// <summary>The client version a VERSION reply names: this build's, as MinVer stamped it.</summary>
    public static string ClientVersion => GmcpHandshake.ClientVersion;

    /// <summary>The tags this client renders, by name.</summary>
    public static IReadOnlyList<string> SupportedTags { get; } =
        ["b", "i", "u", "s", "h", "color", "font", "send", "a", "br", "image",
         "!element", "!entity", "var", "gauge", "stat", "sound", "music", "frame", "dest"];

    /// <summary>
    /// The attributes acted on, by tag, for a <c>&lt;SUPPORT tag.attr&gt;</c> or <c>&lt;SUPPORT tag.*&gt;</c>. The same
    /// honesty rule as <see cref="SupportedTags"/>: <c>image.ismap</c>, read and ignored, is not listed.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> SupportedAttributes { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["color"] = ["fore", "back"],
            ["send"] = ["href", "hint", "prompt"],
            ["a"] = ["href", "hint"],
            ["image"] = ["fname", "url", "t", "w", "h"],
            ["gauge"] = ["entity", "max", "caption"],
            ["stat"] = ["entity", "max", "caption"],
            ["sound"] = ["fname", "v", "l", "p", "t", "u"],
            ["music"] = ["fname", "v", "l", "c", "t", "u"],
            ["frame"] = ["name", "action", "title"],
            ["dest"] = ["name"],
        };

    /// <summary>The same table as MarkupString takes it.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Supports { get; } =
        SupportedTags.ToDictionary(
            tag => tag,
            tag => (IReadOnlyList<string>)(SupportedAttributes.GetValueOrDefault(tag) ?? []),
            StringComparer.OrdinalIgnoreCase);

    private static readonly MarkupString.Mxp.Decoding.MxpOptions Options = new() { Supports = Supports };

    /// <summary>
    /// The answer to a <c>&lt;SUPPORT&gt;</c> that asked about <paramref name="asked"/>: every tag in
    /// <see cref="SupportedTags"/> when it asked about nothing, otherwise each entry it named, as one this client
    /// renders or one it does not. <c>tag.attr</c> is supported when <see cref="SupportedAttributes"/> lists it, and
    /// <c>tag.*</c> is answered with each attribute listed for the tag; a tag this client does not render is answered
    /// <c>-tag.*</c>.
    /// </summary>
    public static (IReadOnlyList<string> Supported, IReadOnlyList<string> Unsupported) AnswerSupport(IReadOnlyList<string> asked) =>
        Options.Answer(asked);
}
