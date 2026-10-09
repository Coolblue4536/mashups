# MODLOG: Stellar Ascension

## Route (and why)
- **Civ VI (primary, Act 1):** a plain data and Lua mod in `My Games/.../Mods`. Melty installs no Civ VI loader, and none is needed. It uses FrontEnd Parameters for the setup option, `Technologies.Description` for the tooltips, a gameplay script for the space-race order (`Game:SetProperty`) and Ruthless (`Game.SetWinningTeam`), and a UI context to write the handoff block to `Lua.log` and show the popup.
- **Stellaris (companion, Act 2):** a script mod generated at handoff time into `Documents/Paradox Interactive/Stellaris/mod` and enabled through `dlc_load.json`. Melty installs no loader here either. Empires are cloned from the player's own human prescripted empire, so every portrait, room, flag and civic key exists in their version.
- **Bridge:** Civ VI's sandboxed Lua has no file I/O, so the handoff travels through `print()` → `Lua.log`, which the launcher tails. The two games run one after the other, not together.

## Sources used (the container's network blocks the wikis and forums)
- Civ VI API names: sukritact/Civilization-VI-Modding-Knowledge-Base, for `Events.CityProjectCompleted`'s 7 args, Get/SetProperty, `Game.SetWinningTeam` (script context) and `PlayerManager.GetAliveMajorIDs`.
- Stellaris: cwtools/cwtools-stellaris-config, for the `modifiers.log` and `trigger_docs.log` dumps of the game's script_documentation and the schemas for prescripted countries, deposits and static modifiers. `tools/preflight.py --stellaris-docs` checks against them, and `sheets/verified/stellaris_keys.json` is the lock.

## Verified (outside the games)
- Preflight: 491 cells clean. 59 Civ VI type-name cells are pending an in-game check (the Lua skips any missing type).
- `tests/test_civ6.py`: 31 checks of the real generated Lua against a Civ VI API stand-in, in Lua 5.1.
- `launcher` go test: handoff parse, chunked tail, truncated block rejected, Stellaris generation and install, `dlc_load.json` merge, pdx round-trip.
- Melty: `validate_recipe` valid, and `one_click_check` with files returns yes.

## Open, needs the real games (Windows PC with both)
1. Civ VI: the mod shows in Additional Content and is enabled by default (unknown for local mods).
2. The setup dropdown appears; the tooltips show; the popup XML styles exist.
3. `print()` reaches `Lua.log` with the prefix `SA_Handoff: ` (believed on by default on Windows).
4. `Game.SetWinningTeam(team, victoryIndex)`: the argument order is a guess, and it's wrapped in pcall.
5. Launching CivilizationVI.exe directly from Steam; launching stellaris.exe directly reads `dlc_load.json` (the Paradox launcher may override it).
6. Stellaris loads the generated mod: prescripted spawns, `on_game_start` names, wonder deposits, rare systems claimed by outposts.
