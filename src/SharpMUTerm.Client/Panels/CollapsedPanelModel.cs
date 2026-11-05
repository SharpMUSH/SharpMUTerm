namespace SharpMUTerm.Client.Panels;

/// <summary>
/// A panel the layout collapsed, as the one-line bar at the foot of its column: what it is (<c>Chat</c>), how many lines
/// have arrived in it unseen, and why it is folded away (<c>collapsed in combat</c>). The cells draw it as a row
/// (<c>CollapsedBarRenderer</c>) and the window as a native bar over that row; a click on either expands the panel into a
/// pane that shares its column. Only a host that draws strips (<c>HostChrome.Strips</c>) gives a collapsed panel a bar;
/// elsewhere it is a background tab, as it always was.
/// </summary>
/// <param name="Title">The panel's name: its first window's title.</param>
/// <param name="Unread">Lines that arrived in its windows while folded away.</param>
/// <param name="Note">Why it is folded: <c>collapsed in combat</c>, after the layout.</param>
public sealed record CollapsedPanelModel(string Title, int Unread, string Note)
{
    /// <summary>The unread count in words, or empty when there is none: <c>2 new</c>.</summary>
    public string UnreadText => Unread > 0 ? $"{(Unread > 99 ? "99+" : Unread.ToString(System.Globalization.CultureInfo.InvariantCulture))} new" : string.Empty;

    /// <summary>The note for a panel collapsed by <paramref name="layoutName"/>.</summary>
    public static string NoteFor(string? layoutName) =>
        string.IsNullOrWhiteSpace(layoutName) ? "collapsed" : $"collapsed in {layoutName.Trim().ToLowerInvariant()}";

    /// <summary>What a click on the bar does, for a tooltip and a screen reader.</summary>
    public const string ExpandHint = "Click to show it in the column";
}
