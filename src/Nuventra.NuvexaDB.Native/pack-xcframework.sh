#!/usr/bin/env bash
# Build an iOS xcframework from Native AOT static libraries. Requires the iOS workload.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:-$root/artifacts/native}"
version="${2:-}"
device="$out/ios-arm64"
sim="$out/iossimulator-arm64"

chmod +x "$root/src/Nuventra.NuvexaDB.Native/publish.sh"
"$root/src/Nuventra.NuvexaDB.Native/publish.sh" "$version" ios-arm64 "$out"
"$root/src/Nuventra.NuvexaDB.Native/publish.sh" "$version" iossimulator-arm64 "$out"

header="$root/src/Nuventra.NuvexaDB.Native/include/nuvexa.h"
headers="$out/headers"
mkdir -p "$headers"
cp "$header" "$headers/nuvexa.h"

lib_device="$(ls "$device"/libnuvexa.a "$device"/nuvexa.a 2>/dev/null | head -n1 || true)"
lib_sim="$(ls "$sim"/libnuvexa.a "$sim"/nuvexa.a 2>/dev/null | head -n1 || true)"
if [[ -z "$lib_device" || -z "$lib_sim" ]]; then
  echo "Native AOT did not produce static libraries. Check ios-arm64 / iossimulator-arm64 publish output."
  exit 1
fi

rm -rf "$out/Nuvexa.xcframework"
xcodebuild -create-xcframework \
  -library "$lib_device" -headers "$headers" \
  -library "$lib_sim" -headers "$headers" \
  -output "$out/Nuvexa.xcframework"
echo "Wrote $out/Nuvexa.xcframework"
