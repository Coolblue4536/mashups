# Stellar Ascension

A two-act mashup of **Sid Meier's Civilization VI** and **Stellaris**. You build your civilization in Civ VI, race your rivals to space, and then the same empire carries on across a Stellaris galaxy.

> Status: **v0.1.0, built and tested outside the games only.** It has not been played in Civ VI or Stellaris yet. See *Testing* below.

## What you play

**Act 1, in Civilization VI.**
- Play a normal game. Choose **Space Race (Stellar Ascension)** under *Advanced Setup*:
  - **Relaxed:** if a rival reaches space first, you only lose the rare star systems.
  - **Standard:** rivals who reach space before you also start Act 2 with stronger industry and fleets.
  - **Ruthless:** if a rival reaches space first, you lose and there's no Act 2.
- 19 real Civ VI techs, from Astrology to Nuclear Fusion, carry a *Space Age legacy* that their tooltip names. Each one you research becomes a starting bonus in Stellaris.
- Act 1 ends when you complete the **Moon Landing** project. A popup tells you to exit Civ VI.

**Act 2, in Stellaris.**
- Stellaris opens with the *Stellar Ascension* mod enabled. In the empire list, pick your civ (for example, *Roman Empire*).
- **You:** your civ's name and leader. Your homeworld and home system take your capital's name, and the other planets there are named after your largest cities.
- **Your rivals:** every other civ from your Civ VI game becomes a Stellaris AI empire with its own name, leader and cities.
- **Your wonders:** each world wonder you built becomes a feature on your homeworld, with its Civ VI name, description and a bonus.
- **Rare systems:** each natural wonder on your Civ VI map becomes a rare star system with a bonus. Whoever reached space first starts out owning them. The bonus goes to whoever holds the system, so you can take them back.
- **The rest of the galaxy:** Stellaris's own alien empires and primitive civilizations.

The mashup never copies anything from either game. Every name and description comes from your own installed copy of Civ VI as you play, and the Stellaris empires are built from your own copy of Stellaris.

## Requirements

- Sid Meier's Civilization VI (Windows). The base game is enough.
- Stellaris (Windows). The base game is enough.
- Single player.

## Install

Through Melty: press **Play**. Melty installs the Civ VI mod and the launcher, then starts Civ VI through the launcher. The launcher builds and starts Stellaris when Act 1 is over.

By hand:
1. Copy `StellarAscension/` to `Documents/My Games/Sid Meier's Civilization VI/Mods/`.
2. Run:
   ```
   launcher/StellarAscension.exe --civ6 "<Civ VI folder>" --stellaris "<Stellaris folder>" --mygames "<Documents>/My Games" --documents "<Documents>"
   ```

In Civ VI, check that *Stellar Ascension* is enabled under *Additional Content → Mods*. Make sure Stellaris's *AI Empires* setting is at least the number of rivals you're bringing.

## How it works

| Part | What it does |
|---|---|
| `sheets/*.json` | The design, as data: techs, wonders, natural wonders, Stellaris modifiers, space-race levels, handoff records, game hooks. **The source of truth.** |
| `tools/preflight.py` | Checks every sheet cell, every cross-sheet reference, and every Stellaris modifier and script keyword against the game's documentation. |
| `tools/gen_civ6.py` + `civ6/` | Generates the Civ VI mod (setup option, tech tooltips, gameplay and UI Lua) from the sheets. |
| `launcher/` (Go) | Starts Civ VI and reads the handoff block that the mod prints to `Lua.log`. Then it generates the Stellaris mod from the sheets, the handoff and your own Stellaris human template, enables it in `dlc_load.json` and starts Stellaris. |
| `tests/` | Runs the Civ VI scripts against a stand-in Civ VI Lua API (Lua 5.1). The launcher tests use a stand-in Stellaris install. |

Build it with `./build.sh` (needs Python 3 and Go; `pip install lupa` for the Civ VI script tests).

If the launcher missed the handoff, you can rebuild Act 2 from an existing log:
`StellarAscension.exe --from-log "<My Games>/Sid Meier's Civilization VI/Logs/Lua.log" --stellaris ... --documents ...`

The launcher writes `launcher.log` next to itself.

## Testing

Done so far, outside the games:
- Preflight is clean: every cell is filled, every reference resolves, and every Stellaris key is in the game's documentation.
- The Civ VI scripts pass 31 checks against a stand-in API: the handoff block, space-race order, Ruthless defeat and the popups.
- The launcher passes its tests: handoff parsing, Stellaris generation and install, and keeping your other mods enabled in `dlc_load.json`.

Still to confirm in the running games: the Civ VI type names in the sheets, the setup option, the tooltips and popup, `Lua.log` output, Ruthless ending the game, Stellaris loading the generated mod, and the empires, planets, wonders and rare systems at game start.
