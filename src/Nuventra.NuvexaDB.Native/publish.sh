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
mkdir -p "$dest"
echo "Publishing nuvexa Native AOT ($rid) → $dest"
dotnet publish "$root/src/Nuventra.NuvexaDB.Native/Nuventra.NuvexaDB.Native.csproj" \
  -c Release \
  -r "$rid" \
  -o "$dest" \
  --nologo \
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
