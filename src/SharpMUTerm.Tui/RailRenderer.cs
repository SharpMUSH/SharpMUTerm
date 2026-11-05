using System.Text;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Renders <see cref="RailModel"/> rows into markup lines for the connection rail: a header, worlds
/// with an accent spine, characters with a connected dot and active marker, and windows with
/// unread/unsent detail and the chord that goes to them. Pure so the rail layout is unit-testable.
/// <para>
/// A row carrying a <see cref="RailRow.Target"/> is wrapped in a <c>[link=…]</c> span, which is how
/// clicking it switches. The span emits no cell, so it cannot change the sidebar's width — which the
/// panes, and through per-pane NAWS every connected server, are sized from. It covers the row's content
/// but not its indent or the tail out to the column edge, so a click aimed at the splitter beside the
/// rail lands on nothing.
/// </para>
/// </summary>
internal static partial class RailRenderer
{
    /// <summary>
    /// The sidebar's cells, drawn from the model the window's native sidebar is drawn from: the collapsed strip
    /// when the model says collapsed, else the full rail fitted to <paramref name="maxWidth"/>.
    /// </summary>
    /// <param name="cursorBand">
    /// The band the keyboard cursor's row is drawn on while the sidebar holds the keys (<see cref="SidebarModel.Cursor"/>,
    /// ⌥C): a background, or <c>#ink on #band</c> for the row's unstyled text. The row is padded to the widest row so the band runs the width of the rail and costs no column.
    /// </param>
    public static List<string> Render(SidebarModel model, int maxWidth, ChromeInk? ink, string? cursorBand = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var rows = model.Rows();
        var lines = model.Collapsed ? RenderCollapsed(rows, ink) : Render(rows, maxWidth, ink);
        if (model.Cursor is not { } cursor || cursorBand is null)
        {
            return lines;
        }

        // Which line each row became: one per row expanded, worlds and characters only when folded.
        var drawn = model.Collapsed ? rows.Where(r => r.Kind is RailRowKind.World or RailRowKind.Character).ToList() : rows.ToList();
        var at = drawn.FindIndex(r => r.Target is not null && SidebarModel.KeyOf(r) == cursor);
        if (at >= 0 && at < lines.Count)
        {
            var widest = lines.Max(SharpMUTermApp.MarkupWidth);
            var pad = new string(' ', Math.Max(0, widest - SharpMUTermApp.MarkupWidth(lines[at])));
            var on = cursorBand.IndexOf(" on ", StringComparison.Ordinal);
            var band = on < 0 ? cursorBand : cursorBand[(on + 4)..];
            lines[at] = $"[{(on < 0 ? "on " + cursorBand : cursorBand)}]{OnBand(lines[at], band)}{pad}[/]";
        }

        return lines;
    }

    /// <summary>
    /// The row's inks held to the floor on the band it is drawn on: they were resolved against the rail's plane, and a
    /// quiet one (`offline`, a chord) can fall under 3:1 on the brighter band. A span with a background of its own
    /// keeps its pair; escaped text (<c>[[</c>) is never a tag.
    /// </summary>
    private static string OnBand(string line, string band)
    {
        var plane = Hex(band);
        return Tag().Replace(line, tag => Ink().Replace(tag.Value, ink => Contrast.Legible(Hex(ink.Value), plane).ToHex()));

        static Rgb Hex(string hex) => new(
            Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!\[)\[(?!\[)[^\[\]]*\]")]
    private static partial System.Text.RegularExpressions.Regex Tag();

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!on )#[0-9a-fA-F]{6}\b")]
    private static partial System.Text.RegularExpressions.Regex Ink();

    /// <param name="rows">The rail's rows, as <see cref="RailModel"/> projects them.</param>
    /// <param name="maxWidth">
    /// The widest a row may be in visible cells. The sidebar's width is the widest row's <em>clamped</em>
    /// width, so a name past the clamp — a web page's title, most easily — would wrap; elide instead.
    /// </param>
    /// <param name="ink">
    /// The client's own voice for the active theme, held to the legibility floor against the plane the
    /// rail is drawn on. Null means the unmeasured base hues, which is what a themeless unit test wants.
    /// </param>
    public static List<string> Render(
        IReadOnlyList<RailRow> rows, int maxWidth = int.MaxValue, ChromeInk? ink = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var voice = ink ?? ChromeInk.Default;

        // Decided once for the whole rail and per row kind. Reserved, so the width does not move as
        // unread arrives or the ⌥J/⌥K pair travels between rows; per kind, because a window row's chord
        // is the ⌥N numbering and a character row's is the cycle, and with one character open no
        // character row can have one — reserving across both would spend three cells on the common case.
        var reserveWindow = rows.Any(r => r.Kind == RailRowKind.Window && r.Chord is { Length: > 0 });
        var reserveCharacter = rows.Any(r => r.Kind == RailRowKind.Character && r.Chord is { Length: > 0 });

        var lines = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            lines.Add(Fit(row, maxWidth, r => RenderRow(r, reserveWindow, reserveCharacter, voice)));
        }

        return lines;
    }

    private static string RenderRow(RailRow row, bool reserveWindow, bool reserveCharacter, ChromeInk ink) => row.Kind switch
    {
        // The terminal mockup's rail: a plain heading and bold world names, both lined up with the character rows'
        // ▸ column. The world's accent is carried by its characters' dots; the ▚ spine and the ┌ ⏻ heading were
        // marks of a box nothing drew.
        RailRowKind.Header => "  [dim]CONNECTIONS[/]",
        RailRowKind.World => Link(row, $"  [bold]{Escape(row.Label)}[/]"),
        RailRowKind.Host => $"{Indent(row)}[dim]{Escape(row.Label)}[/]",
        RailRowKind.Empty => $"{Indent(row)}{Link(row, $"[dim]{Escape(row.Label)}[/]")}",
        RailRowKind.Character => Character(row, reserveCharacter, ink),
        RailRowKind.Window => Window(row, reserveWindow, ink),
        _ => Escape(row.Label),
    };

    /// <summary>
    /// Renders a row, and if it overruns, renders it again with the label shortened by the overrun. The
    /// label is the only part that may give ground — everything else is information. Measured with
    /// <see cref="SharpMUTermApp.MarkupWidth"/>, the same measure the sidebar's width is derived from.
    /// <para>
    /// <b>Only the label varies in width.</b> Everything else is one cell whatever it says or sits in a
    /// reserved field that is blank when empty, which is what stops a keystroke or a line of output
    /// resizing the sidebar and every connected server's terminal size with it.
    /// </para>
    /// </summary>
    private static string Fit(RailRow row, int maxWidth, Func<RailRow, string> render)
    {
        var line = render(row);
        var over = SharpMUTermApp.MarkupWidth(line) - maxWidth;
        if (over <= 0 || row.Label.Length == 0)
        {
            return line;
        }

        var elements = row.Label.EnumerateRunes().Select(r => r.ToString()).ToList();
        var keep = Math.Max(1, elements.Count - over - 1); // one cell goes to the ellipsis
        return render(row with { Label = string.Concat(elements.Take(keep)) + "…" });
    }

    /// <summary>
    /// Renders the collapsed rail (⌃B b): a ~6-col strip of per-world accent marks and, under
    /// each, its characters as a status dot + initial + unread count. Both stay clickable — an initial
    /// is the only handle a collapsed rail offers.
    /// <para>
    /// A world is a solid <c>▌</c> in its accent, the bar the window's folded sidebar draws (a <c>▚</c> read as a
    /// glyph glitch at one cell). Characters show one letter, or two for every character once two of them share a
    /// first letter (<see cref="InitialLength"/>): <c>Ro</c> and <c>Ri</c> rather than two <c>R</c>s nobody can tell
    /// apart. The length is the whole strip's, so every row keeps one width and it changes only when the set of
    /// characters does.
    /// </para>
    /// </summary>
    public static List<string> RenderCollapsed(IReadOnlyList<RailRow> rows, ChromeInk? ink = null)
    {
        var voice = ink ?? ChromeInk.Default;
        var lines = new List<string>();
        var letters = InitialLength(rows.Where(r => r.Kind == RailRowKind.Character).Select(r => r.Label));
        foreach (var row in rows)
        {
            switch (row.Kind)
            {
                case RailRowKind.World:
                    lines.Add(Link(row, $"[{Accent(row, voice)}]{CollapsedWorldMark}[/]"));
                    break;
                case RailRowKind.Character:
                    var initial = Escape(Initials(row.Label, letters));
                    var dot = row.Connected ? "●" : "○";
                    var name = row.Active ? $"[bold]{initial}[/]" : initial;

                    // Reserved here too: the strip is clamped to 4–10 cells, so it moves less, but a
                    // strip that widens when a background world speaks is the same reflow.
                    lines.Add(Link(row, $"[{Accent(row, voice)}]{dot}[/]{name}{UnreadField(row.Unread, voice)}"));
                    break;
            }
        }

        return lines;
    }

    /// <summary>The collapsed strip's mark for a world: a solid bar in the world's accent.</summary>
    internal const string CollapsedWorldMark = "▌";

    /// <summary>
    /// How many letters the collapsed strip gives each character: 1, or 2 once two names share a first letter
    /// (case ignored). Two at most — the strip is a handle, and past two letters the tooltip and the open rail say it.
    /// </summary>
    internal static int InitialLength(IEnumerable<string> names)
    {
        var firsts = names.Select(n => Initials(n, 1).ToUpperInvariant()).ToList();
        return firsts.Count == firsts.Distinct(StringComparer.Ordinal).Count() ? 1 : 2;
    }

    /// <summary>The first <paramref name="letters"/> letters of a name, padded so every row is as wide; <c>?</c> for none.</summary>
    internal static string Initials(string name, int letters)
    {
        var runes = name.EnumerateRunes().Where(r => !Rune.IsWhiteSpace(r)).Take(letters).Select(r => r.ToString()).ToList();
        var text = runes.Count == 0 ? "?" : string.Concat(runes);
        return text.PadRight(letters);
    }

    /// <summary>
    /// A character row: its chord, the active marker, the connected dot, the name and its unread total.
    /// <para>
    /// The chord leads, in the column the window rows use. It is <c>⌥J</c> on the character one step
    /// forward in the cycle and <c>⌥K</c> one step back — the only two a single keystroke away — and
    /// blank on everybody else, including the row you are on, whose <c>▸</c> already says so. Reserved
    /// (<see cref="ChordFieldWidth"/>) so a row does not change width as the cycle moves, and leading
    /// rather than trailing so no blank status field separates it from the name it names.
    /// </para>
    /// </summary>
    private static string Character(RailRow row, bool reserve, ChromeInk ink)
    {
        var marker = row.Active ? "[bold]▸[/]" : " ";
        // A member of the send group wears a diamond in the dot's cell: the same width, so joining the group
        // moves no column and re-announces no pane size.
        var dot = row.InGroup ? (row.Connected ? "◆" : "◇") : row.Connected ? "●" : "○";
        var name = row.Active ? $"[bold]{Escape(row.Label)}[/]" : Escape(row.Label);
        return $"{Indent(row)}{ChordField(row.Chord, reserve)}"
            + Link(row, $"{marker} [{Accent(row, ink)}]{dot}[/] {name}{UnreadField(row.Unread, ink)}");
    }

    /// <summary>
    /// Cells kept for a row's chord whether or not it has one: the sigil, one digit or letter, and a
    /// separating space. Reserved for the reason <see cref="UnsentFieldWidth"/> is, and this one moves on
    /// events a reader would not call structural — a window past the ninth loses its chord, and the
    /// ⌥J/⌥K pair moves on every character switch.
    /// </summary>
    private const int ChordFieldWidth = 3;

    /// <summary>
    /// A chord in its fixed-width field, the same width in blanks when this row has none, or nothing at
    /// all when no row in the rail has one. Outside the row's <see cref="Link"/> span deliberately: it is
    /// chrome that names a key rather than part of the thing the row points at, so keeping it out leaves
    /// the click target on the name.
    /// </summary>
    private static string ChordField(string? chord, bool reserve)
    {
        if (!reserve)
        {
            return string.Empty;
        }

        if (chord is not { Length: > 0 } value)
        {
            return new string(' ', ChordFieldWidth);
        }

        var escaped = Escape(value);
        var pad = Math.Max(1, ChordFieldWidth - SharpMUTermApp.MarkupWidth(escaped));
        return $"[dim]{escaped}[/]{new string(' ', pad)}";
    }

    /// <summary>
    /// Cells kept for a row's unsent-draft pen whether or not there is one. <b>Nothing volatile on a row
    /// may cost a cell only when it has something to say:</b> the sidebar's width is its widest row, the
    /// panes are what is left over, and per-pane NAWS re-announces that size to every connected server —
    /// so a pen appearing on the first keystroke of a line reflows the game's own output.
    /// </summary>
    private const int UnsentFieldWidth = 2;

    /// <summary>
    /// Cells kept for an unread count, blank when there is none. See <see cref="UnreadField"/>. It is
    /// <see cref="UnreadBadge.FieldWidth"/> because the badge's own cap is what makes the field finite —
    /// the two numbers are one fact and may not be written down twice.
    /// </summary>
    private const int UnreadFieldWidth = UnreadBadge.FieldWidth;

    /// <summary>The pen, or the same width in blanks. See <see cref="UnsentFieldWidth"/>.</summary>
    private static string Unsent(bool unsent, ChromeInk ink) =>
        unsent ? $" [{ink.Draft}]{Glyphs.Draft}[/]" : new string(' ', UnsentFieldWidth);

    /// <summary>
    /// An unread count in a fixed-width field, right-aligned, blank at zero. Reserved for
    /// <see cref="UnsentFieldWidth"/>'s reason and more urgently: unread arrives unbidden from the wire.
    /// The cap is what makes the field finite. Wording and colour both come from
    /// <see cref="UnreadBadge"/>, so the sidebar and the tab strip cannot disagree about one count.
    /// </summary>
    private static string UnreadField(int unread, ChromeInk ink) =>
        unread <= 0
            ? new string(' ', UnreadFieldWidth)
            : $"[{UnreadBadge.TintFor(ink)}]{UnreadBadge.Format(unread).PadLeft(UnreadFieldWidth)}[/]";

    /// <summary>
    /// A window row: how you get to it, then what it is, then its badges.
    /// <para>
    /// The chord leads, in the reserved column the character rows use, so no blank status field sits
    /// between a name and the key that reaches it; the badges trail, where status belongs.
    /// </para>
    /// <para>
    /// The column is drawn only once the character holds a second window — with one there is one place to
    /// be. A window past the ninth shows blanks: it is still clickable and still reachable by ⌃N, and a
    /// column claiming a key that would go elsewhere is what this numbering exists to prevent.
    /// </para>
    /// <para>
    /// <c>closed</c> is a state rather than a destination, so it is drawn where the badges are rather than
    /// in the chord's field: a closed window has no chord, and putting the word where a key goes would be
    /// the two-meanings-in-one-column mistake again.
    /// </para>
    /// </summary>
    private static string Window(RailRow row, bool reserve, ChromeInk ink)
    {
        var name = Escape(row.Label);
        var closed = row.Closed ? " [dim]closed[/]" : string.Empty;
        return $"{Indent(row)}{ChordField(row.Closed ? null : row.Chord, reserve)}"
            + Link(row, $"[dim]▪[/] {name}{Unsent(row.Unsent, ink)}{UnreadField(row.Unread, ink)}{closed}");
    }

    /// <summary>
    /// Wraps already-styled markup in the row's click target, or returns it untouched when the row has
    /// none. The target is percent-escaped by <see cref="LinkUrl"/>, which is not cosmetic: both the
    /// framework's parser and our own <c>MarkupWidth</c> read a tag by scanning to the next <c>]</c>,
    /// so a world or window name containing a bracket would otherwise end the tag early — breaking the
    /// link and, worse, leaking the rest of the target into the row as visible text that changes the
    /// rail's width.
    /// </summary>
    private static string Link(RailRow row, string content) =>
        row.Target is { Length: > 0 } target ? $"[link={LinkUrl.Escape(target)}]{content}[/]" : content;

    private static string Indent(RailRow row) => new(' ', row.Indent * 2);

    /// <summary>
    /// A row's own accent as a markup hex, or the client's when it has none — either way held to the
    /// legibility floor against the plane the sidebar is drawn on. A world's accent is a colour a user
    /// picked in F5, and this is where it meets a plane it was not chosen against.
    /// </summary>
    private static string Accent(RailRow row, ChromeInk ink) =>
        row.Accent.Kind == TerminalColorKind.Rgb
            ? ink.Lift(new Rgb(row.Accent.R, row.Accent.G, row.Accent.B))
            : ink.Accent;
}
