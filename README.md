# Slay the Spire × Wardogs

A Slay the Spire mod (ModTheSpire + BaseMod) that adds **the Wardog**, a playable character whose cards are real Wardogs weapons and gear. Winning fights pays **War Funds**, Wardogs-style cash you spend at rest sites on Medic, Sapper and Assault kits.

Wardogs content (weapon and gear names, sounds) is read at runtime from the player's own Steam install of Wardogs. Nothing from Wardogs is shipped. Wardogs itself is never modified or started.

- Game you play in: Slay the Spire (Melty slug `slay-the-spire`), loader ModTheSpire.
- Game whose content it reads: Wardogs (Steam app 1867240), named as a secondary game.
- Single player.

Status: design stage. The sheets in `sheets/` are the source of truth; `python3 tools/preflight.py` lists what is unfinished.

Bundled libraries (MIT): BaseMod by t-larson, kiooeht, test447 and contributors.
