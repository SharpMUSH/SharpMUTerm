#!/usr/bin/env bash
# Builds SharpMUTerm-<version>-x86_64.AppImage from a publish folder holding both executables.
# Usage: packaging/linux/build-appimage.sh <publish-dir> <version> <output-dir>
set -euo pipefail

publish=$(readlink -f "$1")
version=$2
out=$(readlink -f "$3")
here=$(dirname "$(readlink -f "$0")")
icons=$(readlink -f "$here/../icons")

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
appdir="$work/SharpMUTerm.AppDir"

install -Dm755 "$publish/sharpmuterm-window" "$appdir/usr/bin/sharpmuterm-window"
install -Dm755 "$publish/sharpmuterm" "$appdir/usr/bin/sharpmuterm"
install -Dm755 "$here/AppRun" "$appdir/AppRun"
install -Dm644 "$here/sharpmuterm.desktop" "$appdir/sharpmuterm.desktop"
install -Dm644 "$here/sharpmuterm.desktop" "$appdir/usr/share/applications/sharpmuterm.desktop"
install -Dm644 "$icons/sharpmuterm.png" "$appdir/sharpmuterm.png"
install -Dm644 "$icons/sharpmuterm.png" "$appdir/usr/share/icons/hicolor/256x256/apps/sharpmuterm.png"
ln -s sharpmuterm.png "$appdir/.DirIcon"

tool=${APPIMAGETOOL:-}
if [ -z "$tool" ]; then
    tool="$work/appimagetool"
    curl -fsSL -o "$tool" \
        https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x "$tool"
fi

mkdir -p "$out"
# The runner has no FUSE, so the tool runs from its extracted self rather than mounting.
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 VERSION="$version" \
    "$tool" --no-appstream "$appdir" "$out/SharpMUTerm-$version-x86_64.AppImage"
