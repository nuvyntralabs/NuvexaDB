#!/usr/bin/env bash
# Compile and run tests/interop/abi_runner.c against a published C ABI.
# Usage: run-abi-tests.sh <native-dir> [host|ios-simulator|android-arm64]
set -euo pipefail

native_dir="${1:?native-dir}"
mode="${2:-auto}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
src="$root/tests/interop/abi_runner.c"
include="$root/src/Nuventra.NuvexaDB.Native/include"
build="$root/artifacts/abi-tests"
native_dir="$(cd "$native_dir" && pwd)"

mkdir -p "$build"

detect_mode() {
  if [[ -d "$native_dir/Nuvexa.xcframework" || -f "$native_dir/libnuvexa.a" || -f "$native_dir/nuvexa.a" || -d "$native_dir/iossimulator-arm64" ]]; then
    echo ios-simulator
    return
  fi
  local lib=""
  for name in libnuvexa.so nuvexa.so libnuvexa.dylib nuvexa.dylib nuvexa.dll; do
    if [[ -f "$native_dir/$name" ]]; then
      lib="$native_dir/$name"
      break
    fi
  done
  if [[ -z "$lib" ]]; then
    echo "No nuvexa library in $native_dir" >&2
    ls -la "$native_dir" >&2 || true
    exit 1
  fi
  if [[ "$lib" == *.so ]] && command -v file >/dev/null 2>&1 && file "$lib" | grep -qi aarch64; then
    echo android-arm64
    return
  fi
  echo host
}

if [[ "$mode" == "auto" ]]; then
  mode="$(detect_mode)"
fi

echo "ABI tests mode=$mode native=$native_dir"

run_host() {
  local lib=""
  local arch_flag=()
  local run_prefix=()
  for name in libnuvexa.dylib nuvexa.dylib libnuvexa.so nuvexa.so nuvexa.dll; do
    if [[ -f "$native_dir/$name" ]]; then
      lib="$native_dir/$name"
      break
    fi
  done
  if [[ -z "$lib" ]]; then
    echo "No host nuvexa library in $native_dir" >&2
    exit 1
  fi

  if [[ "$(uname -s)" == "Darwin" ]] && command -v file >/dev/null 2>&1; then
    if file "$lib" | grep -q 'x86_64' && [[ "$(uname -m)" == "arm64" ]]; then
      arch_flag=(-arch x86_64)
      run_prefix=(arch -x86_64)
    fi
  fi

  local out="$build/abi_runner"
  if [[ "$lib" == *.dll ]]; then
    out="$build/abi_runner.exe"
    if command -v cl >/dev/null 2>&1 && [[ -f "$native_dir/nuvexa.lib" ]]; then
      cl /nologo /O1 /I "$include" "$src" /Fe"$out" /link /LIBPATH:"$native_dir" nuvexa.lib
    elif command -v clang >/dev/null 2>&1; then
      clang -O1 -I "$include" "$src" -L "$native_dir" -lnuvexa -o "$out"
    else
      echo "cl.exe (with nuvexa.lib) or clang is required on Windows" >&2
      exit 1
    fi
    PATH="$native_dir:${PATH:-}" "${run_prefix[@]}" "$out"
  else
    cc "${arch_flag[@]}" -O1 -I "$include" "$src" -L "$native_dir" -lnuvexa \
      -Wl,-rpath,"$native_dir" -o "$out"
    export DYLD_LIBRARY_PATH="$native_dir${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
    export LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    "${run_prefix[@]}" "$out"
  fi
}

ios_lib() {
  local lib=""
  if [[ -d "$native_dir/Nuvexa.xcframework" ]]; then
    lib="$(find "$native_dir/Nuvexa.xcframework" \( -name 'libnuvexa.a' -o -name 'nuvexa.a' \) | grep -i simulator | head -n1 || true)"
  fi
  if [[ -z "$lib" && -d "$native_dir/iossimulator-arm64" ]]; then
    lib="$(ls "$native_dir/iossimulator-arm64"/libnuvexa.a "$native_dir/iossimulator-arm64"/nuvexa.a 2>/dev/null | head -n1 || true)"
  fi
  if [[ -z "$lib" ]]; then
    lib="$(find "$native_dir" \( -name 'libnuvexa.a' -o -name 'nuvexa.a' \) | grep -i simulator | head -n1 || true)"
  fi
  if [[ -z "$lib" ]]; then
    echo "No iOS simulator libnuvexa.a under $native_dir" >&2
    exit 1
  fi
  echo "$lib"
}

boot_simulator() {
  local name
  for name in "iPhone 16" "iPhone 16 Pro" "iPhone 15" "iPhone 14"; do
    if xcrun simctl boot "$name" 2>/dev/null; then
      echo "$name"
      return 0
    fi
    if xcrun simctl list devices | grep -F "$name" | grep -q Booted; then
      echo "$name"
      return 0
    fi
  done
  echo "No iPhone simulator could be booted" >&2
  xcrun simctl list devices available >&2 || true
  exit 1
}

run_ios() {
  local lib
  lib="$(ios_lib)"
  local sysroot
  sysroot="$(xcrun --sdk iphonesimulator --show-sdk-path)"
  local out="$build/abi_runner_ios"
  xcrun clang -O1 -fobjc-arc -I "$include" "$src" "$lib" \
    -isysroot "$sysroot" \
    -target arm64-apple-ios13.0-simulator \
    -o "$out"
  boot_simulator >/dev/null
  xcrun simctl spawn booted "$out"
}

ndk_clang() {
  local ndk="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
  if [[ -z "$ndk" ]]; then
    echo "ANDROID_NDK_HOME is required for android-arm64 ABI tests" >&2
    exit 1
  fi
  local prebuilt
  prebuilt="$(echo "$ndk"/toolchains/llvm/prebuilt/*)"
  if [[ ! -d "$prebuilt" ]]; then
    echo "NDK llvm prebuilt not found under $ndk" >&2
    exit 1
  fi
  echo "$prebuilt/bin/aarch64-linux-android26-clang"
}

run_android() {
  local lib=""
  for name in libnuvexa.so nuvexa.so; do
    if [[ -f "$native_dir/$name" ]]; then
      lib="$native_dir/$name"
      break
    fi
  done
  if [[ -z "$lib" ]]; then
    echo "No Android libnuvexa.so in $native_dir" >&2
    exit 1
  fi
  local clang
  clang="$(ndk_clang)"
  local out="$build/abi_runner_android"
  "$clang" -O1 -fPIE -pie -I "$include" "$src" -L "$native_dir" -lnuvexa -o "$out"

  local ndk="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
  local sysroot
  sysroot="$(echo "$ndk"/toolchains/llvm/prebuilt/*/sysroot)"
  if ! command -v qemu-aarch64-static >/dev/null 2>&1 && ! command -v qemu-aarch64 >/dev/null 2>&1; then
    echo "qemu-aarch64 is required to run android-arm64 ABI tests" >&2
    exit 1
  fi
  local qemu="qemu-aarch64-static"
  command -v qemu-aarch64-static >/dev/null 2>&1 || qemu="qemu-aarch64"
  export LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  "$qemu" -L "$sysroot" "$out"
}

case "$mode" in
  host) run_host ;;
  ios-simulator) run_ios ;;
  android-arm64) run_android ;;
  *)
    echo "Unknown ABI test mode: $mode" >&2
    exit 1
    ;;
esac
