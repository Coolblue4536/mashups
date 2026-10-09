# Handoff: test Grand Galactic on the player's PC

This file is for the Claude session that continues the work on the user's Windows PC (Claude Desktop). The game was
designed and built in a cloud session that had neither game installed. Everything below that says "verified" was checked
there; everything that needs real game files is still to do, here.

## Where things stand

- **What it is:** Grand Galactic, a Stellaris x Stacklands mashup. A Windows program (C# / .NET 8, raylib) that plays
  from the player's Stellaris folder (Melty passes it with `--stellaris "{game}"`) and finds Stacklands through Steam.
  It reads both games' files at run time and ships none of them. See `README.md` for the player's view.
- **Design:** the whole design is in `sheets/design.json`, the source of truth (cards, components, recipes, packs,
  systems, crises, tutorial, asset refs...). `python tools/preflight.py` checks it; `python tools/gen.py` turns it into
  `src/GrandGalactic/Generated/Defs.g.cs`. Change the sheet before the code.
- **Verified in the cloud:**
  - the simulation self-test passes (`--selftest`), including every recipe, the combat and balance sweep, and the
    tutorial;
  - the Windows build ran under Wine: the self-test passed and the window opened;
  - the Stellaris reader works against a fake test folder;
  - Melty: `validate_recipe` is valid and `one_click_check` says yes.
- **Not yet verified (needs this PC):**
  - the Stacklands reader against a real install;
  - every one of the ~150 `asset_refs` rows (all `"verified": false`);
  - a real playthrough;
  - the gameplay screenshot.
- **Melty:** connected; no draft listing exists yet (`list_my_mods` was empty), so there's no modId. The listing text,
  license and remix choice have **not** been asked yet.

## Steps, in order

1. **Tools.** Install what's missing yourself and tell the user what you installed:
   - .NET 8 SDK: `winget install Microsoft.DotNet.SDK.8`
   - Python 3: `winget install Python.Python.3.12`

   Both Stellaris and Stacklands must be installed through Steam on this PC.
2. **Build and self-test:**
   ```
   python tools/preflight.py
   python tools/gen.py
   dotnet build -c Release src/GrandGalactic
   dotnet src/GrandGalactic/bin/Release/net8.0/GrandGalactic.dll --selftest
   dotnet publish src/GrandGalactic -c Release -r win-x64 --self-contained true -p:DebugType=None -o dist/GrandGalactic
   copy README.md dist\GrandGalactic\ & copy THIRD-PARTY-NOTICES.txt dist\GrandGalactic\
   ```
3. **Probe the real games.** Run `dist\GrandGalactic\GrandGalactic.exe --probe probe`. It finds both games via Steam
   (or pass `--stellaris "<folder>" --stacklands "<folder>"`) and writes `probe/probe.json` plus `probe/png/<asset id>.png`
   and `probe/audio/`. These files stay on this PC: `/probe/` is gitignored and must never be committed or uploaded.
   - If Stacklands fails to read, check `%LOCALAPPDATA%\GrandGalactic\logs\latest.log` and fix
     `src/GrandGalactic/Assets/StacklandsData.cs`. That reader (AssetsTools.NET with the bundled `classdata.tpk`) has
     never run against a real Stacklands.
4. **Verify every asset row.** Look at each PNG. Do the Stellaris ones show the right thing (the Red Laser icon on
   `st_comp_red_laser`, a black hole on `st_star_black_hole`)? Are the Stacklands card frame, pack art and board
   background sensible, and do the sounds and font load?
   - Where a lookup missed or picked the wrong file, fix its `lookup` in `sheets/design.json`, using the real names
     listed in `probe.json` (`sprites`, `eventPictures`, Stacklands `textures` / `sprites` / `audio` / `fonts`).
   - Set `"verified": true` only on rows you have looked at. Component rows' icons are under `components[].icon` and
     `tech_icon`.
   - Also check the `names` list in `probe.json`: Stellaris localisation keys that resolve to odd names should get a
     better `name_loc` (or a key that doesn't exist, so the sheet name is used).
   - Do the same for the `game_systems` rows.
5. **Play it.** Run `dist\GrandGalactic\GrandGalactic.exe --stellaris "<Stellaris folder>"`, play through the tutorial
   and into combat, and fix what breaks. Re-run preflight, gen and the self-test after any sheet change.
6. **Real gameplay screenshot.** Run
   `GrandGalactic.exe --stellaris "<folder>" --ethic militarist --screenshot shot.png --screenshot-at 10`
   (makes a few ordinary opening moves, then captures the window), or capture a real session with computer use. It must
   show the real Stellaris art and Stacklands frames. Never use `--dev-no-assets` (that's a layout test with no game
   files). Capture only the game window.
7. **Release gate.** Run `python tools/preflight.py --release`. It must be clean, meaning no unverified rows.
8. **Listing and publishing.** Ask the user, one question at a time:
   - title (30 characters at most), tagline and short description: suggest options based on the README;
   - credits (who's credited; `madeBy` if not the account itself);
   - content license;
   - whether others may remix it.

   The user will paste a fresh Melty publish prompt with their token into this session (it's deliberately not in the
   repo). Then:
   - `create_mod` with `githubRepo` `coolblue4536/mashups`, games `stellaris` and `custom-stacklands`;
   - zip `dist/GrandGalactic` as `GrandGalactic-1.0.0.zip`, then `inspect_package`, `validate_recipe` and
     `one_click_check` with the recipe below;
   - `start_upload`, `finish_upload`, `submit_release`;
   - `add_screenshot` with the real screenshot;
   - summarise for the user and ask before `publish`.

   It goes live after the user presses Play in the Melty app.

### Validated recipe (save as `melty.json` at the repo root only when the user agrees)

```json
{
  "schemaVersion": 1,
  "mode": "standalone",
  "games": [{"slug": "stellaris", "role": "primary"}, {"slug": "custom-stacklands", "role": "secondary"}],
  "components": [{"id": "main", "fileName": "GrandGalactic-*.zip", "label": "Grand Galactic (Windows)", "kind": "main", "required": true}],
  "requirements": [],
  "mappings": [{"component": "main", "from": "", "to": "{managed}"}],
  "launch": {"kind": "exe", "path": "{managed}/GrandGalactic.exe", "args": ["--stellaris", "{game}"], "cwd": "{managed}"}
}
```

For a single upload (not the melty.json form), use the exact file name `GrandGalactic-1.0.0.zip` for the component's
`fileName`.

## House rules carried over

- Never ship either game's files, and never commit probe output, extracted assets or tokens.
- Stacklands isn't in Melty's catalog, so it is role `secondary`. The game finds it itself and shows a clear "Stacklands
  needed" screen when it's missing.
- Bundled libraries and their licenses are in `THIRD-PARTY-NOTICES.txt`. Keep it in the release.
- Keep labels honest: only call something tested once it has run here with the real games.
