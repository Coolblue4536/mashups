# MODLOG: Stellar Ascension

## Route (and why)
- **Standalone game (v0.2.0).** Stellar Ascension is one Windows program (C# / .NET 8 with raylib), built the way *Grand Galactic* was built. It never starts or changes Civilization VI or Stellaris. It reads their art, names, descriptions and music from the player's installs each time it runs, and it ships none of their files.
- **Why it changed.** v0.1 was a pair of mods, a Civ VI mod for Act 1 and a generated Stellaris mod for Act 2, joined by a launcher that ran one game and then the other. The creator asked for a single application like their Stellaris + Stacklands mashup, following the README design: Act 1 should look like Civ VI, and Act 2 should look like Stellaris but play in turns. The mod version, its launcher and its tests were removed in this branch.
- **Design as data.** `sheets/design.json` is the source of truth. `tools/gen.py` generates `Defs.g.cs` from it, and `tools/preflight.py` checks every Civ VI id, art name and Stellaris key against the real installs.

## How the games' files are read
- **Civ VI.** Its `.blp` art packages are decoded directly: texture entries, BC1 to BC4 and RGBA textures, and the tile atlases used for icons and leader pictures. Its XML gameplay data and text databases supply the techs, civilizations, leaders, buildings, wonders and names. Fonts come from the install too.
- **Stellaris.** `.dds` pictures, localisation, planet classes, star names, prescripted empires, loading screens and music.

## v0.5.0, 2026-10-11: Stellaris's own ship models, and exotic sites drawn as part of the land

### What was asked
- **Ships:** combat ships that look like Stellaris's.
- **Exotic sites:**
  - Not a picture stuck on a tile.
  - Since you must hold the tiles around one, the site should spread onto them to make that clear.
  - Proper tile graphics drawn for the game (for example gas smoking above an Exotic Gas Swamp), clean and good-looking.

### What was built
1. **Stellaris ship models, rendered by the game.**
   - **Reading the models:** a reader for Stellaris's binary `.mesh` format (`PdxMesh`): positions, normals, tangents, UVs, triangles, materials and locators. Collision meshes and glow or trail meshes are left out.
   - **Assembling a ship:** a corvette is one model. A destroyer is a bow and a stern. A cruiser or battleship is a bow, the longest mid section of its set, and a stern, joined at the frame's `part2` locators. Any gap left between sections is closed.
   - **Drawing it:** a shader lights the hull, follows the normal map, and paints the empire's colour where Stellaris's texture marks it. A soft tone curve keeps white hulls readable.
   - **The sprite:** each ship is drawn once, seen from the side and a little above, then reused as a picture.
   - **Which style:** each Stellaris empire's own `graphical_culture` (now read from `prescripted_countries`). Earth civilizations get one of seven common styles. Psionic and mindwarden hulls come out near-black without Stellaris's effects, so empires with those styles use a common one.
   - **Where they show:** the battle window (bigger now, big ships at the back, small ones screening, fire passing ship to ship), the system view, the shipyard, the fleet panel and the outliner. Map markers stay Stellaris's icons, as in Stellaris.
   - **New tool:** `--ships <folder>` renders every style's ships to PNGs.
2. **Exotic sites as part of the land.** The deposit picture in a frame is gone.
   - **The ground:** each kind has its own ground, painted by the game from noise when it starts (no image files), with soft ragged edges:
     - liquid metal reflecting the sky, inside a rim of solid metal,
     - a murky bog,
     - rock split by glowing crystal veins,
     - a black pool with violet bands,
     - grey nanite sand,
     - sand over worn paving,
     - purple spore loam,
     - scorched rock with molten cracks,
     - stormy water with foam.
   - **Spreading:** the ground spills onto the six tiles around the site (land sites over land, sea sites over sea). Terrain sprites are left off the site's own tile.
   - **The area to hold:** an outline runs round the seven tiles. Ring tiles inside your borders glow in the site's colour.
   - **Animated effects above each site:**
     - gas puffs smoking up from the swamp,
     - ripples and glints on the metal lake,
     - crystal spires on the site and around it,
     - dark motes drawn into the pool,
     - a shimmering nanite swarm,
     - standing stones with lit glyphs,
     - glowing spore stalks with spores drifting up,
     - rocks floating over a glow,
     - lightning striking the water.
   - Sites you only remember (not in sight now) stand still and take the fog's tint.
   - **New test aids:** `exoticgallery` (all nine near your capital) and `exoticat <kind> <zoom>`, used by the new `tests/ui/exotics.txt`.

### Fixed while testing
- **Section joins:** some ship styles came apart. Two causes:
  - The shortest-named mid section was sometimes a short filler piece. The longest mid is now used.
  - Some sets aren't authored with the joint at the origin. Gaps are now closed, measured from the vertices actually drawn (the stored bounding boxes can include other pieces).
- **Hull lighting:** white hulls (cybernetics, synthetics) were overexposed. A tone curve fixes it.
- **Paint:** the empire's colour first covered whole hulls. It is now an accent where Stellaris's texture marks it.
- **Gas swamp:** it barely showed on green land. It now has a murkier bog and thicker, brighter gas.
- **Living metal:** at first it looked like a white vortex. It is now a dark mirror with a sky sheen.

### Tested
- **Self-test: ALL PASS.** New check: Stellaris ship models read (15 of 19 styles' first corvette file have textured parts).
- **Matrix (10 games): ALL PASS,** with the same results as before (the simulation is unchanged).
- **UI scripts:** `both-acts` (31), `units` (22), `galaxy` (23), `v4` (37), `offers` (25) and the new `exotics` (13), all passing.
- **Screenshots:**
  - New: `act1-exotic-sites.jpg` (all nine kinds), `act1-gas-swamp.jpg`, and `act2-ship-styles.jpg` (16 Stellaris styles, four classes each, rendered by the game).
  - Retaken: `act2-battle.jpg`, `act2-system-view.jpg` and `act1-exotic-site.jpg`.

### Still open
- A few sets still show a slight seam between sections (lithoid and infernal cruisers).
- Ships are pictures drawn from one angle, not 3D models turning in the scene.

## v0.4.1, 2026-10-11: Real alien portraits, and the AI offers you deals

### What was asked
- "My Stellaris install should have alien portraits; try looking for it again."
- "Alien empires should be able to trade with me": AI empires should send offers to the player, not only take the player's.

### What was found and built
1. **Portraits were there.** The loader only read the old `texturefile` entries, so it found none.
   - Modern Stellaris portraits are 3D entities. Their painted textures are listed under `character_textures` (or `tied_texture`), and some keys are aliases of others (`default = pla17_m`).
   - The loader now reads both forms and follows aliases: **430 portraits** (it was 0).
   - **The crop:** each texture holds the figure plus loose parts. The game crops to the largest painted piece and clears any other pieces inside its box.
   - **Curated list:** some textures are only a body or a scrap, so 135 portraits that come out whole were picked by eye from contact sheets of all 359 species portraits. They are listed in the new `portraits` table (key and species group).
   - A species uses its own portrait when it is listed, else a listed one from the same group (the same one every time).
   - Alien empires now show their portrait in the Diplomacy window, the battle window, the offer popup and the end screen. The emblem stays only as a fallback.
2. **AI offers to the player, in both acts:**
   - **Act 1** (civs with open relations):
     - What they offer: a resource swap, gold each turn for one of your resources, one of theirs for your gold, open borders, or an alliance at opinion 40+.
     - About 12% a turn per civ, with 15 turns between offers (20 after a refusal). Offers last 5 turns.
   - **Act 2** (empires with communications):
     - What they offer: their best surplus for the resource they lack most and you produce well. The deal leans slightly your way, more if they like you. Friendly empires (opinion 40+) offer alliances.
     - About 7 years between offers (10 after a refusal), and no new trade offer while one of their deals with you runs. Offers last 3 turns.
   - **On screen:** a message popup with the leader's picture or species portrait, what you receive and give (with icons), their opinion, and **Accept offer**, **Decline offer** or **Later**. An offer you put off shows in the Diplomacy window, marked *Offer* in the list, until it expires.
   - Each offer is also logged as an event.
   - Accepting checks that the deal still works (both sides still hold the resources, no war).

### Fixed while testing
- **Too many offers in space:** at first each empire offered every 2 years, about 200 offers a game (nearly one a turn). Now 26–81 a game, about one every 3–7 turns.
- **Popup stayed hidden:** the popup waits while another window (such as the research chooser) is open, and the toasts hide while it shows so they don't cover it.
- **Grammar:** "Korean Empire offer…" now reads "offers".

### Tested
- **Preflight:** clean. The self-test checks the `portraits` table against the install.
- **Self-test: ALL PASS.** New checks:
  - Stellaris portraits found (430), with every listed one present.
  - Every species gets a whole portrait.
  - Accepting an AI civ's offer makes the deal.
  - Declining makes them wait before offering again.
  - An alien empire offers the player a deal, and accepting it makes the deal.
- **Matrix (10 games): ALL PASS.**
  - The headless player accepts two offers in three and declines the third.
  - Offers per game: Act 1, 0–18 (0 in the Deity game, where the AIs dislike the player); Act 2, 1–81.
  - New matrix checks: AI civs and AI empires offer the player deals.
- **UI scripts:** `both-acts` (31), `units` (22), `galaxy` (23), `v4` (37) and the new `offers` (25), all passing. The `offers` script covers the popup in both acts, Later, the offer in the Diplomacy window, and Accept.
- **Screenshots:** new `act1-offer.jpg` and `act2-offer.jpg`. `act2-battle.jpg` and `act2-diplomacy.jpg` were retaken with real portraits.

## v0.4.0, 2026-10-11: Builders, traits, diplomacy with envoys, tribes, multi-turn battles with retreat, pre-FTL research, exotic sites, a tiered tech tree, ship icons and the system view

### What was asked
- **Builders and traits:** Builders that make farms, quarries and the like to raise a tile's yields, and civilization traits that help in both acts.
- **Diplomacy:** trade, war and alliances in both acts. Relations need an envoy first, Stellaris style, including with new species.
- **Battles:**
  - Appealing battle actions when engaging hostiles.
  - Starship combat over several turns with an emergency FTL retreat: the fleet goes missing, then returns home.
  - The same multi-turn fights early in Act 1, with a way to retreat.
- **Act 1 world:** tribes, and nicer units that move like Civ VI's.
- **Galaxy view:** better Stellaris-style ship icons, and a system view (for looks).
- **Pre-FTL research** before the space age (Stellaris's tier-0 list).
- **Exotic wonders** like a Living Metal Lake or Exotic Gas Swamp:
  - a bonus in Act 1 for holding the tiles around them,
  - a related technology in space (for example Regenerative Hull Tissue),
  - only a few per game, for replayability.
- **New research in both acts,** tiered like Stellaris's, with costs that rise with strength, and no top-tier research first.

### What was built
1. **Builders** (Civ VI's `UNIT_BUILDER`, 3 charges, more for Master Builders).
   - They make **farms, mines, quarries, plantations, pastures, camps, fishing boats, lumber mills and oil wells** inside your borders. The new `improvements` table gives each one's tech, yields, valid terrain, features and resources (resources must take the improvement that works them).
   - Builders share a tile with a soldier and can be captured by enemy soldiers at war.
   - The AI trains and uses them.
   - Improvements show as Civ VI's strategic-view sprites, and the tile card names them.
2. **Traits** (`civ_traits`, `civ_trait_map`): nine traits, mapped to 54 civilizations (any other civilization gets one picked from its name).
   - Each is titled with the civilization's own Civ VI ability name, read from `CivilizationTraits` and `Traits`.
   - Act 1 effects: yields, wonder production, unit strength and cost, Settler cost, Builder charges, opinion, early embarking.
   - Act 2 effects come as modifiers, including the new `trade_mult` and `envoys_add`.
   - Traits show on the New Game screen and in diplomacy.
3. **Tribes:**
   - **Camps** (1.6 per 100 land tiles) send raiders after nearby civilizations. Raiders are the best units most civs know, they pillage, and they stop once someone reaches the modern era.
   - Moving a unit in clears a camp (+50 gold).
   - **Villages** (2.2 per 100 land tiles) give gold, science, a citizen, a free unit or maps.
   - The tribes are a hidden "civ" at war with everyone, outside the space race and diplomacy.
4. **Act 1 diplomacy** (`Diplomacy1`):
   - **Meeting** happens when units or borders come within 3 tiles.
   - An **envoy** (25 gold, 3 turns) opens relations; war and peace need only a meeting.
   - **Trade** for 30 turns: share resources (they count as yours for units that need them), pay gold each turn, or open borders.
   - **Alliances:** allies join defensive wars, and a war against them breaks deals.
   - **Opinion** comes with reasons.
   - The AI sends envoys, swaps resources and allies with friends, and only attacks civs it has met and isn't allied with.
   - The screen is Civ VI-like: leader picture, ability name, opinion, status chips, resource-icon trading.
5. **Fights in Act 1:**
   - Units are medallions with Civ VI's unit portraits (`ICON_UNIT_*_PORTRAIT`), class badge, health and veteran pips.
   - They glide between tiles and lunge when they attack. Damage numbers float up, and a result banner shows both sides.
   - An **attack preview** (portraits, strength, expected damage) asks before each attack.
   - **Multi-turn melee:** a melee fight both survive locks the pair in battle for up to 4 more rounds, one each turn. **Withdraw** pulls back for half a round's damage; walking away does the same, and the AI withdraws badly hurt units.
6. **Pre-FTL research** (`preftl_techs`):
   - 18 of Stellaris's starting technologies (Red Lasers, Fission Power, Deflectors, Scientific Method, Unified Government, Eco Simulation, Space Exploration, Ground Defense Planning, Industrial Base, Offworld Construction, Mechanized Mining, Chemical Thrusters, Mass Drivers, Nuclear Missiles, Flak Battery, Nanocomposite Materials, Corvettes, Starbase Construction) join the end of the Civ VI tree. They have Stellaris's names and icons and need late Civ VI technologies.
   - Six are **core** and needed for the Moon Landing.
   - Each one researched is known in Act 2, along with its modifiers. Native species know all of them.
   - Science and construction ships and corvettes now need their pre-FTL technology.
7. **Exotic sites** (`exotic_sites`): nine, using Stellaris's deposit pictures and resource icons.
   - **Act 1:** 3–5 per map (by map size), placed away from starts. Each yields well; with 3 of its 6 neighbouring tiles also inside your borders it gives an empire-wide bonus (production, science, gold, food, or unit strength).
   - **The handoff:** sites you hold carry into space.
   - **Act 2:** 4 random sites sit on planets near the core, reachable from every arm. Holding one gives a modifier.
   - **Technology:** each site opens its own technology (Regenerative Hull Tissue then Living Metal, Exotic Gas Refining, Crystal-Infused Plating, Dark Matter Power then Dark Matter Deflectors, Nanite Transmutation, Archaeostudies, Psionic Theory, Mote Stabilization, Field Modulation).
8. **The Act 2 tech tree** (`act2_techs` rebuilt): 67 technologies in Stellaris's tiers 0–5, each with Stellaris's name and icon.
   - Each technology needs everything in its `requires` list (for example Mega-Engineering needs Zero Point Power and the Mega Shipyard, and Zero Point Power needs Antimatter, which needs Cold Fusion).
   - Costs rise by tier (about 600, 1,000, 2,100, 3,700, 6,300 and 11,000 on average) and with strength.
   - The research window shows the tiers as columns, colour-coded by area, with locked, open, exotic and researched states.
9. **Space battles** (`Battle`): each month both sides deal 45% of their power as hull damage, so battles of equal fleets last several turns.
   - The battle window shows both fleets (Stellaris class silhouettes in empire colours), hulls, losses and a monthly log.
   - It opens itself when a battle involves you, with **Fight on**, **Emergency FTL** and **View system**.
   - **Emergency FTL:** the fleet loses about a quarter of its ships and is **missing** for 18 months, then reappears at the capital. AI fleets jump when outgunned nearly 3 to 1.
   - **Regenerative Hull Tissue** mends ships even in battle.
   - Battles show as a pulsing ring on the map, and a rail button opens the current one.
10. **Act 2 diplomacy** (`Diplomacy2`):
    - **First contact** happens through ships in someone's space or touching borders.
    - **Envoys** (2, plus traits and Xeno-Linguistics) **establish communications** in 12 months, or stay to **improve relations** (+25 opinion).
    - Communications open **monthly trade deals** for 10 years and **alliances**, whose members join defensive wars.
    - Contacts, communications and alliances carry over from Act 1.
    - Unknown empires show as "?".
    - The Empires window is now the diplomacy screen.
11. **Galaxy fleets:**
    - They use Stellaris's map markers (`ship_icons_type.dds`, coloured by relation) pointing along their route, with the ship-class icon (`ship_classes.dds`), count and health.
    - The outliner shows missing fleets ("home in ~3 turns") and fleets in battle.
12. **System view** (View button or a double-click): the star with its glow, planets on their orbits (slowly turning), names and owners, exotic worlds glowing, ships near the star and the starbase.

### Fixed while testing
- **Long Act 1:** pre-FTL research at first made Act 1 run 235–355 turns. Core pre-FTL costs went down 40% and the others 50%; Act 1 is now about 220–255 turns.
- **Early tribes too harsh:** in one test game the player was left with a single city. Raiders now start after turn 15, one per camp in the ancient era, within 6 tiles of their camp.
- **The UI test couldn't rebuild** while the matrix ran in the background (the program was locked). The tests now run one after another.
- **Ship pictures (cropped paintings) looked like tiles** in the battle window and system view. They are now Stellaris's class silhouettes in empire colours.
- **Alien empires had no portrait.** They got an emblem in their colours. (This was wrong; v0.4.1 finds the portraits.)
- **The battle banner touched the unit panel.** It now moves aside and hides behind open windows.

### Tested
- **Preflight:** clean against both installs, with checks for every new table (Civ VI ids, base techs, improvement sprites, Stellaris icons, tier order and costs).
- **Self-test: ALL PASS.** New checks:
  - The 28-unit roster with the Builder.
  - Tribal camps and villages, exotic sites on the map, and a trait for every civ.
  - A Builder's farm adds 1 food and uses a charge.
  - Camps send raiders, and clearing one pays gold.
  - The tribes can't be talked to.
  - Meeting alone doesn't open trade; an envoy takes 3 turns; a gold-and-open-borders deal works; an ally joins a defensive war.
  - A melee fight both survive goes on, the next round deals damage, and a unit can withdraw.
  - No Moon Landing without the core pre-FTL research.
  - In space: pre-FTL research carries over, natives know tier 0, 4 exotic worlds exist, and traits carry over.
  - Top-tier research is closed at the start, prerequisites never come from a higher tier, and costs rise tier by tier.
  - An exotic technology needs its site.
  - An envoy needs contact and establishes communications after a year.
  - Equal fleets fight for at least 3 months.
  - Emergency FTL loses ships, the fleet goes missing, then returns home.
- **Matrix (10 games): ALL PASS.**
  - Act 1 ends at turns 133–300 (Ruthless and conquered games end early).
  - Units reach Modern Armor or Helicopters.
  - In 3 games a deliberately weak or Deity-difficulty player was conquered.
  - Act 2 runs up to 24 seconds headless in a huge galaxy.
- **UI scripts:** `both-acts` (31), `units` (22), `galaxy` (23), and the new `v4` (37): setup with traits, a Builder, the attack preview, the battle banner, diplomacy, an exotic site, the handoff, galaxy fleets, a space battle, the system view, the tech tree, and diplomacy in space. All pass.
- **Screenshots:** 16 shots at 1080p in `docs/screenshots/`.

### Still open
- **Some of the "Fight on / Emergency FTL" choice is automatic:** the battle window opens at the end of your turn, and fleets keep fighting through the turn's six months before you see it.
- **No naval units or ranged city attacks you control.** City strikes are automatic.
- ~~AI trade offers to the player~~ (done in v0.4.1).
- **Balance needs human play:** tribes, war frequency, the pace of pre-FTL research, and Act 2 battle length.

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
