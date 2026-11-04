using System.Globalization;

namespace SharpMUTerm.Client.Modals;

/// <summary>One line of the history view: its absolute index in the session's scrollback, its text, and where the query matched.</summary>
public readonly record struct ScrollbackViewLine(long Index, string Text, int MatchStart, int MatchLength);

/// <summary>What a line of the history view is to the line the search found.</summary>
public enum ScrollbackLineRole
{
    /// <summary>A line of the page, away from the hit: drawn quieter.</summary>
    Page,

    /// <summary>One of the <see cref="ScrollbackViewModel.ContextRows"/> lines either side of the hit: the body ink.</summary>
    Context,

    /// <summary>The line the search found: marked.</summary>
    Hit,
}

/// <summary>
/// The read-only history view (⏎ on an older-history hit in ⌃F): <see cref="PageRows"/> lines of a session's scrollback
/// — the ring or the file-backed spill — around the line the search found, the found line marked and the query's run in
/// each line marked. It opens <b>centred on the found line</b> (<see cref="TopFor"/>), and the <see cref="ContextRows"/> lines
/// either side of it are its context (<see cref="Role"/>): drawn in the body ink, the rest of the page quieter, so the hit
/// reads with what was said around it. Drawn by the cell surface (<c>ScrollbackViewSurface.Render</c>) and the window's native modal from
/// the same record; every move is a ranged read the client makes (<c>ScrollHistoryView</c>), so the two show the same page.
/// <para>
/// <b>A view, not a pane.</b> It never puts old lines back into a pane buffer: a pane holds the newest
/// <c>PaneLines</c> and its indices are what every bar and boundary counts by, so a page of the past inserted there would
/// move all of them. It is a modal over the workspace, and Esc puts the reader back where they were.
/// </para>
/// </summary>
/// <param name="Window">What the window is called (the session's own window).</param>
/// <param name="Query">The search it came from, marked in each line.</param>
/// <param name="Top">The absolute index of the first line shown.</param>
/// <param name="Oldest">The oldest line the scrollback still holds.</param>
/// <param name="End">One past the newest line.</param>
/// <param name="Marked">The line the search found.</param>
/// <param name="Lines">The page, oldest first, at most <see cref="PageRows"/>.</param>
/// <param name="PageRows">
/// The lines a page holds: <see cref="PageRowsFor"/> the screen the client draws on, so the cell view fits an 80×24
/// terminal with its keys on screen. The window's view reads the same number, so both hosts show the same page.
/// </param>
public sealed record ScrollbackViewModel(
    string Window,
    string Query,
    long Top,
    long Oldest,
    long End,
    long Marked,
    IReadOnlyList<ScrollbackViewLine> Lines,
    int PageRows = ScrollbackViewModel.MaxPageRows)
{
    /// <summary>The most lines a page holds, on a screen with room for more.</summary>
    public const int MaxPageRows = 20;

    /// <summary>The fewest lines a page holds, however small the screen.</summary>
    public const int MinPageRows = 5;

    /// <summary>Below this many columns the title and the position take a row each (<see cref="TwoTitleRows"/>).</summary>
    public const int WideColumns = 100;

    /// <summary>
    /// The page a screen of <paramref name="rows"/> × <paramref name="columns"/> cells has room for: the view is a modal
    /// centred on the screen that must end above the command line and the status row, so it is at most <c>rows − 4</c>
    /// tall; less its frame (2), its title (one row, two below <see cref="WideColumns"/>), the blank under it, the blank
    /// above the keys and the keys (3). Between <see cref="MinPageRows"/> and <see cref="MaxPageRows"/>; 13 at 80×24.
    /// </summary>
    public static int PageRowsFor(int rows, int columns) =>
        Math.Clamp(rows - 4 - 2 - (TwoTitleRows(columns) ? 2 : 1) - 3, MinPageRows, MaxPageRows);

    /// <summary>Whether a screen <paramref name="columns"/> wide puts the title and the position on a row each.</summary>
    public static bool TwoTitleRows(int columns) => columns < WideColumns;

    /// <summary>The keys as a ladder, longest first, for a key row too narrow for all of them; every rung keeps Esc.</summary>
    public static IReadOnlyList<string> KeysLadder { get; } =
    [
        Keys,
        "↑↓ line · PgUp PgDn page · Home End ends · Esc closes",
        "↑↓ PgUp PgDn Home End · Esc closes",
        "↑↓ · Esc closes",
    ];

    /// <summary>How many lines either side of the found line are its context.</summary>
    public const int ContextRows = 3;

    /// <summary>The keys, in glyphs (a native view spells them in words).</summary>
    public const string Keys = "↑↓ a line · PgUp PgDn a page · Home End the ends · Esc closes";

    /// <summary>Where the page sits: <c>lines 12,301–12,320 of 40,000 · 27,680 lines before the newest</c>.</summary>
    public string Position
    {
        get
        {
            if (Lines.Count == 0)
            {
                return "nothing held here any more";
            }

            var first = Lines[0].Index - Oldest + 1;
            var last = Lines[^1].Index - Oldest + 1;
            var held = End - Oldest;
            var back = End - 1 - Lines[^1].Index;
            return $"lines {N(first)}–{N(last)} of {N(held)}" + (back > 0 ? $" · {N(back)} line{(back == 1 ? string.Empty : "s")} before the newest" : " · the newest");
        }
    }

    /// <summary>The title row: whose history, and that it is only read.</summary>
    public string Title => $"OLDER HISTORY · {Window} · read only";

    /// <summary>
    /// The top that shows <paramref name="marked"/> in the middle of a page of <paramref name="page"/> lines — half of them
    /// above it, the rest below — clamped to what is held, so a hit near either end sits as near the middle as the history allows.
    /// </summary>
    public static long TopFor(long marked, long oldest, long end, int page = MaxPageRows) => Clamp(marked - page / 2, oldest, end, page);

    /// <summary>What a line of the page is to the hit: the hit itself, its context (<see cref="ContextRows"/> either side), or the rest.</summary>
    public ScrollbackLineRole Role(ScrollbackViewLine line) =>
        line.Index == Marked ? ScrollbackLineRole.Hit
        : Math.Abs(line.Index - Marked) <= ContextRows ? ScrollbackLineRole.Context
        : ScrollbackLineRole.Page;

    /// <summary><paramref name="top"/> kept to a page of <paramref name="page"/> lines that starts at or after the oldest line and ends at or before the newest.</summary>
    public static long Clamp(long top, long oldest, long end, int page = MaxPageRows) => Math.Max(oldest, Math.Min(top, Math.Max(oldest, end - page)));

    /// <summary>The top a key moves to: ↑↓ a line, PgUp PgDn a page, Home End the ends; null for a key that does not move.</summary>
    public long? Move(ConsoleKey key) => key switch
    {
        ConsoleKey.UpArrow => Clamp(Top - 1, Oldest, End, PageRows),
        ConsoleKey.DownArrow => Clamp(Top + 1, Oldest, End, PageRows),
        ConsoleKey.PageUp => Clamp(Top - (PageRows - 1), Oldest, End, PageRows),
        ConsoleKey.PageDown => Clamp(Top + (PageRows - 1), Oldest, End, PageRows),
        ConsoleKey.Home => Oldest,
        ConsoleKey.End => Clamp(End, Oldest, End, PageRows),
        _ => null,
    };

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public bool Equals(ScrollbackViewModel? other) =>
        other is not null
        && Window == other.Window && Query == other.Query && Top == other.Top && Oldest == other.Oldest
        && End == other.End && Marked == other.Marked && PageRows == other.PageRows && ModelLists.Same(Lines, other.Lines);

    public override int GetHashCode() => HashCode.Combine(Window, Query, Top, Oldest, End, Marked, PageRows, ModelLists.Hash(Lines));
}

/// <summary>
/// One opening of the history view in a host's own controls. A move goes back to the client, which reads the page and
/// republishes; Esc closes.
/// </summary>
public sealed class ScrollbackViewSession : ModalSession<ScrollbackViewModel>
{
    private readonly Action<long> _scroll;

    /// <param name="model">The page it opens on.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="scroll">Reads the page starting at a line (<c>ScrollHistoryView</c>); called from any thread.</param>
    /// <param name="closed">Told the view closed.</param>
    public ScrollbackViewSession(ScrollbackViewModel model, bool isDark, Action<long> scroll, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        _scroll = scroll;
    }

    public override string Surface => "history-view";

    /// <summary>The host's: a key that moves the page. False for a key that does not.</summary>
    public bool Key(ConsoleKey key)
    {
        if (IsEnded || Model.Move(key) is not { } top)
        {
            return false;
        }

        if (top != Model.Top)
        {
            _scroll(top);
        }

        return true;
    }
}
