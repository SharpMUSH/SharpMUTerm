using System.Globalization;
using System.Text;
using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Core.Configuration;

/// <summary>
/// One reading on the status row: a short label and the game data it shows, optionally out of a maximum —
/// <c>hp 450/500</c>. Each path may list alternatives separated by <c>|</c>, tried in order, so one meter reads
/// a GMCP game and an MSDP game alike.
/// </summary>
public sealed class StatusMeter
{
    /// <summary>What the row prints before the value. Kept short; it is clipped to <see cref="MaxLabelLength"/>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The value's path (<c>Char.Vitals.hp|MSDP.HEALTH</c>).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The maximum's path, or null for a meter with none (<c>gold</c>).</summary>
    public string? Max { get; set; }

    /// <summary>The longest label the row draws.</summary>
    public const int MaxLabelLength = 8;

    /// <summary>A label cut to <see cref="MaxLabelLength"/>.</summary>
    public static string Clip(string label) => label.Length > MaxLabelLength ? label[..MaxLabelLength] : label;

    /// <summary>
    /// The default row: health, mana and movement, by the names IRE, Aardwolf and the MSDP convention use.
    /// </summary>
    public static List<StatusMeter> Defaults() =>
    [
        new()
        {
            Label = "hp",
            Value = "Char.Vitals.hp|MSDP.HEALTH",
            Max = "Char.Vitals.maxhp|Char.MaxStats.maxhp|MSDP.HEALTH_MAX",
        },
        new()
        {
            Label = "mp",
            Value = "Char.Vitals.mp|Char.Vitals.mana|MSDP.MANA",
            Max = "Char.Vitals.maxmp|Char.MaxStats.maxmana|MSDP.MANA_MAX",
        },
        new()
        {
            Label = "mv",
            Value = "Char.Vitals.moves|MSDP.MOVEMENT",
            Max = "Char.MaxStats.maxmoves|MSDP.MOVEMENT_MAX",
        },
    ];

    /// <summary>The first alternative of <paramref name="paths"/> the store holds a value for, as text.</summary>
    public static string? Read(GameDataStore data, string? paths)
    {
        foreach (var path in Alternatives(paths))
        {
            if (data.GetText(path) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>The alternatives in a path list, trimmed, blanks dropped.</summary>
    public static IEnumerable<string> Alternatives(string? paths) =>
        (paths ?? string.Empty).Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The MSDP variables a list of meters reads — what a session asks an MSDP server to <c>REPORT</c>.
    /// </summary>
    public static IReadOnlyList<string> MsdpVariables(IEnumerable<StatusMeter> meters) => meters
        .SelectMany(m => Alternatives(m.Value).Concat(Alternatives(m.Max)))
        .Where(p => p.StartsWith(GameDataStore.MsdpPrefix + ".", StringComparison.OrdinalIgnoreCase))
        .Select(p => p[(GameDataStore.MsdpPrefix.Length + 1)..])
        .Where(v => v.Length > 0 && !v.Contains('.', StringComparison.Ordinal))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Writes meters as the one-line form F7 edits: <c>hp=Char.Vitals.hp/Char.Vitals.maxhp; gold=Char.Status.gold</c>.
    /// </summary>
    public static string Format(IEnumerable<StatusMeter> meters)
    {
        var sb = new StringBuilder();
        foreach (var meter in meters)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }

            sb.Append(meter.Label).Append('=').Append(meter.Value);
            if (!string.IsNullOrWhiteSpace(meter.Max))
            {
                sb.Append('/').Append(meter.Max);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads the one-line form back. A part with no <c>=</c>, no label or no value is skipped, so a half-typed
    /// entry costs nothing; the label is clipped to <see cref="MaxLabelLength"/>.
    /// </summary>
    public static List<StatusMeter> Parse(string? text)
    {
        var meters = new List<StatusMeter>();
        foreach (var part in (text ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var label = part[..eq].Trim();
            var paths = part[(eq + 1)..];
            var slash = paths.IndexOf('/', StringComparison.Ordinal);
            var value = (slash < 0 ? paths : paths[..slash]).Trim();
            var max = slash < 0 ? null : paths[(slash + 1)..].Trim();
            if (label.Length == 0 || value.Length == 0)
            {
                continue;
            }

            meters.Add(new StatusMeter
            {
                Label = label.Length > MaxLabelLength ? label[..MaxLabelLength] : label,
                Value = value,
                Max = string.IsNullOrEmpty(max) ? null : max,
            });
        }

        return meters;
    }

    /// <summary>
    /// A reading fitted to <paramref name="width"/> cells without wrapping a number past recognition: a whole
    /// number too long is shown in thousands or millions (<c>123k</c>, <c>12M</c>), anything else is clipped
    /// with an ellipsis. Never longer than the width, which is what lets the row reserve a fixed field.
    /// </summary>
    public static string Fit(string text, int width)
    {
        if (text.Length <= width)
        {
            return text;
        }

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
        {
            foreach (var (scale, suffix) in new[] { (1_000L, "k"), (1_000_000L, "M"), (1_000_000_000L, "G") })
            {
                var scaled = (n / scale).ToString(CultureInfo.InvariantCulture) + suffix;
                if (scaled.Length <= width)
                {
                    return scaled;
                }
            }
        }

        return width <= 1 ? "…" : text[..(width - 1)] + "…";
    }
}
