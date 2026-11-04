using System.Text;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Protocols;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// The vitals panel: one row of the configured meters as bars —
/// <c>hp ████████░░  820/1000      mp ██░░░░░░░░  190/1000  low</c> — and, right-aligned, what the character is
/// fighting when the game says: <c>target a rotting ghoul ████░░░░░░  40%</c>.
/// <para>
/// <b>Every field is reserved</b>, the status row's rule (<see cref="StatusMeterRenderer"/>): the value and the
/// maximum take <see cref="StatusMeterRenderer.ValueWidth"/> cells each, the <c>low</c> mark takes its three
/// whether it is showing or not, and the target's name is padded to its field. A number changing never moves a
/// cell. The <em>level</em> of detail is chosen from the box's width alone — full bars, short bars, no bars,
/// percentages — so it changes only when the pane does.
/// </para>
/// </summary>
internal static class VitalsStripRenderer
{
    /// <summary>The meter's mark when it is below <see cref="StatusMeterRenderer.LowShare"/>.</summary>
    internal const string LowMark = "low";

    /// <summary>The panel's colours: the bars, the warning, the empty part of a bar, and the card heading's band.</summary>
    internal readonly record struct Ink(string Bar, string Low, string Empty, HeadingBand.Ink? Heading = null);

    /// <summary>The one-cell low mark the tighter levels use in place of <see cref="LowMark"/>.</summary>
    internal const string ShortLowMark = "!";

    /// <summary>
    /// How much a level draws: the bar's cells, whether values are shown (else percentages), whether the low mark is
    /// the word or one cell, the target's name field and bar, and the gap between meters.
    /// </summary>
    private readonly record struct Level(int Bar, bool Values, bool LongLow, int TargetName, int TargetBar, int Gap);

    /// <summary>From the most to the least; the first that fits the width is drawn. The target gives way first.</summary>
    private static readonly Level[] Levels =
    [
        new(10, true, true, 20, 10, 3),
        new(10, true, true, 14, 0, 3),
        new(8, true, false, 12, 0, 2),
        new(5, true, false, 12, 0, 2),
        new(0, true, false, 12, 0, 2),
        new(0, false, false, 10, 0, 2),
    ];

    /// <summary>
    /// The strip for a box <paramref name="width"/> cells wide, or empty when no meter has a value and the game
    /// names no target.
    /// </summary>
    internal static string Render(IReadOnlyList<StatusMeterRenderer.MeterReading> meters, CombatTarget? target, int width, Ink ink)
    {
        if (meters.Count == 0 && target is null)
        {
            return string.Empty;
        }

        width = Math.Max(1, width);
        string? last = null;
        foreach (var level in Levels)
        {
            last = Compose(meters, target, level, width, ink);
            if (VisibleLength(last) <= width)
            {
                return last;
            }
        }

        return ElideVisible(last!, width);
    }

    /// <summary>The strip for a <see cref="VitalsModel"/>: the same model the window's native gauges are drawn from.</summary>
    internal static string Render(VitalsModel vitals, int width, Ink ink) =>
        Render(
            [.. vitals.Meters.Select(m => new StatusMeterRenderer.MeterReading(m.Label, m.Value, m.Max, m.HasMax, m.Share, m.Low))],
            vitals.Target is { } target ? new CombatTarget(target.Name, target.Share) : null,
            width,
            ink);

    /// <summary>
    /// The card a tall vitals pane draws (<see cref="Core.Layouts.PanelSizing.VitalsCardRows"/>): a heading, then per meter its title
    /// and numbers on one row and a bar the width of the box under it, then a rule and the target the same way, its
    /// rows held whether or not the game names one — the window's stacked gauges, block for block. A box too short
    /// for that draws one row a meter, and one row or two draws the strip.
    /// </summary>
    internal static IReadOnlyList<string> RenderCard(VitalsModel vitals, int width, int height, Ink ink)
    {
        width = Math.Max(1, width);
        var blocks = vitals.Meters.Count;
        if (height < 3 || vitals.IsEmpty)
        {
            return [Render(vitals, width, ink)];
        }

        var lines = new List<string>();

        // The card titles itself (a one-panel pane draws no tab strip): VITALS and where the numbers come from, as the
        // window's heading says it — when there is a row for it beside the blocks.
        if (height >= (2 * blocks) + 5)
        {
            var from = vitals.Source is { } named ? Escape(Fit(named, Math.Max(1, width - 8))) : null;
            lines.Add(ink.Heading is { } band
                ? HeadingBand.Row("VITALS", from is null ? string.Empty : "  " + from, string.Empty, width, band)
                : $"[bold {ink.Bar}]VITALS[/]" + (from is null ? string.Empty : $"  [dim]{from}[/]"));
            lines.Add(string.Empty);
        }

        if (height >= (2 * blocks) + 3)
        {
            foreach (var meter in vitals.Meters)
            {
                var colour = meter.Low ? ink.Low : ink.Bar;
                var title = Escape(Fit(meter.Low ? $"{meter.Title} · {LowMark}" : meter.Title, width - 1));
                lines.Add(Spread(meter.Low ? $"[bold {colour}]{title}[/]" : $"[bold]{title}[/]", Numbers(meter), width));
                lines.Add(Bar(meter.HasMax ? meter.Share ?? 0 : 1, width, colour, ink));
            }

            // The rule parts the meters from the target; with no target there is nothing to part them from.
            if (vitals.Target is { } target)
            {
                lines.Add($"[{ink.Empty}]{new string('─', width)}[/]");
                var percent = target.Share is { } share ? StatusMeterRenderer.Percent(share) : string.Empty;
                var name = Escape(Fit(target.Name, Math.Max(1, width - 8 - percent.Length)));
                lines.Add(Spread("[dim]Target[/] " + (target.Low ? $"[{ink.Low}]{name}[/]" : name), percent, width));
                lines.Add(target.Share is { } s ? Bar(s, width, target.Low ? ink.Low : ink.Bar, ink) : string.Empty);
            }

            return lines;
        }

        // One row a meter: the label, a bar in what is left, the value.
        foreach (var meter in vitals.Meters.Take(Math.Max(1, height - (vitals.Target is null ? 0 : 1))))
        {
            var colour = meter.Low ? ink.Low : ink.Bar;
            var value = Escape(StatusMeter.Fit(meter.Value, StatusMeterRenderer.ValueWidth));
            var label = Fit(meter.Label, 4).PadRight(4);
            var bar = Math.Max(0, width - label.Length - VisibleLength(value) - 2);
            var drawn = bar > 0 && meter.HasMax ? " " + Bar(meter.Share, bar, colour, ink) : new string(' ', bar + 1);
            lines.Add($"[dim]{Escape(label)}[/]{drawn} " + (meter.Low ? $"[bold {colour}]{value}[/]" : $"[bold]{value}[/]"));
        }

        if (vitals.Target is { } t && lines.Count < height)
        {
            var percent = t.Share is { } share ? StatusMeterRenderer.Percent(share) : string.Empty;
            lines.Add(Spread("[dim]target[/] " + Escape(Fit(t.Name, Math.Max(1, width - 8 - percent.Length))), percent, width));
        }

        return lines;
    }

    /// <summary>The numbers a card's meter row ends in: <c>412/500</c>, or the value alone.</summary>
    private static string Numbers(VitalsMeter meter) => meter.HasMax ? $"{meter.Value}/{meter.Max ?? "?"}" : meter.Value;

    /// <summary><paramref name="left"/> markup at the start of a row of <paramref name="width"/>, <paramref name="right"/> text at its end.</summary>
    private static string Spread(string left, string right, int width)
    {
        var room = width - VisibleLength(left) - right.Length;
        if (room < 1)
        {
            return ElideVisible(left, Math.Max(1, width - right.Length - 1)) + " " + Escape(right);
        }

        return left + new string(' ', room) + $"[bold]{Escape(right)}[/]";
    }

    /// <summary><paramref name="text"/> cut to <paramref name="width"/> cells with an ellipsis.</summary>
    private static string Fit(string text, int width) =>
        width <= 0 ? string.Empty : text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    private static string Compose(
        IReadOnlyList<StatusMeterRenderer.MeterReading> meters, CombatTarget? target, Level level, int width, Ink ink)
    {
        var left = string.Join(new string(' ', level.Gap), meters.Select(m => Meter(m, level, ink)));
        if (target is null)
        {
            return left;
        }

        var right = Target(target, level, ink);
        var gap = width - VisibleLength(left) - VisibleLength(right);
        return left.Length == 0
            ? new string(' ', Math.Max(0, gap)) + right
            : left + new string(' ', Math.Max(level.Gap, gap)) + right;
    }

    private static string Meter(StatusMeterRenderer.MeterReading meter, Level level, Ink ink)
    {
        var sb = new StringBuilder();
        sb.Append($"[dim]{Escape(meter.Label)}[/]");
        var colour = meter.Low ? ink.Low : ink.Bar;
        if (!level.Values)
        {
            var text = meter.Share is { } share
                ? StatusMeterRenderer.Percent(share)
                : StatusMeter.Fit(meter.Value, StatusMeterRenderer.ValueWidth);
            var width = meter.Share is null ? StatusMeterRenderer.ValueWidth : StatusMeterRenderer.ShareWidth;
            sb.Append(' ').Append(meter.Low
                ? $"[bold {colour}]{Escape(text.PadLeft(width))}[/]"
                : $"[bold]{Escape(text.PadLeft(width))}[/]");
            return sb.ToString();
        }

        if (level.Bar > 0 && meter.HasMax)
        {
            sb.Append(' ').Append(Bar(meter.Share, level.Bar, colour, ink));
        }

        var value = Escape(StatusMeter.Fit(meter.Value, StatusMeterRenderer.ValueWidth).PadLeft(StatusMeterRenderer.ValueWidth));
        sb.Append(' ').Append(meter.Low ? $"[bold {colour}]{value}[/]" : $"[bold]{value}[/]");
        if (meter.HasMax)
        {
            var max = StatusMeter.Fit(meter.Max ?? string.Empty, StatusMeterRenderer.ValueWidth).PadRight(StatusMeterRenderer.ValueWidth);
            sb.Append($"[dim]/{Escape(max)}[/]");
            var mark = level.LongLow ? LowMark : ShortLowMark;
            sb.Append(' ').Append(meter.Low ? $"[{colour}]{mark}[/]" : new string(' ', mark.Length));
        }

        return sb.ToString();
    }

    private static string Target(CombatTarget target, Level level, Ink ink)
    {
        var name = target.Name.Length <= level.TargetName
            ? target.Name.PadRight(level.TargetName)
            : target.Name[..(level.TargetName - 1)] + "…";
        var sb = new StringBuilder($"[dim]target[/] {Escape(name)}");
        if (target.Share is { } share)
        {
            var low = share < StatusMeterRenderer.LowShare;
            if (level.TargetBar > 0)
            {
                sb.Append(' ').Append(Bar(share, level.TargetBar, low ? ink.Low : ink.Bar, ink));
            }

            sb.Append($" [bold]{StatusMeterRenderer.Percent(share).PadLeft(StatusMeterRenderer.ShareWidth)}[/]");
        }
        else
        {
            sb.Append(new string(' ', (level.TargetBar > 0 ? level.TargetBar + 1 : 0) + 1 + StatusMeterRenderer.ShareWidth));
        }

        return sb.ToString();
    }

    /// <summary>
    /// A bar of <paramref name="cells"/>: the filled part in solid blocks, the rest in light shade. A meter whose
    /// maximum has not arrived draws all empty, so the bar's cells are held from the first value.
    /// </summary>
    internal static string Bar(double? share, int cells, string colour, Ink ink)
    {
        var filled = share is { } s ? (int)Math.Round(Math.Clamp(s, 0, 1) * cells) : 0;
        var full = filled > 0 ? $"[{colour}]{new string('█', filled)}[/]" : string.Empty;
        var empty = filled < cells ? $"[{ink.Empty}]{new string('░', cells - filled)}[/]" : string.Empty;
        return full + empty;
    }
}
