#!/usr/bin/env bash
# Publish the nuvexa CLI for one RID into the VS Code extension, then pack the VSIX.
# Usage: pack-vscode.sh <version> <rid> <output.vsix>
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${1:?Usage: pack-vscode.sh <version> <rid> <output.vsix>}"
rid="${2:?Usage: pack-vscode.sh <version> <rid> <output.vsix>}"
output="${3:?Usage: pack-vscode.sh <version> <rid> <output.vsix>}"
ext="$root/src/Nuventra.NuvexaDB.VSCode"
cli_out="$ext/cli"

vsce_target=""
case "$rid" in
  win-x64) vsce_target=win32-x64 ;;
  win-arm64) vsce_target=win32-arm64 ;;
  osx-x64) vsce_target=darwin-x64 ;;
  osx-arm64) vsce_target=darwin-arm64 ;;
  linux-x64) vsce_target=linux-x64 ;;
  linux-arm64) vsce_target=linux-arm64 ;;
  *)
    echo "Unsupported RID $rid (need win-x64, win-arm64, osx-x64, osx-arm64, linux-x64, linux-arm64)." >&2
    exit 1
    ;;
esac

python3 "$root/.github/scripts/check-versions.py" --repo-root "$root" --write
cp "$root/LICENSE" "$ext/LICENSE"

chmod +x "$root/.github/scripts/pack-cli.sh"
"$root/.github/scripts/pack-cli.sh" "$rid" "$cli_out"

cd "$ext"
npm ci
npm test
npx --yes @vscode/vsce package --target "$vsce_target" --no-git-tag-version --no-update-package-json -o "$output"
echo "Packed $output (RID $rid, vsce $vsce_target, cli $cli_out)"
