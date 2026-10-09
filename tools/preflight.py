#!/usr/bin/env python3
"""Preflight: lay every sheet over the others and list what would fail.

Checks every row x column cell is filled, every cross-sheet reference resolves,
enum columns hold allowed values, Stellaris modifier keys exist in the game's own
modifier documentation, and scopes match where each sheet uses a modifier.

Cells that can only be confirmed inside the running game (Civ VI type names) are
reported as "pending in-game check": the mod tolerates a missing one, but they
are not ticked until a test run confirms them.

Usage: tools/preflight.py [--stellaris-docs <cwtools-stellaris-config/config/logs>]
Exit code 1 when anything is unfilled, unresolved or invalid.
"""
import json, os, sys, glob, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "sheets")
LOCK = os.path.join(SHEETS, "verified", "stellaris_keys.json")
INGAME = os.path.join(SHEETS, "verified", "ingame.json")

def load():
    out = {}
    for p in sorted(glob.glob(os.path.join(SHEETS, "*.json"))):
        d = json.load(open(p, encoding="utf-8"))
        out[d["sheet"]] = d
    return out

def main(argv):
    docs = None
    if "--stellaris-docs" in argv:
        docs = argv[argv.index("--stellaris-docs") + 1]
    sheets = load()
    errors, pending = [], []
    lock = json.load(open(LOCK)) if os.path.exists(LOCK) else {"modifiers": [], "keywords": []}
    ingame = json.load(open(INGAME)) if os.path.exists(INGAME) else {}

    known_mods = None
    if docs:
        known_mods = set(re.findall(r"^- ([a-z0-9_]+), Category", open(os.path.join(docs, "modifiers.log"), encoding="utf-8", errors="replace").read(), re.M))

    def col(sheet, c):
        return {str(r[c]) for r in sheets[sheet]["rows"]}

    used_mods = set()
    for name, s in sheets.items():
        cols = list(s["columns"])
        for i, row in enumerate(s["rows"]):
            key = f"{name}[{i}:{row.get(cols[0])}]"
            for c in cols:
                v = row.get(c)
                if v is None or (isinstance(v, str) and v.strip() == ""):
                    errors.append(f"unfilled  {key}.{c}")
            extra = set(row) - set(cols)
            if extra:
                errors.append(f"unknown columns {key}: {sorted(extra)}")
            for c, rule in s.get("verify", {}).items():
                v = row.get(c)
                if v is None:
                    continue
                if rule.startswith("ref:"):
                    tsheet, tcol = rule[4:].split(".")
                    if str(v) not in col(tsheet, tcol):
                        errors.append(f"unresolved {key}.{c} -> {tsheet}.{tcol} = {v}")
                elif rule.startswith("refs:"):
                    tsheet, tcol = rule[5:].split(".")
                    if v != "none":
                        for part in str(v).split():
                            if part not in col(tsheet, tcol):
                                errors.append(f"unresolved {key}.{c} -> {tsheet}.{tcol} = {part}")
                elif rule.startswith("enum:"):
                    if str(v) not in rule[5:].split(","):
                        errors.append(f"invalid   {key}.{c} = {v} (allowed {rule[5:]})")
                elif rule == "stellaris-modifier-list":
                    used_mods.add(v)
                    if known_mods is not None:
                        if v not in known_mods:
                            errors.append(f"invalid   {key}.{c} = {v} (not a Stellaris modifier)")
                    elif v not in lock["modifiers"]:
                        errors.append(f"unverified {key}.{c} = {v} (run with --stellaris-docs)")
                elif rule == "civ6-in-game":
                    if v != "DEFAULT" and v not in ingame.get(c, []):
                        pending.append(f"{key}.{c} = {v}")

    # scope checks: where a modifier is used must match its scope
    mscope = {r["id"]: r["scope"] for r in sheets["modifiers"]["rows"]}
    need = {"techs": "country", "natural_wonders": "country", "wonders": "planet"}
    for sname, scope in need.items():
        for r in sheets[sname]["rows"]:
            if mscope.get(r["modifier"]) not in (None, scope):
                errors.append(f"scope     {sname}.{r['modifier']} is {mscope[r['modifier']]}, needs {scope}")
    for r in sheets["space_race"]["rows"]:
        for c in ("rival_lead_modifiers", "player_first_modifiers"):
            for m in str(r[c]).split():
                if m != "none" and mscope.get(m) != "country":
                    errors.append(f"scope     space_race.{r['level']}.{c} {m} must be country")
    # every modifier is used somewhere
    used_ids = set()
    for sname in ("techs", "wonders", "natural_wonders"):
        used_ids |= {r["modifier"] for r in sheets[sname]["rows"]}
    for r in sheets["space_race"]["rows"]:
        used_ids |= set(r["rival_lead_modifiers"].split()) | set(r["player_first_modifiers"].split())
    for m in mscope:
        if m not in used_ids:
            errors.append(f"unused    modifiers.{m} is defined but nothing grants it")
    # handoff records that point at sheets
    for r in sheets["handoff"]["rows"]:
        m = re.search(r"ref:(\w+)\.(\w+)", r["stellaris_use"])
        if m and m.group(1) not in sheets:
            errors.append(f"unresolved handoff.{r['record']} -> sheet {m.group(1)}")
    # Stellaris script keywords the launcher writes, against the game's effect/trigger docs
    kwfile = os.path.join(ROOT, "launcher", "stellaris_keywords.txt")
    if os.path.exists(kwfile):
        words = [w.strip() for w in open(kwfile) if w.strip() and not w.startswith("#")]
        if docs:
            docs_text = open(os.path.join(docs, "trigger_docs.log"), encoding="utf-8", errors="replace").read()
            known = set(re.findall(r"^([a-z_0-9]+) - ", docs_text, re.M))
            for w in words:
                if w not in known:
                    errors.append(f"invalid   launcher keyword '{w}' is not a Stellaris effect/trigger")
        else:
            for w in words:
                if w not in lock.get("keywords", []):
                    errors.append(f"unverified launcher keyword '{w}' (run with --stellaris-docs)")
        if docs and not errors:
            os.makedirs(os.path.dirname(LOCK), exist_ok=True)
            json.dump({"source": "cwtools-stellaris-config logs (Stellaris script_documentation)",
                       "modifiers": sorted(used_mods), "keywords": sorted(words)}, open(LOCK, "w"), indent=1)

    cells = sum(len(s["rows"]) * len(s["columns"]) for s in sheets.values())
    print(f"preflight: {len(sheets)} sheets, {cells} cells")
    for e in errors:
        print("  FAIL " + e)
    if pending:
        print(f"  {len(pending)} cells pending in-game check (Civ VI type names; the mod skips any the game lacks):")
        for p in pending:
            print("    - " + p)
    print("preflight: " + ("CLEAN" if not errors else f"{len(errors)} problem(s)"))
    return 1 if errors else 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
