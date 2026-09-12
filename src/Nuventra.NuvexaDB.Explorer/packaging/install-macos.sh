#!/usr/bin/env bash
# Registers .nvx with a NuvexaDB Explorer .app bundle via Launch Services.
set -euo pipefail
APP="${1:?Usage: install-macos.sh /path/to/NuvexaDB Explorer.app}"
if [[ ! -d "$APP" ]]; then
  echo "App bundle not found: $APP" >&2
  exit 1
fi
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$APP"
echo "Registered $APP for .nvx (see packaging/Info.plist CFBundleDocumentTypes)."
