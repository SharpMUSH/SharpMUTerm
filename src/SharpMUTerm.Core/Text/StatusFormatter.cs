namespace SharpMUTerm.Core.Text;

/// <summary>
/// Builds the design's input-prompt string and its right-hand gutter (destination window + other
/// windows holding drafts + character count), both bound to the focused character. Pure and
/// UI-agnostic; the view colours/positions the pieces.
/// </summary>
public static class StatusFormatter
{
    /// <summary>
    /// What goes between the character and the world: the client's one spelling of a character's name,
    /// <c>Corvid · Aetherfall</c> (<c>SharpMUTerm.Client.Chrome.CharacterNames</c>, which Core cannot reference — a
    /// Client test holds the two together).
    /// </summary>
    public const string NameSeparator = " · ";

    /// <summary>The prompt label, e.g. <c>Corvid · Aetherfall ›</c>. Falls back gracefully when disconnected.</summary>
    public static string CharacterPrompt(string? character, string? world)
    {
        if (string.IsNullOrEmpty(character))
        {
            return "› ";
        }

        return string.IsNullOrEmpty(world) ? $"{character} › " : $"{character}{NameSeparator}{world} › ";
    }
}
