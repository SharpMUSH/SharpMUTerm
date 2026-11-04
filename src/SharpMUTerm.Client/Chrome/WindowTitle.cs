using System.Text;

namespace SharpMUTerm.Client.Chrome;

/// <summary>
/// The title the window — the desktop window's title bar and taskbar entry, or the terminal's tab — wears for what is
/// in front: <c>Character@World — SharpMUTerm</c>. One function, read off the same <see cref="HeaderModel"/> the
/// header band is drawn from, so the title and the header cannot name different characters.
/// <para>
/// The names are the player's configuration, but a world a <c>telnet://</c> link added is named after the link's host,
/// so they are cleaned like any outside text: control characters are dropped (a title is written into an escape
/// sequence in a terminal, where a BEL or ESC would end it early) and each part is bounded.
/// </para>
/// </summary>
public static class WindowTitle
{
    /// <summary>The product name every title ends with, and the whole title when nothing is in front.</summary>
    public const string Product = "SharpMUTerm";

    /// <summary>The longest a character or world name runs in a title before it is cut with an ellipsis.</summary>
    public const int MaxPart = 48;

    /// <summary>The title for what the header says is in front.</summary>
    public static string For(HeaderModel header) => For(header.Character?.Name, header.World?.Name);

    /// <summary>
    /// <c>Character@World — SharpMUTerm</c>; <c>World — SharpMUTerm</c> for a world with no character in front; and
    /// <c>SharpMUTerm</c> alone when nothing is.
    /// </summary>
    public static string For(string? character, string? world)
    {
        var who = Clean(character);
        var where = Clean(world);
        return (who, where) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{who}@{where} — {Product}",
            ({ Length: > 0 }, _) => $"{who} — {Product}",
            (_, { Length: > 0 }) => $"{where} — {Product}",
            _ => Product,
        };
    }

    /// <summary>The most a title's unread count says before it reads <c>99+</c>.</summary>
    public const int MaxUnread = 99;

    /// <summary>
    /// <paramref name="title"/> led by how many lines arrived while the window was behind another —
    /// <c>(3) Corvid@Aetherfall — SharpMUTerm</c> — or the title alone for none. Capped at <c>99+</c>, so a busy
    /// channel does not retitle the window with every line.
    /// </summary>
    public static string WithUnread(string title, int unread) =>
        unread <= 0 ? title : $"({(unread > MaxUnread ? $"{MaxUnread}+" : unread.ToString(System.Globalization.CultureInfo.InvariantCulture))}) {title}";

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var clean = new StringBuilder(Math.Min(text.Length, MaxPart + 1));
        foreach (var c in text)
        {
            if (!char.IsControl(c) && char.GetUnicodeCategory(c) is not (System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator))
            {
                clean.Append(c);
            }
        }

        var result = clean.ToString().Trim();
        return result.Length > MaxPart ? result[..(MaxPart - 1)] + "…" : result;
    }
}
