#!/usr/bin/env bash
# Installs a user-local binary, MIME type, and desktop entry for .nvx.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
EXE="${1:-$ROOT/nuvexa-explorer}"
if [[ ! -x "$EXE" && -f "$EXE" ]]; then
  chmod +x "$EXE"
fi
if [[ ! -f "$EXE" ]]; then
  echo "Usage: install-linux.sh [/path/to/nuvexa-explorer]" >&2
  exit 1
fi
EXE="$(cd "$(dirname "$EXE")" && pwd)/$(basename "$EXE")"
BINDIR="${XDG_BIN_HOME:-$HOME/.local/bin}"
APPDIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
MIMEDIR="${XDG_DATA_HOME:-$HOME/.local/share}/mime/packages"
mkdir -p "$BINDIR" "$APPDIR" "$MIMEDIR"
install -m 0755 "$EXE" "$BINDIR/nuvexa-explorer"
if [[ -f "$ROOT/nuvexa.desktop" ]]; then
  sed "s|^Exec=nuvexa-explorer %f|Exec=$BINDIR/nuvexa-explorer %f|" "$ROOT/nuvexa.desktop" > "$APPDIR/nuvexa.desktop"
elif [[ -f "$ROOT/packaging/nuvexa.desktop" ]]; then
  sed "s|^Exec=nuvexa-explorer %f|Exec=$BINDIR/nuvexa-explorer %f|" "$ROOT/packaging/nuvexa.desktop" > "$APPDIR/nuvexa.desktop"
fi
if [[ -f "$ROOT/nuvexa.xml" ]]; then
  cp "$ROOT/nuvexa.xml" "$MIMEDIR/nuvexa.xml"
elif [[ -f "$ROOT/packaging/nuvexa.xml" ]]; then
  cp "$ROOT/packaging/nuvexa.xml" "$MIMEDIR/nuvexa.xml"
fi
update-mime-database "${XDG_DATA_HOME:-$HOME/.local/share}/mime" >/dev/null 2>&1 || true
update-desktop-database "$APPDIR" >/dev/null 2>&1 || true
echo "Installed $BINDIR/nuvexa-explorer and .nvx association."
