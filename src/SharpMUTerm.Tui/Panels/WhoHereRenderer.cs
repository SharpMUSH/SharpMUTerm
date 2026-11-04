using System.Globalization;
using SharpMUTerm.Core.Protocols;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// The who-here panel: <c>HERE  The Grand Plaza · 4</c>, then one row per person — players with a dot and how
/// long they have been idle when the game says, creatures dimmed under them. Every name is a link
/// (<see cref="LinkPrefix"/> and its row), answered by the panel's own handler and by nothing else.
/// </summary>
internal static class WhoHereRenderer
{
    /// <summary>The scheme a row's link carries, followed by its index in the roster drawn.</summary>
    internal const string LinkPrefix = "mux:who:";

    /// <summary>The cells the idle column takes when any row has one: <c>idle 59m</c>.</summary>
    internal const int IdleWidth = 8;

    /// <summary>Under this many seconds idle a player reads <c>active</c>.</summary>
    internal const int ActiveSeconds = 60;

    /// <summary>The panel's colours: the dot of an active player, and the plane they are measured on.</summary>
    internal readonly record struct Ink(string Accent);

    /// <summary>
    /// The panel's rows for a box <paramref name="width"/> by <paramref name="height"/> cells. Never wider than the
    /// box and never taller: a room fuller than the box ends in <c>… and N more</c>.
    /// </summary>
    internal static List<string> Render(string? room, IReadOnlyList<RoomOccupant> occupants, int width, int height, Ink ink)
    {
        width = Math.Max(8, width);
        height = Math.Max(1, height);
        var lines = new List<string>();

        var count = $" · {occupants.Count.ToString(CultureInfo.InvariantCulture)}";
        var title = string.IsNullOrWhiteSpace(room) ? string.Empty : "  " + Escape(room.Trim());
        lines.Add(ElideVisible($"[bold {ink.Accent}]HERE[/]{title}[dim]{count}[/]", width));
        if (height == 1)
        {
            return lines;
        }

        if (occupants.Count == 0)
        {
            lines.Add("[dim]Nobody else is here.[/]");
            return lines;
        }

        var showIdle = occupants.Any(o => o.IdleSeconds is not null) && width >= IdleWidth + 8;
        var nameWidth = width - 2 - (showIdle ? IdleWidth + 1 : 0);
        var rows = height - 1;
        var fits = occupants.Count <= rows ? occupants.Count : Math.Max(0, rows - 1);
        for (var i = 0; i < fits; i++)
        {
            lines.Add(Row(occupants[i], i, nameWidth, showIdle, ink));
        }

        if (fits < occupants.Count)
        {
            lines.Add($"[dim]… and {(occupants.Count - fits).ToString(CultureInfo.InvariantCulture)} more[/]");
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
    internal static string Idle(int seconds) => seconds switch
    {
        < ActiveSeconds => "active",
        < 3600 => $"idle {(seconds / 60).ToString(CultureInfo.InvariantCulture)}m",
        < 86400 => $"idle {(seconds / 3600).ToString(CultureInfo.InvariantCulture)}h",
        _ => $"idle {Math.Min(99, seconds / 86400).ToString(CultureInfo.InvariantCulture)}d",
    };

    private static string Row(RoomOccupant who, int index, int nameWidth, bool showIdle, Ink ink)
    {
        var name = Escape(Fit(who.Display, nameWidth));
        var link = $"[link={LinkPrefix}{index.ToString(CultureInfo.InvariantCulture)}]";
        if (!who.IsPlayer)
        {
            return $"[dim]·[/] {link}[dim]{name}[/][/]";
        }

        var idle = who.IdleSeconds is { } seconds && seconds >= ActiveSeconds;
        var dot = idle ? "[dim]●[/]" : $"[{ink.Accent}]●[/]";
        var row = $"{dot} {link}{name}[/]";
        if (!showIdle)
        {
            return row;
        }

        var status = who.IdleSeconds is { } s ? Idle(s) : string.Empty;
        return PadVisible(row, nameWidth + 2) + " " + $"[dim]{status.PadLeft(IdleWidth)}[/]";
    }

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : width <= 1 ? "…" : text[..(width - 1)] + "…";
}
