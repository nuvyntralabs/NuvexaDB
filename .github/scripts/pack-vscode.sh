#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${1:?Usage: pack-vscode.sh <version> <output.vsix>}"
output="${2:?Usage: pack-vscode.sh <version> <output.vsix>}"
ext="$root/src/Nuventra.NuvexaDB.VSCode"
python3 "$root/.github/scripts/check-versions.py" --repo-root "$root" --write
cp "$root/LICENSE" "$ext/LICENSE"
cd "$ext"
npm ci
npm test
npx --yes @vscode/vsce package --no-git-tag-version --no-update-package-json -o "$output"
echo "Packed $output"
