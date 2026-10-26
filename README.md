<p align="center">
  <img src="docs/logo.svg" alt="SharpMUTerm logo" width="120" height="120">
</p>

<h1 align="center">SharpMUTerm</h1>

<p align="center">A hyper-modern, cross-platform <strong>TUI client for MU*</strong> (MUSH / MUCK / MUD) worlds, built for GPU-accelerated terminals (Kitty, WezTerm, Ghostty) on <strong>Windows and Linux</strong>.</p>

Part of the [SharpMUSH](https://github.com/SharpMUSH) family: **SharpMUSH** is the server,
[**SharpClient**](https://github.com/SharpMUSH/SharpClient) is the graphical client, and
**SharpMUTerm** is the terminal one. It speaks plain telnet, so it connects to any MU\* world —
not just SharpMUSH ones.

The goal is [BeipMU](https://beipdev.github.io/BeipMU/)-class feature parity in a terminal-native
client: rich truecolor text, inline graphics, powerful automation, and full MU\* protocol support.

> **Status:** first release, **0.1.5**. It is a usable text client with automation engines, inline
> graphics, Lua scripting, theming and a desktop window, all covered by unit tests. Each release's changes are
> in [`CHANGELOG.md`](CHANGELOG.md). The architecture and roadmap are in [`docs/PLAN.md`](docs/PLAN.md).

## Install

Download a package from the [latest release](https://github.com/SharpMUSH/SharpMUTerm/releases/latest).
Each one contains both programs: `sharpmuterm`, the terminal client, and `sharpmuterm-window`, the same
client in a desktop window. No .NET runtime is needed.

| Platform | Package | What to run |
|---|---|---|
| Windows | `SharpMUTerm-<version>-win-x64-setup.exe` | Start menu ▸ SharpMUTerm opens the window. Tick *Add to PATH* to run `sharpmuterm` from any terminal. Installs per user, with no administrator prompt. |
| Windows | `sharpmuterm-win-x64.zip` | Unzip and run either `.exe`. |
| Linux | `SharpMUTerm-<version>-x86_64.AppImage` | `chmod +x` it and run it to open the window. Add `--terminal` as the first argument to run the terminal client instead. |
| Linux | `sharpmuterm-linux-x64.tar.gz` | Unpack and run either program. |

`sharpmuterm --window` starts the window from the terminal client, as long as `sharpmuterm-window` is next to
it or on `PATH`.

## Why a TUI

"GPU acceleration" is a property of the terminal *emulator*, not the app. Any TUI running inside
Kitty / WezTerm / Ghostty gets GPU-accelerated glyph rendering for free. SharpMUTerm focuses on emitting
rich truecolor/styled text and using the **Kitty graphics protocol** (with Sixel and half-block
fallbacks) for inline images and maps.

## Tech stack

- **.NET 10** / C#
- **[SharpConsoleUI](https://github.com/nickprotop/ConsoleEx)** — compositor-based TUI framework: split layouts, tabs, resizable/mouse windows, Spectre-style markup, and a native Kitty graphics protocol (+ Sixel/half-block)
- **[TelnetNegotiationCore](https://www.nuget.org/packages/TelnetNegotiationCore/)** — telnet option negotiation (NAWS, MTTS, CHARSET, EOR/GA, MSSP, GMCP)
- **[MoonSharp](https://www.moonsharp.org/)** — embedded, sandboxed Lua scripting
- Custom app-layer parsers for **ANSI** (256 + 24-bit), **MXP**, and **Pueblo**

## Planned feature set (BeipMU parity)

Multi-world tabs · regex triggers · aliases · macros/keybinds · spawns · maps · stat panes ·
inline image viewer · puppets · multiple input windows · scripting (Lua) · TLS + IPv6 ·
HTML logging · GMCP / MSDP / MSSP / MCCP · MXP + Pueblo · Unicode/emoji.

**MSSP is in and readable.** Every server's own description of itself — name, players, uptime,
codebase, contact, website, and the rest of what it chose to publish, official variables and invented
ones alike — is captured on connect, kept between launches, and read from **F5 ▸ `i`** on the selected
world. (Capture is bounded and stripped of control characters, because the report comes off the wire
from a stranger; the limits are far above anything a real server sends.) It says when it was captured,
because a player count from last week is not a player count; and it distinguishes a world you have
never dialled from a server that answered and publishes no MSSP, which is the ordinary case on a MUSH
and not a fault.

## Solution layout

| Project | Responsibility |
|---|---|
| `SharpMUTerm.Core` | Transport, telnet, ANSI/MXP/Pueblo parsers, GMCP/MSDP routing, scrollback, trigger/alias/macro engines, logging (UI-agnostic) |
| `SharpMUTerm.Graphics` | Kitty graphics protocol, capability probe, Sixel + half-block fallbacks, `GraphicsView` |
| `SharpMUTerm.Scripting` | MoonSharp host + scripting API |
| `SharpMUTerm.Tui` | SharpConsoleUI application (windows, panes, settings, wiring) |
| `SharpMUTerm.Web` | The text-mode web view |
| `SharpMUTerm.Avalonia` | The desktop window: the same client on a console driver drawn with Skia |
| `*.Tests` | TUnit test projects |

## What works today

- **Transport & telnet** — TCP with optional TLS (`SslStream`) and IPv6, wrapping
  [TelnetNegotiationCore] for NAWS/MTTS/CHARSET/EOR/GA/GMCP/MSSP/MSDP/MCCP negotiation.
  Prompt (GA/EOR) detection surfaces prompts separately from scrollback.
- **ANSI parser** — incremental SGR parsing: 16-colour, 256-colour, and 24-bit truecolour
  (semicolon and colon forms), with the usual rendition attributes; non-SGR CSI/OSC sequences
  are recognised and discarded.
- **Scrollback** — bounded, thread-safe styled-line model with change events.
- **Automation** — regex **triggers** (gag / highlight / rewrite / respond / spawn-route /
  script), **aliases** (capture-group expansion, multi-command), **macros/keybinds** (F-keys and
  Ctrl/Alt chords — the numpad is not deliverable through the terminal, and F4 says so per
  binding), and a recurring/one-shot **timer** scheduler. User regexes run with a ReDoS
  match-timeout guard.
- **MXP & Pueblo** — first-class parsers for both markup protocols: tags → styled spans, with
  **clickable** `<SEND>`/`<A>` links and commands (`SpanInteraction`), colours, entities, and
  line breaks. Selectable per world.
- **Emoji** — optional emoticon (`:)` → 🙂) and `:shortcode:` (`:fire:` → 🔥) substitution.
- **Logging** — plain-text and styled **HTML** session logs.
- **Config** — a fresh JSON schema: worlds (servers) hold **characters**, and automation lives in
  shared, named **trigger sets** that characters opt into; versioned with automatic migration. Character
  **passwords are saved in a separate `secrets.json`** (owner-only, `0600`) and the config carries only a
  meaningless GUID pointing at them — so your `config.json` stays safe to paste into a bug report.
- **Inline graphics** — Kitty graphics-protocol encoder (incl. Unicode placeholders), Sixel and
  half-block fallbacks, and a capability probe that degrades cleanly when no protocol is present.
- **Scripting** — sandboxed **Lua** (MoonSharp) exposing `world`/`output`/`trigger`/`alias`/
  `timer`/`gmcp`/`log`, with hot-reload.
- **Theming** — yazi-style named themes (Dark / Light / Solarized Dark) with a 16-colour palette
  override and semantic UI colours, serialised to the config as hex.
- **TUI** — a [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) app rendering the multi-pane
  workspace design over the `Core.Workspaces` model: a **connection rail** (worlds → characters →
  windows with connected/active/unread markers, any of which you can click to switch to — collapsed to
  a strip of initials, those are clickable too), **split panes** with tabbed windows (tmux-style
  split/close/zoom), a **command surface** (`Ctrl+P`) with ranked GO TO / WORLD / TERMINAL / LAYOUT
  actions, per-world **accent colours**, a GMCP-driven **status bar** with HP/EN meters, and a
  character-bound input prompt + destination/draft gutter. Output is truecolor markup with clickable
  MXP/Pueblo/web spans; every connected world is told its **own pane's** size over NAWS, on every
  resize and every layout change (rate-limited, with the settled size always delivered).
  `Ctrl+N` next tab · `Ctrl+O` next pane · `Ctrl+W` close · `Ctrl+R` command history (per command
  line, newest first, typing filters it; ⏎ puts an entry on the input line rather than sending it) ·
  `Ctrl+Q` quit (asks first, naming the connections and unsent drafts it would end). Hand-typed login
  lines (`connect <name> <password>`) are kept out of history, and history is never written to disk.
- **Web view** — an in-TUI text-mode browser (`SharpMUTerm.Web`, AngleSharp): fetch a URL or
  follow an MXP/Pueblo/HTML link and read the page as styled, word-wrapped text with clickable
  links you can navigate in-pane. `<img>` shows as a labelled link (graphics-terminal image
  rendering reuses the Kitty/Sixel/half-block pipeline).
- **Packaging** — self-contained single-file builds for Linux and Windows, released as an AppImage, a
  Windows installer, a `.tar.gz` and a `.zip`, each holding both programs. A tag builds them, and the
  release notes come from the changelog. See [`docs/PACKAGING.md`](docs/PACKAGING.md) and
  [`docs/RELEASING.md`](docs/RELEASING.md).

## Building

Requires the **.NET 10 SDK**.

```bash
dotnet build SharpMUTerm.slnx -c Release
```

## Running

```bash
dotnet run --project src/SharpMUTerm.Tui -- <host> <port> [--tls] [--insecure] [--name NAME]
sharpmuterm --help    # from a release package
```

In-app: **Alt+Up/Down** input history · **Ctrl+P** command surface · **Ctrl+F** search · **Ctrl+N** next tab ·
**Ctrl+W** close window · **Alt+F** freeze the pane · **Alt+R** reconnect (drops the connection and redials it) ·
**Alt+D** disconnect · **Ctrl+Q** quit (y/n confirmation). Alt+R and Alt+D act at once, on the character whose
pane is focused, and say so when there is nothing connected. F4 (macros) says which keys the client already uses.
(Windows/panes are mouse-resizable; each window keeps its own input draft.)

### In a desktop window

The same client can run in its own window instead of a terminal. It draws the cells itself with Skia,
so pictures sent by a world are drawn as real bitmaps on every platform — including Windows, whose
terminals cannot show Kitty graphics. Every terminal option works, plus a few of its own (`--help`):

```bash
sharpmuterm-window <host> <port>        # from a release package
dotnet run --project src/SharpMUTerm.Avalonia -- <host> <port> [--font "Cascadia Mono"] [--font-size 14]
dotnet run --project src/SharpMUTerm.Avalonia -- --demo-config --view mxp-image   # the demo worlds, saves nothing
```

The window has no title bar: drag the blank part of the top row to move it, double-click there or press
Alt+Enter to maximise and restore, drag an edge to resize, and Ctrl+Q to quit (`--title-bar` brings the
system one back). A newline in the command line is Ctrl+Enter or Shift+Enter here, which a window can
see and a terminal cannot; Alt+Enter is the window's.
Paste is Ctrl+Shift+V or Shift+Insert. Install a Nerd Font for the header and sidebar icons.

## Testing

The test projects use [TUnit], which runs on the Microsoft.Testing.Platform. Run each directly
(the classic `dotnet test`/VSTest path is not used by MTP on .NET 10):

```bash
dotnet run --project tests/SharpMUTerm.Core.Tests
dotnet run --project tests/SharpMUTerm.Graphics.Tests
dotnet run --project tests/SharpMUTerm.Scripting.Tests
dotnet run --project tests/SharpMUTerm.Web.Tests
dotnet run --project tests/SharpMUTerm.Tui.Tests
dotnet run --project tests/SharpMUTerm.Avalonia.Tests
```

## License

[MIT](LICENSE) © 2026 Harry Cordewener

[TelnetNegotiationCore]: https://www.nuget.org/packages/TelnetNegotiationCore/
[TUnit]: https://github.com/thomhurst/TUnit
