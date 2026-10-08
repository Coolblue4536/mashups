"""Preflight: lay every sheet over the others and list what is unfinished.

Reports
  - empty cells (null) that are not allowed to be empty,
  - cells waiting on verification (verified / verifiedInGame false),
  - references between sheets that do not resolve,
  - rules the game itself enforces (card pool sizes per rarity).

Exit code 0 only when the build may start (no empty cells, no broken references,
no rule failures). Unverified cells are listed but do not block a build: they are
what the in-game test has to check off.

Usage: python3 tools/preflight.py [sheets_dir]
"""
import json
import pathlib
import sys

# Columns that may be null by design (meaning "none"), per sheet.
NULLABLE = {
    "cards": {"kit", "wardogsItem"},
    "hooks": {"wardogsUse"},
}

# Columns holding references: (sheet, column) -> target sheet.
REFS = {
    ("character", "startingDeck"): "cards",
    ("character", "startingRelic"): "relics",
    ("character", "selectSoundFrom"): "wardogs_content",
    ("character", "art"): "art",
    ("cards", "kit"): "kits",
    ("cards", "wardogsItem"): "wardogs_content",
    ("cards", "soundFrom"): "wardogs_content",
    ("cards", "art"): "art",
    ("kits", "cards"): "cards",
    ("kits", "roleNameFrom"): "wardogs_content",
    ("kits", "art"): "art",
    ("relics", "art"): "art",
}

VERIFY_COLUMNS = ("verified", "verifiedInGame")
MIN_PER_RARITY = 3  # card rewards loop forever when a rarity has fewer unique cards


def load(sheets_dir):
    sheets = {}
    for path in sorted(sheets_dir.glob("*.json")):
        data = json.loads(path.read_text())
        sheets[data["sheet"]] = data
    return sheets


def as_list(value):
    return value if isinstance(value, list) else [value]


def main():
    sheets_dir = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "sheets")
    sheets = load(sheets_dir)
    ids = {name: {row["id"] for row in s["rows"]} for name, s in sheets.items()}
    empty, broken, unverified, rules = [], [], [], []

    for name, sheet in sheets.items():
        for row in sheet["rows"]:
            for col in sheet["columns"]:
                if col not in row:
                    empty.append(f"{name}.{row['id']}.{col} (missing)")
                    continue
                value = row[col]
                if value is None and col not in NULLABLE.get(name, set()):
                    empty.append(f"{name}.{row['id']}.{col}")
                if col in VERIFY_COLUMNS and value is False:
                    unverified.append(f"{name}.{row['id']}")
                target = REFS.get((name, col))
                if target and value is not None:
                    for ref in as_list(value):
                        if ref not in ids.get(target, set()):
                            broken.append(f"{name}.{row['id']}.{col} -> {target}:{ref}")

    economy = {r["id"]: r["value"] for r in sheets["economy"]["rows"]}
    for kit in as_list(economy.get("shop_offers", [])):
        if kit not in ids["kits"]:
            broken.append(f"economy.shop_offers -> kits:{kit}")

    for hook in sheets["hooks"]["rows"]:
        for ref in hook["implements"]:
            sheet_name, _, row_id = ref.partition(":")
            if sheet_name not in sheets:
                broken.append(f"hooks.{hook['id']}.implements -> {ref} (no sheet)")
            elif row_id != "*" and row_id not in ids[sheet_name]:
                broken.append(f"hooks.{hook['id']}.implements -> {ref}")

    # Every card's kit column must agree with the kit's card list.
    for kit in sheets["kits"]["rows"]:
        for card in sheets["cards"]["rows"]:
            listed = card["id"] in kit["cards"]
            tagged = card["kit"] == kit["id"]
            if listed != tagged:
                rules.append(f"cards.{card['id']}.kit disagrees with kits.{kit['id']}.cards")

    for rarity in ("COMMON", "UNCOMMON", "RARE"):
        count = sum(1 for c in sheets["cards"]["rows"] if c["rarity"] == rarity)
        if count < MIN_PER_RARITY:
            rules.append(f"cards: only {count} {rarity} (need {MIN_PER_RARITY}+ for card rewards)")

    def section(title, items):
        print(f"\n{title}: {len(items)}")
        for item in items:
            print(f"  - {item}")

    section("Empty cells", empty)
    section("Broken references", broken)
    section("Rule failures", rules)
    section("Waiting on verification", unverified)
    blocking = len(empty) + len(broken) + len(rules)
    print(f"\nPreflight {'CLEAN' if not blocking else 'NOT CLEAN'}: {blocking} blocking item(s).")
    return 0 if not blocking else 1


if __name__ == "__main__":
    sys.exit(main())
