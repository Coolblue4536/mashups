# Marvel Rivals × Slay the Spire

A Slay the Spire mod (ModTheSpire + BaseMod) adding a roster of Marvel Rivals heroes as playable characters: **Emma Frost**, **Psylocke** and **Invisible Woman**. Each has a starter deck and reward cards built from her Rivals abilities, a starting relic from her passive, her Ultimate as a rare card, and Team-Ups with the other two.

Marvel Rivals content is read at runtime from the player's own install, whose folder Melty passes to the mod (Rivals is a companion game). Nothing from Marvel Rivals is shipped, and Rivals is never modified or started.

- Game you play in: Slay the Spire (Melty slug `slay-the-spire`), loader ModTheSpire.
- Companion: Marvel Rivals (`marvel-rivals`, Steam app 2767030).
- Single player.

Status: checking which Rivals files are readable (`tools/check-rivals.cmd`). Design sheets go in `sheets/`; `python3 tools/preflight.py` lists what is unfinished.

Bundled library (MIT): BaseMod by t-larson, kiooeht, test447 and contributors.
