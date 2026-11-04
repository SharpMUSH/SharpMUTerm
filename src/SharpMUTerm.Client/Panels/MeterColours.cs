using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Panels;

/// <summary>
/// What colour a meter is drawn in, as <c>#rrggbb</c> before any host holds it to its plane's floor. A meter's colour is
/// written as F2's highlight colours are (<see cref="HighlightColours"/>: a palette name, <c>#rrggbb</c>, <c>idx:N</c>);
/// a palette <em>name</em> is drawn in a meter shade of that name rather than its web colour, because web <c>green</c> is
/// <c>#008000</c> and web <c>blue</c> <c>#0000ff</c>, a mud and a hole on a dark pane. The shades are the mockups' gauges
/// (health <c>#4cc38a</c>, movement <c>#5aa9e6</c>, target <c>#ff8a65</c>) and their neighbours at the same weight.
/// </summary>
public static class MeterColours
{
    /// <summary>What a meter with no colour of its own reads as in a picker: its label's default (<see cref="StatusMeter.DefaultColour"/>).</summary>
    public const string Auto = "auto";

    /// <summary>The names a meter's colour is picked from: <see cref="Auto"/>, then the highlight palette's names.</summary>
    public static IReadOnlyList<string> Choices { get; } = [Auto, .. HighlightColours.NameColours];

    /// <summary>What the target's bar is drawn in (the mockups' <c>#ff8a65</c>); a low target is the warning colour.</summary>
    public const string Target = "#ff8a65";

    private static readonly IReadOnlyDictionary<string, string> Shades = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "#ff6b6b",
        ["orange"] = "#ff8a65",
        ["gold"] = "#e8b84a",
        ["yellow"] = "#f5e663",
        ["lime"] = "#9be15d",
        ["green"] = "#4cc38a",
        ["cyan"] = "#4fd6e8",
        ["teal"] = "#2ec4b6",
        ["blue"] = "#5aa9e6",
        ["magenta"] = "#e06bd8",
        ["purple"] = "#a98bf0",
        ["pink"] = "#f08ab8",
        ["white"] = "#e8ecf4",
        ["silver"] = "#b8c0cc",
        ["grey"] = "#8a93a3",
        ["black"] = "#3a4257",
    };

    /// <summary>The meter's colour (its own, else its label's default) as <c>#rrggbb</c>, or null for the client's accent.</summary>
    public static string? For(StatusMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return Hex(meter.EffectiveColour);
    }

    /// <summary>A meter colour as written (<c>green</c>, <c>#4cc38a</c>, <c>idx:2</c>) as <c>#rrggbb</c>; null for none, auto or nonsense.</summary>
    public static string? Hex(string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour) || colour.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var text = colour.Trim();
        if (Shades.TryGetValue(text, out var shade))
        {
            return shade;
        }

        return HighlightColours.TryParse(text, out var parsed) ? HighlightColours.Hex(parsed) : null;
    }

    /// <summary>What a colour column shows for a meter: its own colour as written, else <see cref="Auto"/>.</summary>
    public static string Label(StatusMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        return string.IsNullOrWhiteSpace(meter.Colour) ? Auto : meter.Colour.Trim();
    }

    /// <summary>
    /// A typed or picked colour as it is stored: null for <see cref="Auto"/> or blank, a palette name lower-cased, anything
    /// else <see cref="HighlightColours"/> reads kept as written. False (and nothing stored) for text that names no colour.
    /// </summary>
    public static bool TryNormalise(string? text, out string? stored)
    {
        stored = null;
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase)
            || text.Trim().Equals(HighlightColours.None, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = text.Trim();
        if (Shades.ContainsKey(trimmed))
        {
            stored = trimmed.ToLowerInvariant();
            return true;
        }

        if (HighlightColours.TryParse(trimmed, out var parsed) && parsed is not null)
        {
            stored = trimmed;
            return true;
        }

        return false;
    }
}
