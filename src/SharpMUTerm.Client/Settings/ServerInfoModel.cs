using System.Globalization;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Telnet.Mssp;

namespace SharpMUTerm.Client.Settings;

/// <summary>Which of the three states a world's server information is in.</summary>
public enum ServerInfoState
{
    /// <summary>Nothing has dialled this world's endpoint.</summary>
    NeverConnected,

    /// <summary>The server answered and published no MSSP.</summary>
    PublishesNothing,

    /// <summary>A report, as of <see cref="ServerInfoModel.Captured"/>.</summary>
    Report,
}

/// <summary>One variable of a report, or one configured fact, as both hosts list it.</summary>
/// <param name="Label">The row's name: a headline's label, or the variable's own name off the wire (sanitised).</param>
/// <param name="Values">Each value as it reads (sanitised, <c>-1</c> as <see cref="ServerInfoModel.Unavailable"/>), at most <see cref="ServerInfoModel.MaxValueRows"/>.</param>
/// <param name="More">How many values past those were left out.</param>
/// <param name="Unofficial">Whether the name is not in the MSSP specification.</param>
public sealed record ServerInfoRow(string Label, IReadOnlyList<string> Values, int More = 0, bool Unofficial = false);

/// <summary>
/// What a world's server says about itself (MSSP), as one model the cell report (F5 ▸ <c>i</c>) and the window's world
/// page both draw: the world's own configuration first, then which of the three states the cache is in, then the
/// headline variables and everything else. Every value came off the wire from a stranger, so each is sanitised here
/// (control characters become spaces) and never longer than <see cref="MsspCache.MaxValueLength"/> already made it; a
/// host still fits it to its own width.
/// </summary>
public sealed record ServerInfoModel(
    IReadOnlyList<ServerInfoRow> Configured,
    ServerInfoState State,
    string? Captured,
    string? LastSeen,
    IReadOnlyList<ServerInfoRow> Headline,
    IReadOnlyList<ServerInfoRow> Others)
{
    /// <summary>How many values of one variable are listed before the rest are counted.</summary>
    public const int MaxValueRows = 8;

    /// <summary>What a row shows where the server said nothing at all.</summary>
    public const string Unreported = "—";

    /// <summary>What a numeric variable of <c>-1</c> reads as: the specification's "not available".</summary>
    public const string Unavailable = "unknown";

    /// <summary>The words the never-connected state is put in.</summary>
    public const string NeverConnected = "No server information yet — connect once and this fills in.";

    /// <summary>The words the connected-and-silent state is put in: the absence is normal, because it is.</summary>
    public const string NoMssp = "This server does not publish MSSP. It is optional, and most MUSHes do not.";

    /// <summary>The heading over the headline rows.</summary>
    public const string SummaryHeading = "SERVER";

    /// <summary>The heading over the variables that did not earn a headline row.</summary>
    public const string EverythingElse = "EVERYTHING ELSE";

    /// <summary>What the report says under <see cref="EverythingElse"/> when nothing else was sent.</summary>
    public const string NothingElse = "nothing else was sent";

    /// <summary>The legend saying what an unofficial mark means.</summary>
    public const string UnofficialLegend = "not in the MSSP specification";

    /// <summary>The first-class rows, in the order somebody browsing a world list wants them.</summary>
    public static IReadOnlyList<(string Label, string Variable)> HeadlineVariables { get; } =
    [
        ("name", MsspVariables.Name),
        ("players", MsspVariables.Players),
        ("uptime", MsspVariables.Uptime),
        ("codebase", MsspVariables.Codebase),
        ("family", MsspVariables.Family),
        ("hostname", MsspVariables.Hostname),
        ("port", MsspVariables.Port),
        ("ssl", MsspVariables.Ssl),
        ("charset", MsspVariables.Charset),
        ("contact", MsspVariables.Contact),
        ("website", MsspVariables.Website),
        ("genre", MsspVariables.Genre),
        ("status", MsspVariables.Status),
        ("minimum age", MsspVariables.MinimumAge),
    ];

    /// <summary>The model for <paramref name="world"/> and what the cache holds for its endpoint, read at <paramref name="now"/>.</summary>
    public static ServerInfoModel For(WorldDefinition? world, MsspObservation? observation, DateTimeOffset now)
    {
        var configured = world is null
            ? Array.Empty<ServerInfoRow>()
            : new[]
            {
                new ServerInfoRow("world", [Sanitize(world.Name)]),
                new ServerInfoRow("address", [Sanitize($"{world.Host}:{world.Port.ToString(CultureInfo.InvariantCulture)}")]),
                new ServerInfoRow("transport", [world.UseTls ? "TLS" : "plain"]),
            };

        if (observation is null)
        {
            return new ServerInfoModel(configured, ServerInfoState.NeverConnected, null, null, [], []);
        }

        if (observation is { Report: null } or { ObservedAt: null })
        {
            return new ServerInfoModel(configured, ServerInfoState.PublishesNothing, null, Since(observation.ConnectedAt, now), [], []);
        }

        var report = observation.Report!;
        var unofficial = new HashSet<string>(report.UnofficialNames, StringComparer.Ordinal);
        var drawn = new HashSet<string>(StringComparer.Ordinal);
        var headline = new List<ServerInfoRow>();
        foreach (var (label, variable) in HeadlineVariables)
        {
            drawn.Add(variable);
            headline.Add(Row(label, variable, report, unofficial, now));
        }

        var others = report.Keys
            .Where(name => !drawn.Contains(name))
            .Select(name => Row(Sanitize(name), name, report, unofficial, now))
            .ToList();
        return new ServerInfoModel(configured, ServerInfoState.Report, Since(observation.ObservedAt!.Value, now), null, headline, others);
    }

    private static ServerInfoRow Row(string label, string variable, MsspData report, HashSet<string> unofficial, DateTimeOffset now)
    {
        var values = report[variable];
        var mark = unofficial.Contains(variable);
        if (values.Count == 0)
        {
            // Two absences: a variable sent with no value is a fact about the server; one never mentioned, about the report.
            return new ServerInfoRow(label, [report.ContainsKey(variable) ? Unavailable : Unreported], 0, mark);
        }

        var shown = Math.Min(values.Count, MaxValueRows);
        return new ServerInfoRow(
            label,
            [.. values.Take(shown).Select(v => Sanitize(Reading(variable, v, now)))],
            values.Count - shown,
            mark);
    }

    /// <summary>How one value reads: <c>UPTIME</c> as a duration, <c>-1</c> as <see cref="Unavailable"/>, the rest as themselves.</summary>
    public static string Reading(string variable, string value, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(value);
        if (string.Equals(variable, MsspVariables.Uptime, StringComparison.Ordinal)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
            && unix > 0)
        {
            var booted = DateTimeOffset.FromUnixTimeSeconds(unix);
            return $"{Duration(now - booted)} (since {booted.UtcDateTime:yyyy-MM-dd HH:mm} UTC)";
        }

        return string.Equals(value, "-1", StringComparison.Ordinal) ? Unavailable : value;
    }

    /// <summary>Replaces every control character with a space, so a value off the wire cannot end a row early.</summary>
    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Any(char.IsControl))
        {
            return value;
        }

        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? ' ' : source[i];
            }
        });
    }

    /// <summary>How long ago something was, in words, with the exact instant beside it.</summary>
    public static string Since(DateTimeOffset at, DateTimeOffset now)
    {
        var ago = now - at;
        var words = ago < TimeSpan.Zero ? "just now" : $"{Duration(ago)} ago";
        return $"{words}  ({at.UtcDateTime:yyyy-MM-dd HH:mm} UTC)";
    }

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalDays >= 1 ? Plural((int)span.TotalDays, "day")
            : span.TotalHours >= 1 ? Plural((int)span.TotalHours, "hour")
            : span.TotalMinutes >= 1 ? Plural((int)span.TotalMinutes, "minute")
            : "moments";
    }

    private static string Plural(int count, string noun) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {noun}{(count == 1 ? string.Empty : "s")}";
}
