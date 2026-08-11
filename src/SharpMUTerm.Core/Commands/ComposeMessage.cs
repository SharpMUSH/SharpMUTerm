using System.Text;

namespace SharpMUTerm.Core.Commands;

/// <summary>
/// How a body typed in the composer becomes the one line that goes to the game.
/// <para>
/// A MUSH stores a post as a single string and renders its line breaks itself, so what
/// <c>+bbpost 12=…</c> or <c>@mail bob=…</c> wants is one command whose breaks are written <c>%r</c> —
/// not a line per row of the editor. This turns the editor's buffer into exactly that, and it is the
/// whole of what the composer sends: the buffer <em>is</em> the command, verb and all, so nothing is
/// prepended and nothing is guessed at.
/// </para>
/// <para>
/// It is pure and lives in Core because it is the part worth asserting: every interesting case here is
/// a string in and a string out, and none of it needs a terminal.
/// </para>
/// </summary>
public static class ComposeMessage
{
    /// <summary>MUSH's line break, which is what a break in the editor becomes.</summary>
    public const string LineBreak = "%r";

    /// <summary>
    /// The characters <see cref="ComposeEscaping.Literal"/> protects, each by the escape MUSH reads
    /// them by: <c>%</c> doubles, and the rest take a backslash.
    /// <para>
    /// The set is the one a MUSH's parser acts on in a command argument — a substitution (<c>%</c>), a
    /// function or attribute evaluation (<c>[</c>, <c>]</c>), a command separator (<c>;</c>), and the
    /// braces that group an argument (<c>{</c>, <c>}</c>). <c>\</c> is in it because it is the escape
    /// itself: left alone, a backslash the writer typed would eat the character after it.
    /// </para>
    /// </summary>
    private static readonly char[] Specials = ['\\', '%', '[', ']', '{', '}', ';'];

    /// <summary>
    /// The one line to send for <paramref name="body"/>, or null when there is nothing to send.
    /// <para>
    /// Null rather than an empty string, and the caller refuses on it: sending an empty command to a
    /// MUSH is not nothing — it is a blank line, which some games answer and all of them log.
    /// </para>
    /// </summary>
    public static string? Build(string? body, ComposeEscaping escaping)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var lines = Split(body);
        Trim(lines);
        if (lines.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder(body.Length + (lines.Count * LineBreak.Length));
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(LineBreak);
            }

            // Escaping happens per line, *before* the breaks are joined in — so the %r this writes is
            // never itself escaped. Doing it the other way round produces `%%r`, which posts the
            // characters "%r" into the body instead of a line break, on every line of every literal
            // post. The ordering is the whole correctness of this function.
            sb.Append(escaping == ComposeEscaping.Literal ? Escape(lines[i]) : lines[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits a buffer into lines, treating CRLF, LF and a lone CR alike. The editor writes
    /// <see cref="Environment.NewLine"/>, a paste carries whatever the source had, and a body that
    /// travelled through either must break in the same places.
    /// </summary>
    private static List<string> Split(string body)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] is not ('\n' or '\r'))
            {
                continue;
            }

            lines.Add(body[start..i]);
            if (body[i] == '\r' && i + 1 < body.Length && body[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        lines.Add(body[start..]);
        return lines;
    }

    /// <summary>
    /// Drops blank rows at both ends and keeps every one in between. A trailing blank line is where the
    /// caret was left, not part of the post; an interior one is a paragraph break and becomes
    /// <c>%r%r</c>, which is what the writer typed and what the game will render.
    /// </summary>
    private static void Trim(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        while (lines.Count > 0 && lines[0].Trim().Length == 0)
        {
            lines.RemoveAt(0);
        }
    }

    /// <summary>Escapes one line's MUSH metacharacters. See <see cref="Specials"/> for the set.</summary>
    private static string Escape(string line)
    {
        if (line.IndexOfAny(Specials) < 0)
        {
            return line;
        }

        var sb = new StringBuilder(line.Length + 8);
        foreach (var c in line)
        {
            switch (c)
            {
                case '%':
                    sb.Append("%%");
                    break;

                case '\\' or '[' or ']' or '{' or '}' or ';':
                    sb.Append('\\').Append(c);
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// What the composer does with the MUSH metacharacters in a body — the window's own toggle.
/// <para>
/// Two modes rather than one because both are right for different posts and neither is right for both.
/// A prose post full of <c>100%</c> and <c>[brackets]</c> wants them to arrive as themselves; a post
/// that deliberately carries <c>ansi()</c>, a <c>%r</c> of its own or an attribute evaluation wants the
/// game to read them. Guessing which is which from the text is not possible, so the writer says.
/// </para>
/// </summary>
public enum ComposeEscaping
{
    /// <summary>
    /// Send what was typed. Only the line breaks become <c>%r</c>; every other character reaches the
    /// game as itself and the game does what it does with it.
    /// </summary>
    AsTyped,

    /// <summary>
    /// Protect the body, so the post shows the characters that were typed. See
    /// <see cref="ComposeMessage"/> for the set and why the escaping precedes the join.
    /// </summary>
    Literal,
}
