using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SharpMUTerm.Core.Automation;

namespace SharpMUTerm.Core.Import;

/// <summary>
/// The pieces every reader shares: bounding the text it takes, turning another client's capture references into the
/// <c>$1</c> this client's engines expand, and reading XML without letting the file reach anything but itself.
/// </summary>
internal static class ImportText
{
    /// <summary>A name: control characters gone, trimmed, at most <see cref="ImportLimits.MaxName"/> long.</summary>
    public static string Name(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > ImportLimits.MaxName ? clean[..(ImportLimits.MaxName - 1)] + "…" : clean;
    }

    /// <summary>
    /// A pattern or a command: line breaks kept as <c>\n</c>, every other control character dropped. Null when it is
    /// longer than <see cref="ImportLimits.MaxText"/> — never cut short, because a shorter command is another command.
    /// </summary>
    public static string? Body(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            if (c == '\n' || !char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        var body = builder.ToString().Trim('\n');
        return body.Length > ImportLimits.MaxText ? null : body;
    }

    /// <summary>Text the engines read as a substitution with nothing to substitute: every <c>$</c> doubled.</summary>
    public static string Literal(string text) => text.Replace("$", "$$", StringComparison.Ordinal);

    /// <summary>
    /// A MUSHclient send: <c>%0</c>–<c>%9</c> become <c>$0</c>–<c>$9</c>, <c>%%</c> is a percent sign, and a <c>$</c>
    /// is doubled so the engine does not read it as a reference.
    /// </summary>
    public static string PercentReferences(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '%' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                builder.Append('$').Append(text[i + 1]);
                i++;
            }
            else if (c == '%' && i + 1 < text.Length && text[i + 1] == '%')
            {
                builder.Append('%');
                i++;
            }
            else if (c == '$')
            {
                builder.Append("$$");
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>A whole-line wildcard pattern (<c>* tells you *</c>) as an anchored regex, each <c>*</c> a capture.</summary>
    public static string AnchoredWildcard(string pattern) => "^" + TriggerPatterns.WildcardToRegex(pattern) + "$";

    /// <summary>Literal text as a regex matching exactly the whole line.</summary>
    public static string ExactLine(string text) => "^" + TriggerPatterns.Literal(text) + "$";

    /// <summary>Whether <paramref name="pattern"/> compiles as a .NET regex.</summary>
    public static bool Compiles(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, AutomationDefaults.RegexMatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads XML that came from another program: no DTD processed (both MUSHclient and Mudlet files carry a bare
    /// <c>DOCTYPE</c>, which is ignored), no entity expanded from outside, nothing resolved, and the size capped.
    /// Null when it is not XML.
    /// </summary>
    public static XDocument? Xml(string text)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = ImportLimits.MaxFileBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CheckCharacters = false,
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>An attribute's value, or null.</summary>
    public static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    /// <summary>Whether a MUSHclient-style flag attribute (<c>y</c>/<c>1</c>/<c>yes</c>) is set.</summary>
    public static bool Flag(XElement element, string name, bool fallback = false)
    {
        var value = Attr(element, name);
        return value is null
            ? fallback
            : value.Equals("y", StringComparison.OrdinalIgnoreCase)
              || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
              || value == "1"
              || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An attribute read as an integer, or <paramref name="fallback"/>.</summary>
    public static int Int(XElement element, string name, int fallback = 0) =>
        int.TryParse(Attr(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>An attribute read as a number, or <paramref name="fallback"/>.</summary>
    public static double Number(XElement element, string name, double fallback = 0) =>
        double.TryParse(Attr(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>A child element's text, or null.</summary>
    public static string? Child(XElement element, string name) => element.Element(name)?.Value;

    /// <summary>A port read as one, or null when it is not 1–65535.</summary>
    public static int? Port(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? port
            : null;

    /// <summary>A host as one: no spaces or control characters, at most a DNS name long. Null when it is not one.</summary>
    public static string? Host(string? text)
    {
        var host = text?.Trim() ?? string.Empty;
        if (host.Length is 0 or > 253 || host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '\\' or '@'))
        {
            return null;
        }

        return host;
    }
}
