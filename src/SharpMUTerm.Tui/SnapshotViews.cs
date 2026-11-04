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
        "settings-search", "settings-meters", "settings-meters-pick", "settings-changed",
        "settings-themes", "settings-themes-solarized", "settings-themes-used",
        "triggers-edit", "logging-edit", "keypad-edit", "password-edit", "startup-edit",
        "freeze", "freeze-scrollback", "spawn", "split", "split-stacked", "move", "drag",
        "history", "history-search", "history-search-filter", "draft", "draft2",
        "menu", "menu-split", "menu-fuzzy", "menu-recent", "menu-alerts", "messages", "notices", "quit", "connections", "characters",
        "tint", "tint-input", "tint-input-moved", "tint-tabs",
        "compose", "compose-literal", "compose-preview", "moo-edit", "mssp", "mssp-none", "mssp-never", "web",
        "rail-long", "scrollback", "scrollback-up", "scrollback-split", "reflow", "links", "away", "away-scrollback",
        "search", "search-regex", "search-all", "search-landed", "activity-bar",
        "mxp-image", "mxp-figure", "input-selection", "selection", "trigger-builder",
        "prefix", "prefix-panel", "focus", "focus-moved", "tabs", "tabs-many",
        "collapsed", "timestamps", "timestamps-toggled", "theme-switched", "rail-keys", "prompt", "meters", "gamedata", "map", "map-walking", "map-split", "map-edit", "map-level", "map-area", "map-find", "map-mine",
        "packages", "package-review", "package-browse", "send-menu", "layouts", "layout-combat", "layout-roleplay",
        "panel-who", "panel-vitals", "panel-quickbar", "panel-composer",
        "welcome", "welcome-connect", "tour", "reconnect", "help", "help-topic", "dictionary-offer", "browse-worlds", "browse-worlds-failed", "browse-worlds-picked", "import-review", "join-wraps", "join-wraps-f5",
        "script-console", "script-list", "script-edit", "search-older", "search-history", "channel-capture", "highlight-names", "channels-f5", "scene-log",
    ];
}
