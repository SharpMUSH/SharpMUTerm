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
    internal const double LowShare = 0.25;

    /// <summary>The meters that have a value, as one markup run, or empty when none does.</summary>
    internal static string Render(IReadOnlyList<StatusMeter> meters, GameDataStore data, string lowInk)
    {
        var cells = new List<string>();
        foreach (var meter in meters)
        {
            if (StatusMeter.Read(data, meter.Value) is not { } value)
            {
                continue;
            }

            var max = string.IsNullOrWhiteSpace(meter.Max) ? null : StatusMeter.Read(data, meter.Max);
            var low = IsLow(value, max);
            var label = MarkupText.Escape(meter.Label);
            var shown = MarkupText.Escape(StatusMeter.Fit(value, ValueWidth).PadLeft(ValueWidth));
            var cell = low
                ? $"[dim]{label}[/] [bold {lowInk}]{shown}[/]"
                : $"[dim]{label}[/] [bold]{shown}[/]";
            if (!string.IsNullOrWhiteSpace(meter.Max))
            {
                cell += $"[dim]/{MarkupText.Escape(StatusMeter.Fit(max ?? string.Empty, ValueWidth).PadRight(ValueWidth))}[/]";
            }

            cells.Add(cell);
        }

        return string.Join("  ", cells);
    }

    private static bool IsLow(string value, string? max) =>
        max is not null
        && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
        && double.TryParse(max, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
        && m > 0
        && v / m < LowShare;
}
