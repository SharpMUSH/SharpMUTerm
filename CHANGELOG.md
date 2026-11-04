# Changelog

Every change a user could notice is recorded here, in the release that ships it. The format is
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). `tools/changelog.py check` enforces the shape,
and a release's notes are its section. The procedure is in [`docs/RELEASING.md`](docs/RELEASING.md).

## [Unreleased]

### Added

- In the terminal, a docked panel's heading band is the boundary between stacked panes: Roleplay's composer and the
  who-is-here list no longer have a grey row between them (drag the HERE band's empty part to resize), and the panes
  are one column wider, because the spare column between the sidebar's rule and the output is gone.
- Vitals have colours: health is green, mana blue and movement gold on the status row, the terminal's vitals strip and card,
  and the desktop window's gauges, and the target's bar is orange. A low vital turns red everywhere. Pick any meter's
  colour in F7's meter table (a new colour column; `auto` keeps the default for its name).
- In the desktop window, Combat's map uses its pane: rooms are drawn at full size, every room's name is written out in
  full where it fits without covering another room or name (shortened only where it truly cannot fit), the map pane no
  longer repeats a lone "Map" tab above its own heading, and the vitals card is only as tall as its gauges, so the map
  gets the rows that used to stay empty under Movement.
- In the terminal, the who-is-here, composer and vitals panels head themselves with a filled band, the scrolled-back
  divider is a filled band too, modal pop-ups have a quiet grey frame instead of a cyan one, the header shows your
  character in the world's colour without a grey chip, the vitals card no longer draws a stray rule when nothing is
  targeted, and the welcome's `/connect` example is highlighted — closer to the terminal mockups.
- In the desktop window's Light theme, the header sits on the same plane as the sidebar and status row instead of a
  separate lavender grey, text fields in the composer, the welcome and the pop-ups have a visible edge, and the
  welcome's fields are sunk into their cards (with a magnifier in the search box) in every theme.
- The settings window is closer to its mockup: while searching, a section with no match no longer shows a greyed `0`,
  the meter table's paths and live values are in the 12-point monospace face, the list of received paths scrolls inside
  its panel instead of pushing the page down, the list editors' drop-downs line up with the text boxes beside them,
  and a disabled button no longer looks heavier than an enabled one.

- In the desktop window, the map is drawn the way the mockups draw it: dark rooms outlined in a rule, exits as thin lines,
  your room ringed in the accent with an `@`, Up/Down/Areas as proper buttons, and the legend in a strip at the foot with
  "Click a room to walk there" under it, so it never covers a room.
- In the desktop window, the header shows only the search box and your character, and the status bar reads
  "Corvid connected · UTF-8 · Log off" with a short hint on the right such as "2 new in Chat" (or the keys of the mode
  you are in). Notices still take the whole bar while they show.
- In the desktop window, Combat's quick bar is a proper strip of buttons, each key a key cap beside its name, and a
  binding marked *danger* on F4 (new `style` field) is drawn in red there and in the terminal's quick bar.
- In the desktop window, a panel a layout collapses (Combat's chat) is a one-row bar at the foot of its column saying
  how many new lines it holds; click it to open it as a pane. The terminal keeps it as a background tab.
- The composer has a Command field above the text, in F1 and the docked panel, in the terminal and the desktop window:
  put the verb there (`+pose`, `say`, `+bbpost 3=`) and write the post below it. ⇥ moves between the two (⏎ in the
  command moves on to the text); the command is sent in front of the text unescaped, joined by a space (none after
  `=` or a one-character shorthand like `:`), and it stays after a send for the next post. Leave it empty to write the
  whole command in the text as before. The window's docked panel now reads as the design: Command and Text fields, the
  Literal/As typed chip, `2 paragraphs · 31 words`, Preview and Send.
- The package review keeps ⏎ install and Esc cancel on screen at 80×24: the review scrolls (↑↓ PgUp PgDn Home End)
  under its keys and stops above the command line.
- Browse worlds: a world listed at several addresses (TLS and plain, another port, an old one) now lets you choose
  which before the connect form opens; the form takes that address's TLS setting, and the world is saved with the
  game's character set when mu*index names one other than UTF-8. `/browse N M` says address M in `--plain`.
- Browse worlds shows mu*index's list as mu*index gives it — every row, in its order, with its player counts and their
  labels — and asks it to leave out games listed only by an address.
- In the desktop window, every docked panel has a heading saying what it shows (Compose for Corvid, Here The Grand
  Plaza · 6, Vitals GMCP Char.Vitals, Map Aetherfall · level 0). Who is here marks each person active, idle or NPC; a
  tall vitals pane draws a card with the target under a rule; the quick bar's buttons show their key and end in a
  "+ Bind a key" button that opens F4.
- The docked composer in the window has its escaping on a chip you can click, a paragraph and word count, and Preview
  and Send buttons; Preview opens the full-screen composer with the as-sent band.
- The window's welcome is one page: find a world from mu*index and add it, connect directly (with the `/connect` line
  it stands for), and pick a starting layout from two cards. Nothing is fetched until you ask for the list. A world
  listed at several addresses lets you choose one right in the Find a world card (Esc goes back to the worlds).
- The desktop window's status line is now a footer under the whole window, in the window's own type, with keys in words.
  Clicking "Ctrl+P palette" opens the palette, and clicking a notice's action (Reconnect, Open F5…) runs it.
- The desktop window draws the command line as a rounded field with the prompt in its own type and a hint at the right
  ("Enter sends · Ctrl+Enter new line") that steps aside when your typing reaches it. Typing, history, completion and
  selection work exactly as before.
- The desktop window ships the Inter typeface for its menus, panels and dialogs, and uses the bundled JetBrains Mono for
  the text unless `--font` names another, so it looks the same on every computer. `--line-height` spaces the rows
  (1–2, default 1.12).

- In the desktop window each pane's tabs are drawn as native tabs: the tab in front is a card joined to its pane,
  unread counts are in the accent, a tab's close appears when you point at it, a right click shows the tab's menu,
  and tabs that do not fit are behind a `+N` button. The terminal is unchanged.
- In the desktop window a pane split by scrolling back shows a native band — "Scrolled back N rows · New lines keep
  arriving below" — with a Jump to latest button that does what End does.
- A script error in the script console that names a file and line is now a link: click it to open that script with the
  caret on the line and the line marked. Closing the editor goes back to the console.
- ⌃P ▸ Recent alerts lists the pages and alerts every character has had this run, newest first, with how long ago;
  choosing one goes to that character's window.
- ⌥S sends the command line to the send group (⌃P ▸ Send to group), and `g` on a sidebar row while ⌥C holds the keys
  adds that character to the group or takes them out.

- The script editor numbers its lines in the desktop window too, and after a save whose reload fails, the line the
  error names is marked in both the terminal and the window.
- When Ctrl+F finds fewer matches than it might because the pane no longer holds older lines, its status line offers
  Alt+H to search older history, saying how many more lines there are (in the window, clicking the hint does it).
- The read-only history page an older match opens is centred on the match, with the three lines either side drawn as
  its context and the rest of the page quieter.
- Browse worlds: the welcome's Browse row, ⌃P ▸ Browse worlds… and `/browse [words]` list worlds from the mu*index
  directory, most played first, searchable and sortable by name. Picking one fills in the connect form with its host,
  port and TLS; nothing is saved or dialled until you press ⏎ there. The directory is asked only when you open the list.
  With `--plain`, `/browse` reads the list out and `/browse 3` tells you the `/connect` line for world 3.

- Scripts can be edited without leaving the client. ⌃P ▸ Edit script… (or ⌃E in the script console, or `/script edit`)
  lists your character's script files; one opens in the composer with line numbers, exactly as written. ⌃S saves it,
  reloads that character's scripts and shows the first error with its file and line. ⌃P ▸ New script… (or
  `/script new <name>`) starts one. Scripts from a package open read only.
- ⌃F can search older history. Press ⌥H to search the whole of a character's scrollback, including what has been
  moved to disk, as well as what the pane still shows. Results stream in below the pane's own, marked "older"; ⏎ on
  one opens a read-only page of history around it. Capture windows keep no older history, and the search says so.
- Alerts reach more of the desktop. On Linux (X11, and XWayland) the window is marked as wanting attention, so the
  taskbar or window list highlights it until you come back. On Windows, an installed copy shows a toast as well as
  flashing the taskbar, and the window groups with its Start menu shortcut.
- A new world alerts you when you are paged, told or whispered to while you are in another window (F5 ▸ alert on ▸
  pages). Alerts for your character's name stay off until you choose "name & pages", since a name comes up in every
  scene. Worlds you already have keep the setting they had.
- While a scene log records, the status row shows `● REC` and the desktop window's header says so. The "scene saved"
  notice offers Open (the scene file in your viewer; only files the client saved under its own log folder) and Copy
  path.
- `/map find` lists the rooms it found in Ctrl+P as "Walk to …" entries; Enter walks there.

- Import worlds, characters, triggers, aliases, timers and keys from MUSHclient (.mcl), Mudlet (a profile folder,
  an XML package or a .mpackage) and TinTin++ with `/import <file>`, Ctrl+P ▸ Import from another client… or the welcome
  screen. A review shows what can act as you, what will be added and what was left out (scripts are never imported or
  run) before anything is written; nothing you already have is replaced, and passwords are stored only if you say so.
- The terminal map names the room you are in, and where each of its exits leads when the pane has room.
- Maps can be edited by hand: Ctrl+P ▸ Edit map in the terminal (arrow keys select, Shift+arrows move, r, a, l, w, x,
  n, s, Del and u edit), or right-click a room in the desktop window for its menu and inspector. Rename a room, set
  its area, lock it, weight it, add or remove an exit (or link one to the next room you walk into), move it, delete it,
  or give it a note or a symbol; the last ten edits can be undone. What you changed by hand (a name, an area, exits,
  a position) is kept when the game describes the room again; Reset to server (R, or the inspector's button) gives it
  back to the game.
- The map can show another level or area and come back: the ▲ ▼ and "here" in the map's title and the area names in
  its legend in the terminal, the Up, Down, Areas and "Back to where I am" buttons (or Shift+wheel) in the desktop
  window, PgUp/PgDn, v and h while editing, Ctrl+P ▸ Map: level up / level down / next area / back to where I am, and
  `/map level up|down`, `/map view <area>` and `/map here`. Walking still goes from where you are.
- In the desktop window, drag a room while editing the map to move it; it snaps to the grid.
- Ctrl+P ▸ Find room… and `/map find <text>` list the rooms whose name or area matches, with the `/walk` for each,
  and show the first on the map.
- Ctrl+P ▸ Script console: one per character, it shows what your Lua scripts printed and the errors they raised
  (with the file and line), runs a line of Lua as that character (a runaway loop is stopped after half a second),
  remembers what you typed, lists the character's `store` keys with Delete, and reloads the scripts. Without a
  scripts folder it says what to create.
- F5 ▸ channels puts each GMCP channel (`Comm.Channel.Text`) in a window of its own: `copy` keeps the line in the
  main window too, `move` takes it out. Off by default.
- F5 ▸ names highlights the names you list (`Ashlyn, Merrow=red`) in a colour of your choice, as whole words,
  wherever they appear.
- Ctrl+P ▸ Start scene log records the window in front to a file of its own under the log folder, HTML or plain text,
  poses only or every line (F7 ▸ Scene log), with a header naming who was in the room; Stop scene log closes it.
- A world can reconnect by itself after its connection drops (F5 ▸ reconnect). It waits 5, 15, 30, 60 and 60
  seconds between tries, counts down on the status row, and stops after five; Alt+R dials at once, and Disconnect or
  Ctrl+P ▸ Stop reconnecting cancels. Your own Disconnect, quitting, or the game closing right after you typed a
  command (a logout) never reconnects.
- The client can tell your desktop when something wants you while you are in another window: tick "alert me when
  away" on a trigger, or set F5 ▸ alert on to "name & pages" for a world. The desktop window flashes or shows a
  notification; kitty, WezTerm, Ghostty and foot show a notification, other terminals ring the bell. At most one every
  ten seconds, and F7 ▸ alert when away turns it all off. While the desktop window is behind another, its title
  counts the lines that arrived.
- Command stacking, per world and off by default (F5 ▸ command stacking): `n;e;e` sends three commands and `#3 kill rat`
  sends one three times. Only lines typed on the command line are split — never a composer post.
- A tab strip too narrow for its tabs now says how many it could not draw (`+3 ▾` at its right edge); click it to
  list them and go to one — a menu in the desktop window, a short list in a terminal.
- The composer shows how many characters the post will be once escaped and joined, and Alt+P shows exactly what will
  be sent (both in the terminal and in the desktop window).
- When a pane is narrower than the line width a game wraps at, the client says so once and offers to turn on
  "join wraps" for that world.
- Help is built in: `/help` (or Ctrl+P ▸ Help) lists the slash commands, every key this client answers to, and short
  how-to pages on getting started, reading output, layouts, triggers and the composer. Type to search; Enter on a
  slash command puts it on the command line without sending it.
- No spelling dictionary? Alt+W in the composer (or Ctrl+P ▸ Get a spelling dictionary) offers to download an English
  one into your settings folder. Nothing is fetched until you say yes, and the file is checked before it is saved —
  so spelling now works on Windows too.
- While a pane is narrower than the width a game wraps at, Ctrl+P suggests "Join server wraps for" that world and F5
  marks its "join wraps" setting as recommended. It stays off until you turn it on.

- Desktop window: the map, vitals, who is here and the quick bar can be popped out into windows of their own — from
  the button in the panel's corner, the pane's right-click menu, or Ctrl+P ▸ Pop out. The output takes the room back,
  the floating window works exactly like the panel did, typing in it still goes to the command line, and closing it
  (or Dock) puts the panel back in its place. Where each one floats is remembered with your layout and brought back on
  screen if a monitor has gone. The terminal keeps every panel in its pane.

- Desktop window: the quit prompt, search (Ctrl+F), history search (Ctrl+R), the layouts list (Alt+M) and the
  first-run welcome are native dialogs in the window's light or dark look, worked from the keyboard exactly as in the
  terminal. The quit prompt always comes up in front of whatever is open — closing the window while writing a post
  no longer hides it behind the composer — and Stay takes you back to what you were doing.
- Packages (F10) open in the desktop window's settings as a package manager: switch each installed package on or
  off, see what it adds, who it applies to and whether the index offers an update, copy its sets to edit or remove
  it. The Browse tab fetches the package index only when you ask, lists your world's packages first, and Install
  shows a review that leads with what can act as you; nothing is written until you install. In the terminal, F10
  now also shows each package's author, description, scripts, media and update, and the install review opens with
  what can send commands as you.
- `telnet://` and `telnets://` links open the client: it adds the world (or dials the one you already have for that
  address) and connects, and never logs in or sends anything a link names. Links with a user name, a password or
  anything after the address are refused, and say why. If SharpMUTerm is already running the link opens there. The
  Windows installer has an option to register the links, and the Linux desktop file declares them.
- Character passwords are kept in the system's credential store — Windows Credential Manager, the Secret Service
  keyring on Linux, the Keychain on macOS — when there is one, and moved there from `secrets.json` the first time.
  Without one, `secrets.json` works as before.
- The window's title, and the terminal's, say who is in front: `Character@World — SharpMUTerm`. The terminal's own
  title comes back when you quit.
- Desktop window: a screen reader that moves to the output finds a read-only document holding the newest lines of the
  window in front, alongside the lines it already hears as they arrive.
- Triggers, aliases, key bindings and timers (F2, F3, F4, F6) open in the desktop window's settings as a list
  beside the selected item's fields: tick a box to switch one on or off, add, copy, delete or move it up and down,
  and watch the trigger's live test mark the matching lines as you type its pattern. A package's sets are shown
  read-only with *Copy to edit*. *Make a trigger from the selection* opens there on the new rule. In the terminal,
  Alt+Up and Alt+Down move the item under the cursor within its set, and the settings search finds every one of
  their fields.
- Desktop window: the composer is a native text editor. F1 opens full screen over the panes and the docked composer
  is an editor in its pane, with the platform's selection, undo, input methods and paste, a monospace or proportional
  face (right-click), and a wavy red line under a misspelt word whose right-click menu offers suggestions and Add to
  dictionary. The draft, Ctrl+S, Alt+L, Alt+W and Esc work exactly as in the terminal, and the same text sends the same
  command.
- Alt+C moves the keyboard into the connection sidebar, in the terminal and the desktop window: the arrow keys,
  Home and End walk its rows, Enter goes to the one you are on, and Esc (or Alt+C, or just typing) returns to the
  command line.
- Desktop window: a sidebar with more rows than fit scrolls, with the mouse wheel or a scrollbar like the panes',
  and a right click on a world, character or window opens a menu of what you can do with it (switch, connect or
  disconnect, edit, go to, close, clear), each with its key.
- Switching theme repaints the output already on screen, not only what arrives next, and Space on a theme in the
  picker uses it as Enter does.
- A narrow terminal gives its room to the game: below 100 columns the connection rail folds to its slim strip
  on its own (in the desktop window, the sidebar does), and below 100 columns or 30 rows an empty command line
  is one row tall and grows as you type. At 80×24 a pane is now 72 columns wide instead of 60, two rows taller,
  and a world's 80-column text no longer leaves a stray word on every other line. Folding or unfolding the rail
  yourself (Ctrl+B b) always wins, until the window next crosses 100 columns.
- The status row names the keys of whatever you are in: scrolled back (End returns, Alt+F freezes), frozen
  (Alt+F thaws), on a search result (Alt+G next hit), or writing in a docked composer (Ctrl+S sends, Esc
  releases, F1 full screen), in place of the pane hints, and only where they fit.
- Desktop window: notices also appear as toasts in the bottom-right corner. They stay while you hover over them,
  fade after a few seconds, and never take the keyboard. Some carry a button, such as *Change layout* or
  *Reconnect*. The status row still shows each notice, as it does in the terminal.
- Maps colour each area, mark rooms with stairs up or down, and show a legend: a one-line legend under the map in
  the terminal, and a legend in the corner in the desktop window. In the window, room names appear when you zoom
  in, and hovering over a room shows its name.
- A theme picker at the top of F7: each theme with a preview of the panes, a tab strip and the command line.
  Moving onto a theme shows the screen in it, Enter uses it and Esc keeps the one you had. The desktop window's
  settings show the same themes as cards; click one to use it. A customised theme stays in the list when you
  try another.
- Desktop window: the settings window now edits Worlds & Characters (F5) and Characters (F9) too, with a
  picker for which world or character you are editing and a button to the full screen for adding and deleting.
- Ctrl+P lists *Fold or unfold the connection rail*, the same fold as Ctrl+B b and as the desktop window's
  sidebar button.
- Desktop window: the header and the connections sidebar now have room of their own — a full-height toolbar
  across the top and a 232-pixel sidebar down the left — instead of being squeezed into one row and one column
  of text, and the text area is the rest of the window, so every pane is as wide and tall as it really is (and
  that is the size your games are told). While Ctrl+B is armed the header lists the keys it answers, dimming the
  ones that cannot run, with each key's meaning in its tooltip. The sidebar folds to a slim strip of characters
  with the chevron at its top or Ctrl+B b, and stays in place, inactive, while a dialog or settings screen is
  open. `--cell-chrome` draws them as text, the way the terminal does.
- Desktop window: Ctrl+P opens a native command palette, a rounded popup near the top of the window with a
  search box, grouped results, the matched letters highlighted and each command's key on the right. It lists
  the same commands in the same order as the terminal's palette, recent and suggested first. Arrow keys and the
  mouse choose, Enter runs, Escape closes and returns you to the command line. The header's search box and a new
  *More commands…* entry at the foot of every right-click menu open it too.
- Desktop window: F7 (Text & display) and F8 (Input) open a settings window with real switches, number boxes
  and a search box. Matches are counted per section as you type. A changed setting shows a dot and a Reset
  button, and every change applies and saves as you make it. The status-row meters are a table that shows each
  meter's current value, with the paths your game has sent listed for you to pick from. The other screens are
  listed in the window and open as before.
- Desktop window: a Layouts button at the right end of the header opens a menu of the built-in and your saved
  layouts (the one in use is checked), with *Save changes*, *Save as new*, *Use for* the world or the character,
  and *Manage layouts…*. It runs the same actions as Alt+M.
- Desktop window: the vitals panel is drawn as smooth gauges, with each meter's name and numbers, the low ones
  in the warning colour (*Mana · low*) and what you are fighting. A tall vitals pane stacks them; a strip lays
  them side by side.
- Desktop window: the header and the connections sidebar are drawn as native controls. The header has a
  search box that opens the command palette (Ctrl+P) and a pill naming the character in front, its world and
  whether the connection uses TLS. The sidebar lists worlds, characters with presence dots, unread badges and
  "offline", the open character's windows with their Alt+number keys, and an *Add a game or connect* button.
  Clicking a row switches exactly as clicking it in a terminal does, and a drag on empty header space still
  moves the window.
- Desktop window: the map is drawn as a canvas of rooms and exits. The wheel zooms, a drag pans, a
  double-click re-centres on you, and a click on a room walks there. Hovering a room shows its name.
- Desktop window: who is here is a native list, with a coloured dot for each player (dim when idle), how long
  they have been idle, and creatures dimmed below. Hovering a name lights the row and shows the full name; a click
  puts `look` and the name on that character's command line, as in the terminal. The quick bar is a row of
  buttons, with the bindings that do not fit under a `+N` menu and *F4 edits keys* as a link. Neither takes the
  keyboard away from the command line, and native panels no longer sit on top of a settings screen or ⌃P.
- Scrolling a pane back splits it: the history you are reading stays on top, a band says how far back you
  are (`scrolled back 120 rows · End returns · ⌥F freezes`), and the newest lines keep arriving underneath.
  End (with the cursor at the end of the command line), Ctrl+End, or scrolling the history back down to its end
  closes it; ⌥F turns it into a freeze. The split is inside the pane, so the game is not told a new window
  size. Text & display (F7) ▸ *split the pane when scrolled back* turns it off.
- Worlds & Characters (F5) has a `join wraps` row on each world: on, lines the game broke at its own width
  (usually 78 or 80 columns) are joined back together, so a paragraph fills a wide pane and does not wrap twice
  in a narrow one. It is careful — it joins only a full row that ends mid-sentence onto a row that carries on in
  lower case, in the same colour — and triggers see the joined line. It applies to lines that arrive after you
  turn it on.
- A first run has somewhere to start: with no worlds, the client opens on a welcome offering *Connect to a
  host* (a small form for the host, port, TLS and your character's name), *Browse games* from the package
  index and *Open settings*, with the `/connect` line spelt out and a starting layout to pick (Roleplay,
  Combat, or suggested from what the game sends). Esc closes it; `/welcome` or Ctrl+P ▸ *Welcome* brings it
  back. With `--plain` the choices are read out as commands to type.
- After the first connect of a new configuration, one row names the keys worth knowing (Ctrl+P, Alt+M, F1,
  F5). Ctrl+P ▸ *Key tour* hides it and shows it again.

- Ctrl+P matches as you type by initials and fragments, not only by prefix: `trig sel` finds *Make a trigger
  from the selection*, and the letters it matched are highlighted. The commands you last ran from it are
  listed first under RECENT, followed by what the moment suggests (copy while something is selected, back to
  live output while scrolled back). Every entry's key sits in one right-aligned column, spelt one way (⌥⏎,
  not Alt+⏎), and a narrow window shortens titles but never keys.
- Keypad & hotkeys (F4) marks a binding that hides one of the client's own keys (such as Ctrl+C copy) with an
  amber △ and says the binding wins, and marks a second binding on the same key as one that never runs.
  Ctrl+P lists *Key clashes (N)* while there are any.
- Layouts. Alt+M (or Ctrl+P ▸ *Layouts*) lists the built-in Roleplay and Combat layouts and the ones you save:
  ⏎ applies one to the panes, `s` saves the panes as they are into the selected layout, `n` saves them as a
  new one, `w` makes it the world's default and `c` this character's; `r` renames one of your own and `d`
  deletes it, after asking. Worlds & Characters (F5) has a `layout`
  row on each world and each character. A panel appears only once the game sends what it shows, so a MUSH
  never gets an empty map pane, and the first time you connect to a world the client picks Combat or Roleplay
  from what the game sends and says which in a notice. A workspace you arranged yourself is still what comes
  back after a restart.
- Search the settings: press `/` on any settings screen and type. Every option, and every settings screen by
  name, is listed with how many matches each screen holds; ⏎ goes straight to the option on its own screen.
- A setting you have changed from its default is marked `● changed` on Text & display (F7) and Input (F8),
  and `r` on it puts the default back.
- Four panels a layout can place, each also on Ctrl+P: *Show who is here* (the players and creatures in your
  room, from GMCP; click a name to put `look <name>` on the command line), *Show vitals* (your meters as bars
  with values, a warning colour when low, and what you are fighting), *Show quick bar* (your function-key
  macros as `F11 kick`; click one to run it) and *Dock the composer* (the F1 editor as a pane that shares F1's
  draft: ⌃S sends, ⌥L switches escaping, F1 opens it full screen, Esc goes back to the command line).
  The vitals strip and the quick bar are exactly one row tall, with no tab over them, and the docked composer
  never shrinks below a few rows. While the vitals strip is on screen the status row leaves its meters out
  rather than showing them twice. Panels are part of the layout, not places you go: they take no row in the
  sidebar and no Alt+number, so your map and capture windows keep theirs.
- Make a trigger without writing a regex: select a line of output and choose Ctrl+P ▸ *Make a trigger from
  the selection* (also on the right-click menu). Triggers (F2) opens on a new rule whose pattern is the line
  with its numbers, quoted speech and known player names turned into `*`, and shows the regex behind it.
  While you edit a pattern, F2 shows how many recent lines it matches and the newest few, and a rule can now
  match "contains the text", "starts with" or "matches with * wildcards" as well as a regular expression.
- `/connect <host> [port] [tls]` adds a world and connects to it, and `/connect <name>` opens a configured
  one. It is the way to add a world in `--plain`, where the settings screens cannot be used.
- The desktop window bundles JetBrains Mono and the Nerd Font symbols, so its icons draw on a machine with no
  Nerd Font installed instead of showing boxes.
- A crash now leaves its stack in `logs/client-diagnostics-*.log`, in the window and the terminal alike,
  along with errors the UI framework used to swallow without a trace.
- A `scripts` field on each trigger set in Worlds & Characters (F5), and Ctrl+P ▸ *Reload scripts*, which
  re-reads the focused character's files.
- GMCP games now send their data. The client introduces itself (`Core.Hello`) and asks for the common
  modules (`Core.Supports.Set`: Char, Char.Skills, Char.Items, Room, Comm.Channel) once GMCP is agreed, and
  asks an MSDP game to report the variables the status row reads. Worlds & Characters (F5) has a
  `gmcp modules` field for a world that needs more, such as `IRE.Rift 1`.
- Health, mana and movement on the status row, read from GMCP or MSDP. A meter shows only once its value
  arrives, keeps its width as the numbers change, and turns to the warning colour below a quarter. Which
  values the row shows is the `meters` setting in Text & ANSI (F7).
- Ctrl+P ▸ *Show game data* lists every GMCP package and MSDP variable the focused connection holds, and
  shows the selected one in full.
- Lua scripts can read and send game data: `gmcp.get('Char.Vitals')`, `msdp.get('HEALTH')` and
  `gmcp.send('Char.Skills.Get', { group = 'combat' })`.
- Worlds & Characters (F5) shows three settings that could only be changed in `config.json`: a world's
  `content` (`auto`, `ansi`, `mxp` or `pueblo`; `ansi` keeps a world on plain colour even when the server
  offers MXP), its `emoji` substitution (off, emoticons, shortcodes or both), and a character's
  `disconnect` commands.
- The client records a map of each game as you walk it, from GMCP `Room.Info` (Iron Realms, Aardwolf and
  MUME shapes) or, for games that send no room data, from the `map room` and `map exits` patterns in
  Worlds & Characters (F5). Maps are saved per server in a `maps` folder beside `config.json`.
- Ctrl+P ▸ *Show map* opens the focused character's map as a tab, centred on where you stand. Click a room
  to walk there, or type `/walk <room name or number>`; the walk sends one step at a time and stops when a
  step goes somewhere else, when a line matches the world's `walk fail` pattern (F5), or when a step gets no
  answer for 8 seconds. `/walk stop` or Ctrl+P ▸ *Stop walking* ends it, and `/map lock <room>` keeps walks
  out of a room. The status row names the room you are in.
- Screen reader support. `sharpmuterm --plain` runs the client as plain text: game output a line at a time
  as it arrives, the client's own messages marked `[client]`, and each line you type is sent. In the desktop
  window, output and the client's messages are given to the screen reader as they arrive. ⌃P ▸ `Stop reading
  this window aloud` keeps a busy window quiet. The desktop window's support has not yet been tried with a
  screen reader.
- Spelling in the composer (F1). Words the dictionary does not know are shown in red, and ⌥W adds the word
  at the caret to your own list. The client uses a Hunspell dictionary (`.aff` and `.dic`) from a
  `dictionaries` folder beside `config.json`, or the system's (`/usr/share/hunspell` on Linux,
  `~/Library/Spelling` on macOS; on Windows only the folder). None is included, and the composer's header
  says when there is none. `%r` and the other `%` codes, function names, and words in capitals or with a
  capital inside (`OOC`, `McTavish`) are not checked.
- Games that publish a Discord status (GMCP `External.Discord`) can show it on your Discord profile. It is off
  for every world; turn it on with F5 ▸ `discord`. Only the world whose window is in front is shown, and it
  is cleared when you switch away or disconnect. It talks only to the Discord app on your computer.
- One line can go to several characters. With two characters open, ⌃P offers `Add Ann to the send group` for
  each of them, and the rail marks members with ◆. `Send the command line to the group` sends what you typed
  to every connected member once and names any that are not connected. Lua scripts can send to another open
  character with `world.sendTo('Ann', 'wave')`, which returns false and the reason when it cannot.
- The client answers a server's NEW-ENVIRON request (MNES) with its name, version, terminal type and MTTS
  flags, so servers that read MNES identify it. It sends nothing about your computer, and offers nothing to a
  server that does not ask. The MTTS flags now include MNES.
- Worlds can log in over GMCP. Set F5 ▸ `gmcp login` to `if offered`, and a character with a saved password is
  logged in with GMCP's `Char.Login` on a server that offers it (Achaea and other IRE games, and servers
  written for Mudlet). Any other server still gets the connect line, after waiting up to five seconds for the
  offer. The password is sent only to the server and never shown or logged.
- Lua scripts can keep values between sessions: `store.set('kills', 3)`, `store.get('kills')` and
  `store.keys()`. Each character has its own store, kept in `script-data/` beside `config.json`. A store holds
  at most 1,024 keys and 256 KB, and `store.set(key, nil)` removes a key.
- Lua scripts can use the map: `map.current()`, `map.path('Town Square')` and `map.walk('Town Square')`.
- Game sound. A GMCP game that uses MCMP (`Client.Media`, as Mudlet does) can now play sounds and music:
  the client asks for it, fetches the files the game names into a cache folder, and plays WAV, MP3, OGG and
  FLAC. Text & ANSI (F7) has a SOUND section: on or off, master, sound and music volume, and *sound
  captions*, which prints a dim `♪ …` line for each sound. Ctrl+P ▸ *Mute sound* silences everything until
  you unmute or restart, and the status row says `♪ muted` meanwhile. Worlds & Characters (F5) has a
  `sounds` field to stop a world downloading new files.
- MSP sound. A game that offers the MUD Sound Protocol now gets the client's agreement when sound can play,
  and its `!!SOUND(…)` and `!!MUSIC(…)` lines play like MCMP's instead of showing as text.
- Packages. A package is a `.zip` of trigger sets, Lua scripts and sound files for one or more games.
  Ctrl+P ▸ *Install package…* (or `/package install <file or https link>`) shows what it adds first,
  including how many of its rules send commands and which scripts it runs, and writes nothing until you
  press Enter. Its sets then apply to every character on the games it names, after their own. F10 or
  Ctrl+P ▸ *Packages* lists what is installed: Space switches one off, `c` copies its sets into your own
  so you can edit them on F2, and Delete (twice) removes it.
- Ctrl+P ▸ *Browse packages* (or `b` on F10, or `/package browse`) lists the packages in the project's
  package index, with the current game's first and an update shown beside any you have at an older
  version. The index is fetched only when you open it. A package from it is checked against the index's
  SHA-256 before its review opens. Another index can be named as `packageIndexUrl` in `config.json`.
- An MXP link with several commands (`<SEND "look|get|drop">`) still sends the first on a click, and a
  right-click offers all of them: in the window's own menu, or in a small chooser in a terminal (arrows or
  1–9, then Enter). Its tooltip says how many more there are.
- Editing MOO verbs, properties and notes in the composer. On a MOO that speaks MCP 2.1 (LambdaMOO and its
  descendants), `@edit` opens what you named in the F1 composer, and Ctrl+S saves it back to the MOO line
  for line. An edit that arrives while another window or screen is in front waits for F1 in that
  character's window. The client also tells the MOO its name, version and window size, and shows the MOO's
  status text on the status row.
- Tab on the command line completes the word you are typing from the game's recent output, with the people
  the game names over GMCP (in the room, on a channel) offered first. Tab again goes to the next match and
  back round to what you typed. With nothing to complete, Tab still moves to the second command line.
- An `edit grab` prefix per world in Worlds & Characters (F5), empty by default. A line from the game that
  starts with it is not shown; its text goes on that character's command line for you to change and send.
  AresMUSH uses `FugueEdit >` for its edit commands. The client never sends a grabbed line itself, and a
  line you were typing is kept in history (Alt+Up).
- More of MXP: elements a game defines with `<!ELEMENT>` (including room names, exits and numbers, which
  feed the automapper), entities from `<!ENTITY>` and `<VAR>` (kept with the game data on Ctrl+P ▸ *Show game
  data*), `<GAUGE>` and `<STAT>` meters on the status row, `<SOUND>` and `<MUSIC>`, which play as MSP sounds
  do, and `<DEST>`, which sends text to a window of its own. A line that is only definitions no longer
  prints as a blank line.

### Changed

- In the desktop window the command line sits under the output column only, as wide as the text it sends to; the
  side column (composer and who is here, or vitals and map) runs down to the status footer. The field is also a few
  pixels further in from its edges, as the mockups draw it. The terminal is unchanged.
- The Dark theme is navy, as the mockups are, in the terminal and the window alike: deep blue panes with the focused
  one a step lighter, the sidebar and status row on the darkest ground, a dark header with a hairline under it, a
  command line on `#233049` with a blue edge, blue-grey rules and text. Light and Solarized Dark are unchanged, and
  pane tints look as they did.
- The window's sidebar marks one row as selected, with a teal plate: the character, while their main window is in
  front, or the other window that is.
- A tab strip whose tabs all belong to one character names them plainly (`Corvid`, `Chat 2`); the owner is still
  written in a strip holding two characters' windows, and on a capture alone in its pane.
- The desktop window lines up: one spacing scale for every native view, the header at the mockup's 44 px, output
  starting about 20 px in from its pane's edges with a row of space under the tab strip, the sidebar's names, the brand
  and every panel's heading, rows and buttons on one 16 px edge, the "+ Add a world or connect" button left-aligned,
  equal-height buttons and fields, and every panel heading the same height. Rules and borders are one crisp pixel at
  125% and 150% display scaling instead of a blurred two.

- In the desktop window, the line between two panes is a hairline where the panes meet instead of a grey band, and
  lights up wider while you hover or drag it. In the command line, what you type starts right after the prompt.
  The docked composer's "Click to write a post" is in the interface font.
- The command palette in the terminal shrinks to the rows a search matches instead of filling the screen with blank.
- In the window, notices are drawn on the same card colour as the palette and dialogs, and body text is a size larger
  (13 px) across the window's own views.
- The built-in layouts follow the new designs: Roleplay puts the composer in the side column above who is here, with
  the chat windows as tabs behind the output; Combat puts the vitals in the side column above the map as a card, one
  meter over another. Below 100 columns they fall back to what fits: Roleplay's composer goes under the output and
  Combat's vitals become a one-row strip across the top. Layouts you saved are unchanged. A pane showing only a panel
  (the composer, who is here, the vitals) no longer draws a tab strip over it; the panel's own heading names it.
- In the desktop window, each pane's tabs are taller with larger, centred labels, game text no longer runs against the
  pane's edge (a column of space either side, reported to the game as the pane's width), the header shows the plain
  "SharpMUTerm" name with the search box beside your character, and the command line's prompt sits at the field's
  left edge.
- In the desktop window, ⌃P, the quit prompt, search, history, layouts, help, the script console, toasts and the
  right-click menu now match the window's new look: blue-grey cards, search boxes sunk into the page with a magnifier,
  keys drawn as small key caps, an accent bar on the selected row, and a footer band naming the keys.
- The desktop window's settings look like the rest of the redesigned app: a header with the search box and, for worlds
  and characters, which one the settings apply to; the sections down a darker side column; each group of settings in a
  card of its own, with what you searched for marked in the labels, a "Changed" mark and a "Reset to default" link; and
  a footer saying changes apply as you make them. The meter table shows the paths received as a drop-down style panel.
- The older-history page fits an 80×24 terminal: it sizes its page to the screen, keeps its keys on screen and no longer
  covers the command line.
- Ctrl+F with older history says both counts on one line (`this window 3 of 39 · older 4 of 6,000`). Older matches say
  how far back they are (`1.6k lines ago`) and the pane's own say `here`.
- The script console opens at the size of what it holds, so its log no longer starts under blank rows.
- In the desktop window the map opens sized to the rooms around you, with their names, and stays where you put it
  once you zoom or pan (a double-click fits it again).
- The pop-out button on a docked panel shows only while the pointer is over that panel, and never covers the text of a
  one-row strip.
- The command palette's script entries are under Help & setup, and a five-letter search such as "scene" no longer
  matches letters scattered across unrelated entries.
- F5 at 80 columns shortens long values with "…" instead of cutting them off, and the highlight-names help shows each
  colour in that colour.

- The settings screens' key hints no longer lose words from the middle at 80 columns: the least-used keys are left
  out and the way to close is always there.
- Help topics have headings and show their keys as keys, in the terminal and the window, and a topic's title fits
  on one row at 80 columns.
- The window's dialogs (quit, search, history, layouts, welcome) dim the window behind them more clearly.
- Editing F5 ▸ highlight names shows the `Name=colour` syntax and the colours you can use, in both the terminal
  and the window.
- "Connecting", "connection lost" and "connection failed" are now client rows in the window, like the reconnect
  countdown: they no longer go into transcripts, the restore log, scene logs, triggers or Ctrl+F.
- At 80×24, F5's world column shows a page of fields instead of one, naming the group at the top.
- When a notice has an action, the terminal's status row names it (`· ⌃P ▸ Show messages`, or the action's key)
  if there is room.
- At 80 columns the terminal's key rows fit on one line: the F1 composer, the script console, the map's edit keys,
  the import review and F10 shorten their words and then leave out the least-used keys, never the one that closes.
  The map's edit keys are now one row under the map instead of a block beside it; `?` lists them all (also under
  `/help` ▸ Maps).
- The terminal map writes room names beside the rooms when the pane is wide enough, and always lists where your
  room's exits lead in the same place, under the map.
- F5's world page lists its fields under headings (Connection, Login & protocols, Text & input, Alerts & channels,
  Map, Sound & layout) with their full names, in the same order in the terminal and in the desktop window.
- Ctrl+P's OUTPUT group is split into WRITING, MAP, PANELS and HELP & SETUP. A new configuration's palette suggests
  Help, Welcome and Import first.
- Reconnect messages are written as a client row in the window, not as session lines, so they no longer reach
  transcripts, the restore log, scene logs or Ctrl+F.
- The desktop window no longer shows a toast for each map edit; failed edits still do. A connection failure's notice
  offers Show messages instead of a bare Ctrl+P.
- The first-run status row no longer says which graphics protocol is in use, and F5 says "1 character", not "1 chars".
- Light theme: background tabs and one-row panels (vitals, quick bar) sit on a lighter grey, and small text on the
  settings screens is darker, so it is as easy to read as body text.
- Desktop window: the composer's "no dictionary" chip is a button that offers to get one; in the terminal F1's key row names
  Alt+W beside it. The world settings show the same "join wraps" recommendation the terminal's F5 does.

- A character is named one way everywhere: the command line's prompt, the window title and tab titles now read
  `Corvid · Aetherfall` and `Corvid · Chat` instead of `Corvid@Aetherfall` and `Corvid - Chat`.
- Messages from packages, the map and sound name where they came from as a label ("Sound: muted.") rather than in the
  place a shortcut key is shown. The desktop palette's empty search box now starts with "Command palette".

- Desktop window: one look across settings, menus, the command palette and every prompt — the client's own teal
  instead of a stock blue, one set of text sizes and corner radii, and every colour readable on Dark, Light and
  Solarized Dark. The settings panel now has a title bar with a close button, dims the window behind it, and when
  opened from "Make a trigger from the selection" sits on the right half so the line you picked stays visible.
- Light themes: the header's coloured segments are deeper, with light text that is easy to read, and the low
  vitals warning is a readable dark orange instead of a pale yellow — in the terminal and the window.
- The terminal's command palette, quit prompt, search, history, Ctrl+B panel, game data and message log now
  follow the theme instead of staying dark on Light.
- A character is named the same way everywhere — `Corvid · Aetherfall` — in the quit prompt, the command palette,
  the layouts list, Packages and the client's messages, instead of `Aetherfall.Corvid` in some places and
  `Corvid (Aetherfall)` in others.
- The command palette is called that everywhere, its output commands are grouped under OUTPUT, and a short word
  you type (four letters or fewer) now matches only at the start of words, so `layo` finds the layout commands
  rather than unrelated entries that happen to contain those letters.
- The keypad screen (F4) and the quick bar show keys as `⌃F1`, `⌥M` like every other surface.
- The welcome says *world* throughout, labels every field of its connect form, and the desktop window's version
  matches the terminal's word for word.
- The folded sidebar marks each world with a coloured bar and gives two characters with the same initial two
  letters each, so they can be told apart.
- Desktop window: a trigger's route on F2 is a drop-down listing every window, and still takes a new name typed in.
- "Offline" notices and messages that tell you how to reconnect name the key (`⌥R`) as well as the palette entry.
- The command line starts as one row and grows as you type; a height you set before is kept.
- The status row no longer wraps onto a second row when a message is long; it is shortened to fit.
- The same message said twice in a row is shown once. In the desktop window, notification cards no longer cover the
  quick bar or the composer, and are hidden while settings, Ctrl+P or a dialog is open.

- Panes stacked one above the other no longer have an empty row between them when the lower pane shows its
  tabs: the tab row is the boundary, and the panes get the row back. Drag the empty part of that tab row to
  resize, or double-click it to even the panes out. The vitals and quick-bar strips sit flush against their
  neighbours, on a slightly darker background. Side-by-side panes are unchanged.
- The status row's meters are edited in a table on Text & display (F7): one row per meter with its label,
  value path and maximum path, what it reads now, `[+ meter]`, and Delete to remove one. While you are in
  the table it lists the game data this connection has received, and a path field offers those paths, with
  their values, to pick from.
- F7 is now called Text & display and its options are grouped under Colour & legibility, Text, Links &
  pictures, Status row & meters, Activity and Sound. A list taller than the terminal scrolls with the cursor
  instead of running off the bottom.
- The ten pane colours a character can be given (F5 ▸ `tint`) have been re-chosen so they stay apart for
  players with red-green or blue-yellow colour blindness, and a little further apart for everyone else.
  The names are unchanged, so nothing needs setting again; Ochre is now a red ochre and Ember a red. In the
  window, pointing at a tinted character in the sidebar or at its tab names the colour.
- An MXP picture is downloaded and decoded once per session. A room picture shown on every look used
  to be fetched again whenever the pane's width had changed, and once per look that arrived while it was
  still loading.

### Fixed

- In the desktop window, the Roleplay layout no longer draws a thick coloured bar between the Compose and Here panels,
  only the hairline seam; the docked composer's Command hint now reads whole (`+pose, say, +bbpost 3=`).
- Terminal polish from a sweep of every screen at 80×24 and 120×32: the header's brand reads `SharpMUTerm` where the row
  has room; the welcome's chosen row is a band across the list; pop-ups keep a cell between their frame and their words;
  a tall pop-up (the import review at 80×24) no longer covers the command line; the `❄ FROZEN` bar's rule runs to the
  pane's edge instead of vanishing in a narrow pane or stopping short in a wide one; the script editor's line numbers are
  legible on the Light theme; and the status row no longer says a key twice (`⌥R … connects it · ⌥R Reconnect`) or a
  label twice (`Map: map: …`).

- The command palette no longer covers the command line on an 80×24 terminal.
- A tab squeezed by a crowded strip keeps at least three letters of its name.
- The build no longer warns about an ImageSharp advisory in a decoder SharpMUTerm never uses.

- The import review at 80×24 no longer scrolls its Enter and Esc keys and the passwords question out of sight: they
  stay at the bottom while the rest scrolls (arrow keys, PgUp/PgDn, Home/End).
- The script console's store no longer says "read only" while Del deletes a key; the console names Del only on the
  store list, where it works.
- The map's edit keys at 160 columns no longer read "PgUp PgDnlevel".
- F5: editing a world field low on the screen (channels, names) now scrolls the field into view instead of leaving
  the cursor off screen.
- F5 ▸ channels `move`: a channel message whose echo arrives just before its GMCP in the same read is no longer left
  in the main window as well.

- Desktop window: the settings window can be resized again from any edge, and opens at the size you last left it
  for the rest of the session. Notification cards disappear as soon as it opens rather than showing beside it.
- Clicking a tab strip's `+N ▾` no longer also selects (or closes) the tab drawn underneath it.
- Light theme: the trigger and keypad screens at 80 columns are no longer drawn in the dark theme's colours, error
  notices on the status row are readable, the command palette's selected row is readable, and the sidebar and status
  row are a light grey rather than a mid-grey that looked disabled. The window's sidebar labels are easier to read too.
- The composer no longer marks the command word (`+bbpost`, `@emit`) or your own character's and the room's names as
  misspelt.
- Who is here keeps whole names at 80 columns by dropping the "active" / "idle" words first (the dot still says it).
- The status row's room name uses the space a wide terminal has instead of being cut short.
- The composer's "as sent" preview wraps at spaces instead of in the middle of a word.
- Desktop window: the docked composer no longer says "click to write" twice.
- Demo: the composer and the startup message no longer say nothing is connected while the header says Corvid is.

- The away, new-activity, search and restore bars fit the pane they are in: their rule runs to the pane's edge,
  never past it, and the words shorten before the rule does in a narrow pane. They redraw when the pane is resized.
- Search (Ctrl+F) fits an 80-column terminal: its key hints shorten instead of pushing the window wider than the
  screen.
- A trigger pattern anchored at the start of the line (`^\[public\]`) now finds lines in search and in F2's live
  test that a highlight rule has marked, as it already did in the game.
- Desktop window: number-pad keys bound on F4 fire, and F4 no longer marks them as keys that never arrive.
- F1 opened from an offline character's window names that character instead of no one.
- Starting the client with a saved workspace whose panes held a panel (vitals, who is here, the quick bar, the
  composer) or the map no longer fails before the first frame.
- Desktop window: the "no world configured" notice at first start shows once, not as two identical toasts.
- Desktop window: a notice you point at stays where it is while the others fade, and notices no longer cover the
  map's legend.
- Desktop window: every key is written in words everywhere in the window — the menus, notices, Ctrl+P, the composer,
  dialogs and settings now all say Ctrl+P, Alt+M and Enter rather than mixing them with ⌃P, ⌥M and ⏎.
- Desktop window: the settings window takes the keyboard when it opens, so you can type in its search straight away,
  and gives it back to the command line when it closes.
- Desktop window: the composer's proportional or monospace font is remembered for each character, and kept after a
  restart.
- Desktop window: the trigger, alias, timer and key lists in settings remember which item you had selected when you
  switch section or reopen the window.
- Packages (F10) fit an 80×24 terminal: the list no longer scrolls to hide its last rows or the close key.
- While Alt+C has the keyboard in the sidebar, the command line is drawn idle, since Enter goes to the sidebar row.
- Desktop window: the Keys screen (F4) and the quick bar now describe the window's keys rather than a
  terminal's. Ctrl+digit, Ctrl+Shift+letter, Ctrl+Tab and Alt+O are shown as working keys you can bind, and
  Alt+Enter is marked as taken because it maximises the window.
- Terminal: the quick bar's *F4 edits keys* can be clicked, as it already could in the desktop window. With no
  room known, the map panel now centres on the same room in the terminal as in the window.
- The message a client with no worlds shows no longer wraps the status row onto a second row at 80 columns.
- A `--snapshot` frame with an MXP picture in cells a server left for it no longer sometimes leaves one of
  the picture's rows blank.
- At 80 columns the status row no longer wraps onto a second row: the encoding and the Ctrl+P hint are
  dropped first.
- Health, mana and movement meters stay on the status row on a narrow terminal, shown as percentages when
  the full numbers do not fit.
- Triggers (F2) and Keypad (F4) no longer draw their editors off the right-hand edge on a narrow terminal.
  When both columns do not fit, the screen shows the one you are in, and header and action bars shorten
  their text rather than cutting off the keys.
- The settings screens (F1–F10), the deletion review and the package and send lists follow the Light and
  Solarized Dark themes instead of staying dark on every theme.
- MXP pictures draw again. Since the TelnetNegotiationCore update the client stopped answering a
  server's MXP `<SUPPORT>` and `<VERSION>` questions, so a game such as SharpMUSH decided it could not show
  `<IMAGE>` and sent the picture's address as text instead.
- Pueblo worlds work. The client answers a server's Pueblo greeting and switches to Pueblo markup when the
  server does, and Pueblo worlds now show their ANSI colours instead of the escape codes. A world set to
  `mxp` or `ansi` on F5 does not answer the greeting.
- A character's on-disconnect commands are now sent, before the client closes the connection itself
  (Disconnect, Reconnect or quitting). They were saved and never used.
- Lua scripts now run. A trigger set's script files are loaded from the `scripts` folder beside
  `config.json` when its character's session opens, and the `script` field of a trigger, alias, timer or
  macro calls the Lua function it names, with the match and its groups. Until now no script was ever loaded,
  although 0.1.5 listed Lua scripting. Script errors are shown in the character's window and in the client
  messages. Files outside the `scripts` folder are refused.

## [0.1.5] - 2026-10-25

### Added

- A desktop window (`sharpmuterm-window`) that runs the same client as the terminal and draws pictures as
  real bitmaps on every platform, Windows included. `sharpmuterm --window` starts it.
- Release packages: a Linux AppImage and a Windows installer, each carrying the terminal and the window.
  The `.tar.gz` and `.zip` now hold both executables.
- Telnet over TCP with TLS and IPv6; NAWS per pane, MTTS, CHARSET, EOR/GA prompts, GMCP, MSDP, MSSP and
  MCCP.
- ANSI (16, 256 and 24-bit colour), MXP with its line-security model and inline `<IMAGE>`, and Pueblo.
- Triggers, aliases, macros and timers in shared trigger sets; sandboxed Lua scripting.
- A multi-pane workspace with tabs, splits, a connection sidebar, a command surface (Ctrl+P), search
  (Ctrl+F), mouse selection and copy, a post composer (F1), and markers for output you missed.
- Inline pictures through Kitty graphics, half-blocks, and a Sixel overlay on Windows Terminal.
- Plain-text and HTML session logs, restored panes after a restart, and an MSSP information screen.
- Dark, Light and Solarized Dark themes, per-character pane colours, and a legibility floor on every
  colour the client paints.
