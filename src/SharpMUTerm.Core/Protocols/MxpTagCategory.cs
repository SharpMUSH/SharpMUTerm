namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// Which MXP tags may appear on an open line.
/// </summary>
/// <remarks>
/// The spec draws the line in one sentence — "Only the tags described in this section are OPEN tags.
/// All other MXP tags are SECURE tags" — so this is an allow-list and must stay one. A deny-list
/// would make every tag added to the spec in future secure-by-omission in the wrong direction.
/// </remarks>
public static class MxpTagCategory
{
    private static readonly HashSet<string> OpenTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "B", "BOLD", "STRONG",
        "I", "ITALIC", "EM",
        "U", "UNDERLINE",
        "S", "STRIKEOUT",
        "C", "COLOR",
        "H", "HIGH",
        "FONT",
    };

    /// <summary>
    /// True when <paramref name="tagName"/> is an open tag.
    /// </summary>
    /// <param name="tagName">
    /// The bare element name: no angle brackets, no attributes, and no leading slash. A closing tag
    /// takes the category of the element it closes, so the caller strips the slash first.
    /// </param>
    public static bool IsOpen(string tagName) => OpenTags.Contains(tagName);
}
