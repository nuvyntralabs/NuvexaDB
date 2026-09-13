#!/usr/bin/env bash
# Zip each downloaded Actions artifact folder into one file for GitHub Releases.
# Usage: stage-release-assets.sh <artifact-root> <output-dir>
set -euo pipefail

root="${1:?artifact root}"
out="${2:?output dir}"
mkdir -p "$out"

shopt -s nullglob
for dir in "$root"/*; do
  [ -d "$dir" ] || continue
  name="$(basename "$dir")"
  (
    cd "$dir"
    zip -qr "$out/${name}.zip" .
  )
done

count="$(find "$out" -name '*.zip' | wc -l | tr -d ' ')"
if [ "$count" -eq 0 ]; then
  echo "No release zips staged under $out" >&2
  exit 1
fi
echo "Staged $count release zip(s) in $out"
ls -1 "$out"
