# MODLOG

## Route
- Host: Slay the Spire 1 (Java/libGDX). ModTheSpire is installed by Melty (v3.6.3). Mod = ModTheSpire jar using BaseMod hooks and @SpirePatch.
- BaseMod: MIT. GitHub release assets are stale (5.5.0, 2018); current is 5.56.0 (source only on GitHub). Build from source and bundle in {game}/mods.
- StSLib not needed for this scope.
- Wardogs: UE5 + Easy Anti-Cheat. Read files on disk only, never launch or touch the client. If its paks/utoc are encrypted, do not extract keys: only read what is readable.

## Gotchas
- Card rewards need >= 3 unique cards of each of COMMON/UNCOMMON/RARE in the character's pool, or reward generation can loop forever. Preflight enforces this.
- BaseMod has no campfire-option API: patch CampfireUI.initializeButtons.

## Blocked on
- Wardogs file layout (tools/inspect-wardogs.ps1 on the player's PC).
- desktop-1.0.jar from the player's Slay the Spire install to compile against (never committed).
