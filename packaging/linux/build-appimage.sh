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

# A fixed release, checked against the hash it had when it was pinned: the tool builds what we ship, so a
# replaced download must fail the build rather than run. Move both lines together.
APPIMAGETOOL_VERSION=1.9.1
APPIMAGETOOL_SHA256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0

tool=${APPIMAGETOOL:-}
if [ -z "$tool" ]; then
    tool="$work/appimagetool"
    curl -fsSL -o "$tool" \
        "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-x86_64.AppImage"
    echo "$APPIMAGETOOL_SHA256  $tool" | sha256sum -c --quiet -
    chmod +x "$tool"
fi

mkdir -p "$out"
# The runner has no FUSE, so the tool runs from its extracted self rather than mounting.
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 VERSION="$version" \
    "$tool" --no-appstream "$appdir" "$out/SharpMUTerm-$version-x86_64.AppImage"
