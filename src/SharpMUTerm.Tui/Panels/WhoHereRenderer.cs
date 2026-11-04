using System.Globalization;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Protocols;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// The who-here panel: <c>HERE  The Grand Plaza · 4</c>, then one row per person — players with a dot and how
/// long they have been idle when the game says, creatures dimmed under them. Every name is a link
/// (<see cref="LinkPrefix"/> and its row), answered by the panel's own handler and by nothing else. Drawn from a
/// <see cref="WhoHereModel"/>, the one the window's native list is drawn from, so row N is the same person in both.
/// </summary>
internal static class WhoHereRenderer
{
    /// <summary>The scheme a row's link carries, followed by its index in the roster drawn.</summary>
    internal const string LinkPrefix = "mux:who:";

    /// <summary>The cells the idle column takes when any row has one: <c>idle 59m</c>.</summary>
    internal const int IdleWidth = 8;

    /// <summary>Under this many seconds idle a player reads <c>active</c>.</summary>
    internal const int ActiveSeconds = WhoHereModel.ActiveSeconds;

    /// <summary>What the panel says before the game has said who is here.</summary>
    internal const string UnknownText = WhoHereModel.UnknownText;

    /// <summary>What the panel says in an empty room.</summary>
    internal const string EmptyText = WhoHereModel.EmptyText;

    /// <summary>
    /// The panel's colours: the dot of an active player, and the heading's band (<see cref="HeadingBand"/>); with no
    /// band the heading is the bare accent label it was before the band.
    /// </summary>
    internal readonly record struct Ink(string Accent, HeadingBand.Ink? Heading = null);

    /// <summary>
    /// The panel's rows for a box <paramref name="width"/> by <paramref name="height"/> cells. Never wider than the
    /// box and never taller: a room fuller than the box ends in <c>… and N more</c>. The idle column is dropped (the
    /// dot keeps saying it) before a name would be cut to make room for it.
    /// </summary>
    internal static List<string> Render(string? room, IReadOnlyList<RoomOccupant> occupants, int width, int height, Ink ink) =>
        Render(WhoHereModel.From(room, occupants), width, height, ink);

    /// <summary>The panel's rows for a model; see the other overload.</summary>
    internal static List<string> Render(WhoHereModel model, int width, int height, Ink ink)
    {
        if (!model.Known)
        {
            return [$"[dim]{UnknownText}[/]"];
        }

        width = Math.Max(8, width);
        height = Math.Max(1, height);
        var lines = new List<string>();
        var rowsIn = model.Rows;

        var count = $" · {rowsIn.Count.ToString(CultureInfo.InvariantCulture)}";
        var title = model.Room is { } room ? "  " + Escape(room) : string.Empty;
        lines.Add(ink.Heading is { } band
            ? HeadingBand.Row("HERE", title + count, string.Empty, width, band)
            : ElideVisible($"[bold {ink.Accent}]HERE[/]{title}[dim]{count}[/]", width));
        if (height == 1)
        {
            return lines;
        }

        if (rowsIn.Count == 0)
        {
            lines.Add($"[dim]{EmptyText}[/]");
            return lines;
        }

        var rows = height - 1;
        var fits = rowsIn.Count <= rows ? rowsIn.Count : Math.Max(0, rows - 1);

        // The idle words go before any name is cut: the dot already says active or idle, and a name is who it is.
        var longest = rowsIn.Take(fits).Select(r => r.Display.Length).DefaultIfEmpty(0).Max();
        var showIdle = model.ShowsIdle && width >= IdleWidth + 8 && longest <= width - 2 - (IdleWidth + 1);
        var nameWidth = width - 2 - (showIdle ? IdleWidth + 1 : 0);
        for (var i = 0; i < fits; i++)
        {
            lines.Add(Row(rowsIn[i], i, nameWidth, showIdle, ink));
        }

        if (fits < rowsIn.Count)
        {
            lines.Add($"[dim]… and {(rowsIn.Count - fits).ToString(CultureInfo.InvariantCulture)} more[/]");
        }

        return lines;
    }

    /// <summary>The roster index a row's link names, or null for any other link.</summary>
    internal static int? ParseLink(string url) =>
        url.StartsWith(LinkPrefix, StringComparison.Ordinal)
        && int.TryParse(url.AsSpan(LinkPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;

    /// <summary>How an idle time reads: <c>active</c>, <c>idle 4m</c>, <c>idle 2h</c>, <c>idle 3d</c>.</summary>
    internal static string Idle(int seconds) => WhoHereModel.IdleText(seconds);

    private static string Row(WhoHereRow who, int index, int nameWidth, bool showIdle, Ink ink)
    {
        var name = Escape(Fit(who.Display, nameWidth));
        var link = $"[link={LinkPrefix}{index.ToString(CultureInfo.InvariantCulture)}]";
        if (who.IsNpc)
        {
            return $"[dim]·[/] {link}[dim]{name}[/][/]";
        }

        var dot = who.State == WhoPresence.Idle ? "[dim]●[/]" : $"[{ink.Accent}]●[/]";
        var row = $"{dot} {link}{name}[/]";
        if (!showIdle)
        {
            return row;
        }

        return PadVisible(row, nameWidth + 2) + " " + $"[dim]{(who.Idle ?? string.Empty).PadLeft(IdleWidth)}[/]";
    }

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : width <= 1 ? "…" : text[..(width - 1)] + "…";
}
