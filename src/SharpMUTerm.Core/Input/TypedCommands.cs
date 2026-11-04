using System.Globalization;
using System.Text;

namespace SharpMUTerm.Core.Input;

/// <summary>
/// What one typed line becomes once a world's command stacking is applied
/// (<see cref="Configuration.WorldDefinition.CommandSeparator"/>): the commands to run, in order, or the reason none
/// will run.
/// </summary>
/// <param name="Commands">Each command, before alias expansion. Empty when <paramref name="Refusal"/> is set.</param>
/// <param name="Refusal">Why nothing is sent, or null.</param>
public sealed record TypedExpansion(IReadOnlyList<string> Commands, string? Refusal = null);

/// <summary>
/// Command stacking for a typed line: <c>n;e;e</c> is three commands and <c>#3 kill rat</c> is one command three
/// times, the way every MUD client reads them. Applied by <c>WorldSession.SendTypedAsync</c> — what the command line
/// sends — and nowhere else: a composed post, a grab and a script's <c>world.send</c> go out as written.
/// <para>
/// <b>Off unless the world names a separator</b>, and the repeat comes with it, because a MUSH spends both: <c>;</c>
/// is a semipose and separates attribute actions, and <c>#123 command</c> is PennMUSH's force shorthand. A world with
/// no separator gets the line back untouched.
/// </para>
/// <para>
/// <c>\</c> before the separator makes it literal (<c>say a\;b</c> says <c>a;b</c>); any other backslash is left
/// alone. Each command is trimmed and empty ones are dropped. A repeat is 1 to <see cref="MaxRepeat"/>, and a line
/// may not come to more than <see cref="MaxCommands"/> commands: past either bound <b>nothing</b> is sent, because
/// half of a stacked line is a line the player did not type.
/// </para>
/// </summary>
public static class TypedCommands
{
    /// <summary>The most times <c>#N</c> repeats a command.</summary>
    public const int MaxRepeat = 100;

    /// <summary>The most commands one typed line may come to, repeats included.</summary>
    public const int MaxCommands = 100;

    /// <summary>Splits and repeats <paramref name="input"/> by <paramref name="separator"/>; null or blank changes nothing.</summary>
    public static TypedExpansion Expand(string input, string? separator)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(separator))
        {
            return new TypedExpansion([input]);
        }

        var pieces = Split(input, separator);
        if (pieces.Count == 0)
        {
            return new TypedExpansion([input]);
        }

        var commands = new List<string>();
        foreach (var piece in pieces)
        {
            if (Repeat(piece) is { } repeat)
            {
                if (repeat.Count is < 1 or > MaxRepeat)
                {
                    return Refused($"#{repeat.Count} is not a repeat this client sends (1 to {MaxRepeat}) — nothing was sent");
                }

                for (var i = 0; i < repeat.Count; i++)
                {
                    commands.Add(repeat.Command);
                }
            }
            else
            {
                commands.Add(piece);
            }

            if (commands.Count > MaxCommands)
            {
                return Refused($"that line comes to more than {MaxCommands} commands — nothing was sent");
            }
        }

        return new TypedExpansion(commands);
    }

    private static TypedExpansion Refused(string why) => new([], why);

    private static List<string> Split(string input, string separator)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && string.CompareOrdinal(input, i + 1, separator, 0, separator.Length) == 0)
            {
                current.Append(separator);
                i += separator.Length;
                continue;
            }

            if (string.CompareOrdinal(input, i, separator, 0, separator.Length) == 0)
            {
                Add(pieces, current);
                i += separator.Length - 1;
                continue;
            }

            current.Append(input[i]);
        }

        Add(pieces, current);
        return pieces;

        static void Add(List<string> pieces, StringBuilder current)
        {
            var piece = current.ToString().Trim();
            current.Clear();
            if (piece.Length > 0)
            {
                pieces.Add(piece);
            }
        }
    }

    /// <summary><c>#N command</c>: the count and the command, or null for anything else (<c>#</c> alone, <c>#abc</c>).</summary>
    private static (int Count, string Command)? Repeat(string piece)
    {
        if (piece.Length < 3 || piece[0] != '#')
        {
            return null;
        }

        var digits = 1;
        while (digits < piece.Length && char.IsAsciiDigit(piece[digits]))
        {
            digits++;
        }

        if (digits == 1 || digits >= piece.Length || !char.IsWhiteSpace(piece[digits]))
        {
            return null;
        }

        var command = piece[digits..].Trim();
        if (command.Length == 0)
        {
            return null;
        }

        // Past int's range is just "too many", which the caller refuses.
        var count = digits > 6 ? int.MaxValue : int.Parse(piece.AsSpan(1, digits - 1), CultureInfo.InvariantCulture);
        return (count, command);
    }
}
