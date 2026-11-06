#!/usr/bin/env bash
# Lays out SharpMUTerm.AppDir from a publish folder holding both executables, for `vpk pack --packDir` to turn into
# the AppImage (release.yml). Velopack adds its updater (UpdateNix) and manifest beside the executables in usr/bin and
# keeps AppRun as it is, so the AppImage still starts the window, and `--terminal` the terminal client.
# Usage: packaging/linux/build-appdir.sh <publish-dir> <appdir>
set -euo pipefail

publish=$(readlink -f "$1")
appdir=$2
here=$(dirname "$(readlink -f "$0")")
icons=$(readlink -f "$here/../icons")

rm -rf "$appdir"
install -Dm755 "$publish/sharpmuterm-window" "$appdir/usr/bin/sharpmuterm-window"
install -Dm755 "$publish/sharpmuterm" "$appdir/usr/bin/sharpmuterm"
install -Dm755 "$here/AppRun" "$appdir/AppRun"
install -Dm644 "$here/sharpmuterm.desktop" "$appdir/sharpmuterm.desktop"
install -Dm644 "$here/sharpmuterm.desktop" "$appdir/usr/share/applications/sharpmuterm.desktop"
install -Dm644 "$icons/sharpmuterm.png" "$appdir/sharpmuterm.png"
install -Dm644 "$icons/sharpmuterm.png" "$appdir/usr/share/icons/hicolor/256x256/apps/sharpmuterm.png"
ln -sf sharpmuterm.png "$appdir/.DirIcon"
