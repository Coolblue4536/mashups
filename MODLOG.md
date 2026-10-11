# MODLOG: Stellar Ascension

## Route (and why)
- **Standalone game (v0.2.0).** Stellar Ascension is one Windows program (C# / .NET 8 with raylib), built the way *Grand Galactic* was built. It never starts or changes Civilization VI or Stellaris. It reads their art, names, descriptions and music from the player's installs each time it runs, and it ships none of their files.
- **Why it changed.** v0.1 was a pair of mods, a Civ VI mod for Act 1 and a generated Stellaris mod for Act 2, joined by a launcher that ran one game and then the other. The creator asked for a single application like their Stellaris + Stacklands mashup, following the README design: Act 1 should look like Civ VI, and Act 2 should look like Stellaris but play in turns. The mod version, its launcher and its tests were removed in this branch.
- **Design as data.** `sheets/design.json` is the source of truth. `tools/gen.py` generates `Defs.g.cs` from it, and `tools/preflight.py` checks every Civ VI id, art name and Stellaris key against the real installs.

## How the games' files are read
- **Civ VI.** Its `.blp` art packages are decoded directly: texture entries, BC1 to BC4 and RGBA textures, and the tile atlases used for icons and leader pictures. Its XML gameplay data and text databases supply the techs, civilizations, leaders, buildings, wonders and names. Fonts come from the install too.
- **Stellaris.** `.dds` pictures, localisation, planet classes, star names, prescripted empires, loading screens and music.

## v0.3.0, 2026-10-11: galaxy options, Act 1 units and war, resources and the tile card, a Stellaris-style galaxy view

### What was asked
- Options for the galaxy's size, for the other AI empires, and for their overall difficulty.
- A cleaner, more appealing galaxy view, with zoom in and out.
- Act 1 units that progress from the stone age to the space age and get stronger over time.
- A Civ VI-style card when hovering a tile, showing what it provides.

### What was built
1. **Galaxy options on the New Game screen.** It now has an Earth section (Act 1) and a galaxy section (Act 2).
   - **Galaxy size:** Tiny to Huge, 9 to 24 systems per empire.
   - **Alien empires:** 0 to 8.
   - **AI empires:** Cadet, Ensign, Captain, Commodore, Admiral or Grand Admiral. This multiplies every AI empire's output (×0.65 to ×1.65) and their war-readiness.
   - Act 1 difficulty now applies to Act 1 only.
   - The sheet has two new tables, `galaxy_sizes` and `galaxy_difficulties`. The unused `difficulties.ai_act2_mult` and `map_sizes.act2_systems` columns were removed.
2. **Act 1 units** (`units` table, `Sim/Military.cs`, `Sim/Act1AiMilitary.cs`).
   - **Roster:** 28 of Civ VI's base land units in 7 lines, from Warrior and Slinger to Mechanized Infantry, Modern Armor and Rocket Artillery.
   - **Read from your Civ VI:** strength, ranged strength and range, moves, cost, upkeep, prerequisite tech, strategic resource and upgrade path (`Units`, `UnitUpgrades`). The sheet keeps the base game's numbers as fallbacks.
   - **Rules:**
     - One military unit per tile.
     - Terrain movement costs: hills and woods cost +1. Mountains and natural wonders can't be entered.
     - Units embark after Shipbuilding, and can cross ocean after Cartography.
     - Borders are closed in peacetime.
     - Moves can span several turns.
     - Combat uses Civ VI's damage curve, 30·e^(0.04·Δstrength). Hills, woods, fortifying and anti-cavalry give their bonuses, and ranged units take Civ VI's penalty against cities.
     - Cities have strength and 200 HP, heal, and fire on nearby enemies.
     - A melee unit captures a city at 0 HP. A civ that loses its last city is destroyed.
   - **Progression:** obsolete units can't be trained; units upgrade for gold inside your borders; units gain XP and up to 3 veteran levels (+4 strength each).
   - **Upkeep:** units cost gold each turn, and units are disbanded if the gold runs out.
   - **AI:**
     - It keeps a garrison in every city and trains a balanced army sized to its cities and wars.
     - It upgrades units when it has the gold.
     - Now and then it declares war on a weaker neighbour, more often on harder difficulties.
     - In war it marches on the nearest enemy city, attacks only at good odds, and makes peace when the war stops paying.
   - **Diplomacy:** the player declares war or makes peace in the Space Race window.
   - **Starting units:** everyone starts with a Warrior.
   - **On screen:**
     - Units show as Civ VI unit flags (`ICON_UNIT_*_WHITE` in the owner's colours), with health and veteran pips.
     - Selecting a unit shows its reach in blue and its targets in red.
     - Hovering a far tile previews the path, and hovering a target previews the attack.
     - A unit panel shows the Civ VI portrait, with Fortify, Skip, Upgrade and Disband buttons. **F** fortifies and **Space** skips.
     - The end-turn button prompts "A unit needs orders", with a "Skip all units" option.
3. **Resources.**
   - Civ VI's bonus, luxury and strategic resources are placed by Civ VI's own frequencies and valid terrains and features. Their yields come from `Resource_YieldChanges`; culture and faith count as gold.
   - Strategic resources stay hidden until their reveal tech.
   - Every start gets food, Horses, Iron and Niter in reach.
   - Strategic resources gate units, as in Civ VI: Swordsmen need Iron in your borders.
4. **The tile card.** Hovering any tile shows the terrain and feature, the resource with its Civ VI icon and class, and which units need it.
   - Yields appear as Civ VI's yield icons with numbers. The card also shows the movement cost and defence bonus, the owner, and any unit or city there with its strength and HP.
   - With a unit selected, it shows the attack forecast.
   - The hovered tile shows its yield icons on the map. **Y** or the Yields button turns them on for every tile you can see.
5. **The galaxy view, rebuilt in Stellaris's layout.**
   - **Top bar:** resource pills with income, the fleet cap, and the date and turn.
   - **Left rail:** Stellaris's own navbar icons (`navbar_icons_*.dds`): Research (with a badge when idle), Shipyard, Empires (with a badge at war), Outliner, Event log, and Next idle fleet.
   - **Outliner:** your planets and fleets with their status. Click one to jump there.
   - **Panels:** the system panel has a planet drill-down with a back button, and the fleet panel sits below it.
   - **Hover card:** star, owner, planet pictures, habitable count and fleets.
   - **Zoom:** smooth zoom toward the cursor (0.18× to 3.2×), the **+ / −** keys, and buttons for zoom in, zoom out, All and Cap, with a zoom bar.
   - **Zoomed out:** names are hidden except for claimed systems, and each empire's name sits across its territory, sized to fit and never overlapping.
   - **Map detail:** hyperlanes have a soft glow. Markers under each star show your colonies and free habitable worlds. Fleet chevrons show ship counts and red rings at war, and the selected fleet's route is dashed.
   - **Event log:** the always-on log was replaced by a log window (**L**); news pop-ups remain.

### Bugs found and fixed while testing
1. **Civ VI resource icons didn't load.** The atlas is listed at 256, 64, 50 and 38 px, the reader kept only the biggest, and the 256 px one isn't in the packages. Icons now try each listed size, biggest first. A new `--icon` command reports whether icons decode.
2. **The starting Warrior had no moves on turn 1.**
3. **Iron couldn't always be placed near a start.** Iron needs hills, so a start with no hills nearby got none. Balanced placement now falls back to any free land tile in reach.
4. **Conquest victory came too early.** In a tiny two-empire galaxy, owning 60% of the few claimed systems won by 2202. Conquest now also needs half of every system in the galaxy, after the first 10 years.
5. **The top-bar Yields button ran into the date at 1600 px wide.** The research box now shrinks to fit.
6. **The yields lens was cluttered,** covering every revealed ocean tile. It now covers tiles you can see.
7. **The tile card hinted at hidden resources** ("research X to reveal it"). That line was removed, as in Civ VI.
8. **The self-test's Ruthless check broke.** Its deliberately weak player could now be conquered before the race ended, so that test now turns AI wars off.

### Tested
- **Self-test: ALL PASS.** New checks:
  - The unit roster, unit flag, portrait and resource icons.
  - Starting Warriors, resources on the map, and Horses and Iron near every start.
  - **Unit rules:** reach never includes impassable tiles, foreign cities block movement, no attacks at peace, war allows them, and a melee unit takes a city at 0 HP and moves in.
  - **Upgrades:** Warrior → Swordsman needs Iron Working plus Iron and costs gold, and obsolete units can't be trained.
  - **Peace:** units are sent out of the other side's borders.
  - **Galaxy options:** 5 aliens, a huge galaxy, and Grand Admiral applied to every AI empire.
- **Matrix (10 games, each with a different galaxy size, alien count and AI level): ALL PASS.**
  - Units reach Mechanized Infantry, Modern Armor or Helicopters by the space age, and 0 to 4 cities change hands per game.
  - A deliberately weak player (and the player at Deity) was conquered in 3 of 10 games, which ends Act 1. The Ruthless defeat rules held.
- **Act 1 balance (4 games):** space at turns 167 to 236, with 2 wars at a time in the middle eras and units growing from 10 to about 55 across the map.
- **UI scripts: all pass.** `tests/ui/both-acts.txt` (31 steps), `units.txt` (22), `galaxy.txt` (23) and `listing-shots.txt` (24). The new script commands are `unit`, `hover`, `hoversys`, `lens`, `zoom` and `galaxy`, with a pretend mouse so the real cursor is never moved.
- **Screenshots:** new 1080p listing shots in `docs/screenshots/`: setup, units with the tile card, the yields lens, a city, the handoff, the home system with its hover card, and the galaxy.

### Still open
- **No naval units.** Land units embark instead. There are also no barbarians or city-states.
- **No human playthrough yet** with mouse and keyboard. Everything was driven by script and checked in screenshots.
- **AI war balance needs play-testing.** At Prince and above, AIs will attack a player who builds no army, as Civ VI's AIs do. On Settler and Chieftain they rarely start wars.

## Test report (v0.2.0), 2026-10-11 (Windows 11 PC; Civ VI on Steam with all DLC; Stellaris 4.5.2)

### Safety and the player's games
- **Backups.** Before anything was changed, the old mod build's files were backed up to `backups/cleanup-20261011-1459/` and `backups/stellaris-20261011-0315/` in the folder above the repo. That covers `dlc_load.json`, `settings.txt`, `pdx_settings.txt` and the installed mods.
- **Removed from the games.** The old v0.1 test mods were taken out of Civ VI's `Mods` folder and Stellaris's `mod` folder, and `dlc_load.json` was restored to `{"disabled_dlcs":[],"enabled_mods":[]}`. Both games are back as they were.
- **Game windows.** Earlier in the session, testing the mod version opened and closed Civ VI and Stellaris several times, and a leftover test loop kept relaunching Stellaris. The creator asked why; the loop was stopped and the games have not been opened since.
- **No desktop control.** Desktop control of the Stellar Ascension window was declined, so no clicks were made on screen. Instead, the game drives its own interface (`--uiscript`, below).
- **Nothing published, nothing bought.**

### What was tested
1. **Self-test (`--selftest`): ALL PASS.**
   - Readers: 18+ civilizations, the 68-tech tree, icons, a leader picture, strategic-view sprites, every terrain and wonder sprite, Stellaris star names, empires, music and every Stellaris picture in the sheet.
   - Act 1: setup, natural wonders, fog of war, districts on their own tiles, save and load, a game to space, and the calendar reaching the space age.
   - Handoff: every civ becomes an empire plus aliens, one home each, a connected galaxy, no crossing hyperlanes, identical arms, the homeworld named after the capital, wonders as homeworld features, natural wonders as rare systems owned by the first civ in space, and legacy bonuses.
   - Act 2: save and load, the game ending, every living empire holding something, and **no resource running down at the start** (new).
   - Ruthless: a rival first ends the game.
2. **Simulation matrix (`--matrix`), 10 games: ALL PASS.** Each game is played through both acts by the AI.

| # | Setup | Act 1 | Act 2 |
|---|---|---|---|
| 1 | Relaxed, small, 1 rival, Prince, short | player 1st to space, turn 122 (1982 AD), 14 wonders, 4 natural wonders | won on score, 2250 |
| 2 | Relaxed, large, **10 rivals**, King, standard | player **last** (11th of 11), turn 319 (2150 AD, the year limit), 29 wonders, 8 natural wonders | lost on score, 2275 |
| 3 | Relaxed, standard, 5 rivals, Settler, long, **long Greek, Russian, Japanese and Icelandic names** | player 1st, turn 241 | won, 2300; the long names carry to the homeworld and the other home planets |
| 4 | Standard, small, 3 rivals, Prince, standard | player 3rd of 3 (middle) | lost on score, 2275 |
| 5 | Standard, large, 10 rivals, Prince, standard, long non-English names | player 1st, turn 152 | won on score, 2275 |
| 6 | Standard, standard, 5 rivals, Emperor, short | player last (6th of 6), 2150 AD | player conquered in 2233 |
| 7 | Standard, standard, 7 rivals, Deity, long | player last (8th of 8) | lost on score, 2300 |
| 8 | Ruthless, small, 2 rivals, Prince, standard | player 1st, turn 136 | won on score, 2275 |
| 9 | Ruthless, standard, 5 rivals, Prince, standard | a rival first: **defeat, no Act 2** (correct) | n/a |
| 10 | Ruthless, large, 7 rivals, Chieftain, long names | a rival first: defeat (correct) | n/a |

   Each game checks the civ count, that every name in both acts can be drawn by the fonts, that Ruthless defeat happens exactly when a rival is first, every civ becoming an empire, one home per empire, a connected galaxy, the renamed capital reaching the homeworld, and Act 2 ending.
3. **Balance (`--fullbalance 6`).**
   - Act 1 reaches space at about turns 190 to 280.
   - In Act 2 every empire's income stays positive, technologies run out around 2245 to 2255 (not by 2240), 2 to 4 wars are fought at once, and the player wins some games and loses others.
   - Every home has the same number of habitable worlds within 3 jumps, except the first-to-space civ, whose nearby worlds became its rare systems.
4. **The screens, driven by script (`--uiscript`).**
   - The game presses its own buttons by their labels, clicks map tiles and systems, plays turns with the End Turn button and takes screenshots. Each press goes through the same code as a mouse click.
   - `tests/ui/both-acts.txt` covers 31 steps across both acts: New Game, Start, picking research, 25 Act 1 turns including Settler placement, the Wonders and Space Race windows, the end of Act 1, the handoff, Begin Act 2, the Research, Shipyard and Empires windows, and 20 Act 2 turns.
   - **31 passed, 0 failed.**
5. **Listing screenshots.** These are real frames from the game window, taken by `tests/ui/listing-shots.txt` and stored in `docs/screenshots/`: the Act 1 map and city panel, the Wonders and Space Race windows, the handoff, the Act 2 home system, the galaxy, and the shipyard.
6. **Packaging.** `tools/package.sh` builds `dist/StellarAscension-0.2.0.zip` (34 MB, self-contained), and the packaged exe passes `--selftest`.

### What broke, and the fixes
1. **Act 2 economy (serious).** Pops left identical jobs unworked: a set operation (`Except`) treated two identical jobs as one, so a 14-pop homeworld filled only 9 jobs. Every empire started at −1.9 energy a month and ran dry by about turn 11. Fixed: income at the start is now +17 energy and research doubled, and the self-test now checks that no resource runs down at the start.
2. **Act 2 research too quick once fixed.** All 17 techs were done by about 2240. Tech costs went up 1.6×.
3. **AI stockpiles.** The AI sat on thousands of minerals. It now builds industry with a surplus, keeps up to two colony ships when food is plentiful, and cuts down a fleet it can't pay for.
4. **Crash with many empires (serious).** A large map with 10 rivals plus aliens crashed at the start of Act 2 because no colour could be found far enough from the others. Fixed: it now picks the most distinct colour left.
5. **Act 1 could run past 2200 AD**, after Act 2's 2200 start (2232 AD on turn 401 with a slow player). Act 1 now also ends in 2150 (`act1_end_year`), when the civs go to space in order of their science.
6. **"Hạ Long Bay" drew as "H? Long Bay".** The fonts have no Vietnamese letters, and the Vietnamese natural wonder becomes a rare system. Letters the fonts lack now lose their accent ("Ha Long Bay"), and the matrix checks every name.
7. **News pop-ups covered open windows**, and the event log showed through them. Both now stay hidden while a window is open (the log keeps the news).
8. **Hard-to-read empire names.** Dark empire colours (deep blue, purple) were nearly invisible as text on the dark panels. Names are now lightened just enough to read.
9. **Small text fixes.**
   - Gold shows as a whole number ("757", not "757.1"), and so do Act 2 stockpiles.
   - The shipyard's colony-ship line no longer runs under its Build button.
10. **`tools/package.sh` failed on Windows**, where `python3` is the Microsoft Store stub. It now falls back to `python`.

### Still open
1. **No human playthrough in the window.** Every screen has been driven by script and checked in screenshots. Nobody has played a full game with mouse and keyboard, and sound (Stellaris music) was not listened to.
2. **The scripted test covers the main path only.** Buying with gold, placing districts by hand, the player declaring war and making peace, and saving and loading from the menu work in the headless tests but weren't pressed on screen.
3. **Balance.**
   - At equal yields the player, played by the AI, sometimes still loses Ruthless at Chieftain (matrix run 10). A human will likely do better, but this needs play-testing.
   - The AI player keeps a large energy stockpile in Act 2.
4. **Fonts.** Names in scripts the fonts don't cover at all (CJK, Thai and so on) would show as "?". Civ VI's English text doesn't use them.
5. **Not a Melty mod.** Melty's catalog takes mods of existing games, so this standalone build is not set up for Melty and was not published anywhere.

## History: v0.1 (mod version, retired)
- A Civ VI mod (setup option, tech tooltips, gameplay and UI Lua) wrote a handoff block to `Lua.log`.
- A Go launcher read that block, generated a Stellaris mod, enabled it in `dlc_load.json` and started Stellaris.
- It loaded cleanly in both games and passed 10 simulated handoffs.
- Fixes made then: home systems flagged instead of prescripted country flags, the Lua.log location, Civ VI start-up timing, tech text that kept the game's own descriptions, markup cleaned from Stellaris localisation, names passed through localisation keys, all 10 Stellaris languages, leader sex, and deduplicated wonders.
- It was replaced by the standalone game at the creator's request; see the git history for details.
