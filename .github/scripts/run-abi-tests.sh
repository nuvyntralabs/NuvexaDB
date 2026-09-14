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
  local udid
  udid="$(xcrun simctl list devices available | grep iPhone | grep -oE '[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}' | head -n1 || true)"
  if [[ -z "$udid" ]]; then
    echo "No iPhone simulator is available" >&2
    xcrun simctl list devices available >&2 || true
    exit 1
  fi
  # Already-booted devices return a non-zero boot status; spawn uses "booted".
  xcrun simctl boot "$udid" >/dev/null 2>&1 || true
  echo "$udid"
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

host_is_arm64() {
  # Git Bash on windows-11-arm often reports uname -m as x86_64.
  [[ "${VSCMD_ARG_TGT_ARCH:-}" == "arm64" ]] && return 0
  echo "${PROCESSOR_ARCHITECTURE:-} ${PROCESSOR_IDENTIFIER:-} $(uname -m)" | grep -qiE 'ARM64|aarch64'
}

dll_is_arm64() {
  local dll="$1"
  local dumpbin_exe
  dumpbin_exe="$(msvc_exe dumpbin)" || true
  if [[ -n "$dumpbin_exe" ]]; then
    windows_msvc "$dumpbin_exe" /HEADERS "$(to_win "$dll")" | tr -d '\r' | grep -qiE 'machine \(ARM64\)'
    return $?
  fi
  [[ "$native_dir" == *win-arm64* ]]
}

# GNU ld (MinGW / MSYS2 CLANGARM64 / llvm-mingw) can consume a Native AOT
# DLL as an input file. MSVC link.exe cannot (LNK1107 on the DLL; LNK2019
# on a lib /def import library — those thunks do not match AOT export slots).
# Git\clangarm64 is Git's ARM64 runtime, not a compiler — do not treat it
# as a toolchain unless clang.exe is actually there.
direct_dll_cc() {
  local want_arm="${1:-0}"
  local candidate dump
  local -a candidates=(
    "/c/msys64/clangarm64/bin/gcc.exe"
    "/c/msys64/clangarm64/bin/clang.exe"
    "$(command -v clang || true)"
    "$(command -v gcc || true)"
    "/c/Program Files/Git/clangarm64/bin/clang.exe"
    /c/mingw64/bin/gcc
    /mingw64/bin/gcc
  )
  for candidate in "${candidates[@]}"; do
    [[ -n "$candidate" && -x "$candidate" ]] || continue
    dump="$("$candidate" -dumpmachine 2>/dev/null || true)"
    [[ -n "$dump" ]] || continue
    if [[ "$want_arm" -eq 1 ]]; then
      echo "$dump" | grep -qiE 'aarch64|arm64' || continue
      echo "$dump" | grep -qiE 'x86_64|i686' && continue
      echo "$dump" | grep -qiE 'mingw|windows-gnu' || continue
      printf '%s\n' "$candidate"
      return 0
    fi
    echo "$dump" | grep -qiE 'mingw|windows-gnu' || continue
    echo "$dump" | grep -qiE 'aarch64|arm64' && continue
    printf '%s\n' "$candidate"
    return 0
  done
  return 1
}

link_with_direct_dll() {
  local cc="$1"
  local dll="$2"
  local out="$3"
  echo "link Native AOT DLL with $cc ($("$cc" -dumpmachine))"
  if ! "$cc" -O1 -I "$include" "$src" "$dll" -o "$out"; then
    echo "retry $cc -L native-dir -lnuvexa"
    "$cc" -O1 -I "$include" "$src" -L "$native_dir" -lnuvexa -o "$out"
  fi
}

# Git usr\bin\link.exe is GNU coreutils ("extra operand"). Always use
# VCToolsInstallDir\bin\Host*\*\*.exe — do not rely on PATH order.
msvc_host_bin() {
  local tools="${VCToolsInstallDir:-}"
  [[ -n "$tools" ]] || return 1
  local host="${VSCMD_ARG_HOST_ARCH:-x64}"
  local tgt="${VSCMD_ARG_TGT_ARCH:-x64}"
  local host_dir tgt_dir
  case "$(printf '%s' "$host" | tr '[:upper:]' '[:lower:]')" in
    arm64|aarch64) host_dir=ARM64 ;;
    *) host_dir=X64 ;;
  esac
  case "$(printf '%s' "$tgt" | tr '[:upper:]' '[:lower:]')" in
    arm64|aarch64) tgt_dir=ARM64 ;;
    x86) tgt_dir=x86 ;;
    *) tgt_dir=x64 ;;
  esac
  tools="${tools//\\/\/}"
  tools="${tools%/}"
  printf '%s/bin/Host%s/%s\n' "$tools" "$host_dir" "$tgt_dir"
}

msvc_exe() {
  local name="$1"
  local dir unix
  dir="$(msvc_host_bin)" || return 1
  unix="$dir"
  if command -v cygpath >/dev/null 2>&1; then
    unix="$(cygpath -u "$dir")"
  fi
  if [[ -x "$unix/${name}.exe" ]]; then
    to_win "$unix/${name}.exe"
    return 0
  fi
  return 1
}

windows_msvc_link() {
  local dll="$1"
  local out="$2"
  local dumpbin_exe lib_exe cl_exe link_exe
  dumpbin_exe="$(msvc_exe dumpbin)" || true
  lib_exe="$(msvc_exe lib)" || true
  cl_exe="$(msvc_exe cl)" || true
  link_exe="$(msvc_exe link)" || true
  if [[ -z "$dumpbin_exe" || -z "$lib_exe" || -z "$cl_exe" || -z "$link_exe" ]]; then
    echo "MSVC dumpbin/lib/cl/link.exe not found under VCToolsInstallDir (need ilammy/msvc-dev-cmd)." >&2
    echo "VCToolsInstallDir=${VCToolsInstallDir:-unset} host=${VSCMD_ARG_HOST_ARCH:-?} tgt=${VSCMD_ARG_TGT_ARCH:-?}" >&2
    exit 1
  fi
  echo "MSVC tools dumpbin=$dumpbin_exe"
  echo "MSVC tools link=$link_exe"

  local exports="$build/nuvexa.exports.txt"
  local def="$build/nuvexa.def"
  local implib="$build/nuvexa.lib"
  local obj="$build/abi_runner.obj"
  local script="$build/link-abi.cmd"
  local count dll_win def_win lib_win inc_win src_win obj_win out_win machine=X64

  echo "dumpbin $(to_win "$dll")"
  windows_msvc "$dumpbin_exe" /EXPORTS "$(to_win "$dll")" | tr -d '\r' > "$exports"
  if windows_msvc "$dumpbin_exe" /HEADERS "$(to_win "$dll")" | tr -d '\r' | grep -qiE 'machine \(ARM64\)'; then
    machine=ARM64
  fi
  echo "MSVC import lib machine=$machine"
  {
    printf 'LIBRARY nuvexa.dll\r\nEXPORTS\r\n'
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
  # Full paths: PATH on windows-11-arm puts Git usr\bin\link (GNU) ahead of
  # MSVC link.exe, which then prints "extra operand" and --help.
  write_crlf "$script" \
    "@echo off" \
    "setlocal" \
    "\"$lib_exe\" /nologo /def:\"$def_win\" /machine:$machine /name:nuvexa.dll /out:\"$lib_win\"" \
    "if errorlevel 1 exit /b 1" \
    "\"$cl_exe\" /nologo /c /O1 /DNUVEXA_DLLIMPORT /I \"$inc_win\" \"$src_win\" /Fo\"$obj_win\"" \
    "if errorlevel 1 exit /b 1" \
    "\"$link_exe\" /nologo /OUT:\"$out_win\" \"$obj_win\" \"$lib_win\"" \
    "if errorlevel 1 exit /b 1"
  echo "MSVC link via $(to_win "$script")"
  windows_msvc cmd.exe /c "$(to_win "$script")"
}

windows_link_and_run() {
  local dll="$1"
  local out="$2"
  local bindir testdir cc want_arm=0
  bindir="$(dirname "$out")"
  testdir="$build/tmp"
  mkdir -p "$bindir" "$testdir"

  if dll_is_arm64 "$dll"; then
    want_arm=1
  fi
  if cc="$(direct_dll_cc "$want_arm")"; then
    link_with_direct_dll "$cc" "$dll" "$out"
  elif [[ "$want_arm" -eq 1 ]]; then
    echo "win-arm64 requires an aarch64 GNU toolchain (MSYS2 CLANGARM64 or llvm-mingw)." >&2
    echo "MSVC link.exe cannot consume a Native AOT DLL (LNK1107), and a" >&2
    echo "lib /def import library does not resolve nuvexa_* (LNK2019)." >&2
    exit 1
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
