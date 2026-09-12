#!/usr/bin/env bash
# Installs a user-local MIME type and desktop entry for .nvx.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
EXE="${1:?Usage: install-linux.sh /path/to/nuvexa-explorer}"
APPDIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
MIMEDIR="${XDG_DATA_HOME:-$HOME/.local/share}/mime/packages"
mkdir -p "$APPDIR" "$MIMEDIR"
sed "s|^Exec=nuvexa-explorer %f|Exec=${EXE} %f|" "$ROOT/nuvexa.desktop" > "$APPDIR/nuvexa.desktop"
cp "$ROOT/nuvexa.xml" "$MIMEDIR/nuvexa.xml"
update-mime-database "${XDG_DATA_HOME:-$HOME/.local/share}/mime" >/dev/null 2>&1 || true
update-desktop-database "$APPDIR" >/dev/null 2>&1 || true
echo "Installed .nvx association for $EXE"
