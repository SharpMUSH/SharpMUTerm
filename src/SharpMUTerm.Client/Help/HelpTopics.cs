using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Client.Help;

/// <summary>
/// The short how-to pages help lists. Plain sentences, keys in glyphs (a window spells them in words on the way to the
/// screen). Each page answers one question a new player asks, and names the key or the screen that does the thing; it
/// does not try to be the manual.
/// </summary>
public static class HelpTopics
{
    /// <summary>The pages, with <paramref name="newline"/> (the host's newline chord) where a page names it.</summary>
    public static IReadOnlyList<HelpTopic> For(string newline) =>
    [
        new("getting-started", "Getting started", "connect to a game, and what the screen is", [
            "Connect: type /connect mud.example.org 4000 (add tls for a secure port), or open the welcome",
            "with /welcome. F5 keeps worlds and their characters; a character's password and connect line",
            "log you in once a connection is open.",
            "",
            "The screen: output fills the panes, the command line is at the bottom and ⏎ sends what is on",
            "it to the character whose pane is focused. The sidebar lists worlds, characters and windows;",
            "click a row, or ⌥C to walk it with the keys.",
            "",
            $"Typing: ↑/↓ recall what you sent, ⌃R searches it, {newline} adds a second line, ⇥ completes a name.",
            "",
            "Everything else: ⌃P lists every command with its key. Type a few letters to find one.",
            "⌃Q quits and says first what a quit would end.",
        ]),
        new("reading", "Reading output", "scroll back, search, freeze, join a server's wraps", [
            "Scroll: PgUp/PgDn a page, ⇧↑/⇧↓ a line, the wheel. Scrolled back, the pane splits: history on",
            "top, the live tail below. End or ⌃End goes back to live output.",
            "",
            "Search: ⌃F finds text in what the panes hold. ⌥E reads the query as a pattern, ⌥A widens it",
            "to every window, ⏎ goes to the line, ⌥G to the next one.",
            "",
            "Freeze: ⌥F holds a pane still while output keeps arriving under it; ⌥F again thaws it.",
            "",
            "Ragged lines: a game that wraps its text at 80 columns leaves a stray word on a row of its own",
            "in a narrower pane. F5 ▸ join wraps joins those lines back so they wrap to the pane. It is off",
            "until you turn it on, because a wrong join runs two lines into one; while a pane is narrower than",
            "its game wraps, ⌃P suggests it.",
            "",
            "Copy: drag across output, then ⌃C.",
        ]),
        new("layouts", "Layouts", "panes, panels, and saving an arrangement", [
            "⌥M lists the layouts: Roleplay (who is here, a composer), Combat (vitals, a map, a quick bar)",
            "and your own. ⏎ applies one; n saves the panes as they are now as a new layout.",
            "",
            "Panes: ⌃B | splits right, ⌃B - splits down, ⌃B z zooms, ⌃B x closes. ⌃←→↑↓ move between panes,",
            "⌃O cycles them, ⌃N cycles a pane's tabs. ⌥⇧ and an arrow make the pane bigger or smaller.",
            "Drag a tab onto another pane to move it there; an edge splits.",
            "",
            "A layout shows a panel only once there is something for it: the map after the first room,",
            "vitals after the game sends them. F5 ▸ layout picks one per world.",
        ]),
        new("triggers", "Triggers", "match a line, then colour it, send it to a window, or reply", [
            "F2 holds the rules. A rule matches a line (contains, a wildcard pattern with *, or a regex),",
            "and then does any of: highlight it, route it to a window (Chat, say), gag it, or send a reply.",
            "",
            "Quickest way: drag across a line of output, then ⌃P ▸ Make a trigger from the selection.",
            "F2 opens on a rule for that line with the variable parts as *, and a live test underneath",
            "shows which recent lines it would match.",
            "",
            "Rules live in trigger sets, shared between characters: F5 says which sets a character uses.",
            "Aliases (F3) rewrite what you type; macros (F4) put commands on keys; timers (F6) repeat them.",
            "/triggers says why a window did or did not open.",
        ]),
        new("composer", "Composer", "write a long post, check its spelling", [
            "F1 opens a full-screen editor. The whole buffer is one command, verb and all (+bbpost Title=...),",
            "and its line breaks are sent as %r. ⌃S sends; Esc closes and keeps the draft for that character.",
            "",
            "⌥L switches between sending the text as typed and escaping it, so % [ ] and ; arrive as written.",
            "⌥P shows the post exactly as it will be sent.",
            "",
            "Spelling: with a Hunspell dictionary, misspelt words are marked and ⌥W adds the word at the caret",
            "to your own list. With none, ⌥W offers to download one (English, from LibreOffice) into the",
            "dictionaries folder beside your configuration. Nothing is downloaded until you say so.",
            "",
            "The Roleplay layout docks a composer under the output that shares F1's draft.",
        ]),
        new("keys", "Keys", "windows, characters, panes, and the ⌃B keys", [
            "Windows: ⌥1 to ⌥9 go to a numbered window of the character you are on — the numbers are the ones",
            "the sidebar prints beside each window.",
            "Characters: ⌥J and ⌥K move to the next and previous character you have open.",
            "Connection: ⌥R reconnects, ⌥D disconnects. Neither asks.",
            "",
            "Panes: ⌃B then a key — | split right, - split down, z zoom, o next, 1-9 go to a pane, x close,",
            "b fold the sidebar, m move a window, i a second command line. Pause after ⌃B to see them all.",
            "",
            $"Typing: {newline} newline, ⌥←/→ by word, ⌃Z/⌃Y undo and redo, ⌃A select all, ⌃C copy, ⌃X cut.",
            "",
            "F4 puts your own commands on keys and says which keys this terminal cannot deliver.",
            "Every key below is also an entry on ⌃P.",
        ]),
    ];
}
