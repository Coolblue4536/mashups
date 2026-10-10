# MODLOG: Stellar Ascension

## Route (and why)
- **Civ VI (primary, Act 1):** a plain data and Lua mod in `My Games/.../Mods`. Melty installs no Civ VI loader, and none is needed. It uses FrontEnd Parameters for the setup option, `Technologies.Description` for the tooltips, a gameplay script for the space-race order (`Game:SetProperty`) and Ruthless (`Game.SetWinningTeam`), and a UI context to write the handoff block to `Lua.log` and show the popup.
- **Stellaris (companion, Act 2):** a script mod generated at handoff time into `Documents/Paradox Interactive/Stellaris/mod` and enabled through `dlc_load.json`. Melty installs no loader here either. Empires are cloned from the player's own human prescripted empire, so every portrait, room, flag and civic key exists in their version. Each civ's home system is cloned from the player's own custom-empire home systems and carries a `sa_home_<slot>` system flag, which is how the game-start scripts find each civ.
- **Bridge:** Civ VI's sandboxed Lua has no file I/O, so the handoff travels through `print()` → `Lua.log`, which the launcher tails. The two games run one after the other, not together.

## Sources used
- Civ VI API names: sukritact/Civilization-VI-Modding-Knowledge-Base, for `Events.CityProjectCompleted`'s 7 args, Get/SetProperty, `Game.SetWinningTeam` (script context) and `PlayerManager.GetAliveMajorIDs`. Table columns (`Leaders.Sex`, `Technologies.Description`, `Buildings.IsWonder`, `Features.NaturalWonder`) checked in the game's own `DebugGameplay.sqlite`.
- Stellaris: the game's own `logs/script_documentation` (written at every startup; Cygnus v4.5.2), plus the vanilla `prescripted_countries`, `solar_system_initializers` and `deposits` files for the formats. `tools/preflight.py --stellaris-docs` checks against it, and `sheets/verified/stellaris_keys.json` is the lock.

## Verified
- Preflight: 491 cells clean against Stellaris 4.5.2's documentation and the installed Civ VI data. All 59 Civ VI type names are confirmed (`sheets/verified/ingame.json`).
- `tests/test_civ6.py`: 31 checks of the real generated Lua against a Civ VI API stand-in, in Lua 5.1.
- `launcher` go test: handoff parse, chunked tail, truncated block rejected, Lua.log locations and restarts, Stellaris generation (home systems, no prescripted flags, localisation in every language, ruler gender) and install, `dlc_load.json` merge, pdx round-trip.
- In the games: see the test report below.

## Test report, 2026-10-11 (Windows 11 PC; Civ VI on Steam with all DLC; Stellaris Cygnus v4.5.2)

Both games had never been started on this PC, so there were no saves or settings to change. After Stellaris first created its user folder, `settings.txt`, `pdx_settings.txt` and `dlc_load.json` were copied to `backups/stellaris-20261011-0315/` (outside the repo). The launcher also keeps `dlc_load.json.stellar_ascension.bak` next to `dlc_load.json` every time it installs.

### What was tested
1. **Toolchain.** Installed Go 1.27.0 (winget) and `lupa` 2.8 (pip). `build.sh` now builds the release zip in Git Bash on Windows (it falls back to Python when `zip` is missing).
2. **Preflight against the real games.** Every Stellaris modifier and script keyword is in 4.5.2's own script documentation. All 59 Civ VI type names are in the installed game data.
3. **Civ VI load.** The mod was installed into `Documents/My Games/Sid Meier's Civilization VI/Mods` and Civ VI was started by running `CivilizationVI.exe` directly. Steam took over the launch and started the DX12 build. `Modding.log` shows the mod discovered and in the target (enabled) set by default. `Config/SA_Config.sql` and the text loaded, and the Space Race parameter with its 3 levels is in the real configuration database under Advanced Options. `Database.log` has no errors from the mod (only Civ VI's own promo "clickout" text duplicates).
4. **Stellaris load.** The launcher's `--from-log` mode built and installed the Stellaris mod from the test handoff, then `stellaris.exe` was started directly. It used `dlc_load.json` and loaded the mod. With the fixes below, `error.log` has no lines from the mod. A deliberately planted bad trigger was reported, which shows that Stellaris validates the generated events when it loads, not only at game start.
5. **10 simulated handoffs through the launcher** (the exact lines `SA_Handoff.lua` prints). Each was built and installed by the launcher into a scratch Documents folder and checked: one empire and home system per civ, the player not auto-spawned and rivals always spawned, ruler gender, flags set from the home system, every modifier, deposit and localisation key defined, localisation identical in all 10 Stellaris languages and free of `[ ] $ § £ "`, script files ASCII-only, rare systems capped at 8 and deduplicated by name, the right space-race bonuses per level, and `dlc_load.json` merged with a backup. All 10 pass.

| # | Run | Result |
|---|---|---|
| 1 | Relaxed; player 1st; 2 rivals; 1 wonder; 1 natural wonder | pass |
| 2 | Relaxed; player last (5th); 6 rivals; no wonders or natural wonders | pass |
| 3 | Standard; player 3rd of 5; 7 rivals; 10 wonders incl. 2 not in the sheet; 5 natural wonders | pass |
| 4 | Standard; player 1st; 11 rivals (max); all 28 sheet wonders; all 12 natural wonders (8 kept) | pass |
| 5 | Ruthless; player 1st; 3 rivals; 3 wonders; 2 natural wonders | pass |
| 6 | Ruthless; a rival first (defeat block) | pass: no Act 2 built, `dlc_load.json` untouched |
| 7 | Standard; Japanese, Russian, Korean, Greek and accented names; player middle | pass (in-game rendering not seen) |
| 8 | Relaxed; very long names, quotes, apostrophes, `$`, `[ ]`, `§`; player last | pass |
| 9 | Standard; player 2nd; the same natural wonder twice, 2 not in the sheet; a duplicate wonder | pass |
| 10 | Ruthless; player 1st; female leader with 1 city; a rival with no cities; no techs; 6 wonders | pass |

### What broke, and the fixes
1. **Stellaris rejected every empire's `flags = { ... }` block** (`Unexpected token: flags` in `prescripted_countries`). Prescripted countries can't carry country flags, so none of the game-start logic (your bonuses, wonders, names, the space-race bonuses, rare systems) could find any civ. Fix: each civ gets its own home system, cloned from the player's own `usage = custom_empire` initializers (single-star ones first), with a `sa_home_<slot>` system flag. `stellar_ascension.1` sets the country flags from that, and the rare-system event finds the first civ by its home system, so it no longer depends on which on_action runs first.
2. **The launcher watched the wrong `Lua.log`.** Current Civ VI writes its logs, options and `Mods.sqlite` under `%LOCALAPPDATA%\Firaxis Games\Sid Meier's Civilization VI`, not `Documents/My Games`. The launcher would never have seen the handoff. Fix: it watches both locations.
3. **The launcher gave up on Civ VI too early.** Starting `CivilizationVI.exe` hands off to Steam, and the first launch ran Steam's install scripts for about 2 minutes before the game process appeared. The launcher waited only 90 s. It now waits 5 minutes.
4. **The tech tooltip SQL replaced Civ VI's own description** where a tech has one (Rocketry's quarry bonus). Fix: `Text/SA_TechText.sql` builds the description at load time from the player's own text plus the legacy line, in every language their game has.
5. **Wonder features showed Civ VI build rules and markup** in Stellaris (for example "Must be built on the Coast…" and `[ICON_TradeRoute]`). `[ ]` is Stellaris scripted localisation, and `$`, `§` and `£` are variable, colour and icon codes. Fix: the wonder text is now a plain sentence with its name and bonus, and every localisation value is cleaned of markup and control characters.
6. **Non-English names were written as raw strings in event scripts** (`set_name = "Kyōto"`). Fix: every name goes through localisation keys.
7. **Localisation existed only in English**, so a Stellaris running in another language would show raw keys. Fix: the same text is written for all 10 Stellaris languages. Civ VI text rows are likewise written for all 12 Civ VI languages.
8. **The wonder icon fell back to `d_active_volcano`** (none of the preferred icons exist in 4.5). It now uses `d_monument`.
9. **Every leader got the template's female portrait.** The handoff now carries the leader's sex (`Leaders.Sex`), and male leaders get a male ruler with a portrait the game picks.
10. **A rival with no cities had its empire name as its homeworld name.** It is now "<Adjective> Prime".
11. **Clearer text:** space-race level descriptions now say what each level does in numbers, and the popups and setup tooltip are reworded. The rivals' bonus is renamed "Ahead in the Space Race" (several rivals can get it, so "Won the Race to Space" was wrong).
12. **The same natural wonder could make two rare systems** (multi-feature wonders), and a repeated wonder could duplicate a deposit. Both are now deduplicated.

### Still open
1. **Real game starts not done.** Desktop control (screenshots and clicks) was declined when requested, so nothing has been checked on screen yet: no Stellaris game start, no Civ VI game to the Moon Landing, and no listing screenshots. Without clicks, neither game can start a game.
2. **Stellaris will not start at the moment.** After the session's forced restarts, one Civ VI process and two Stellaris processes are stuck inside Windows (one thread each, immune to termination), and new Stellaris instances hang before writing any log. A reboot should clear them. Until then, loading each of the 10 simulated mods in Stellaris is not done (only the test-handoff mod was loaded).
3. **Unchanged from before:** the `Game.SetWinningTeam(team, victoryIndex)` argument order (Ruthless) is still a guess (wrapped in pcall); the popup's XML styles; whether `print()` from the mod reaches `Lua.log` (Civ VI's own `Lua.log` is written, so this is likely); whether 11 always-spawned rivals fit small galaxies; and how Japanese or Korean names render with Stellaris's English fonts.
