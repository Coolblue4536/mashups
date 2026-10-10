#!/usr/bin/env bash
# Build the Stellar Ascension release: preflight -> Civ VI mod -> tests -> launcher -> zip.
set -euo pipefail
cd "$(dirname "$0")"
VERSION="${VERSION:-0.1.0}"
PY="${PYTHON:-python3}"

"$PY" tools/preflight.py ${STELLARIS_DOCS:+--stellaris-docs "$STELLARIS_DOCS"}
"$PY" tools/gen_civ6.py
if "$PY" -c "import lupa" 2>/dev/null; then "$PY" tests/test_civ6.py; else echo "skip civ6 script tests (pip install lupa)"; fi

rm -rf launcher/sheets && mkdir -p launcher/sheets && cp sheets/*.json launcher/sheets/
(cd launcher && go vet ./... && go test ./... && GOOS=windows GOARCH=amd64 go build -trimpath -ldflags "-s -w" -o ../build/launcher/StellarAscension.exe .)
cp README.md build/launcher/README.md
cp LICENSE.txt build/launcher/LICENSE.txt 2>/dev/null || true

mkdir -p dist
ZIP="dist/StellarAscension-$VERSION.zip"
rm -f "$ZIP"
if command -v zip >/dev/null; then
  (cd build && cp -r civ6/StellarAscension . && zip -qr -X "../$ZIP" StellarAscension launcher -x launcher/launcher.log && rm -rf StellarAscension)
else # Git Bash on Windows has no zip
  "$PY" - "$ZIP" <<'EOF'
import os, sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w", zipfile.ZIP_DEFLATED) as z:
    for src, arc in (("build/civ6/StellarAscension", "StellarAscension"), ("build/launcher", "launcher")):
        for d, _, fs in os.walk(src):
            for f in sorted(fs):
                if f == "launcher.log":
                    continue
                p = os.path.join(d, f)
                z.write(p, os.path.join(arc, os.path.relpath(p, src)).replace(os.sep, "/"))
EOF
fi
echo "built $ZIP ($(stat -c %s "$ZIP") bytes)"
unzip -l "$ZIP"
