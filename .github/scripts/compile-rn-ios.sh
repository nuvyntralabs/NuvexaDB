#!/usr/bin/env bash
# Compile the React Native iOS module (simulator) against Nuvexa.xcframework.
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

fw=""
if [[ -d "$native_dir/Nuvexa.xcframework" ]]; then
  fw="$(find "$native_dir/Nuvexa.xcframework" -path '*simulator*' -name 'Nuvexa.framework' -type d | head -n1 || true)"
fi
if [[ -z "$fw" ]]; then
  echo "No simulator Nuvexa.framework under $native_dir (expected Nuvexa.xcframework)." >&2
  ls -la "$native_dir" >&2 || true
  exit 1
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

xcrun clang++ "$build/NuvexaDB.o" \
  -fobjc-arc \
  -fmodules \
  -std=c++17 \
  -isysroot "$sysroot" \
  -target arm64-apple-ios13.0-simulator \
  -F "$(dirname "$fw")" \
  -framework Nuvexa \
  -framework Foundation \
  -shared \
  -o "$build/libnuvexadb-rn-ios.dylib"
cp "$build/libnuvexadb-rn-ios.dylib" "$out/"
echo "Wrote $out/libnuvexadb-rn-ios.dylib (linked $fw)"
