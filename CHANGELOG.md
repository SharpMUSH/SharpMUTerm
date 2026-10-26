# Changelog

Every change a user could notice is recorded here, in the release that ships it. The format is
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). `tools/changelog.py check` enforces the shape,
and a release's notes are its section. The procedure is in [`docs/RELEASING.md`](docs/RELEASING.md).

## [Unreleased]

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
