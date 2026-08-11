using System.Globalization;

namespace SharpMUTerm.Tui;

/// <summary>
/// Renders the bar marking where a reader was when they left the terminal: the one row saying that
/// everything below it arrived while they were away.
/// <para>
/// <b>Why a bar and not a restyling.</b> The same reasoning <see cref="RestoreBarRenderer"/> sets out —
/// mark the <em>boundary</em>, not the content. The lines below this bar are worth having because they
/// are the game's own text in the game's own colours, so tinting them to prove they are new would
/// destroy the thing being marked. One row, drawn like the freeze bar and the restore bar, which divide
/// the same pane for the same kind of reason.
/// </para>
/// <para>
/// It sits <em>above</em> its content, where the restore bar sits below its own, and the two are the
/// same rule read from opposite ends: a pane bottom-anchors, so what you land on is the newest line.
/// For restored content that newest line is the boundary itself; here the boundary is behind you, and
/// what you are looking for is up.
/// </para>
/// Pure, so the markup is unit-testable without a terminal.
/// </summary>
internal static class AwayBarRenderer
{
    /// <summary>How long the trailing rule is. The same 48 cells the freeze and restore bars draw.</summary>
    private const int RuleCells = 48;

    /// <summary>The label, kept as a constant so a test can look for the exact words a reader will see.</summary>
    internal const string Label = "AWAY";

    /// <summary>
    /// The label on the bar marking a <em>window</em> the reader was not watching, as against
    /// <see cref="Label"/>'s terminal absence. Two words rather than one bar with two meanings: AWAY is
    /// about the reader, NEW is about the window, and a reader who meets both in one client should be
    /// able to tell which absence they are looking at without counting the lines.
    /// </summary>
    internal const string MissedLabel = "NEW";

    /// <summary>
    /// The bar for <paramref name="lines"/> lines that arrived over an absence of <paramref name="away"/>,
    /// on an already-resolved <c>#rrggbb</c> accent.
    /// </summary>
    public static string Bar(int lines, TimeSpan away, string accentHex)
    {
        ArgumentException.ThrowIfNullOrEmpty(accentHex);

        // Both figures, for the reason the restore bar carries two: they answer different questions and a
        // returning reader asks both. The count is "is this a glance or a session's worth" — how much
        // scrolling is in front of me. The duration is "how far behind am I" — five minutes is a
        // conversation you can still join, two hours is a different evening.
        var count = lines == 1 ? "1 line" : $"{lines} lines";
        var rule = new string('─', RuleCells);
        return $"[{accentHex}]{Glyphs.Away} {Label}[/] "
            + $"[dim]{MarkupText.Escape($"{count} since you left · {Duration(away)}")} {rule}[/]";
    }

    /// <summary>
    /// The bar for <paramref name="lines"/> lines that arrived in a window while the reader was not
    /// watching it, on an already-resolved <c>#rrggbb</c> accent.
    /// <para>
    /// It carries a count and no duration, which is the one way it differs from <see cref="Bar"/>. The
    /// terminal bar's span is measured from the last input before the reader vanished — approximate, but
    /// an instant they were part of. This boundary is made at the moment a line lands in a window nobody
    /// is watching, so a duration on it would be timing the <em>output</em> rather than the absence.
    /// </para>
    /// </summary>
    public static string Missed(int lines, string accentHex)
    {
        ArgumentException.ThrowIfNullOrEmpty(accentHex);

        var count = lines == 1 ? "1 line" : $"{lines} lines";
        var rule = new string('─', RuleCells);
        return $"[{accentHex}]{Glyphs.Away} {MissedLabel}[/] "
            + $"[dim]{MarkupText.Escape($"{count} since you were here")} {rule}[/]";
    }

    /// <summary>
    /// An absence in the coarsest unit that still says something useful. Deliberately not seconds past
    /// the first minute and not minutes past the first day: the number is read at a glance to decide how
    /// much scrolling is ahead, and "2 h 14 min" and "2 h" lead to the same decision.
    /// <para>
    /// A negative or sub-minute span reads as "a moment", not as "0 min" — the anchor is the last input
    /// event rather than the moment of departure (focus-out is not observable, see
    /// <see cref="TerminalFocusWatcher"/>), so the figure is approximate by construction and should not
    /// wear a precision it does not have.
    /// </para>
    /// </summary>
    internal static string Duration(TimeSpan away)
    {
        if (away < TimeSpan.FromMinutes(1))
        {
            return "a moment";
        }

        if (away < TimeSpan.FromHours(1))
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)away.TotalMinutes} min");
        }

        if (away < TimeSpan.FromDays(1))
        {
            var hours = (int)away.TotalHours;
            var minutes = away.Minutes;
            return minutes == 0
                ? string.Create(CultureInfo.CurrentCulture, $"{hours} h")
                : string.Create(CultureInfo.CurrentCulture, $"{hours} h {minutes} min");
        }

        var days = (int)away.TotalDays;
        return days == 1 ? "1 day" : string.Create(CultureInfo.CurrentCulture, $"{days} days");
    }
}
