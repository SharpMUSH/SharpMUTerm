# Changelog

Every change a user could notice is recorded here, in the release that ships it. The format is
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). `tools/changelog.py check` enforces the shape,
and a release's notes are its section. The procedure is in [`docs/RELEASING.md`](docs/RELEASING.md).

## [Unreleased]

### Added

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
- Lua scripts can use the map: `map.current()`, `map.path('Town Square')` and `map.walk('Town Square')`.
- Game sound. A GMCP game that uses MCMP (`Client.Media`, as Mudlet does) can now play sounds and music:
  the client asks for it, fetches the files the game names into a cache folder, and plays WAV, MP3, OGG and
  FLAC. Text & ANSI (F7) has a SOUND section: on or off, master, sound and music volume, and *sound
  captions*, which prints a dim `♪ …` line for each sound. Ctrl+P ▸ *Mute sound* silences everything until
  you unmute or restart, and the status row says `♪ muted` meanwhile. Worlds & Characters (F5) has a
  `sounds` field to stop a world downloading new files.
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
- More of MXP: elements a game defines with `<!ELEMENT>` (including room names, exits and numbers, which
  feed the automapper), entities from `<!ENTITY>` and `<VAR>` (kept with the game data on Ctrl+P ▸ *Show game
  data*), `<GAUGE>` and `<STAT>` meters on the status row, `<SOUND>` and `<MUSIC>`, which play as MSP sounds
  do, and `<DEST>`, which sends text to a window of its own. A line that is only definitions no longer
  prints as a blank line.

### Changed

- An MXP picture is downloaded and decoded once per session. A room picture shown on every look used
  to be fetched again whenever the pane's width had changed, and once per look that arrived while it was
  still loading.

### Fixed

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
