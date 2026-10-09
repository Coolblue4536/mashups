# Grand Galactic: Stellaris × Stacklands

Run your Stellaris empire as a game of Stacklands. Every Pop, ship, planet, resource and monster is a card,
and you stack them to make things happen. Survey star systems to open new boards, buy booster packs with
Energy Credits, and build a fleet big enough for the endgame crisis.

It's a mashup in the plain sense: both games are really in it.

- **Stellaris (the host)** brings its pictures and text. That means your species portraits, planet,
  resource, ship, technology and ethic icons, the guardians and crises, and the official names, all read
  from your own Stellaris install. Melty starts the game from your Stellaris folder.
- **Stacklands** brings its look, sound and rules. That means its card frames, pack art, board, font,
  sounds and music, read from your own Stacklands install. Its stack-to-make-something rules, moons and
  feeding are rebuilt in the game's code.

Neither game's files are included. Grand Galactic reads them from your PC each time it runs.

## What you do

- **Found your empire.** Pick a species from your Stellaris portraits and one ethic: Militarist,
  Spiritualist, Materialist or Machine Intelligence. Each one changes your starting cards and gives you a
  free booster pack.
- **Work your worlds.** Stack Pops on your Homeworld, districts and colonised planets to make Energy,
  Minerals, Food, Research, Unity and more.
- **Build.** Stack a Construction Ship with materials to build districts, labs, foundries, shipyards,
  temples and starbases. Shipyards build science, construction and colony ships, then Corvettes, and
  later Titans.
- **Research.** Stack Research on a Scientist to unlock Corvettes, Destroyers, Cruisers, Battleships,
  Titans, Colonization, Robotic Workers and Terrestrial Sculpting.
- **Explore.** A Science Ship on an Uncharted System opens a new star-system board, with new planets,
  anomalies, derelicts, space amoebas, Tiyanki, crystal entities, void clouds, mining drones, pirates and
  marauders. Ships carry stacks between boards.
- **Act 1, Expansion (moons 1–6).** Grow, feed your Pops each moon, and colonise Desert, Arid, Savanna,
  Ocean, Tropical, Continental, Arctic, Tundra, Alpine, Tomb and Gaia worlds. Build outposts on gas
  giants, asteroids and barren, molten, frozen and toxic worlds.
- **Act 2, Guardians (from moon 7).** Three Guardian Signals lead to the Ether Drake, the Dimensional
  Horror and the Enigmatic Fortress. Beating them earns Living Metal, Dark Matter and Zro. The Frontier
  and Strategic Resources packs go on sale.
- **Act 3, Crisis (from moon 14).** A random endgame crisis comes through a rift on your capital board:
  the Prethoryn Scourge, the Unbidden or the Contingency. Destroy its leader to win.
- **Difficulty and moon length.** Pick Ensign, Captain, Admiral or Grand Admiral (enemy strength, raids, pack prices and when each act starts), and a moon length of 60, 90, 120 or 180 seconds.
- **Packs and market.** 7 booster packs, a Market for selling cards, 87 cards, 48 stacking recipes and
  11 star systems in all.

It's single-player only.

## Controls

| Action | How |
|---|---|
| Pick up a card and everything on top of it | Left-drag |
| Stack | Drop onto another card |
| Fight | Drop ships onto a hostile card or a battle |
| Buy a pack | Drop Energy Credits onto a pack on the right |
| Sell | Drop cards onto the Market |
| Travel | Drop a stack that contains a ship onto a system tab |
| Pan | Right or middle drag, or WASD |
| Zoom | Mouse wheel |
| Pause / game speed | Space / 1, 2, 3 |
| Recipe codex | Tab |
| Switch system | F1–F9 or click the tabs |

## Requirements

- Windows 10 or 11 (64-bit)
- **Stellaris** (Steam). Melty finds it and starts Grand Galactic from it.
- **Stacklands** (Steam, store.steampowered.com/app/1948280). Grand Galactic finds it through your Steam
  libraries. If it's missing, the game says so and lets you drop the Stacklands folder onto its window.

## Building from source

The design lives in `sheets/*.json`, one row per card, recipe, pack, system and asset, and they're the
source of truth. To build:

```
python3 tools/preflight.py          # every cell filled, every cross-sheet reference resolves
python3 tools/gen.py                # sheets -> src/GrandGalactic/Generated/Defs.g.cs
tools/package.sh                    # self-test, Windows build, zip into dist/
```

Other useful commands:

- `GrandGalactic --selftest` runs the simulation checks headlessly.
- `GrandGalactic --probe <folder>` reports what it finds in this PC's two games and saves each resolved
  picture so the asset sheet can be checked. The pictures stay on your PC.

## Credits

Grand Galactic is made with these bundled libraries (see `THIRD-PARTY-NOTICES.txt`): raylib and
Raylib-cs (zlib), AssetsTools.NET and its classdata.tpk (MIT, nesrak1), AssetRipper.TextureDecoder (MIT),
Fmod5Sharp (MIT), NAudio.Core (MIT), OggVorbisEncoder (MIT), IndexRange (MIT), Pfim (MIT) and the .NET
runtime (MIT).

Stellaris is © Paradox Interactive, and Stacklands is © Sokpop Collective. Grand Galactic is an
unofficial, free fan mashup and isn't affiliated with either. It was built with AI assistance
(Claude Code).
