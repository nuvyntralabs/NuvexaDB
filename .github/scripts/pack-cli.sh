#!/usr/bin/env bash
# Publish the nuvexa CLI for one RID (self-contained). Used by VS Code and Visual Studio VSIX packs.
# Usage: pack-cli.sh <rid> <output-dir>
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
rid="${1:?Usage: pack-cli.sh <rid> <output-dir>}"
cli_out="${2:?Usage: pack-cli.sh <rid> <output-dir>}"
cli_proj="$root/src/Nuventra.NuvexaDB.Cli/Nuventra.NuvexaDB.Cli.csproj"

case "$rid" in
  win-x64|win-arm64|osx-x64|osx-arm64|linux-x64|linux-arm64) ;;
  *)
    echo "Unsupported RID $rid (need win-x64, win-arm64, osx-x64, osx-arm64, linux-x64, linux-arm64)." >&2
    exit 1
    ;;
esac

rm -rf "$cli_out"
mkdir -p "$cli_out"

publish_props=(
  -p:DebugType=none
  -p:CopyOutputSymbolsToPublishDirectory=false
)
if [[ "$rid" == osx-* ]]; then
  # Same as Data Studio: single-file + extracted natives breaks macOS dylib load.
  publish_props+=(-p:PublishSingleFile=false)
else
  publish_props+=(
    -p:PublishSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
    -p:EnableCompressionInSingleFile=true
  )
fi

dotnet publish "$cli_proj" -c Release -r "$rid" --self-contained true -o "$cli_out" --nologo \
  "${publish_props[@]}"

cli_bin=""
if [[ -f "$cli_out/nuvexa.exe" ]]; then
  cli_bin="$cli_out/nuvexa.exe"
elif [[ -f "$cli_out/nuvexa" ]]; then
  cli_bin="$cli_out/nuvexa"
elif [[ -f "$cli_out/Nuventra.NuvexaDB.Cli.exe" ]]; then
  mv "$cli_out/Nuventra.NuvexaDB.Cli.exe" "$cli_out/nuvexa.exe"
  cli_bin="$cli_out/nuvexa.exe"
elif [[ -f "$cli_out/Nuventra.NuvexaDB.Cli" ]]; then
  mv "$cli_out/Nuventra.NuvexaDB.Cli" "$cli_out/nuvexa"
  cli_bin="$cli_out/nuvexa"
else
  echo "Published nuvexa binary not found in $cli_out" >&2
  ls -la "$cli_out" >&2
  exit 1
fi

if [[ "$rid" != win-* ]]; then
  chmod +x "$cli_bin"
fi
find "$cli_out" \( -name '*.pdb' -o -name '*.xml' \) -delete
echo "Published $cli_bin (RID $rid)"
