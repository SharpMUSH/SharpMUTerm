using System.Globalization;

namespace SharpMUTerm.Tui;

/// <summary>
/// The one spelling of an unread count, shared by the two surfaces that draw one: the connection rail's
/// per-row badge (<see cref="RailRenderer"/>) and a pane's tab label (<see cref="TabTitles"/>). Shared
/// because both are views of a single number, and two formatters would eventually disagree about it.
/// </summary>
internal static class UnreadBadge
{
    /// <summary>The largest count written in full; above it the badge reads <c>99+</c> and stops growing.</summary>
    internal const int Max = 99;

    /// <summary>Cells a badge can occupy, which is the width of <c>99+</c>. See <see cref="RailRenderer"/>.</summary>
    internal const int FieldWidth = 3;

    /// <summary>
    /// The markup colour a count is drawn in — the client's accent for the active theme, resolved rather
    /// than fixed because both surfaces it lands on move with the theme.
    /// <para>
    /// A <em>foreground</em>, and the one hue in the workspace no plane is painted in, so it cannot be
    /// mistaken for focus (a background) or selection (weight): a tab can be any combination of the
    /// three and each reads distinctly.
    /// </para>
    /// </summary>
    internal static string TintFor(ChromeInk? ink) => (ink ?? ChromeInk.Default).Accent;

    /// <summary>A count as it is written, capped at <see cref="Max"/>.</summary>
    internal static string Format(int unread) =>
        unread > Max ? $"{Max}+" : unread.ToString(CultureInfo.InvariantCulture);
}
