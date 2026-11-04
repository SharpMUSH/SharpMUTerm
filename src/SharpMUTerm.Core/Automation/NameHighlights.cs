using System.Text;
using System.Text.RegularExpressions;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Automation;

/// <summary>One name a world highlights wherever it appears as a whole word, and the colour it is drawn in.</summary>
/// <param name="Name">The name, as typed (matched without regard to case).</param>
/// <param name="Colour">A colour name (<c>gold</c>, <c>red</c>) or <c>#rrggbb</c>.</param>
public sealed record NameHighlight(string Name, string Colour);

/// <summary>
/// A world's highlight-names list (F5 ▸ <c>highlight names</c>): friends, enemies, your own name, each drawn in its colour
/// wherever it appears as a whole word in that world's output. Written as one line, <c>Ashlyn=gold, Merrow=red</c>; a name
/// with no colour is <see cref="DefaultColour"/>. Applied at ingest, after the triggers (so a rule matches what the server
/// sent) and before emoji and links, so a spawn window and the main window show the same colours. The colour is a
/// foreground only, so the pane's legibility floor still holds it (the formatter measures it against the reading plane).
/// </summary>
public static class NameHighlights
{
    /// <summary>The colour a name with none is drawn in.</summary>
    public const string DefaultColour = "gold";

    /// <summary>How many names a list may hold; past it the rest are dropped.</summary>
    public const int MaxNames = 64;

    /// <summary>The longest name.</summary>
    public const int MaxNameLength = 40;

    /// <summary>
    /// Reads <c>Name=colour, Name=colour</c> (commas or semicolons between, <c>=</c> or <c>:</c> before a colour). Returns
    /// the list, or why the text is not one — a colour that is not a colour, a name too long or holding a control
    /// character. A blank text is the empty list.
    /// </summary>
    public static (IReadOnlyList<NameHighlight> Names, string? Error) Parse(string? text, Func<string, bool>? colourIsAllowed = null)
    {
        var names = new List<NameHighlight>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return (names, null);
        }

        foreach (var raw in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cut = raw.IndexOfAny(['=', ':']);
            var name = (cut < 0 ? raw : raw[..cut]).Trim();
            var colour = cut < 0 ? DefaultColour : raw[(cut + 1)..].Trim().ToLowerInvariant();
            if (name.Length == 0)
            {
                return (names, $"'{raw}' names nobody");
            }

            if (name.Length > MaxNameLength)
            {
                return (names, $"a name may be at most {MaxNameLength} characters");
            }

            if (name.Any(char.IsControl))
            {
                return (names, "a name cannot contain control characters");
            }

            if (colour.Length == 0)
            {
                colour = DefaultColour;
            }

            if (!WebColors.TryParse(colour, out _) || (colourIsAllowed is not null && !colourIsAllowed(colour)))
            {
                return (names, $"'{colour}' is not one of the highlight colours");
            }

            if (names.Count < MaxNames && !names.Any(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(new NameHighlight(name, colour));
            }
        }

        return (names, null);
    }

    /// <summary>The list as the one line <see cref="Parse"/> reads, or null for none.</summary>
    public static string? Format(IReadOnlyList<NameHighlight>? names) =>
        names is null || names.Count == 0
            ? null
            : string.Join(", ", names.Select(n => string.Equals(n.Colour, DefaultColour, StringComparison.OrdinalIgnoreCase) ? n.Name : $"{n.Name}={n.Colour}"));

    /// <summary>
    /// <paramref name="line"/> with every whole-word occurrence of a listed name drawn in its colour. A name is a whole
    /// word when no letter, digit or underscore touches either end, so <c>Ann</c> lights <c>Ann's</c> and not
    /// <c>Annabel</c>. Returns the line itself when nothing matched.
    /// </summary>
    public static StyledLine Apply(StyledLine line, Matcher? matcher)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (matcher is null || line.IsEmpty)
        {
            return line;
        }

        MatchCollection matches;
        try
        {
            matches = matcher.Pattern.Matches(line.Text);
        }
        catch (RegexMatchTimeoutException)
        {
            return line;
        }

        foreach (Match match in matches)
        {
            if (matcher.ColourOf(match.Value) is { } colour)
            {
                line = StyledText.Restyle(line, match.Index, match.Length, style => style.WithForeground(colour));
            }
        }

        return line;
    }

    /// <summary>The compiled form of a list, built once per list and reused for every line; null for an empty list.</summary>
    public static Matcher? Compile(IReadOnlyList<NameHighlight>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var colours = new Dictionary<string, TerminalColor>(StringComparer.OrdinalIgnoreCase);
        var pattern = new StringBuilder(@"(?<![\w])(?:");
        var first = true;

        // Longest first, so "Ann Marie" wins over "Ann" where both are listed.
        foreach (var name in names.Where(n => n.Name.Length > 0).OrderByDescending(n => n.Name.Length))
        {
            if (!WebColors.TryParse(name.Colour, out var colour) || !colours.TryAdd(name.Name, colour))
            {
                continue;
            }

            pattern.Append(first ? string.Empty : "|").Append(Regex.Escape(name.Name));
            first = false;
        }

        if (first)
        {
            return null;
        }

        pattern.Append(@")(?![\w])");
        return new Matcher(
            new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)),
            colours);
    }

    /// <summary>A list compiled to one pattern, with each name's colour.</summary>
    public sealed class Matcher(Regex pattern, IReadOnlyDictionary<string, TerminalColor> colours)
    {
        public Regex Pattern { get; } = pattern;

        public TerminalColor? ColourOf(string name) => colours.TryGetValue(name, out var colour) ? colour : null;
    }
}
