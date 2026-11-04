using System.Text.RegularExpressions;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Automation;

namespace SharpMUTerm.Client.Settings;

/// <summary>What a declared text field may hold and how it is stored. Both hosts apply the same rule.</summary>
public enum TextRule
{
    /// <summary>Free text that may be blank, stored as null when it is; trimmed (an on-connect command).</summary>
    Optional,

    /// <summary>Free text that may not be blank; trimmed (a host).</summary>
    Required,

    /// <summary>What a thing is called: not blank, no control characters; trimmed.</summary>
    Name,

    /// <summary>A regular expression that may be blank (stored as null); refused when it does not parse; not trimmed.</summary>
    Pattern,

    /// <summary>A secret: blank is no value, no control characters, and never trimmed — a trimmed password is another password.</summary>
    Secret,

    /// <summary>
    /// A value with a default behind it (<see cref="TextField.Fallback"/>): shown as the fallback while unset, and
    /// blank or the fallback itself stores null again, so "unset" has one spelling.
    /// </summary>
    Defaulted,

    /// <summary>A regular expression that may not be blank and must parse (an alias's pattern); not trimmed.</summary>
    RequiredPattern,

    /// <summary>
    /// A trigger's pattern, read as the trigger's own <see cref="TriggerMatchType"/>: only a regex can fail to parse,
    /// and the plain kinds refuse only a pattern that would match every line.
    /// </summary>
    TriggerPattern,

    /// <summary>Optional text with no control characters, stored trimmed or as null when blank (a response, a rewrite).</summary>
    Template,

    /// <summary>Text that may hold line breaks (an alias's expansion) and may not be empty; stored as typed.</summary>
    Lines,

    /// <summary>The name of a window: not blank, no control characters; trimmed (a route).</summary>
    Window,
}

/// <summary>
/// The rules of <see cref="TextRule"/>, applied by the native settings window; the cell screens build their own field
/// of the matching kind from the same rule, so the two surfaces refuse and store the same things.
/// </summary>
public static class TextRules
{
    /// <summary>Why <paramref name="value"/> is refused for <paramref name="field"/>, or null when it is accepted.</summary>
    public static string? Check(TextField field, string? value) => Check(field, value, default);

    /// <summary>
    /// Why <paramref name="value"/> is refused for <paramref name="field"/> on <paramref name="target"/>'s item (a trigger's
    /// pattern is read as that trigger's match kind), or null when it is accepted.
    /// </summary>
    public static string? Check(TextField field, string? value, SettingTarget target)
    {
        ArgumentNullException.ThrowIfNull(field);
        var text = value ?? string.Empty;
        return field.Rule switch
        {
            TextRule.RequiredPattern => text.Length == 0 ? "pattern cannot be empty" : RegexError(text),
            TextRule.TriggerPattern => TriggerPatterns.Validate(
                target.Item is Trigger trigger ? trigger.MatchType : TriggerMatchType.Regex, text),
            TextRule.Template when text.Any(char.IsControl) => $"{field.Label} cannot contain control characters",
            TextRule.Lines when text.Length == 0 => $"{field.Label} cannot be empty",
            TextRule.Window when string.IsNullOrWhiteSpace(text) => $"{field.Label} cannot be empty",
            TextRule.Window when text.Any(char.IsControl) => $"{field.Label} cannot contain control characters",
            TextRule.Required when string.IsNullOrWhiteSpace(text) => $"{field.Label} cannot be empty",
            TextRule.Name when string.IsNullOrWhiteSpace(text) => $"{field.Label} cannot be empty",
            TextRule.Name or TextRule.Secret or TextRule.Defaulted when text.Any(char.IsControl) =>
                $"{field.Label} cannot contain control characters",
            TextRule.Pattern when !string.IsNullOrWhiteSpace(text) => PatternError(text),
            _ => null,
        };
    }

    /// <summary>What is stored for an accepted <paramref name="value"/>: trimmed or not, and null for "unset".</summary>
    public static string? Store(TextField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        var text = value ?? string.Empty;
        string? stored = field.Rule switch
        {
            TextRule.Required or TextRule.Name or TextRule.Window => text.Trim(),
            TextRule.RequiredPattern or TextRule.TriggerPattern or TextRule.Lines => text,
            TextRule.Template => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            TextRule.Pattern => string.IsNullOrWhiteSpace(text) ? null : text,
            TextRule.Secret => text.Length == 0 ? null : text,
            TextRule.Defaulted => text.Trim() is var t && (t.Length == 0 || string.Equals(t, field.Fallback, StringComparison.Ordinal))
                ? null
                : t,
            _ => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
        };

        if (stored is not null && field.MaxLength is { } max && stored.Length > max)
        {
            stored = stored[..max];
        }

        return stored;
    }

    /// <summary>What a field shows for a stored value: the fallback for an unset defaulted field.</summary>
    public static string Shown(TextField field, string? stored)
    {
        ArgumentNullException.ThrowIfNull(field);
        return stored ?? (field.Rule == TextRule.Defaulted ? field.Fallback ?? string.Empty : string.Empty);
    }

    private static string? RegexError(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return null;
        }
        catch (ArgumentException e)
        {
            return "not a valid regex: " + e.Message.Split('\n')[0].Trim();
        }
    }

    private static string? PatternError(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return null;
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
    }
}
