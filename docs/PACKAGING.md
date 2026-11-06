# Packaging SharpMUTerm

SharpMUTerm publishes as a **self-contained, single-file** executable — no .NET runtime required on
the target machine.

## Supported runtimes

`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64` (declared in
`src/SharpMUTerm.Tui/SharpMUTerm.Tui.csproj`).

## Local publish

Publish profiles live in `src/SharpMUTerm.Tui/Properties/PublishProfiles/`:

```bash
# Linux x64 → publish output contains a single `sharpmuterm` binary
dotnet publish src/SharpMUTerm.Tui -p:PublishProfile=linux-x64 -o out/linux-x64

# Windows x64 → `sharpmuterm.exe`
dotnet publish src/SharpMUTerm.Tui -p:PublishProfile=win-x64 -o out/win-x64
```

For a RID without a profile, pass the flags directly:

```bash
dotnet publish src/SharpMUTerm.Tui -c Release -r osx-arm64 \
  --self-contained -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o out/osx-arm64
```

The profiles enable single-file extraction, compression, and ReadyToRun for faster startup.
These knobs live **only** in the publish profiles, so ordinary `dotnet build` / `dotnet run`
(and CI) are unaffected — they never require a runtime identifier.

## Desktop window

The window (`src/SharpMUTerm.Avalonia`, `sharpmuterm-window`) has the same two profiles. Publish it into the
same folder as the terminal, because `sharpmuterm --window` looks for it beside itself:

```bash
dotnet publish src/SharpMUTerm.Tui      -p:PublishProfile=linux-x64 -o out/linux-x64
dotnet publish src/SharpMUTerm.Avalonia -p:PublishProfile=linux-x64 -o out/linux-x64
```

## Installers and updates

Both platforms are packaged by [Velopack](https://velopack.io)'s `vpk` (1.2.161, the version of the `Velopack`
package the programs reference). It builds the installer and the update feed the programs read
(`src/SharpMUTerm.Tui/Hosting/VelopackUpdates.cs`); each release carries `releases.<channel>.json` and a full
`.nupkg` per platform, and an installed copy asks the latest GitHub releases for them.

- **Linux AppImage** — `packaging/linux/build-appdir.sh <publish-dir> <appdir>` lays out `SharpMUTerm.AppDir`, and
  `vpk pack -p <appdir> -e sharpmuterm-window -r linux-x64 …` turns it into `SharpMUTerm.AppImage` (it needs
  `mksquashfs`, from squashfs-tools). Velopack adds its updater (`UpdateNix`) and manifest (`sq.version`) to
  `usr/bin` and keeps our `AppRun`, so the AppImage starts the window and `SharpMUTerm.AppImage --terminal [args]`
  runs the terminal client. An update replaces the AppImage file in place.
- **Windows** — `vpk pack -p <publish-dir> -e sharpmuterm-window.exe -r win-x64 --aumid SharpMUTerm.Window
  --shortcuts StartMenuRoot …` makes `SharpMUTerm-win-Setup.exe` and `SharpMUTerm-win-Portable.zip`. Setup installs
  per user (no UAC prompt) under `%LocalAppData%\SharpMUTerm`, the programs in `current\`, and adds a Start menu
  entry for the window. `vpk` cross-packs for Windows from Linux too (`vpk "[win]" -x pack …`).
  What the old Inno Setup installer offered as tasks, the window does in its install hooks (`VelopackHooks`, run by
  Velopack's `Update.exe` as `sharpmuterm-window --veloapp-install|updated|uninstall`): the AppUserModelID key below,
  `current\` on the user's `PATH` (taken off on uninstall only if the hook put it there), and the Symbols Nerd Font Mono
  font for this user, for the terminal client's icons (never removed: a terminal profile may rely on it). A terminal
  that does not fall back to it on its own needs it added to its font list (Windows Terminal 1.21 or later: *Font
  face* `Cascadia Mono, Symbols Nerd Font Mono`). A copy installed by the old Inno Setup installer has no updater;
  uninstall it and run Setup.

How each program comes back from an update: the window is restarted by Velopack's updater once it has exited. The
terminal on Linux applies the new AppImage and then replaces its own process with it (`execv`), so it comes back in
the same terminal. The terminal on Windows cannot hand its console on, so it applies the update as it exits and says
to run it again. Only a copy laid out by an installer updates; the `.tar.gz`, a build from the repository, and a
copy from the old installer have no updater, and the client offers nothing. What a player sees, the settings and
troubleshooting are in [`UPDATES.md`](UPDATES.md).

Both use `packaging/icons/sharpmuterm.png` / `.ico`; the `.ico` is also each executable's Windows icon.

### Three ways to run it

An install starts in any of three modes, each with its own entry:

| Mode | What it is | Windows (Start menu) | AppImage |
|---|---|---|---|
| GUI | The window with its own controls (the default) | **SharpMUTerm** | the launcher, or `SharpMUTerm.AppImage` |
| Emulated | The window drawn all in cells, as the terminal draws it (`--emulated`) | **SharpMUTerm (Emulated)** | right-click ▸ *SharpMUTerm (Emulated)*, or `--emulated` |
| Native | The terminal client in a terminal | **SharpMUTerm (Native)**, and a *SharpMUTerm* profile in Windows Terminal | right-click ▸ *SharpMUTerm (Native)*, or `--terminal` |

On Windows, Velopack makes the first shortcut; the install hook (`LaunchShortcuts`) makes the other two beside it, both
pointing into `current\`, and a Windows Terminal fragment (`%LocalAppData%\Microsoft\Windows Terminal\Fragments\SharpMUTerm`)
running `sharpmuterm.exe`; uninstall removes all three. The Native shortcut opens in the default terminal (Windows
Terminal on Windows 11). No Windows terminal draws inline pictures (ConPTY drops Kitty graphics), so GUI or Emulated is
the better client there. The portable `.zip` runs no hooks and has none of these.

On Linux the desktop entry carries the other two as desktop actions, which a desktop shows once the AppImage is
integrated (AppImageLauncher, appimaged, Gear Lever). Started with no terminal, `AppRun --terminal` opens one and runs
the AppImage again inside it: kitty, then Ghostty (both draw inline pictures), then `$TERMINAL`, WezTerm, foot,
Konsole, GNOME Terminal, `x-terminal-emulator` and xterm; with none, it opens the emulated window. `--plain` and
`--help` are never moved. `sharpmuterm --emulated` hands off to the window like `--window`.

### `telnet://` links

The Linux package can make `telnet://` and `telnets://` links open the window. The program never registers itself;
the package does:

- **Linux** — `sharpmuterm.desktop` declares `MimeType=x-scheme-handler/telnet;x-scheme-handler/telnets;` and runs
  `sharpmuterm-window --open-link %U`. It takes effect once the desktop integrates the AppImage (AppImageLauncher,
  appimaged), or by hand: copy the desktop file to `~/.local/share/applications/`, point its `Exec=` at the AppImage,
  and run `xdg-mime default sharpmuterm.desktop x-scheme-handler/telnet x-scheme-handler/telnets`.
- **Windows** — not registered. The Inno Setup installer offered it as an unticked task; the Velopack installer has
  no page to offer it on, so it was not carried over. By hand: `Software\Classes\telnet` and `…\telnets` under
  `HKCU`, with `URL Protocol` set and `shell\open\command` = `"…\current\sharpmuterm-window.exe" --open-link "%1"`.

`--open-link` is why the handler can be trusted with a link: with it on the command line, the client reads the one
argument after it and drops every other (`ClientOptions.NarrowToLink`), so a link holding a quote cannot add
arguments of its own. The link itself is read as server text (`TelnetLink`): host, port and TLS only — no user
name or password, no path, no command. A running client is handed the link over a socket in
`$XDG_RUNTIME_DIR/sharpmuterm/` (or beside `config.json`), and the new launch exits.

### Alerts and the AppUserModelID

The window takes the AppUserModelID `SharpMUTerm.Window` at start (`SetCurrentProcessExplicitAppUserModelID`), and
Velopack gives its Start menu shortcut the same id (`--aumid`), and the window's install hook writes
`Software\Classes\AppUserModelId\SharpMUTerm.Window` (`DisplayName`, and `IconUri` pointing at the installed
`sharpmuterm.png`) under `HKCU`, removed on uninstall. That key is what Windows needs before it shows a toast under the
id, and its presence is how the window knows it may: an alert then shows a toast (through Windows PowerShell 5.1's
WinRT `ToastNotificationManager`, the text base64-encoded inside the script) as well as flashing the taskbar. A copy
from the portable `.zip` has no key and only flashes. The program writes the key only from its install hook. **Built, never run on
Windows**: neither the toast nor the shortcut's id has been seen on a desktop.

Linux needs nothing from the package: `notify-send` and, on X11, the window's urgency hint. macOS uses
`/usr/bin/osascript`, which files the notification under Script Editor and asks once whether to allow it.

### Passwords

Character passwords go to the system's credential store when there is one that answers: Windows Credential Manager
(built in), the Secret Service through libsecret's `secret-tool` on Linux (`libsecret-tools` on Debian and Ubuntu,
`libsecret` on Fedora and Arch — recommend it, do not require it), and the login Keychain through `security` on
macOS. Without one, or for a password it refuses, they stay in `secrets.json` beside `config.json`, as before.
Existing `secrets.json` entries move into the store the first time the client starts with one.

## CI / releases

`.github/workflows/release.yml` runs when a release is published on the Releases page (and by manual dispatch). For
`linux-x64` and `win-x64` it publishes the terminal and the window into one folder, smoke-tests both, and packages:

| Asset | Contents |
|---|---|
| `sharpmuterm-linux-x64.tar.gz` | `sharpmuterm`, `sharpmuterm-window`; no updater |
| `SharpMUTerm.AppImage` | both, starting the window; updates itself |
| `SharpMUTerm-<version>-linux-full.nupkg`, `releases.linux.json` | the Linux update feed |
| `SharpMUTerm-win-Setup.exe` | Velopack installer for both |
| `SharpMUTerm-win-Portable.zip` | both, with the updater, for a folder of your choosing |
| `SharpMUTerm-<version>-full.nupkg`, `releases.win.json` | the Windows update feed |

The version is the git tag (MinVer). A manual run builds whatever MinVer makes of the commit and uploads the
assets as workflow artifacts only. The release procedure is [`RELEASING.md`](RELEASING.md).
