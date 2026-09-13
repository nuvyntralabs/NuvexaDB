#!/usr/bin/env bash
# Test one language SDK against a published C ABI, then stage artifacts.
# Usage: run-sdk-ci.sh <sdk> <version> <artifacts-dir> [native-dir]
set -euo pipefail

sdk="${1:?sdk}"
version="${2:?version}"
out="${3:?artifacts-dir}"
native_dir="${4:-}"
root="$(cd "$(dirname "$0")/../.." && pwd)"

py() {
  if command -v python3 >/dev/null 2>&1; then
    python3 "$@"
  else
    python "$@"
  fi
}

resolve_lib() {
  local dir="$1"
  for name in libnuvexa.dylib nuvexa.dylib libnuvexa.so nuvexa.so nuvexa.dll; do
    if [[ -f "$dir/$name" ]]; then
      echo "$dir/$name"
      return 0
    fi
  done
  echo "No nuvexa native library in $dir" >&2
  ls -la "$dir" >&2 || true
  exit 1
}

mkdir -p "$out"
stage_jni="$root/.github/scripts/stage-jni-lib.sh"

if [[ -z "$native_dir" || ! -d "$native_dir" ]]; then
  echo "Native directory is required for $sdk" >&2
  exit 1
fi
native_dir="$(cd "$native_dir" && pwd)"
if [[ "$sdk" != "react-native-ios" ]]; then
  export NUVEXA_NATIVE_DIR="$native_dir"
  export NUVEXA_NATIVE_LIB="$(resolve_lib "$native_dir")"
  export DYLD_LIBRARY_PATH="$native_dir${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
  export LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  export PATH="$native_dir:${PATH:-}"
  export CGO_ENABLED=1
  export CGO_LDFLAGS="-L$native_dir -lnuvexa"
  echo "NUVEXA_NATIVE_LIB=$NUVEXA_NATIVE_LIB"
fi

case "$sdk" in
  jvm)
    gradle -p "$root/bindings/jvm" test jar --no-daemon
    cp "$root/bindings/jvm/build/libs/"*.jar "$out/"
    ;;
  android)
    chmod +x "$stage_jni"
    "$stage_jni" "$native_dir" "$root/bindings/android/src/main/jniLibs"
    gradle -p "$root/bindings/android" assembleRelease --no-daemon
    found=0
    for aar in "$root/bindings/android/build/outputs/aar/"*.aar; do
      [[ -f "$aar" ]] || continue
      cp "$aar" "$out/"
      found=1
      unzip -l "$aar" | grep -E 'jni/arm64-v8a/libnuvexa.so' >/dev/null
    done
    if [[ "$found" -eq 0 ]]; then
      echo "No Android AAR was produced" >&2
      exit 1
    fi
    ;;
  react-native-android)
    chmod +x "$stage_jni"
    "$stage_jni" "$native_dir" "$root/bindings/react-native/android/src/main/jniLibs"
    gradle -p "$root/bindings/react-native/android" assembleRelease --no-daemon
    found=0
    for aar in "$root/bindings/react-native/android/build/outputs/aar/"*.aar; do
      [[ -f "$aar" ]] || continue
      cp "$aar" "$out/"
      found=1
      unzip -l "$aar" | grep -E 'jni/arm64-v8a/libnuvexa.so' >/dev/null
    done
    if [[ "$found" -eq 0 ]]; then
      echo "No React Native Android AAR was produced" >&2
      exit 1
    fi
    ;;
  react-native-ios)
    chmod +x "$root/.github/scripts/compile-rn-ios.sh" "$root/.github/scripts/run-abi-tests.sh"
    "$root/.github/scripts/compile-rn-ios.sh" "$native_dir" "$out"
    if [[ -f "$native_dir/libnuvexa.dylib" || -f "$native_dir/nuvexa.dylib" ]]; then
      "$root/.github/scripts/run-abi-tests.sh" "$native_dir" host
    elif find "$native_dir" \( -name 'libnuvexa.a' -o -name 'nuvexa.a' \) | grep -qi simulator; then
      "$root/.github/scripts/run-abi-tests.sh" "$native_dir" ios-simulator
    fi
    ;;
  python)
    (
      cd "$root/bindings/python"
      py -m pip install --upgrade pip build
      py -m pip install -e .
      py -m unittest discover -s tests -v
      py -m build -o "$out"
    )
    ;;
  node)
    (
      cd "$root/bindings/node"
      npm ci
      npm test
      npm pack --pack-destination "$out"
    )
    ;;
  react-native)
    (
      cd "$root/bindings/react-native"
      npm ci
      npm test
      npm pack --pack-destination "$out"
    )
    ;;
  go)
    (
      cd "$root/bindings/go"
      go test -count=1 -coverprofile="$out/coverage.out"
    )
    tar -C "$root/bindings/go" -czf "$out/nuvexadb-go-${version}.tgz" \
      go.mod nuvexa.go nuvexa_test.go include README.md examples
    ;;
  cpp)
    cmake -S "$root/bindings/cpp" -B "$root/bindings/cpp/build"
    cmake --build "$root/bindings/cpp/build"
    ctest --test-dir "$root/bindings/cpp/build" --output-on-failure
    cp "$root/bindings/cpp/include/"* "$out/"
    cp "$root/bindings/cpp/CMakeLists.txt" "$out/"
    cp "$root/bindings/cpp/README.md" "$out/"
    ;;
  swift)
    (
      cd "$root/bindings/swift"
      swift test
      swift build -c release
    )
    tar -C "$root/bindings/swift" -czf "$out/nuvexadb-swift-${version}.tgz" \
      Package.swift Sources Tests Examples README.md
    ;;
  flutter)
    (
      cd "$root/bindings/flutter"
      dart pub get
      dart test
      # dart pub has unpack, not pack. `dart pub pack --help` still exits 0
      # because --help is a global dart pub flag.
      tar -czf "$out/nuvexadb-flutter-${version}.tgz" \
        --exclude='.dart_tool' --exclude='.packages' \
        -C "$root/bindings/flutter" pubspec.yaml lib test README.md analysis_options.yaml
    )
    ;;
  *)
    echo "Unknown SDK: $sdk" >&2
    exit 1
    ;;
esac

echo "Staged $sdk artifacts:"
ls -1 "$out"
