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

### Changed

- An MXP picture is downloaded and decoded once per session. A room picture shown on every look used
  to be fetched again whenever the pane's width had changed, and once per look that arrived while it was
  still loading.

### Fixed

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
