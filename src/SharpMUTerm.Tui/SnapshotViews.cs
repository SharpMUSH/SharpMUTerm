namespace SharpMUTerm.Tui;

/// <summary>
/// Every <c>--view</c> the demo scene can open, with null for the default workspace. The host parity test
/// renders each of them through every driver, and <c>SnapshotViewListTests</c> fails when
/// <see cref="SharpMUTermApp.RenderSnapshot"/> learns a view this list does not name — so a new view is
/// checked in the window the day it is added, not when somebody remembers to.
/// </summary>
internal static class SnapshotViews
{
    public static IReadOnlyList<string?> All { get; } =
    [
        null,
        "worlds", "settings", "triggers", "route", "highlight", "set", "aliases", "timers", "keypad", "textansi",
        "input", "logging", "password", "startup", "deletions",
        "triggers-edit", "logging-edit", "keypad-edit", "password-edit", "startup-edit",
        "freeze", "freeze-scrollback", "spawn", "split", "move", "drag",
        "history", "history-search", "history-search-filter", "draft", "draft2",
        "menu", "menu-split", "messages", "quit", "connections", "characters",
        "tint", "tint-input", "tint-input-moved", "tint-tabs",
        "compose", "compose-literal", "mssp", "mssp-none", "mssp-never", "web",
        "rail-long", "scrollback", "scrollback-up", "links", "away", "away-scrollback",
        "search", "search-regex", "search-all", "search-landed", "activity-bar",
        "mxp-image", "mxp-figure", "input-selection", "selection",
        "prefix", "prefix-panel", "focus", "focus-moved", "tabs", "tabs-many",
        "collapsed", "timestamps", "timestamps-toggled", "prompt", "meters", "gamedata", "map", "map-walking", "map-split",
        "packages", "package-review", "package-browse", "send-menu",
    ];
}
