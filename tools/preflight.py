#!/usr/bin/env python3
"""Preflight: lay every section of sheets/design.json over the others before a build.

Checks every cell is filled, ids are unique, every cross-reference resolves, and (when the game
folders are given) every Civ VI type and icon and every Stellaris picture and text key exists in the
player's own installs.

Usage: tools/preflight.py [--civ6 <Civ VI folder>] [--stellaris <Stellaris folder>]
Exit code 1 when anything is wrong.
"""
import glob
import os
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import design  # noqa: E402

STATS = {"energy_mult", "minerals_mult", "food_mult", "alloys_mult", "research_mult", "unity_mult",
         "energy_add", "minerals_add", "food_add", "alloys_add", "research_add", "unity_add", "influence_add",
         "naval_cap_add", "ship_hull_mult", "ship_damage_mult", "ship_speed_mult", "ship_build_speed_mult",
         "build_speed_mult", "survey_speed_mult", "pop_growth_mult", "starbase_power_mult"}


def main(argv):
    civ6 = argv[argv.index("--civ6") + 1] if "--civ6" in argv else None
    st = argv[argv.index("--stellaris") + 1] if "--stellaris" in argv else None
    S = design.load()
    errors = []

    for name, sh in S.items():
        cols = sh["columns"]
        for i, row in enumerate(sh["rows"]):
            rid = row.get(cols[0], f"#{i}")
            for c in cols:
                if c not in row:
                    errors.append(f"{name}.{rid}.{c}: missing cell")
                elif row[c] is None or (isinstance(row[c], str) and not row[c].strip()):
                    errors.append(f"{name}.{rid}.{c}: empty cell")
            for c in row:
                if c not in cols:
                    errors.append(f"{name}.{rid}.{c}: not a column")
        for k, n in Counter(r[cols[0]] for r in sh["rows"]).items():
            if n > 1:
                errors.append(f"{name}: duplicate {cols[0]} {k}")

    ids = lambda s: {r[S[s]["columns"][0]] for r in S[s]["rows"]}
    mods, assets, a2techs = ids("modifiers"), ids("asset_refs"), ids("act2_techs")
    buildings, res = ids("buildings"), ids("resources")

    def ref(where, val, pool, allow_none=True):
        if allow_none and val == "none":
            return
        if val not in pool:
            errors.append(f"{where}: '{val}' does not resolve")

    for r in S["modifiers"]["rows"]:
        if r["stat"] not in STATS:
            errors.append(f"modifiers.{r['id']}.stat: unknown stat {r['stat']}")
    for s in ("natural_wonders", "wonders"):
        for r in S[s]["rows"]:
            ref(f"{s}.{r['id']}.modifier", r["modifier"], mods, False)
    for r in S["tech_legacy"]["rows"]:
        ref(f"tech_legacy.{r['civ_tech']}.modifier", r["modifier"], mods, False)
    for r in S["space_race"]["rows"]:
        for c in ("rival_lead_modifiers", "player_first_modifiers"):
            for m in r[c]:
                ref(f"space_race.{r['id']}.{c}", m, mods, False)
    for r in S["act2_techs"]["rows"]:
        ref(f"act2_techs.{r['id']}.requires", r["requires"], a2techs)
        ref(f"act2_techs.{r['id']}.modifier", r["modifier"], mods)
        ref(f"act2_techs.{r['id']}.unlocks", r["unlocks"], ids("ships") | ids("planet_buildings"))
        ref(f"act2_techs.{r['id']}.art", r["art"], assets, False)
    for r in S["ships"]["rows"]:
        ref(f"ships.{r['id']}.requires_tech", r["requires_tech"], a2techs)
        ref(f"ships.{r['id']}.art", r["art"], assets, False)
    for r in S["planet_buildings"]["rows"]:
        ref(f"planet_buildings.{r['id']}.requires_tech", r["requires_tech"], a2techs)
        ref(f"planet_buildings.{r['id']}.resource", r["resource"], res, False)
        ref(f"planet_buildings.{r['id']}.art", r["art"], assets, False)
    for r in S["districts"]["rows"]:
        ref(f"districts.{r['id']}.resource", r["resource"], res, False)
        ref(f"districts.{r['id']}.art", r["art"], assets, False)
    for s in ("resources", "star_classes"):
        for r in S[s]["rows"]:
            ref(f"{s}.{r['id']}.art", r["art"], assets, False)
    for r in S["buildings"]["rows"]:
        ref(f"buildings.{r['id']}.requires_building", r["requires_building"], buildings)
    for r in S["projects"]["rows"]:
        ref(f"projects.{r['id']}.requires_building", r["requires_building"], buildings, False)
    for r in S["wonders"]["rows"]:
        if r["scope"] not in ("city", "empire"):
            errors.append(f"wonders.{r['id']}.scope: city or empire")
    if not any(r["kind"] == "moon" for r in S["projects"]["rows"]):
        errors.append("projects: no Moon Landing (kind moon)")
    terrains = ids("terrains")
    for r in S["features"]["rows"]:
        for t in r["terrains"]:
            ref(f"features.{r['id']}.terrains", t, terrains, False)

    if civ6:
        xml = "\n".join(Path(p).read_text(encoding="utf-8", errors="ignore") for p in
                        glob.glob(os.path.join(civ6, "Base", "Assets", "**", "*.xml"), recursive=True) +
                        glob.glob(os.path.join(civ6, "DLC", "*", "Data", "*.xml")))
        def civ(where, v):
            if v != "none" and v != "DEFAULT" and f'"{v}"' not in xml and f">{v}<" not in xml:
                errors.append(f"{where}: {v} is not in the installed Civ VI")
        base_techs = set(re.findall(r'TechnologyType="(TECH_[A-Z_]+)"', Path(civ6, "Base", "Assets", "Gameplay", "Data", "Technologies.xml").read_text(encoding="utf-8")))
        for s, cols in (("buildings", ["id", "requires_tech"]), ("wonders", ["id", "requires_tech"]), ("projects", ["id", "requires_tech", "requires_building"]),
                        ("terrains", ["id", "icon"]), ("features", ["id", "icon"]), ("natural_wonders", ["id"]), ("difficulties", ["id"]),
                        ("eras", ["id"]), ("yields", ["icon"]), ("tech_legacy", ["civ_tech"])):
            for r in S[s]["rows"]:
                for c in cols:
                    civ(f"{s}.{r[cols[0]]}.{c}", r[c])
        for s, c in (("buildings", "requires_tech"), ("wonders", "requires_tech"), ("projects", "requires_tech"), ("tech_legacy", "civ_tech")):
            for r in S[s]["rows"]:
                if r[c] != "none" and r[c] not in base_techs:
                    errors.append(f"{s}.{r[S[s]['columns'][0]]}.{c}: {r[c]} is not a base-game tech")
    if civ6:
        # Every Civ VI map picture the sheet names must be in the installed packages ({n}: any variant 1-6, {v}: Visible).
        names = set()
        for f in glob.glob(os.path.join(civ6, "**", "Platforms", "Windows", "BLPs", "**", "*.blp"), recursive=True):
            b = os.path.basename(f).lower()
            if "strategicview" not in f.replace(chr(92), "/").lower() and not b.startswith(("terrainmaterial", "fow")):
                continue
            data = Path(f).read_bytes()[:0x400000]
            names |= set(m.decode() for m in re.findall(rb"([A-Za-z][A-Za-z0-9_]{3,80})\x00", data))
        def has(pattern):
            if "{n}" in pattern:
                return any(pattern.replace("{n}", str(i)) in names for i in range(1, 7))
            return pattern.replace("{v}", "Visible") in names
        for sec, cols in (("terrains", ["ground", "ground_fow", "overlay", "overlay_fow"]), ("features", ["sprite", "sprite_fow"]), ("natural_wonders", ["sprite"]),
                          ("buildings", ["sprite"]), ("wonders", ["sprite"]), ("eras", ["city_sprite"]), ("improvements", ["sprite"])):
            for r in S[sec]["rows"]:
                for c in cols:
                    if r[c] != "none" and not has(r[c]):
                        errors.append(f"{sec}.{r[S[sec]['columns'][0]]}.{c}: Civ VI picture {r[c]} not found")
    if st:
        files = {p.replace("\\", "/").lower()[len(st) + 1:] for p in glob.glob(os.path.join(st, "gfx", "**", "*.*"), recursive=True)}
        loc = set()
        for f in glob.glob(os.path.join(st, "localisation", "english", "**", "*.yml"), recursive=True):
            loc |= set(re.findall(r"^\s*([A-Za-z0-9_.\-]+):", Path(f).read_text(encoding="utf-8-sig", errors="ignore"), re.M))
        for r in S["asset_refs"]["rows"]:
            if r["game"] != "stellaris" or r["kind"] != "image":
                continue
            ok = False
            for l in r["lookup"]:
                kind, _, arg = l.partition(":")
                arg = arg.split("@")[0].lower()
                if kind == "file" and (arg in files or os.path.splitext(arg)[0] + ".dds" in files):
                    ok = True
                elif kind == "find" and any(arg in f for f in files):
                    ok = True
                elif kind in ("sprite", "planetclass"):
                    ok = True  # resolved by the game's own .gfx / planet_classes at run time
            if not ok:
                errors.append(f"asset_refs.{r['id']}: no lookup resolves in the installed Stellaris")
        for s in ("resources", "districts", "planet_buildings", "ships", "act2_techs", "star_classes", "planet_classes"):
            for r in S[s]["rows"]:
                key = r.get("name_loc", r["id"])
                if key not in loc:
                    errors.append(f"{s}.{r['id']}: Stellaris text key {key} not found")

    cells = sum(len(s["rows"]) * len(s["columns"]) for s in S.values())
    print(f"preflight: {len(S)} sections, {cells} cells" + (" (checked against Civ VI)" if civ6 else "") + (" (checked against Stellaris)" if st else ""))
    for e in errors:
        print("  FAIL " + e)
    print("preflight: " + ("CLEAN" if not errors else f"{len(errors)} problem(s)"))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
