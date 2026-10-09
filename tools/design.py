"""The one design sheet: sheets/design.json. Each section is a table (columns + one row per line)."""
import json
from pathlib import Path

PATH = Path(__file__).resolve().parent.parent / "sheets" / "design.json"
ORDER = ["rules", "difficulties", "moon_lengths", "ethics", "categories", "cards", "recipes", "packs", "loot",
         "systems", "crises", "asset_refs", "game_systems"]


def load():
    d = json.loads(PATH.read_text(encoding="utf-8"))
    return d["sections"]


def save(sections, about):
    out = ['{', f'  "about": {json.dumps(about)},', '  "sections": {']
    names = [n for n in ORDER if n in sections] + [n for n in sections if n not in ORDER]
    for si, name in enumerate(names):
        s = sections[name]
        out.append(f'    {json.dumps(name)}: {{')
        out.append(f'      "about": {json.dumps(s["about"])},')
        out.append(f'      "columns": {json.dumps(s["columns"])},')
        out.append('      "rows": [')
        rows = s["rows"]
        for ri, r in enumerate(rows):
            out.append('        ' + json.dumps(r, ensure_ascii=False) + (',' if ri < len(rows) - 1 else ''))
        out.append('      ]')
        out.append('    }' + (',' if si < len(names) - 1 else ''))
    out += ['  }', '}', '']
    PATH.write_text("\n".join(out), encoding="utf-8")
