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
  if [[ -d "$native_dir/Nuvexa.xcframework" || -f "$native_dir/libnuvexa.dylib" || -d "$native_dir/iossimulator-arm64" ]]; then
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
  local arch_flag=""
  local run_prefix=""
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
      arch_flag="-arch x86_64"
      run_prefix="arch -x86_64"
    fi
  fi

  local out="$build/abi_runner"
  if [[ "$lib" == *.dll ]]; then
    out="$build/abi_runner.exe"
    windows_link_and_run "$lib" "$out"
  else
    # shellcheck disable=SC2086
    cc $arch_flag -O1 -I "$include" "$src" -L "$native_dir" -lnuvexa \
      -Wl,-rpath,"$native_dir" -o "$out"
    export DYLD_LIBRARY_PATH="$native_dir${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
    export LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    if [[ -n "$run_prefix" ]]; then
      $run_prefix "$out"
    else
      "$out"
    fi
  fi
}

ios_framework() {
  local fw=""
  if [[ -d "$native_dir/Nuvexa.xcframework" ]]; then
    fw="$(find "$native_dir/Nuvexa.xcframework" -path '*simulator*' -name 'Nuvexa.framework' -type d | head -n1 || true)"
  fi
  if [[ -z "$fw" && -d "$native_dir/iossimulator-arm64/Nuvexa.framework" ]]; then
    fw="$native_dir/iossimulator-arm64/Nuvexa.framework"
  fi
  if [[ -z "$fw" ]]; then
    echo "No iOS simulator Nuvexa.framework under $native_dir" >&2
    ls -la "$native_dir" >&2 || true
    exit 1
  fi
  echo "$fw"
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
  local fw
  fw="$(ios_framework)"
  local sysroot
  sysroot="$(xcrun --sdk iphonesimulator --show-sdk-path)"
  local out="$build/abi_runner_ios"
  local fwdir
  fwdir="$(dirname "$fw")"
  rm -rf "$build/Nuvexa.framework"
  cp -R "$fw" "$build/Nuvexa.framework"
  xcrun clang -O1 -fobjc-arc -I "$include" "$src" \
    -isysroot "$sysroot" \
    -target arm64-apple-ios13.0-simulator \
    -F "$fwdir" \
    -framework Nuvexa \
    -framework Foundation \
    -framework Security \
    -Wl,-rpath,@executable_path \
    -o "$out"
  boot_simulator >/dev/null
  xcrun simctl spawn booted "$out"
}

to_win() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$1"
  else
    printf '%s\n' "$1"
  fi
}

# Git Bash rewrites a single-slash flag (/EXPORTS) into a drive path (E:\XPORTS).
# Keep MSVC switches intact for this function and its child processes.
windows_msvc() {
  MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' "$@"
}

write_crlf() {
  local dest="$1"
  shift
  printf '%s\r\n' "$@" > "$dest"
}

mingw_gcc() {
  local candidate dump host_arm=0
  if uname -m | grep -qiE 'aarch64|arm64'; then
    host_arm=1
  fi
  for candidate in "$(command -v gcc || true)" /c/mingw64/bin/gcc /mingw64/bin/gcc; do
    [[ -n "$candidate" && -x "$candidate" ]] || continue
    dump="$("$candidate" -dumpmachine 2>/dev/null || true)"
    echo "$dump" | grep -qi mingw || continue
    if echo "$dump" | grep -qiE 'aarch64|arm64'; then
      printf '%s\n' "$candidate"
      return 0
    fi
    # x64 MinGW cannot link a win-arm64 Native AOT DLL.
    [[ "$host_arm" -eq 1 ]] && continue
    printf '%s\n' "$candidate"
    return 0
  done
  return 1
}

windows_msvc_link() {
  local dll="$1"
  local out="$2"
  if ! command -v dumpbin >/dev/null 2>&1 || ! command -v lib >/dev/null 2>&1 || ! command -v cl >/dev/null 2>&1 || ! command -v link >/dev/null 2>&1; then
    echo "dumpbin, lib, cl, and link from the MSVC tools are required when MinGW gcc is absent" >&2
    exit 1
  fi

  local exports="$build/nuvexa.exports.txt"
  local def="$build/nuvexa.def"
  local implib="$build/nuvexa.lib"
  local obj="$build/abi_runner.obj"
  local script="$build/link-abi.cmd"
  local count dll_win def_win lib_win inc_win src_win obj_win out_win machine=X64

  echo "dumpbin $(to_win "$dll")"
  windows_msvc dumpbin /EXPORTS "$(to_win "$dll")" | tr -d '\r' > "$exports"
  if windows_msvc dumpbin /HEADERS "$(to_win "$dll")" | tr -d '\r' | grep -qiE 'machine \(ARM64\)'; then
    machine=ARM64
  fi
  echo "MSVC import lib machine=$machine"
  {
    printf 'LIBRARY nuvexa\r\nEXPORTS\r\n'
    awk '$1 ~ /^[0-9]+$/ && $NF ~ /^nuvexa_[A-Za-z0-9_]+$/ { printf "    %s\r\n", $NF }' "$exports"
  } > "$def"
  count="$(awk '$1 ~ /^[0-9]+$/ && $NF ~ /^nuvexa_[A-Za-z0-9_]+$/ { n++ } END { print n+0 }' "$exports")"
  if [[ "$count" -lt 1 ]]; then
    echo "dumpbin reported no nuvexa_* exports" >&2
    cat "$exports" >&2
    exit 1
  fi
  echo "import lib: $count nuvexa_* exports"

  dll_win="$(to_win "$dll")"
  def_win="$(to_win "$def")"
  lib_win="$(to_win "$implib")"
  inc_win="$(to_win "$include")"
  src_win="$(to_win "$src")"
  obj_win="$(to_win "$obj")"
  out_win="$(to_win "$out")"
  # Every MSVC switch lives in this .cmd so Git Bash cannot rewrite /c, /link, or /OUT.
  write_crlf "$script" \
    "@echo off" \
    "setlocal" \
    "lib /nologo /def:\"$def_win\" /machine:$machine /out:\"$lib_win\"" \
    "if errorlevel 1 exit /b 1" \
    "cl /nologo /c /O1 /I \"$inc_win\" \"$src_win\" /Fo\"$obj_win\"" \
    "if errorlevel 1 exit /b 1" \
    "link /nologo /OUT:\"$out_win\" \"$obj_win\" \"$lib_win\"" \
    "if errorlevel 1 exit /b 1"
  echo "MSVC link via $(to_win "$script")"
  windows_msvc cmd.exe /c "$(to_win "$script")"
}

windows_link_and_run() {
  local dll="$1"
  local out="$2"
  local bindir testdir gcc
  bindir="$(dirname "$out")"
  testdir="$build/tmp"
  mkdir -p "$bindir" "$testdir"

  # MinGW ld can consume the Native AOT DLL. MSVC link.exe cannot (LNK1107),
  # and `cl /link` from Git Bash drops nuvexa.lib (LNK2019).
  if gcc="$(mingw_gcc)"; then
    echo "link with MinGW $gcc ($("$gcc" -dumpmachine))"
    "$gcc" -O1 -I "$include" "$src" "$dll" -o "$out"
  else
    echo "MinGW gcc not found; falling back to MSVC via cmd.exe"
    windows_msvc_link "$dll" "$out"
  fi

  cp -f "$dll" "$bindir/"
  export NUVEXA_TEST_DIR
  NUVEXA_TEST_DIR="$(to_win "$testdir")"
  echo "running $(to_win "$out")"
  windows_msvc cmd.exe /c "$(to_win "$out")"
}

verify_android_exports() {
  local lib="$1"
  local nm=""
  local ndk="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
  if [[ -n "$ndk" ]]; then
    local llvm_nm
    llvm_nm="$(echo "$ndk"/toolchains/llvm/prebuilt/*/bin/llvm-nm)"
    if [[ -x "$llvm_nm" ]]; then
      nm="$llvm_nm"
    fi
  fi
  if [[ -z "$nm" ]] && command -v llvm-nm >/dev/null 2>&1; then
    nm="llvm-nm"
  fi
  if [[ -z "$nm" ]] && command -v nm >/dev/null 2>&1; then
    nm="nm"
  fi
  if [[ -z "$nm" ]]; then
    echo "nm/llvm-nm is required to verify Android exports" >&2
    exit 1
  fi
  local table
  table="$("$nm" -D "$lib" 2>/dev/null || "$nm" "$lib")"
  local missing=0
  local sym
  for sym in nuvexa_abi_version nuvexa_create nuvexa_open nuvexa_close nuvexa_execute nuvexa_insert nuvexa_is_encrypted; do
    if ! printf '%s\n' "$table" | grep -q "$sym"; then
      echo "missing export: $sym" >&2
      missing=1
    fi
  done
  if [[ "$missing" -ne 0 ]]; then
    echo "$table" >&2
    exit 1
  fi
  echo "Android lib exports the C ABI symbols. QEMU cannot run Bionic (/system/bin/linker64). Golden cases run in the Android AAR / JVM job."
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
    ls -la "$native_dir" >&2 || true
    exit 1
  fi
  verify_android_exports "$lib"
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
