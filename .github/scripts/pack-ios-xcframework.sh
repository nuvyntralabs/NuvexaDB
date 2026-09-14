#!/usr/bin/env bash
# Pack ios-arm64 + iossimulator-arm64 shared libs into Nuvexa.xcframework.
# Usage: pack-ios-xcframework.sh <native-root> <output-dir>
set -euo pipefail

native_root="${1:?native-root (contains ios-arm64 and iossimulator-arm64)}"
out="${2:?output-dir}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
header="$root/src/Nuventra.NuvexaDB.Native/include/nuvexa.h"

find_lib() {
  local dir="$1"
  for name in libnuvexa.dylib nuvexa.dylib; do
    if [[ -f "$dir/$name" ]]; then
      printf '%s\n' "$dir/$name"
      return 0
    fi
  done
  echo "No libnuvexa.dylib / nuvexa.dylib in $dir" >&2
  ls -la "$dir" >&2 || true
  return 1
}

write_plist() {
  local dest="$1"
  cat > "$dest" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Nuvexa</string>
  <key>CFBundleIdentifier</key>
  <string>nuventra.nuvexadb.native</string>
  <key>CFBundleVersion</key>
  <string>1.0</string>
  <key>CFBundleExecutable</key>
  <string>Nuvexa</string>
  <key>CFBundlePackageType</key>
  <string>FMWK</string>
</dict>
</plist>
EOF
}

make_framework() {
  local dylib="$1"
  local dest="$2"
  local rpath="$3"
  mkdir -p "$dest/Headers"
  cp "$dylib" "$dest/Nuvexa"
  cp "$header" "$dest/Headers/nuvexa.h"
  write_plist "$dest/Info.plist"
  chmod +x "$dest/Nuvexa"
  install_name_tool -id "@rpath/Nuvexa.framework/Nuvexa" "$dest/Nuvexa" 2>/dev/null || true
  install_name_tool -add_rpath "$rpath" "$dest/Nuvexa" 2>/dev/null || true
}

device="$(find_lib "$native_root/ios-arm64")"
sim="$(find_lib "$native_root/iossimulator-arm64")"

stage="$(mktemp -d "${TMPDIR:-/tmp}/nuvexa-xcframework.XXXXXX")"
cleanup() { rm -rf "$stage"; }
trap cleanup EXIT

make_framework "$device" "$stage/ios-arm64/Nuvexa.framework" "@executable_path/Frameworks"
make_framework "$sim" "$stage/iossimulator-arm64/Nuvexa.framework" "@executable_path/Frameworks"

mkdir -p "$out"
rm -rf "$out/Nuvexa.xcframework"
xcodebuild -create-xcframework \
  -framework "$stage/ios-arm64/Nuvexa.framework" \
  -framework "$stage/iossimulator-arm64/Nuvexa.framework" \
  -output "$out/Nuvexa.xcframework"
cp "$header" "$out/nuvexa.h"

echo "Wrote $out/Nuvexa.xcframework"
ls -la "$out/Nuvexa.xcframework"
