#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${1:?Usage: pack-explorer.sh <version> <rid> <output-dir>}"
rid="${2:?Usage: pack-explorer.sh <version> <rid> <output-dir>}"
outdir="${3:?Usage: pack-explorer.sh <version> <rid> <output-dir>}"
proj="$root/src/Nuventra.NuvexaDB.Explorer/Nuventra.NuvexaDB.Explorer.csproj"
packaging="$root/src/Nuventra.NuvexaDB.Explorer/packaging"
publish="$root/artifacts/explorer-publish-$rid"
stage="$root/artifacts/explorer-stage-$rid"

rm -rf "$publish" "$stage"
mkdir -p "$publish" "$stage" "$outdir"

dotnet publish "$proj" -c Release -r "$rid" --self-contained true -o "$publish" --nologo \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=embedded \
  -p:CopyOutputSymbolsToPublishDirectory=false

bin=""
if [[ -f "$publish/Nuventra.NuvexaDB.Explorer.exe" ]]; then
  bin="$publish/Nuventra.NuvexaDB.Explorer.exe"
elif [[ -f "$publish/Nuventra.NuvexaDB.Explorer" ]]; then
  bin="$publish/Nuventra.NuvexaDB.Explorer"
else
  echo "Published Explorer binary not found in $publish" >&2
  ls -la "$publish" >&2
  exit 1
fi

write_readme() {
  cat > "$1" <<'EOF'
NuvexaDB Explorer
=================

This archive is a ready-to-run desktop app (not a folder of libraries).
EOF
}

case "$rid" in
  win-*)
    cp "$bin" "$stage/NuvexaDB Explorer.exe"
    cp "$packaging/install-windows.ps1" "$stage/Install.ps1"
    write_readme "$stage/README.txt"
    cat >> "$stage/README.txt" <<'EOF'

1. Double-click "NuvexaDB Explorer.exe" to start.
2. Optional: right-click Install.ps1 → Run with PowerShell
   to add a Start Menu shortcut and open .nvx files with Explorer.
EOF
    python3 - "$stage" "$outdir/NuvexaDB-Explorer-${version}-${rid}.zip" <<'PY'
import sys, zipfile
from pathlib import Path
src, dest = Path(sys.argv[1]), Path(sys.argv[2])
dest.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
    for path in sorted(src.iterdir()):
        if path.is_file():
            zf.write(path, path.name)
print(dest)
PY
    ;;
  osx-*)
    app="$stage/NuvexaDB Explorer.app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp "$bin" "$app/Contents/MacOS/NuvexaDB Explorer"
    chmod +x "$app/Contents/MacOS/NuvexaDB Explorer"
    # Keep any leftover native libs next to the host (Avalonia).
    find "$publish" -maxdepth 1 -type f \
      ! -name 'Nuventra.NuvexaDB.Explorer' ! -name 'Nuventra.NuvexaDB.Explorer.exe' \
      ! -name '*.pdb' ! -name '*.xml' \
      -exec cp {} "$app/Contents/MacOS/" \;
    python3 - "$packaging/Info.plist" "$app/Contents/Info.plist" "$version" <<'PY'
from pathlib import Path
import sys
src, dest, version = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
text = src.read_text(encoding="utf-8")
text = text.replace("__VERSION__", version)
dest.write_text(text, encoding="utf-8")
PY
    write_readme "$stage/README.txt"
    cat >> "$stage/README.txt" <<'EOF'

Unzip, then double-click "NuvexaDB Explorer.app" (or drag it to /Applications).
Optional: ./packaging/install-macos.sh "/path/to/NuvexaDB Explorer.app"
EOF
    if command -v codesign >/dev/null 2>&1; then
      codesign --force --deep --sign - "$app" || true
    fi
    (
      cd "$stage"
      if command -v ditto >/dev/null 2>&1; then
        ditto -c -k --keepParent "NuvexaDB Explorer.app" "$outdir/NuvexaDB-Explorer-${version}-${rid}.zip"
      else
        python3 - "$outdir/NuvexaDB-Explorer-${version}-${rid}.zip" <<'PY'
import sys, zipfile
from pathlib import Path
dest = Path(sys.argv[1])
app = Path("NuvexaDB Explorer.app")
with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
    for path in app.rglob("*"):
        zf.write(path, path.as_posix())
PY
      fi
    )
    ;;
  linux-*)
    cp "$bin" "$stage/nuvexa-explorer"
    chmod +x "$stage/nuvexa-explorer"
    cp "$packaging/install-linux.sh" "$packaging/nuvexa.desktop" "$packaging/nuvexa.xml" "$stage/"
    write_readme "$stage/README.txt"
    cat >> "$stage/README.txt" <<'EOF'

chmod +x nuvexa-explorer
./nuvexa-explorer

Optional install (Start menu + .nvx association):
  ./install-linux.sh
EOF
    tar -C "$stage" -czf "$outdir/NuvexaDB-Explorer-${version}-${rid}.tar.gz" .
    ;;
  *)
    echo "Unsupported RID: $rid" >&2
    exit 1
    ;;
esac

echo "Packed Explorer for $rid into $outdir"
