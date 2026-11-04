using System.Globalization;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// The colours a trigger's highlight is picked from, and how one is written as text: a short palette of names, plus
/// <c>#rrggbb</c> and <c>idx:N</c> for a colour already in the configuration that no name covers. Both hosts read
/// and write a highlight through this, so the cell field and the native picker accept and show the same things.
/// </summary>
public static class HighlightColours
{
    /// <summary>What "no colour" reads and is typed as.</summary>
    public const string None = "none";

    /// <summary>The terminal's default colour, as a name.</summary>
    public const string DefaultName = "default";

    /// <summary>The names a picker steps through, <see cref="None"/> first.</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        None, "red", "orange", "gold", "yellow", "lime", "green", "cyan", "teal",
        "blue", "magenta", "purple", "pink", "white", "silver", "grey", "black",
    ];

    /// <summary>
    /// How a highlight-names list is written (F5 ▸ <c>highlight names</c>), said where it is edited in both hosts: the cell
    /// field draws it under the open edit, the window as the field's help.
    /// </summary>
    public const string NameSyntax = "Name=colour, comma between; a name alone is " + Core.Automation.NameHighlights.DefaultColour;

    /// <summary>The colours a highlight-names list may name: the palette without <see cref="None"/>.</summary>
    public static IReadOnlyList<string> NameColours { get; } = [.. Palette.Where(n => n != None)];

    /// <summary>The highlight-names field's help: what it does, how to write it, and every colour it takes.</summary>
    public static string NamesHelp { get; } =
        $"names drawn in a colour wherever they appear, e.g. Ashlyn=gold, Merrow=red. {NameSyntax}. Colours: {string.Join(", ", NameColours)}";

    /// <summary>A colour as text: a palette name where one matches, else <c>#rrggbb</c> or <c>idx:N</c>.</summary>
    public static string Format(TerminalColor? colour)
    {
        if (colour is not { } value)
        {
            return None;
        }

        foreach (var name in Palette)
        {
            if (name != None && WebColors.TryParse(name, out var named) && named == value)
            {
                return name;
            }
        }

        return value.Kind switch
        {
            TerminalColorKind.Rgb => $"#{value.R:x2}{value.G:x2}{value.B:x2}",
            TerminalColorKind.Indexed => $"idx:{value.Index.ToString(CultureInfo.InvariantCulture)}",
            _ => DefaultName,
        };
    }

    /// <summary>Reads a colour as <see cref="Format"/> writes it (or any web colour name); false when it is none of those.</summary>
    public static bool TryParse(string text, out TerminalColor? colour)
    {
        ArgumentNullException.ThrowIfNull(text);

        colour = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Equals(None, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.Equals(DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            colour = TerminalColor.Default;
            return true;
        }

        if (trimmed.StartsWith("idx:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(trimmed.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index is >= 0 and <= 255)
        {
            colour = TerminalColor.FromIndex(index);
            return true;
        }

        if (WebColors.TryParse(trimmed, out var parsed))
        {
            colour = parsed;
            return true;
        }

        return false;
    }

    /// <summary>A colour as <c>#rrggbb</c> for a swatch, or null for none and the terminal's default.</summary>
    public static string? Hex(TerminalColor? colour) => colour switch
    {
        { Kind: TerminalColorKind.Rgb } c => $"#{c.R:x2}{c.G:x2}{c.B:x2}",
        { Kind: TerminalColorKind.Indexed } c => AnsiPalette.ToRgb(c.Index).ToHex(),
        _ => null,
    };
}

/// <summary>
/// A <c>[Flags]</c> enumeration written as words — <c>bold underline</c>, or <c>none</c> — the way a trigger's added
/// attributes are typed on the cell screen and ticked in the window.
/// </summary>
public static class FlagWords
{
    /// <summary>What a set with nothing in it reads and is typed as.</summary>
    public const string NoFlags = "none";

    private static readonly char[] Separators = [' ', '\t', ',', '|', '+'];

    /// <summary>Every non-zero member, lower-cased, in declaration order.</summary>
    public static IReadOnlyList<string> Names<TEnum>()
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Where(v => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0)
            .Select(v => v.ToString().ToLowerInvariant())
            .ToArray();

    /// <summary>A value as its words, or <see cref="NoFlags"/>.</summary>
    public static string Format<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var bits = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        var set = Enum.GetValues<TEnum>()
            .Select(v => (Name: v.ToString().ToLowerInvariant(), Bits: Convert.ToInt64(v, CultureInfo.InvariantCulture)))
            .Where(f => f.Bits != 0 && (bits & f.Bits) == f.Bits)
            .Select(f => f.Name)
            .ToArray();

        return set.Length == 0 ? NoFlags : string.Join(' ', set);
    }

    /// <summary>Whether a typed list names <paramref name="name"/>.</summary>
    public static bool IsListed(string spec, string name)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => string.Equals(word, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The first word of a typed list that names nothing in <paramref name="names"/>, or null.</summary>
    public static string? Unknown(string value, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(names);
        foreach (var word in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.Equals(word, NoFlags, StringComparison.OrdinalIgnoreCase)
                && !names.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                return word;
            }
        }

        return null;
    }

    /// <summary>The value a typed list names; words that name nothing are ignored.</summary>
    public static TEnum Parse<TEnum>(string value)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(value);
        var members = Enum.GetValues<TEnum>()
            .Select(v => (Name: v.ToString(), Bits: Convert.ToInt64(v, CultureInfo.InvariantCulture)))
            .ToArray();

        var bits = 0L;
        foreach (var word in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var member in members)
            {
                if (string.Equals(member.Name, word, StringComparison.OrdinalIgnoreCase))
                {
                    bits |= member.Bits;
                }
            }
        }

        return (TEnum)Enum.ToObject(typeof(TEnum), bits);
    }
}
