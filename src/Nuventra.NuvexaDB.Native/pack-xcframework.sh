#!/usr/bin/env bash
# Publish iOS device + simulator shared libs and pack Nuvexa.xcframework.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:-$root/artifacts/native}"
version="${2:-}"

chmod +x "$root/src/Nuventra.NuvexaDB.Native/publish.sh" \
  "$root/.github/scripts/pack-ios-xcframework.sh"
"$root/src/Nuventra.NuvexaDB.Native/publish.sh" "$version" ios-arm64 "$out"
"$root/src/Nuventra.NuvexaDB.Native/publish.sh" "$version" iossimulator-arm64 "$out"
"$root/.github/scripts/pack-ios-xcframework.sh" "$out" "$out/ios"
echo "Wrote $out/ios/Nuvexa.xcframework"
