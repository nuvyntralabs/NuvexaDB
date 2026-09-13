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

if [[ "$sdk" != "android" ]]; then
  if [[ -z "$native_dir" || ! -d "$native_dir" ]]; then
    echo "Native directory is required for $sdk" >&2
    exit 1
  fi
  native_dir="$(cd "$native_dir" && pwd)"
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
    gradle -p "$root/bindings/android" assembleRelease --no-daemon
    found=0
    while IFS= read -r -d '' aar; do
      cp "$aar" "$out/"
      found=1
    done < <(find "$root/bindings/android" -name '*.aar' -print0)
    if [[ "$found" -eq 0 ]]; then
      echo "No Android AAR was produced" >&2
      exit 1
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
      if dart pub pack --help >/dev/null 2>&1; then
        dart pub pack -o "$out"
      else
        tar -czf "$out/nuvexadb-flutter-${version}.tgz" \
          --exclude='.dart_tool' --exclude='.packages' \
          -C "$root/bindings/flutter" pubspec.yaml lib test README.md analysis_options.yaml
      fi
    )
    ;;
  *)
    echo "Unknown SDK: $sdk" >&2
    exit 1
    ;;
esac

echo "Staged $sdk artifacts:"
ls -1 "$out"
