using System.Globalization;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Telnet.Mssp;
using static SharpMUTerm.Tui.MarkupText;
using static SharpMUTerm.Tui.ScreenPalette;

namespace SharpMUTerm.Tui;

/// <summary>
/// The read-only MSSP report for one world — what the server said about itself, and when it said it.
/// Reached from F5 with <c>i</c> on the selected world.
/// <para>
/// <b>Read-only is the shape, not an omission.</b> Every other settings screen exists to change
/// something, and this project's affordance rule is that a field well means "the keyboard can change
/// this here" and its absence means it cannot (<see cref="ScreenChrome.ReadOnly"/>). A whole screen of
/// well-less rows is therefore exactly right, and it has to be <em>deliberately</em> well-less so it does
/// not read as a form whose wells failed to render. Nothing here calls <see cref="ScreenChrome.Field"/>.
/// </para>
/// <para>
/// <b>Three states, and the two empty ones must not look the same.</b> A world we have never connected
/// to, a server that answered and publishes no MSSP, and a report. Conflating the last two is the easy
/// mistake and it is the one that makes a client look broken: on a MUSH, "this server publishes no MSSP"
/// is the ordinary answer, and saying so — with what we do know from the world's own configuration
/// beside it — is the same information as "no data" and the opposite impression.
/// </para>
/// <para>
/// <b>Every value here came off the wire from a stranger.</b> Three things follow, and all three
/// happen in <c>Row</c> in this order: control characters are replaced (a raw newline in a value would
/// otherwise end the row and shift everything below it), the raw text is truncated <em>before</em>
/// escaping (truncating escaped markup can split a <c>[[</c> pair into an unbalanced tag), and only
/// then is it escaped. Column widths are functions of the <em>terminal</em> and never of the data,
/// because this repository has twice paid for chrome whose width was a function of something arriving
/// from the wire — <c>RailRenderer.UnsentFieldWidth</c>, and the status row's scrollback distance that
/// wrapped every pane's height at 99 → 100.
/// </para>
/// </summary>
internal static class MsspScreenRenderer
{
    /// <summary>The screen's title, and what the ⌃P entry and the snapshot view are named after.</summary>
    internal const string Title = "Server information";

    /// <summary>The snapshot view name (<c>--view mssp</c>).</summary>
    internal const string View = "mssp";

    /// <summary>
    /// How wide the variable-name column runs. Official MSSP names top out at <c>XTERM 256 COLORS</c>
    /// (17) and <c>HIRING BUILDERS</c> (15); an invented one can be any length at all, so the column is
    /// a constant and a longer name is elided into it rather than being allowed to push the values
    /// right. See the type summary for why that is not fussiness.
    /// </summary>
    internal const int NameWidth = 20;

    /// <summary>
    /// The most of one value that is ever drawn. A server may legitimately send a long <c>WEBSITE</c> or
    /// a wordy <c>DESCRIPTION</c>; it may also send half a megabyte. <see cref="MsspCache.MaxValueLength"/>
    /// already bounds what is stored — this bounds what one row spends, which is the layout half of the
    /// same rule.
    /// </summary>
    internal const int ValueWidth = 58;

    /// <summary>The fewest cells a value is still worth drawing in, on a terminal too narrow for more.</summary>
    private const int MinValueWidth = 16;

    /// <summary>What a row spends before the value: the mark column, the name column, and their gaps.</summary>
    private const int RowChrome = 1 + 1 + 1 + NameWidth + 2;

    /// <summary>
    /// How much of a value this frame can afford — <see cref="ValueWidth"/> where there is room for it,
    /// and what is left of the terminal where there is not.
    /// <para>
    /// <b>A function of the terminal, never of the data.</b> That is the whole discipline: this
    /// repository has twice shipped chrome whose width was a function of something arriving from the
    /// wire, and both times the symptom was a row growing past its box and taking a row off everything
    /// below it. A value at the full 58 cells needs 83, so at 80 columns the constant alone would have
    /// overrun by three — visible in a rendered frame and in nothing else.
    /// </para>
    /// </summary>
    private static int ValueCells(int width) =>
        width <= 0 ? ValueWidth : Math.Clamp(width - RowChrome - 1, MinValueWidth, ValueWidth);

    /// <summary>
    /// How many values of one variable are listed before the rest are summarised. Multi-valued variables
    /// are real and are the reason the model is a name → <em>list</em> map at all (<c>PORT</c>,
    /// <c>REFERRAL</c>, <c>CODEBASE</c>), so they are drawn as the list they are — one value per row,
    /// the name printed once — rather than as their first or last value. This caps the rows one variable
    /// can spend.
    /// </summary>
    internal const int MaxValueRows = ServerInfoModel.MaxValueRows;

    /// <summary>What a row shows where the server said nothing at all.</summary>
    internal const string Unreported = ServerInfoModel.Unreported;

    /// <summary>What a numeric world variable of <c>-1</c> reads as: the specification's "not available".</summary>
    internal const string Unavailable = ServerInfoModel.Unavailable;

    /// <summary>The words the never-connected state is put in.</summary>
    internal const string NeverConnected = ServerInfoModel.NeverConnected;

    /// <summary>
    /// The words the connected-and-silent state is put in. It says the absence is normal because it is:
    /// MSSP is optional, most MUSHes do not implement it, and a client that reported the ordinary case
    /// as a failure would be teaching people to distrust the screen.
    /// </summary>
    internal const string NoMssp = ServerInfoModel.NoMssp;

    /// <summary>The heading over the variables that did not earn a headline row.</summary>
    internal const string EverythingElse = ServerInfoModel.EverythingElse;

    /// <summary>The heading over the headline rows.</summary>
    internal const string SummaryHeading = ServerInfoModel.SummaryHeading;

    /// <summary>
    /// How an unofficial variable is marked. Official and unofficial must both be visible and must be
    /// told apart: a name the specification defines is a claim a crawler and a client read the same way,
    /// and a name somebody's codebase invented is not — and the reader cannot tell which is which from
    /// the name alone (<c>DISCORD</c> is official as of 2.7.0; <c>PUEBLO</c>, which looks every bit as
    /// standard, is not).
    /// </summary>
    internal const string UnofficialMark = "·";

    /// <summary>The legend that says what <see cref="UnofficialMark"/> means, so the mark is readable.</summary>
    internal const string UnofficialLegend = ServerInfoModel.UnofficialLegend;

    /// <summary>
    /// The screen's navigable shape: one stop per drawn body row, so ↑↓ scroll a report longer than the
    /// screen through <see cref="ScreenChrome.Window"/>.
    /// <para>
    /// There is nothing to edit, nothing to toggle and nothing to remove, and the shape says so: the
    /// header offers no <c>⏎ edit</c>, no <c>Space toggle</c> and no <c>Del remove</c>, because every one
    /// of those hints is derived from this model rather than written by the screen.
    /// </para>
    /// </summary>
    internal static ScreenModel Model(
        WorldDefinition? world, MsspObservation? observation, DateTimeOffset now, int width = 0) =>
        new(ScreenModel.Stops(Body(world, observation, now, width).Count));

    /// <summary>
    /// The screen's header band: its title, and the one key that leaves it. It does not go through
    /// <see cref="ScreenChrome.Hints"/> because that composes a settings screen's contract —
    /// <c>F5/Esc close</c> — and this screen's Esc does something else: it goes <em>back</em> to the
    /// screen that opened it, with its selection intact. A header offering "close" would name the right
    /// key for the wrong outcome.
    /// </summary>
    internal static string HeaderLine(int width)
    {
        var hints = $"[{Label}]↑↓ scroll · [/][{Accent}]Esc[/][{Label}] back [/]";
        return SpreadTitle($" [bold {Value}]{Escape(Title)}[/]", hints, width);
    }

    /// <summary>The screen's action bar: where the cursor is in the report, and the key that leaves.</summary>
    internal static string FooterLine(
        WorldDefinition? world, MsspObservation? observation, ScreenFocus? focus, int width, DateTimeOffset now)
    {
        var rows = Body(world, observation, now, width).Count;
        var context = ScreenChrome.Context(
            world is null ? null : Escape(world.Name),
            observation is null ? null : Escape(observation.Endpoint),
            focus is { } cursor && cursor.Index >= 0 && rows > 0
                ? ScreenChrome.Position("row", cursor.Index, rows)
                : null);

        return SpreadLR(
            " " + context,
            $"[{Label}] [[Esc]] Back [/]",
            width);
    }

    /// <summary>
    /// The whole report as markup rows, with the cursor band on the focused one and the block windowed
    /// to <paramref name="height"/>. <see cref="Body"/> is the same rows without either, which is what
    /// <see cref="Model"/> counts — so the number of cursor stops and the number of drawn rows are one
    /// number by construction rather than by two functions agreeing.
    /// </summary>
    internal static List<string> Render(
        WorldDefinition? world,
        MsspObservation? observation,
        DateTimeOffset now,
        ScreenFocus? focus = null,
        int height = 0,
        int width = 0)
    {
        var cursor = focus ?? ScreenFocus.None;
        var body = Body(world, observation, now, width);
        for (var i = 0; i < body.Count; i++)
        {
            // The bar spans the terminal, not the columns: this screen is one pane filling the window,
            // so a bar that stopped where the value column stops would read as a second, invisible
            // column edge. On F5 the same call pads to the pane's width, which is the same rule.
            body[i] = ScreenChrome.Cursor(
                body[i], cursor.IsOn(0, i), width > 0 ? width : RowChrome + ValueWidth);
        }

        return ScreenChrome.Window(body, height);
    }

    /// <summary>
    /// The report's rows, unfocused and unwindowed. Deterministic in its arguments, because
    /// <see cref="Model"/> counts these rows and <see cref="Render"/> draws them: two functions that
    /// disagreed about how many there are would give the screen a cursor stop nobody ever drew, which is
    /// the failure <see cref="ScreenChrome.Window"/> exists one level up to stop.
    /// </summary>
    internal static List<string> Body(
        WorldDefinition? world, MsspObservation? observation, DateTimeOffset now, int width = 0)
    {
        // The rows are the Client's model (the window's world page draws the same one); this only lays them out.
        var cells = ValueCells(width);
        var model = ServerInfoModel.For(world, observation, now);
        var rows = new List<string>();
        if (world is null)
        {
            rows.Add($"  [{Muted}]no world selected[/]");
        }

        foreach (var row in model.Configured)
        {
            AddRow(rows, row, cells);
        }

        switch (model.State)
        {
            case ServerInfoState.NeverConnected:
                rows.Add(string.Empty);
                rows.Add($"  [{Muted}]{Escape(NeverConnected)}[/]");
                return rows;
            case ServerInfoState.PublishesNothing:
                rows.Add(string.Empty);
                rows.Add($"  [{Muted}]{Escape(NoMssp)}[/]");
                rows.Add(string.Empty);
                rows.Add(Row("last seen", model.LastSeen ?? string.Empty, cells));
                return rows;
        }

        rows.Add(string.Empty);
        rows.Add(Row("captured", model.Captured ?? string.Empty, cells));
        rows.Add(string.Empty);
        rows.Add($"  [{Label}]{SummaryHeading}[/]");
        foreach (var row in model.Headline)
        {
            AddRow(rows, row, cells);
        }

        rows.Add(string.Empty);
        rows.Add($"  [{Label}]{EverythingElse}[/]");
        if (model.Others.Count == 0)
        {
            rows.Add($"  [{Muted}]{ServerInfoModel.NothingElse}[/]");
            return rows;
        }

        foreach (var row in model.Others)
        {
            AddRow(rows, row, cells);
        }

        rows.Add(string.Empty);
        rows.Add($"  [{Muted}]{UnofficialMark}  {Escape(UnofficialLegend)}[/]");
        return rows;
    }

    /// <summary>
    /// One variable as one row per value, the name on the first row only, so a three-port server reads as one variable
    /// with three values; past <see cref="MaxValueRows"/> the rest are counted.
    /// </summary>
    private static void AddRow(List<string> rows, ServerInfoRow row, int cells)
    {
        var mark = row.Unofficial ? UnofficialMark : " ";
        for (var i = 0; i < row.Values.Count; i++)
        {
            rows.Add(Row(i == 0 ? row.Label : string.Empty, row.Values[i], cells, i == 0 ? mark : " "));
        }

        if (row.More > 0)
        {
            rows.Add(Row(string.Empty, $"… {row.More.ToString(CultureInfo.InvariantCulture)} more", cells));
        }
    }

    /// <summary>
    /// A <c>label   value</c> row: truncated, then escaped (in that order, so a <c>[[</c> pair is never cut in half),
    /// with a one-cell mark column reserved whether or not the variable is unofficial. The label is padded before it
    /// is escaped, because escaping doubles brackets and a padded escape would be fewer visible cells. Both halves are
    /// already sanitised by <see cref="ServerInfoModel"/>; this sanitises again for a caller handing raw text.
    /// </summary>
    private static string Row(string label, string value, int cells, string mark = " ") =>
        $" [{Muted}]{mark}[/] [{Label}]{Escape(Fit(ServerInfoModel.Sanitize(label), NameWidth).PadRight(NameWidth))}[/]  "
        + ScreenChrome.ReadOnly(Fit(ServerInfoModel.Sanitize(value), cells));

    /// <summary>Truncates raw text to <paramref name="width"/> cells, ellipsis included — before escaping, never after.</summary>
    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(1, width - 1)] + "…";
}
