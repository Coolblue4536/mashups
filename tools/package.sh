#!/usr/bin/env bash
# Preflight -> generate -> build -> self-test -> self-contained Windows build -> dist/StellarAscension-<version>.zip
# Needs Python 3 and the .NET 8 SDK; the self-test needs Civilization VI and Stellaris installed.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
PY="${PYTHON:-python3}"
"$PY" -c "" 2>/dev/null || PY=python  # Windows: "python3" may be the Store stub
VERSION=$(grep -oP '<Version>\K[^<]+' src/StellarAscension/StellarAscension.csproj)

"$PY" tools/preflight.py ${CIV6:+--civ6 "$CIV6"} ${STELLARIS:+--stellaris "$STELLARIS"}
"$PY" tools/gen.py
dotnet build -c Release src/StellarAscension -v q -nologo
dotnet src/StellarAscension/bin/Release/net8.0/StellarAscension.dll --selftest | tail -1 | grep -q "ALL PASS" \
  || { echo "self-test failed"; exit 1; }

OUT="$ROOT/dist/StellarAscension"
rm -rf "$OUT" "$ROOT/dist/StellarAscension-$VERSION.zip"
dotnet publish src/StellarAscension -c Release -r win-x64 --self-contained true -p:DebugType=None -nologo -v q -o "$OUT"
cp README.md THIRD-PARTY-NOTICES.txt "$OUT/"
(cd "$ROOT/dist" && "$PY" - "$VERSION" <<'PYEOF'
import os, sys, zipfile
v = sys.argv[1]
with zipfile.ZipFile(f"StellarAscension-{v}.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for base, _, files in os.walk("StellarAscension"):
        for f in sorted(files):
            p = os.path.join(base, f)
            z.write(p, os.path.relpath(p, "StellarAscension"))
PYEOF
)
ls -la "$ROOT/dist/StellarAscension-$VERSION.zip"
sha256sum "$ROOT/dist/StellarAscension-$VERSION.zip"
