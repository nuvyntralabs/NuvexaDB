#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${1:?Usage: pack-vsix.sh <version> <output.vsix>}"
output="${2:?Usage: pack-vsix.sh <version> <output.vsix>}"
proj="$root/src/Nuventra.NuvexaDB.VisualStudio/Nuventra.NuvexaDB.VisualStudio.csproj"
publish="$root/artifacts/vsix-publish"

dotnet publish "$proj" -c Release -f net8.0-windows -o "$publish" --nologo

python3 - "$root" "$publish" "$output" <<'PY'
import sys
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape

root = Path(sys.argv[1])
publish = Path(sys.argv[2])
output = Path(sys.argv[3])
manifest_src = root / "src/Nuventra.NuvexaDB.VisualStudio/source.extension.vsixmanifest"
pkgdef = root / "src/Nuventra.NuvexaDB.VisualStudio/NuvexaDB.pkgdef"

extensions = {
    "vsixmanifest": "text/xml",
    "pkgdef": "text/plain",
    "dll": "application/octet-stream",
    "pdb": "application/octet-stream",
    "json": "application/json",
    "xml": "text/xml",
    "config": "text/xml",
    "md": "text/plain",
}
content_types = ['<?xml version="1.0" encoding="utf-8"?>']
content_types.append('<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">')
for ext, ctype in extensions.items():
    content_types.append(f'  <Default Extension="{escape(ext)}" ContentType="{escape(ctype)}" />')
content_types.append("</Types>")

output.parent.mkdir(parents=True, exist_ok=True)
if output.exists():
    output.unlink()

with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    zf.writestr("extension.vsixmanifest", manifest_src.read_text(encoding="utf-8"))
    zf.writestr("[Content_Types].xml", "\n".join(content_types) + "\n")
    zf.write(pkgdef, "NuvexaDB.pkgdef")
    for path in sorted(publish.rglob("*")):
        if not path.is_file():
            continue
        if path.suffix.lower() in {".nupkg", ".snupkg"}:
            continue
        zf.write(path, path.name)

print(f"Packed {output}")
PY
