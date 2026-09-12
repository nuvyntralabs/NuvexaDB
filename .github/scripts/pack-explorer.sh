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

case "$rid" in
  win-*)
    cp "$bin" "$stage/NuvexaDB Explorer.exe"
    export PATH="$PATH:$HOME/.dotnet/tools"
    if ! command -v wix >/dev/null 2>&1; then
      echo "wix CLI not found. Install with: dotnet tool install -g wix" >&2
      exit 1
    fi
    wix build "$packaging/explorer.wxs" \
      -arch x64 \
      -d "ProductVersion=$version" \
      -d "ExePath=$stage/NuvexaDB Explorer.exe" \
      -o "$outdir/NuvexaDB-Explorer-${version}-${rid}.msi"
    ;;
  osx-*)
    app="$stage/NuvexaDB Explorer.app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp "$bin" "$app/Contents/MacOS/NuvexaDB Explorer"
    chmod +x "$app/Contents/MacOS/NuvexaDB Explorer"
    find "$publish" -maxdepth 1 -type f \
      ! -name 'Nuventra.NuvexaDB.Explorer' ! -name 'Nuventra.NuvexaDB.Explorer.exe' \
      ! -name '*.pdb' ! -name '*.xml' \
      -exec cp {} "$app/Contents/MacOS/" \;
    python3 - "$packaging/Info.plist" "$app/Contents/Info.plist" "$version" <<'PY'
from pathlib import Path
import sys
src, dest, version = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
dest.write_text(src.read_text(encoding="utf-8").replace("__VERSION__", version), encoding="utf-8")
PY
    if command -v codesign >/dev/null 2>&1; then
      codesign --force --deep --sign - "$app" || true
    fi
    if ! command -v pkgbuild >/dev/null 2>&1; then
      echo "pkgbuild not found. Run this step on macOS." >&2
      exit 1
    fi
    payload="$stage/pkgroot"
    mkdir -p "$payload"
    cp -R "$app" "$payload/"
    pkgbuild \
      --root "$payload" \
      --identifier nuventra.nuvexadb.explorer \
      --version "$version" \
      --install-location /Applications \
      "$outdir/NuvexaDB-Explorer-${version}-${rid}.pkg"
    ;;
  linux-*)
    if ! command -v fpm >/dev/null 2>&1; then
      echo "fpm not found. Install ruby + fpm (and rpmbuild for .rpm)." >&2
      exit 1
    fi
    case "$rid" in
      linux-x64) deb_arch=amd64; rpm_arch=x86_64 ;;
      linux-arm64) deb_arch=arm64; rpm_arch=aarch64 ;;
      *)
        echo "Unsupported Linux RID: $rid" >&2
        exit 1
        ;;
    esac
    rootfs="$stage/root"
    mkdir -p "$rootfs/usr/bin" "$rootfs/usr/share/applications" "$rootfs/usr/share/mime/packages"
    cp "$bin" "$rootfs/usr/bin/nuvexa-explorer"
    chmod 0755 "$rootfs/usr/bin/nuvexa-explorer"
    sed 's|^Exec=nuvexa-explorer %f|Exec=/usr/bin/nuvexa-explorer %f|' \
      "$packaging/nuvexa.desktop" > "$rootfs/usr/share/applications/nuvexa.desktop"
    cp "$packaging/nuvexa.xml" "$rootfs/usr/share/mime/packages/nuvexa.xml"
    postinst="$stage/postinst.sh"
    cat > "$postinst" <<'EOF'
#!/bin/sh
set -e
update-mime-database /usr/share/mime >/dev/null 2>&1 || true
update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
exit 0
EOF
    chmod +x "$postinst"
    common=(
      -s dir -n nuvexadb-explorer -v "$version" --iteration 1
      --description "NuvexaDB Explorer — browse encrypted .nvx files"
      --license MIT
      --url https://github.com/nuvyntralabs/NuvexaDB
      --maintainer "Niladri Prasad Padhy"
      --after-install "$postinst"
      -C "$rootfs" usr
    )
    fpm "${common[@]}" -t deb -a "$deb_arch" \
      -p "$outdir/nuvexadb-explorer_${version}_${deb_arch}.deb"
    fpm "${common[@]}" -t rpm -a "$rpm_arch" \
      -p "$outdir/nuvexadb-explorer-${version}-1.${rpm_arch}.rpm"
    ;;
  *)
    echo "Unsupported RID: $rid" >&2
    exit 1
    ;;
esac

echo "Packed Explorer installer for $rid into $outdir"
ls -lh "$outdir"
