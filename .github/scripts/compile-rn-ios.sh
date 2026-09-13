#!/usr/bin/env bash
# Compile the React Native iOS module (simulator). Links libnuvexa.a when present.
# Usage: compile-rn-ios.sh <native-dir> <artifacts-dir>
set -euo pipefail

native_dir="${1:?native-dir}"
out="${2:?artifacts-dir}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
ios="$root/bindings/react-native/ios"
stubs="$root/.github/scripts/rn-ios-stubs"
build="$root/bindings/react-native/ios/build"
sdk=iphonesimulator

mkdir -p "$build" "$out"
native_dir="$(cd "$native_dir" && pwd)"

lib=""
if [[ -d "$native_dir/Nuvexa.xcframework" ]]; then
  lib="$(find "$native_dir/Nuvexa.xcframework" \( -name 'libnuvexa.a' -o -name 'nuvexa.a' \) | grep -i simulator | head -n1 || true)"
fi
if [[ -z "$lib" && -d "$native_dir/iossimulator-arm64" ]]; then
  lib="$(ls "$native_dir/iossimulator-arm64"/libnuvexa.a "$native_dir/iossimulator-arm64"/nuvexa.a 2>/dev/null | head -n1 || true)"
fi
if [[ -z "$lib" ]]; then
  lib="$(find "$native_dir" \( -name 'libnuvexa.a' -o -name 'nuvexa.a' \) | grep -i simulator | head -n1 || true)"
fi

sysroot="$(xcrun --sdk "$sdk" --show-sdk-path)"
xcrun clang++ -x objective-c++ -c "$ios/NuvexaDB.mm" \
  -fobjc-arc \
  -fmodules \
  -std=c++17 \
  -isysroot "$sysroot" \
  -target arm64-apple-ios13.0-simulator \
  -I "$ios/include" \
  -I "$stubs" \
  -o "$build/NuvexaDB.o"
cp "$build/NuvexaDB.o" "$out/"

if [[ -n "$lib" ]]; then
  xcrun libtool -static -o "$build/libnuvexadb-rn-ios.a" "$build/NuvexaDB.o" "$lib"
  cp "$build/libnuvexadb-rn-ios.a" "$out/"
  echo "Wrote $out/libnuvexadb-rn-ios.a"
else
  echo "Compiled NuvexaDB.o (no iOS libnuvexa.a; .NET Native AOT does not support ios-arm64)."
fi
