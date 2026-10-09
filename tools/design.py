"""The one design sheet: sheets/design.json. Each section is a table (columns + one row per line)."""
import json
from pathlib import Path

PATH = Path(__file__).resolve().parent.parent / "sheets" / "design.json"
ORDER = ["rules", "difficulties", "moon_lengths", "ethics", "categories", "cards", "components", "recipes", "packs", "loot",
         "systems", "crises", "tutorial", "asset_refs", "game_systems"]


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


def expand(sections):
    """Each components row also stands for: its card, its tech card, a research recipe, a Shipyard recipe and two icon links.
    Returns a copy of the sections with those added to cards, recipes and asset_refs (the code sees them like any other row)."""
    import copy
    s = copy.deepcopy(sections)
    values = {c["id"]: c["value"] for c in s["cards"]["rows"]}
    blank = {"hp": 0, "attack": 0, "attack_cd": 0, "shield": 0, "armor": 0, "shield_regen": 0, "hull_regen": 0, "evasion": 0, "slots": 0,
             "weapon": "none", "food_upkeep": 0, "energy_upkeep": 0, "boost_tag": "none", "boost_mult": 1.0, "yield": "none",
             "yield_time": 0, "colonize_with": "none"}
    for c in s.get("components", {}).get("rows", []):
        cid, tech = c["id"], "tech_" + c["id"]
        cost = " + ".join(f"{b['n']} {b['card'].replace('_', ' ').title()}" for b in c["build_cost"])
        s["cards"]["rows"].append(dict(blank, id=cid, name_loc=c["name_loc"], name=c["name"], category="component",
                                      tags=["component", c["kind"]], art="st_comp_" + cid,
                                      value=max(1, sum(values.get(b["card"], 1) * b["n"] for b in c["build_cost"]) // 2), desc=c["desc"]))
        s["cards"]["rows"].append(dict(blank, id=tech, name_loc=c["tech_name_loc"], name=c["tech_name"], category="tech",
                                      tags=["tech"], art="st_tech_" + cid, value=3, desc=f"Shipyard + {cost} = {c['name']}."))
        n = sum(i["n"] for i in c["research_inputs"] if i["card"] == "research")
        studied = [i["card"] for i in c["research_inputs"] if i["keep"]]
        s["recipes"]["rows"].append({"id": "r_" + cid, "station": "scientist", "station_keep": True, "inputs": c["research_inputs"],
            "requires_flag": "none", "requires_system": "any", "requires_tech": c["requires_tech"], "time": 10 + 2 * n, "tag": "research",
            "outputs": [{"weight": 1, "give": [{"card": tech, "n": 1}]}], "effect": "none",
            "desc": f"Scientist + {n} Research" + "".join(f" + {i['n']} {i['card'].replace('_', ' ').title()}" for i in c["research_inputs"] if i["card"] != "research")
                    + (f" (studies the {', '.join(x.replace('_', ' ').title() for x in studied)})" if studied else "") + f" = {c['tech_name']}"})
        s["recipes"]["rows"].append({"id": "s_" + cid, "station": "shipyard", "station_keep": True,
            "inputs": [{"card": b["card"], "n": b["n"], "keep": False} for b in c["build_cost"]],
            "requires_flag": "none", "requires_system": "any", "requires_tech": tech, "time": 12, "tag": "build",
            "outputs": [{"weight": 1, "give": [{"card": cid, "n": 1}]}], "effect": "none", "desc": f"Shipyard + {cost} = {c['name']}"})
        for aid, look, used in (("st_comp_" + cid, c["icon"], c["name"] + " card"), ("st_tech_" + cid, c["tech_icon"], c["tech_name"] + " tech card")):
            s["asset_refs"]["rows"].append({"id": aid, "game": "stellaris", "kind": "image", "lookup": look, "used_by": used, "verified": False,
                                            "note": "Resolved at run time from the player's Stellaris install (from the components section)."})
    return s
