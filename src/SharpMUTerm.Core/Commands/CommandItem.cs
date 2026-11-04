namespace SharpMUTerm.Core.Commands;

/// <summary>The section a <see cref="CommandItem"/> is grouped under in the command surface (⌃P).</summary>
public enum CommandGroup
{
    /// <summary>Navigation: switch character, jump to a window.</summary>
    GoTo,

    /// <summary>World/connection actions: connect, disconnect, reconnect.</summary>
    World,

    /// <summary>Terminal/session actions: logging, freeze/scrollback, clear.</summary>
    Terminal,

    /// <summary>Layout actions: split, zoom, close pane, move.</summary>
    Layout,

    /// <summary>The configuration screens — one entry per screen the host offers, F-key and all.</summary>
    Settings,
}

/// <summary>
/// One entry in the command surface — a title, an optional subtitle (its owner / unread / current
/// value), the group it renders under, and a stable <see cref="Id"/> the UI dispatches on. The
/// catalog is generated from live state, so <see cref="Subtitle"/> often carries dynamic detail.
/// </summary>
public sealed record CommandItem(CommandGroup Group, string Title, string Id, string? Subtitle = null)
{
    /// <summary>
    /// The chord that runs this entry on the running host, held apart from <see cref="Subtitle"/> so a surface
    /// can draw keys in a column of their own. Null when nothing but the surface runs it. <see cref="Subtitle"/>
    /// still carries it too, joined with the detail, for the readers that show one line.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// The few words describing the entry, without the key. Null when there are none, and also null on an entry
    /// that set only <see cref="Subtitle"/>; a surface then treats the whole subtitle as the detail.
    /// </summary>
    public string? Detail { get; init; }
}
