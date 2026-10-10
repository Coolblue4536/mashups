# Stellar Ascension

A two-act mashup of **Sid Meier's Civilization VI** and **Stellaris**. You build your civilization in Civ VI, race your rivals to space, and then the same empire carries on across a Stellaris galaxy.

> Status: **v0.1.0, partly tested in the real games.** Both mods load cleanly in Civ VI and Stellaris 4.5.2, but a full Civ VI game to the Moon Landing and a Stellaris game start have not been played yet. See *Testing* below.

## What you play

**Act 1, in Civilization VI.**
- Play a normal game. Choose **Space Race (Stellar Ascension)** under *Advanced Setup*:
  - **Relaxed:** if a rival reaches space first, they start Act 2 owning the rare star systems. Nothing else changes.
  - **Standard:** rivals who reach space before you also start Act 2 with +15% alloys and +10 naval capacity.
  - **Ruthless:** if a rival reaches space first, you lose and there's no Act 2.
- 19 Civ VI techs, from Astrology to Nuclear Fusion, carry a *Space Age legacy*. Their description names it (after the tech's own description, if it has one). Each one you research becomes a starting bonus in Stellaris.
- Act 1 ends when you complete the **Moon Landing** project. A popup tells you to exit Civ VI.

**Act 2, in Stellaris.**
- Stellaris opens with the *Stellar Ascension* mod enabled. Start a new game and pick your civ in the empire list (for example, *Roman Empire*).
- **You:** your civ's name and leader. Your homeworld and home system take your capital's name, and the other planets there are named after your largest cities.
- **Your rivals:** every other civ from your Civ VI game becomes a Stellaris AI empire with its own name, leader and cities.
- **Your wonders:** each world wonder you built becomes a feature on your homeworld, with its Civ VI name and a bonus.
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

Civ VI enables a newly installed local mod by default; you can check under *Additional Content → Mods*. Make sure Stellaris's *AI Empires* setting is at least the number of rivals you're bringing.

Names show in Stellaris exactly as Civ VI wrote them, in the language Civ VI ran in. Play both games in the same language: Stellaris's English fonts may not have every character (for example Japanese or Korean).

## How it works

| Part | What it does |
|---|---|
| `sheets/*.json` | The design, as data: techs, wonders, natural wonders, Stellaris modifiers, space-race levels, handoff records, game hooks. **The source of truth.** |
| `tools/preflight.py` | Checks every sheet cell, every cross-sheet reference, and every Stellaris modifier and script keyword against the game's documentation. |
| `tools/gen_civ6.py` + `civ6/` | Generates the Civ VI mod (setup option, tech tooltips, gameplay and UI Lua) from the sheets. |
| `launcher/` (Go) | Starts Civ VI and reads the handoff block that the mod prints to `Lua.log`. Then it generates the Stellaris mod from the sheets, the handoff, your own Stellaris human template and your own custom-empire home systems, enables it in `dlc_load.json` (keeping a backup) and starts Stellaris. |
| `tests/` | Runs the Civ VI scripts against a stand-in Civ VI Lua API (Lua 5.1). The launcher tests use a stand-in Stellaris install. |

Build it with `./build.sh` (needs Python 3 and Go; `pip install lupa` for the Civ VI script tests; works in Git Bash on Windows). With both games installed, `tools/preflight.py --stellaris-docs "<Documents>/Paradox Interactive/Stellaris/logs/script_documentation" --civ6 "<Civ VI folder>"` checks the sheets against your own copies (Stellaris writes `script_documentation` each time it starts).

Civ VI writes `Lua.log` to `%LOCALAPPDATA%\Firaxis Games\Sid Meier's Civilization VI\Logs` (older builds: `Documents\My Games\Sid Meier's Civilization VI\Logs`); the launcher watches both. If it missed the handoff, you can rebuild Act 2 from an existing log:
`StellarAscension.exe --from-log "%LOCALAPPDATA%\Firaxis Games\Sid Meier's Civilization VI\Logs\Lua.log" --stellaris ... --documents ...`

The launcher writes `launcher.log` next to itself.

## Testing

Done in the real games (Windows, Civ VI with all DLC, Stellaris Cygnus v4.5.2). The full report is in `MODLOG.md`.
- Preflight is clean against the installed games: every Stellaris modifier and script keyword is in Stellaris 4.5.2's own documentation, and all 59 Civ VI type names are in the installed Civ VI data.
- Civ VI loads the mod with no database errors. It is enabled by default, and the *Space Race* setting is in Advanced Setup with its three levels.
- Stellaris loads the mod generated from the test handoff with an empty `error.log`. Stellaris checks every event, modifier, deposit and home system when it loads.
- 10 simulated handoffs (Relaxed, Standard and Ruthless; player first, middle and last to space; 2 to 11 rivals; 0 to 28 wonders; 0 to 12 natural wonders; long, odd and non-English names) each build a Stellaris mod through the launcher that passes the same checks. Loading each one in Stellaris is still to do.
- Outside the games: the Civ VI scripts pass 31 checks against a stand-in API, and the launcher passes its tests.

Still to confirm by playing: a Civ VI game to the Moon Landing (the popup, `Lua.log` output and Ruthless ending the game), and a Stellaris game start (empires, planet names, wonders, rare systems and bonuses on screen).
