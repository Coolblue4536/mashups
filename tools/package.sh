#!/usr/bin/env bash
# Preflight -> generate -> self-test -> Windows build -> dist/GrandGalactic-<version>.zip
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VERSION=$(grep -oP '<Version>\K[^<]+' src/GrandGalactic/GrandGalactic.csproj)

python3 tools/gen.py
dotnet build -c Release src/GrandGalactic -v q -nologo
dotnet src/GrandGalactic/bin/Release/net8.0/GrandGalactic.dll --selftest | tail -1 | grep -q "ALL PASS" \
  || { echo "self-test failed"; exit 1; }

OUT="$ROOT/dist/GrandGalactic"
rm -rf "$OUT" "$ROOT/dist/GrandGalactic-$VERSION.zip"
dotnet publish src/GrandGalactic -c Release -r win-x64 --self-contained true -p:DebugType=None -nologo -v q -o "$OUT"
cp README.md THIRD-PARTY-NOTICES.txt "$OUT/"
(cd "$ROOT/dist" && python3 - "$VERSION" <<'EOF'
import os, sys, zipfile
v = sys.argv[1]
with zipfile.ZipFile(f"GrandGalactic-{v}.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for base, _, files in os.walk("GrandGalactic"):
        for f in sorted(files):
            p = os.path.join(base, f)
            z.write(p, os.path.relpath(p, "GrandGalactic"))
EOF
)
ls -la "$ROOT/dist/GrandGalactic-$VERSION.zip"
sha256sum "$ROOT/dist/GrandGalactic-$VERSION.zip"
