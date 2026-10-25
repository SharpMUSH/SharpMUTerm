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

## Installers

`packaging/` holds what turns a publish folder into an installable package:

- **Linux AppImage** — `packaging/linux/build-appimage.sh <publish-dir> <version> <out-dir>` downloads
  `appimagetool` and builds `SharpMUTerm-<version>-x86_64.AppImage`. It starts the window;
  `SharpMUTerm.AppImage --terminal [args]` runs the terminal client from the same file.
- **Windows installer** — `packaging/windows/sharpmuterm.iss`, an Inno Setup 6 script. Installs both executables
  per user (no UAC prompt) or for everyone, adds a Start menu entry for the window, and optionally puts the
  install folder on `PATH`. Build with
  `iscc /DAppVersion=1.2.3 /DNumericVersion=1.2.3.0 /DPublishDir=<dir> /DOutputDir=<dir> packaging\windows\sharpmuterm.iss`.

Both use `packaging/icons/sharpmuterm.png` / `.ico`; the `.ico` is also each executable's Windows icon.

## CI / releases

`.github/workflows/release.yml` runs on `v*` tags (and manual dispatch). For `linux-x64` and `win-x64` it
publishes the terminal and the window into one folder, smoke-tests both, and packages:

| Asset | Contents |
|---|---|
| `sharpmuterm-linux-x64.tar.gz` | `sharpmuterm`, `sharpmuterm-window` |
| `SharpMUTerm-<version>-x86_64.AppImage` | both, starting the window |
| `sharpmuterm-win-x64.zip` | `sharpmuterm.exe`, `sharpmuterm-window.exe` |
| `SharpMUTerm-<version>-win-x64-setup.exe` | Inno Setup installer for both |

The version is the git tag (MinVer), and a tag must have a dated `CHANGELOG.md` section; a manual run builds
whatever MinVer makes of the commit and uploads the assets as workflow artifacts only. The release procedure
is [`RELEASING.md`](RELEASING.md).
