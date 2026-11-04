using System.Text.RegularExpressions;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Client.Help;

/// <summary>A run of a help page's text: words, or a key the cells draw as a chip and the window as a key cap.</summary>
/// <param name="Text">The text, keys in glyphs (<c>⌃F</c>).</param>
/// <param name="IsKey">Whether it is a key.</param>
public readonly record struct HelpSpan(string Text, bool IsKey);

/// <summary>
/// One block of a help page: an optional heading (the <c>Search:</c> a paragraph opens with), its words as spans, and
/// whether it is a table row laid out in source (the map's key list) rather than a paragraph to wrap.
/// </summary>
public sealed record HelpBlock(string? Heading, IReadOnlyList<HelpSpan> Spans, bool Preformatted)
{
    public bool Equals(HelpBlock? other) =>
        other is not null && Heading == other.Heading && Preformatted == other.Preformatted && Spans.SequenceEqual(other.Spans);

    public override int GetHashCode() => HashCode.Combine(Heading, Preformatted, Spans.Count);

    /// <summary>The block's words as plain text.</summary>
    public string Text => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>
/// A <see cref="HelpTopic"/> as both hosts draw it: blocks with headings and key spans, so a page reads like the rest of
/// the client — a heading over each paragraph, keys drawn as keys — rather than as a wall of sentences. The topics stay
/// plain sentences in source; this reads their shape: a paragraph (or a line inside one) that opens with a short
/// capitalised label and a colon starts a block headed by it, a line indented two spaces is a table row kept as written,
/// and the keys are found by their glyphs.
/// </summary>
public static class HelpPage
{
    /// <summary>The longest label read as a heading (<c>Everything else</c>, <c>Ragged lines</c>).</summary>
    public const int MaxHeading = 22;

    private static readonly Regex HeadingPattern = new(
        $@"^(?<h>[A-Z][A-Za-z' ]{{0,{MaxHeading - 1}}}): (?<rest>.+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// A key as the topics write one: a modifier run and what it modifies (<c>⌃F</c>, <c>⌥⇧</c>, <c>⌃←→↑↓</c>,
    /// <c>⇧↑</c>), a bare key glyph (<c>⏎</c>, <c>⇥</c>, an arrow), or a named key (<c>F5</c>, <c>Esc</c>, <c>PgUp</c>).
    /// </summary>
    private static readonly Regex KeyPattern = new(
        @"[⌃⌥⇧]+(?:F\d{1,2}|PgUp|PgDn|Home|End|Esc|[←→↑↓]+|[^\s,;.:)/])?|[⏎⇥]|[←→↑↓]+|\b(?:F\d{1,2}|Esc|PgUp|PgDn|End)\b",
        RegexOptions.CultureInvariant);

    /// <summary>The blocks of <paramref name="topic"/>, in order.</summary>
    public static IReadOnlyList<HelpBlock> Blocks(HelpTopic topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        var blocks = new List<HelpBlock>();
        string? heading = null;
        var words = new List<string>();

        void Close()
        {
            if (words.Count > 0 || heading is not null)
            {
                blocks.Add(new HelpBlock(heading, Spans(string.Join(' ', words)), false));
            }

            heading = null;
            words.Clear();
        }

        foreach (var line in topic.Lines)
        {
            if (line.Length == 0)
            {
                Close();
                continue;
            }

            if (line.StartsWith("  ", StringComparison.Ordinal))
            {
                Close();
                blocks.Add(new HelpBlock(null, Spans(line.TrimStart()), true));
                continue;
            }

            if (HeadingPattern.Match(line) is { Success: true } match)
            {
                Close();
                heading = match.Groups["h"].Value;

                // The sentence now starts the block: "Copy / Drag across output", not "drag across output".
                var rest = match.Groups["rest"].Value;
                words.Add(char.IsLower(rest[0]) ? char.ToUpperInvariant(rest[0]) + rest[1..] : rest);
                continue;
            }

            words.Add(line);
        }

        Close();
        return blocks;
    }

    /// <summary><paramref name="text"/> split into words and keys.</summary>
    public static IReadOnlyList<HelpSpan> Spans(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<HelpSpan>();
        var at = 0;
        foreach (Match key in KeyPattern.Matches(text))
        {
            if (key.Index > at)
            {
                spans.Add(new HelpSpan(text[at..key.Index], false));
            }

            spans.Add(new HelpSpan(key.Value, true));
            at = key.Index + key.Length;
        }

        if (at < text.Length)
        {
            spans.Add(new HelpSpan(text[at..], false));
        }

        return spans;
    }
}
