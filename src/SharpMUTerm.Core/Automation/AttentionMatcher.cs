using System.Text.RegularExpressions;

namespace SharpMUTerm.Core.Automation;

/// <summary>Why a line asked for the player's attention.</summary>
public enum AttentionReason
{
    /// <summary>A trigger marked <c>alert</c> matched it.</summary>
    Trigger,

    /// <summary>It names the player's character.</summary>
    Name,

    /// <summary>It is a page, a tell or a whisper to the player.</summary>
    Page,
}

/// <summary>A line that asked for attention, and why.</summary>
public sealed class AttentionEventArgs(AttentionReason reason, string text) : EventArgs
{
    public AttentionReason Reason { get; } = reason;

    /// <summary>The line as it is shown.</summary>
    public string Text { get; } = text;
}

/// <summary>
/// The built-in "my name and pages" rule a world opts into (<see cref="Configuration.WorldDefinition.AlertOnName"/>).
/// <para>
/// <b>A page or a tell</b> is read off the shapes the common servers use, anchored at the start of the line:
/// <c>Bob pages: …</c> and <c>From afar, Bob …</c> (PennMUSH, TinyMUX), <c>Bob tells you …</c> (Diku and its
/// family), <c>Bob whispers: …</c> / <c>Bob whispers to you …</c>. A line starting <c>You </c> is the player's own.
/// </para>
/// <para>
/// <b>The name</b> counts as a whole word, case ignored, <em>anywhere but the start of the line</em>: a MUSH shows the
/// player their own pose as <c>Corvid waves.</c>, and every one of those would otherwise be an alert. A name shorter
/// than two letters is never matched. The client's own echo and system lines are not output and never reach this.
/// </para>
/// </summary>
public static partial class AttentionMatcher
{
    /// <summary>Why <paramref name="line"/> asks for attention for <paramref name="character"/>, or null.</summary>
    public static AttentionReason? Classify(string line, string? character)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (PagePattern().IsMatch(line))
        {
            return AttentionReason.Page;
        }

        if (character is { } name && name.Trim().Length >= 2 && MentionsName(line, name.Trim()))
        {
            return AttentionReason.Name;
        }

        return null;
    }

    private static bool MentionsName(string line, string name)
    {
        for (var at = line.IndexOf(name, 1, StringComparison.OrdinalIgnoreCase);
             at > 0;
             at = at + 1 < line.Length ? line.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase) : -1)
        {
            var before = line[at - 1];
            var end = at + name.Length;
            if (!char.IsLetterOrDigit(before) && before != '_'
                && (end >= line.Length || (!char.IsLetterOrDigit(line[end]) && line[end] != '_')))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(
        @"^(?:From afar, \S|\S+ pages(?:[:,]| you\b)|\S+ tells you\b|\S+ whispers(?::| to you\b))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex PagePattern();
}

/// <summary>
/// At most one alert per <see cref="Interval"/>, so a channel spamming the player's name, or a trigger matching
/// every line of a fight, flashes the taskbar once rather than for ever. Not thread-safe: the app calls it on its own
/// thread.
/// </summary>
public sealed class AttentionThrottle(TimeProvider time)
{
    /// <summary>The shortest gap between two alerts.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private DateTimeOffset? _last;

    /// <summary>True, and the clock starts again, when an alert may go now.</summary>
    public bool TryTake()
    {
        var now = time.GetUtcNow();
        if (_last is { } last && now - last < Interval)
        {
            return false;
        }

        _last = now;
        return true;
    }
}
