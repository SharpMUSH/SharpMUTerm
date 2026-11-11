namespace SharpMUTerm.Core.Input;

/// <summary>
/// Recognizes commands carrying typed passwords so command history and local echo can protect them.
/// <para>
/// A configured character never comes through here — <c>WorldSession.SendLoginAsync</c> writes its
/// connect string straight to the transport and never touches the UI's history. The line this exists for
/// is the one a user types by hand on a world they have not configured yet: <c>connect Corvid hunter2</c>.
/// That was always a latent hazard, recoverable one entry at a time with <c>↑</c>; a browsable, searchable
/// history surface is what turns it into a real one, so the rule ships with the surface.
/// </para>
/// <para>
/// <b>Excluded, not masked.</b> A masked entry you cannot recall is useless — the whole point of history
/// is putting the line back on the command line — and a masked entry you <em>can</em> recall still holds
/// the secret, so masking buys nothing but the appearance of care. The line is simply never recorded.
/// </para>
/// <para>
/// <b>Deliberately over-inclusive.</b> The two failure modes are not symmetrical: wrongly dropping
/// <c>connect me here</c> costs one <c>↑</c> that does not work, while wrongly keeping
/// <c>connect Corvid hunter2</c> puts a password in a searchable list. So any login verb with something
/// that could be a password after the name is dropped, without trying to judge whether it looks like one.
/// </para>
/// <para>
/// <b>Nothing here is persisted.</b> History is in-memory for the life of the process and there is no
/// option to write it to disk — that would be a separate decision about secrets at rest.
/// </para>
/// </summary>
public static class HistorySecrets
{
    /// <summary>
    /// The MU* login verbs, which take <c>&lt;name&gt; &lt;password&gt;</c>. <c>cd</c> is connect-dark and
    /// <c>ch</c> connect-hidden (PennMUSH/TinyMUX wizard variants), and both take the same two arguments;
    /// <c>create</c> chooses a password rather than presenting one, which is no less a secret.
    /// PennMUSH also accepts every nonempty prefix of <c>connect</c> at login.
    /// </summary>
    private static readonly string[] LoginVerbs =
        { "connect", "c", "co", "con", "conn", "conne", "connec", "cd", "ch", "create" };

    /// <summary>
    /// Verbs whose <em>first</em> argument is already the secret — <c>@password old=new</c>,
    /// <c>@newpassword player=pass</c>, <c>@pcreate name=pass</c>. One argument is enough to drop these,
    /// where a login verb needs two.
    /// </summary>
    private static readonly string[] PasswordVerbs = { "@password", "@newpassword", "@pcreate" };

    /// <summary>The verbs this rule knows, for a screen or a report that wants to name them.</summary>
    public static IReadOnlyList<string> Verbs { get; } =
        LoginVerbs.Concat(PasswordVerbs).ToArray();

    /// <summary>
    /// Whether a typed line carries a password anywhere in it: on any of its physical lines, or, where the world
    /// stacks commands, in any piece once split on <paramref name="separator"/> and stripped of a <c>#N</c> repeat
    /// (<c>look;#1 connect Corvid hunter2</c>). Split without honouring <c>\</c> escapes and whether or not the stack
    /// would be refused, because this is over-inclusive on purpose (see the type's remarks): the line is judged as
    /// typed, not as sent.
    /// </summary>
    public static bool CarriesCredential(string? line, string? separator)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        foreach (var physical in line.Split(['\r', '\n']))
        {
            if (LooksLikeCredential(physical))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(separator))
            {
                continue;
            }

            foreach (var piece in physical.Split(separator))
            {
                if (LooksLikeCredential(WithoutRepeat(piece.Trim())))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary><c>#3 connect Corvid hunter2</c> → <c>connect Corvid hunter2</c>; anything else unchanged.</summary>
    private static string WithoutRepeat(string piece)
    {
        if (piece.Length < 3 || piece[0] != '#')
        {
            return piece;
        }

        var end = 1;
        while (end < piece.Length && char.IsAsciiDigit(piece[end]))
        {
            end++;
        }

        return end > 1 && end < piece.Length && char.IsWhiteSpace(piece[end]) ? piece[end..] : piece;
    }

    /// <summary>
    /// Whether a command line looks like it carries a password and so must not be recorded. Verb
    /// matching is case-insensitive, because MU* command dispatch is.
    /// </summary>
    public static bool LooksLikeCredential(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var words = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var verb = words[0];
        var arguments = words.Length - 1;

        // `connect Corvid` is a guest-style login with nothing to hide; `connect Corvid hunter2` is not.
        if (arguments >= 2 && LoginVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return arguments >= 1 && PasswordVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase);
    }
}
