using System.Globalization;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Tui;

/// <summary>
/// The status row's meters: <c>hp   450/500    mp    80/120</c>, drawn from the session's game data.
/// <para>
/// <b>Every field is reserved.</b> A value and its maximum each take <see cref="ValueWidth"/> cells whatever
/// they hold, because they arrive from the wire: a meter that grew a cell at 99 → 100 would widen the row,
/// and a status row past the width wraps and takes a row off every pane (the scrollback distance learned that
/// first). A number too long for its field is scaled (<c>123k</c>) rather than allowed to grow.
/// </para>
/// <para>
/// <b>A meter with nothing to read is not drawn</b>, so a world with no GMCP or MSDP costs the row nothing.
/// It appears once, when the first value arrives — a one-off change of the same kind as a spawn window
/// opening — and from then on its width is fixed.
/// </para>
/// </summary>
internal static class StatusMeterRenderer
{
    /// <summary>The cells a value, and separately its maximum, always take.</summary>
    internal const int ValueWidth = 5;

    /// <summary>Below this share of its maximum a value is drawn in the warning ink.</summary>
    internal const double LowShare = SharpMUTerm.Client.Panels.VitalsModel.LowShare;

    /// <summary>The cells a compact meter's share takes: <c>100%</c>.</summary>
    internal const int ShareWidth = 4;

    /// <summary>
    /// The meters that have a value, as one markup run, or empty when none does. <paramref name="compact"/>
    /// draws a meter that has a numeric maximum as its share (<c>hp  82%</c>) in a reserved
    /// <see cref="ShareWidth"/>-cell field, for a status row too narrow for <c>value/max</c>; a meter with no
    /// usable maximum is drawn as its value either way.
    /// </summary>
    internal static string Render(IReadOnlyList<StatusMeter> meters, GameDataStore data, string lowInk, bool compact = false) =>
        string.Concat(Runs(meters, data, lowInk, DimMark, compact).Select(r =>
            r.Ink == DimMark ? $"[dim]{MarkupText.Escape(r.Text)}[/]"
            : r.Bold ? $"[bold{(r.Ink is null ? string.Empty : " " + r.Ink)}]{MarkupText.Escape(r.Text)}[/]"
            : MarkupText.Escape(r.Text)));

    /// <summary>The ink <see cref="Render"/> hands <see cref="Runs"/> to stand for the framework's <c>[dim]</c>.</summary>
    private const string DimMark = "dim";

    /// <summary>
    /// The meters that have a value, as status-row runs, or none when none does: each label in
    /// <paramref name="dimInk"/>, the value bold (in <paramref name="lowInk"/> when low), the maximum in
    /// <paramref name="dimInk"/>, two cells between meters. <see cref="Render"/> is the same runs as markup.
    /// With <paramref name="hold"/>, a meter's label is drawn in its colour (<see cref="MeterReading.Colour"/>) as
    /// <paramref name="hold"/> holds it to the row's plane, and a low meter's label in <paramref name="lowInk"/> with its
    /// value: the colour costs no cell.
    /// </summary>
    internal static List<SharpMUTerm.Client.Chrome.StatusRun> Runs(
        IReadOnlyList<StatusMeter> meters, GameDataStore data, string lowInk, string dimInk, bool compact = false,
        Func<string, string>? hold = null)
    {
        var runs = new List<SharpMUTerm.Client.Chrome.StatusRun>();
        foreach (var reading in Read(meters, data))
        {
            if (runs.Count > 0)
            {
                runs.Add(new("  ", null, false));
            }

            var value = compact && reading.Share is { } share
                ? Percent(share).PadLeft(ShareWidth)
                : StatusMeter.Fit(reading.Value, ValueWidth).PadLeft(ValueWidth);
            var labelInk = hold is null ? dimInk
                : reading.Low ? lowInk
                : reading.Colour is { } colour ? hold(colour)
                : dimInk;
            runs.Add(new(reading.Label, labelInk, false));
            runs.Add(new(" ", null, false));
            runs.Add(new(value, reading.Low ? lowInk : null, true));
            if (!(compact && reading.Share is not null) && reading.HasMax)
            {
                runs.Add(new($"/{StatusMeter.Fit(reading.Max ?? string.Empty, ValueWidth).PadRight(ValueWidth)}", dimInk, false));
            }
        }

        return runs;
    }

    /// <summary>
    /// One meter's reading, as every renderer of meters draws it: the status row and the vitals panel read the
    /// same values through this, so the two cannot disagree about what "low" is.
    /// </summary>
    /// <param name="Label">The meter's label.</param>
    /// <param name="Value">The value as the game sent it.</param>
    /// <param name="Max">The maximum as the game sent it, or null when the game has not sent one.</param>
    /// <param name="HasMax">Whether the meter is configured with a maximum at all, sent yet or not.</param>
    /// <param name="Share">Value over maximum, when both are numbers and the maximum is positive.</param>
    /// <param name="Low">Whether <see cref="Share"/> is under <see cref="LowShare"/>.</param>
    /// <param name="Colour">The meter's colour as <c>#rrggbb</c> before the floor (<c>MeterColours.For</c>), or null for the accent.</param>
    internal readonly record struct MeterReading(
        string Label, string Value, string? Max, bool HasMax, double? Share, bool Low, string? Colour = null);

    /// <summary>The meters that have a value, in their configured order. A meter with nothing to read is left out.</summary>
    internal static IReadOnlyList<MeterReading> Read(IReadOnlyList<StatusMeter> meters, GameDataStore data)
    {
        var readings = new List<MeterReading>();
        foreach (var meter in meters)
        {
            if (StatusMeter.Read(data, meter.Value) is not { } value)
            {
                continue;
            }

            var hasMax = !string.IsNullOrWhiteSpace(meter.Max);
            var max = hasMax ? StatusMeter.Read(data, meter.Max) : null;
            readings.Add(new MeterReading(
                meter.Label, value, max, hasMax, Share(value, max), IsLow(value, max), SharpMUTerm.Client.Panels.MeterColours.For(meter)));
        }

        return readings;
    }

    /// <summary>A share as a whole percentage, at most four cells: <c>82%</c>, <c>100%</c>.</summary>
    internal static string Percent(double share) =>
        $"{Math.Clamp((int)Math.Round(share * 100), 0, 999).ToString(CultureInfo.InvariantCulture)}%";

    private static bool IsLow(string value, string? max) => Share(value, max) < LowShare;

    private static double? Share(string value, string? max) =>
        max is not null
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
        && double.TryParse(max, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
        && m > 0
            ? v / m
            : null;
}
