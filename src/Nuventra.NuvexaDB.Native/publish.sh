#!/usr/bin/env bash
# Publish the Native AOT C ABI. Desktop RIDs first; mobile needs Android / iOS workloads.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${1:-}"
rid="${2:-}"
out="${3:-$root/artifacts/native}"

if [[ -z "$rid" ]]; then
  case "$(uname -s)" in
    Darwin)
      rid="$(uname -m | grep -q arm64 && echo osx-arm64 || echo osx-x64)"
      ;;
    Linux)
      rid="linux-x64"
      ;;
    MINGW*|MSYS*|CYGWIN*)
      rid="win-x64"
      ;;
    *)
      echo "Pass a RID as the second argument (osx-arm64, linux-x64, win-x64, android-arm64, ios-arm64)."
      exit 1
      ;;
  esac
fi

dest="$out/$rid"
publish_rid="$rid"
extra_props=""
# .NET Native AOT cannot target android-arm64 / ios-arm64 (NETSDK1203 /
# PrivateSdkAssemblies). Android JNI loads a Bionic shared library instead.
if [[ "$rid" == "android-arm64" ]]; then
  publish_rid="linux-bionic-arm64"
  dest="$out/android-arm64"
  extra_props="-p:DisableUnsupportedError=true -p:PublishAotUsingRuntimePack=true"
  if [[ -n "${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}" ]]; then
    ndk="${ANDROID_NDK_HOME:-$ANDROID_NDK_ROOT}"
    prebuilt="$(echo "$ndk"/toolchains/llvm/prebuilt/*/bin)"
    if [[ -d "$prebuilt" ]]; then
      export PATH="$prebuilt:$PATH"
    fi
  fi
fi
if [[ "$rid" == ios-arm64 || "$rid" == iossimulator-arm64 ]]; then
  echo "Native AOT cannot publish $rid (.NET SDK NETSDK1203). Use linux-bionic-arm64 for Android JNI." >&2
  exit 1
fi

mkdir -p "$dest"
echo "Publishing nuvexa Native AOT ($publish_rid) → $dest"
# shellcheck disable=SC2086
dotnet publish "$root/src/Nuventra.NuvexaDB.Native/Nuventra.NuvexaDB.Native.csproj" \
  -c Release \
  -r "$publish_rid" \
  -o "$dest" \
  --nologo \
  $extra_props \
  ${version:+-p:Version=$version -p:PackageVersion=$version}

# Unix linkers and JNA look for libnuvexa.*; Native AOT on macOS emits nuvexa.dylib.
if [[ -f "$dest/nuvexa.dylib" && ! -e "$dest/libnuvexa.dylib" ]]; then
  ln -sf nuvexa.dylib "$dest/libnuvexa.dylib"
fi
if [[ -f "$dest/nuvexa.so" && ! -e "$dest/libnuvexa.so" ]]; then
  ln -sf nuvexa.so "$dest/libnuvexa.so"
fi

echo "Outputs:"
ls -1 "$dest"/libnuvexa.* "$dest"/nuvexa.* "$dest"/*.a 2>/dev/null || ls -1 "$dest"
