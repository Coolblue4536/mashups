#!/usr/bin/env python3
"""Preflight: lay every sheet over the others before a build.

Lists every unfilled cell, every cross-sheet reference that does not resolve, recipes that
collide, cards nobody can obtain, and every row still marked unverified.
Exit code 1 when anything blocks a build; --release also blocks on unverified rows.
"""
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import design  # noqa: E402


def main(argv):
    release = "--release" in argv
    errors, unverified, warnings = [], [], []
    sheets = design.load()

    # 1. Every cell filled.
    for name, sh in sheets.items():
        cols = sh["columns"]
        for i, row in enumerate(sh["rows"]):
            rid = row.get("id", row.get("card", f"#{i}"))
            for c in cols:
                if c not in row:
                    errors.append(f"{name}.{rid}.{c}: missing cell")
                elif row[c] is None or (isinstance(row[c], str) and row[c].strip() == ""):
                    errors.append(f"{name}.{rid}.{c}: empty cell")
            for c in row:
                if c not in cols:
                    errors.append(f"{name}.{rid}.{c}: cell not in the sheet's columns")
        key = "id" if "id" in cols else cols[0]
        dup = [k for k, n in Counter(r.get(key) for r in sh["rows"]).items() if n > 1]
        for d in dup:
            errors.append(f"{name}: duplicate {key} {d}")

    cards = {r["id"]: r for r in sheets["cards"]["rows"]}
    cats = {r["id"] for r in sheets["categories"]["rows"]}
    assets = {r["id"]: r for r in sheets["asset_refs"]["rows"]}
    packs = {r["id"] for r in sheets["packs"]["rows"]}
    tags = {t for c in cards.values() for t in c["tags"]}
    techs = {k for k, c in cards.items() if c["category"] == "tech"}

    def card_ref(where, cid):
        if cid not in cards:
            errors.append(f"{where}: card '{cid}' does not exist")

    def asset_ref(where, aid):
        if aid not in assets:
            errors.append(f"{where}: asset_ref '{aid}' does not exist")

    # 2. References.
    for cid, c in cards.items():
        w = f"cards.{cid}"
        if c["category"] not in cats:
            errors.append(f"{w}.category: '{c['category']}' not in categories")
        asset_ref(f"{w}.art", c["art"])
        if c["yield"] != "none":
            card_ref(f"{w}.yield", c["yield"])
            if c["yield_time"] <= 0:
                errors.append(f"{w}.yield_time: must be > 0 when yield is set")
        if c["colonize_with"] != "none":
            card_ref(f"{w}.colonize_with", c["colonize_with"])
        if c["category"] in ("enemy", "boss") and c["hp"] <= 0:
            errors.append(f"{w}.hp: hostile cards need hp")
    for r in sheets["categories"]["rows"]:
        asset_ref(f"categories.{r['id']}.frame_ref", r["frame_ref"])

    obtainable = Counter()
    station_kinds = set()
    sigs = {}
    for r in sheets["recipes"]["rows"]:
        w = f"recipes.{r['id']}"
        st = r["station"]
        if st.startswith("tag:"):
            if st[4:] not in tags:
                errors.append(f"{w}.station: no card has tag '{st[4:]}'")
        elif st == "has:yield":
            pass
        else:
            card_ref(f"{w}.station", st)
        for inp in r["inputs"]:
            if inp["card"].startswith("tag:"):
                if inp["card"][4:] not in tags:
                    errors.append(f"{w}.inputs: no card has tag '{inp['card'][4:]}'")
            else:
                card_ref(f"{w}.inputs", inp["card"])
            if inp["n"] < 1:
                errors.append(f"{w}.inputs: n must be >= 1")
        if r["requires_tech"] != "none" and r["requires_tech"] not in techs:
            errors.append(f"{w}.requires_tech: '{r['requires_tech']}' is not a tech card")
        if r["requires_flag"] not in ("none", "claimed", "unclaimed"):
            errors.append(f"{w}.requires_flag: unknown flag '{r['requires_flag']}'")
        if r["time"] == -1 and not any(g["card"] == "station.yield" for o in r["outputs"] for g in o["give"]):
            errors.append(f"{w}.time: -1 (use station yield_time) only valid when an output is station.yield")
        if r["time"] == -1 and st.startswith("tag:"):
            for c in cards.values():
                if c["yield"] == "none" and st[4:] in c["tags"]:
                    errors.append(f"{w}: station {st} includes card '{c['id']}' which has no yield")
        for o in r["outputs"]:
            for g in o["give"]:
                if g["card"] == "station.yield":
                    for c in cards.values():
                        if c["yield"] != "none":
                            obtainable[c["yield"]] += 1
                else:
                    card_ref(f"{w}.outputs", g["card"])
                    obtainable[g["card"]] += 1
        eff = r["effect"]
        if not (eff == "none" or eff in ("open_board:random", "open_board:guardian", "set_flag:claimed")):
            errors.append(f"{w}.effect: unknown effect '{eff}'")
        sig = (st, tuple(sorted((i["card"], i["n"]) for i in r["inputs"])))
        if sig in sigs:
            errors.append(f"{w}: same stack as recipes.{sigs[sig]}")
        sigs[sig] = r["id"]

    for r in sheets["loot"]["rows"]:
        card_ref(f"loot.{r['card']}", r["card"])
        for d in r["drops"]:
            card_ref(f"loot.{r['card']}.drops", d["card"])
            obtainable[d["card"]] += 1
    looted = {r["card"] for r in sheets["loot"]["rows"]}
    for cid, c in cards.items():
        if c["category"] in ("enemy", "boss") and cid not in looted:
            errors.append(f"loot: hostile card '{cid}' has no loot row")

    for r in sheets["packs"]["rows"]:
        asset_ref(f"packs.{r['id']}.art", r["art"])
        for e in r["contents"]:
            card_ref(f"packs.{r['id']}.contents", e["card"])
            obtainable[e["card"]] += 1
        if r["unlock_act"] not in (1, 2, 3):
            errors.append(f"packs.{r['id']}.unlock_act: must be 1-3")

    for r in sheets["ethics"]["rows"]:
        w = f"ethics.{r['id']}"
        card_ref(f"{w}.worker_card", r["worker_card"])
        obtainable[r["worker_card"]] += 1
        asset_ref(f"{w}.art", r["art"])
        for b in r["bonus_cards"]:
            card_ref(f"{w}.bonus_cards", b["card"])
            obtainable[b["card"]] += 1
        for t in r["start_techs"]:
            if t not in techs:
                errors.append(f"{w}.start_techs: '{t}' is not a tech card")
        if r["free_pack"] not in packs:
            errors.append(f"{w}.free_pack: '{r['free_pack']}' is not a pack")

    kinds = Counter()
    for r in sheets["systems"]["rows"]:
        w = f"systems.{r['id']}"
        kinds[r["kind"]] += 1
        if r["kind"] not in ("home", "random", "guardian"):
            errors.append(f"{w}.kind: must be home, random or guardian")
        for c in r["fixed_cards"]:
            card_ref(f"{w}.fixed_cards", c["card"])
            obtainable[c["card"]] += 1
        for c in r["star_cards"]:
            card_ref(f"{w}.star_cards", c)
            if c in cards and cards[c]["category"] != "star":
                errors.append(f"{w}.star_cards: '{c}' is not a star card")
            obtainable[c] += 1
        for c in r["planet_pool"]:
            card_ref(f"{w}.planet_pool", c["card"])
            if c["card"] in cards and cards[c["card"]]["category"] != "planet":
                errors.append(f"{w}.planet_pool: '{c['card']}' is not a planet")
            obtainable[c["card"]] += 1
        for c in r["extras"]:
            card_ref(f"{w}.extras", c["card"])
            if not 0 < c["chance"] <= 1:
                errors.append(f"{w}.extras: chance for '{c['card']}' must be in (0, 1]")
            obtainable[c["card"]] += 1
        if r["kind"] == "random":
            if r["weight"] <= 0:
                errors.append(f"{w}.weight: random systems need weight > 0")
            if not 0 <= r["planets_min"] <= r["planets_max"]:
                errors.append(f"{w}: need 0 <= planets_min <= planets_max")
            if r["planets_max"] > 0 and not r["planet_pool"]:
                errors.append(f"{w}.planet_pool: empty but planets_max > 0")
    if kinds["home"] != 1:
        errors.append("systems: need exactly one home board")
    if kinds["random"] == 0:
        errors.append("systems: need at least one random system type")

    for r in sheets["crises"]["rows"]:
        for col in ("rift_card", "minion_card", "boss_card"):
            card_ref(f"crises.{r['id']}.{col}", r[col])
            obtainable[r[col]] += 1

    diffs = {r["id"]: r for r in sheets["difficulties"]["rows"]}
    for d in diffs.values():
        w = f"difficulties.{d['id']}"
        for b in d["bonus_cards"]:
            card_ref(f"{w}.bonus_cards", b["card"])
        if not (1 < d["act2_moon"] < d["crisis_moon"]):
            errors.append(f"{w}: need 1 < act2_moon < crisis_moon")
        for col in ("enemy_hp_mult", "enemy_attack_mult", "rift_spawn_mult", "pack_cost_mult", "raid_every_moons", "boss_delay_moons"):
            if d[col] <= 0:
                errors.append(f"{w}.{col}: must be > 0")
    moons = {r["id"]: r for r in sheets["moon_lengths"]["rows"]}
    for m in moons.values():
        if m["seconds"] < 20:
            errors.append(f"moon_lengths.{m['id']}.seconds: too short")

    rules = {r["id"]: r["value"] for r in sheets["rules"]["rows"]}
    if rules.get("default_difficulty") not in diffs:
        errors.append("rules.default_difficulty: not a difficulties row")
    if rules.get("default_moon_length") not in moons:
        errors.append("rules.default_moon_length: not a moon_lengths row")
    if len(rules.get("system_names", [])) < rules.get("max_systems", 0):
        errors.append("rules.system_names: need at least max_systems names")
    for need in ("start_cards", "start_workers", "enemy_aggro_seconds", "max_stack", "default_difficulty", "default_moon_length",
                 "system_names", "max_systems"):
        if need not in rules:
            errors.append(f"rules: missing rule '{need}'")
    for c in rules.get("start_cards", []):
        card_ref("rules.start_cards", c["card"])
        obtainable[c["card"]] += 1
    obtainable["guardian_signal"] += 1  # act 2 event (rules.act2_moon)
    obtainable["pirate_raider"] += 1    # raids (difficulties.raid_every_moons)

    for cid in cards:
        if obtainable[cid] == 0:
            errors.append(f"cards.{cid}: nothing in the game can produce this card")

    for cid, c in cards.items():
        if "workplace" in c["tags"] and c["yield"] == "none":
            errors.append(f"cards.{cid}: tagged workplace but has no yield")
        if c["yield"] != "none" and not ({"workplace", "star"} & set(c["tags"])):
            errors.append(f"cards.{cid}: has a yield but is neither a workplace nor a star")

    # 3. Verification checkboxes.
    for name in ("asset_refs", "game_systems"):
        for r in sheets[name]["rows"]:
            if not r["verified"]:
                unverified.append(f"{name}.{r['id']}")

    total_cells = sum(len(sh["rows"]) * len(sh["columns"]) for sh in sheets.values())
    print(f"Preflight: sheets/design.json, {len(sheets)} sections, {sum(len(s['rows']) for s in sheets.values())} rows, {total_cells} cells")
    for e in errors:
        print("  ERROR      " + e)
    for w in warnings:
        print("  WARN       " + w)
    print(f"  {len(unverified)} rows unverified (need a probe on a real install): "
          + ", ".join(unverified[:6]) + (" …" if len(unverified) > 6 else ""))
    if errors:
        print(f"BLOCKED: {len(errors)} errors")
        return 1
    if release and unverified:
        print("BLOCKED for release: unverified rows remain")
        return 1
    print("CLEAN" + (" (release)" if release else " (build)"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
