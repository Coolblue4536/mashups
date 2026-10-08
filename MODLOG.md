# MODLOG

## Direction
- 2026-10-08: switched from Slay the Spire x Wardogs to Marvel Rivals x Slay the Spire (user's choice). Roster: Emma Frost, Psylocke, Invisible Woman; "fuller" scope (ultimates + team-ups). Build one hero at a time, Emma first.

## Route
- Host: Slay the Spire 1 (Java/libGDX). ModTheSpire installed by Melty (v3.6.3). Mod = ModTheSpire jar with BaseMod hooks and @SpirePatch.
- BaseMod: MIT. GitHub release assets are stale (5.5.0, 2018); current is 5.56.0 (source only). Build from source, bundle in {game}/mods.
- Marvel Rivals: companion; Melty passes {game:marvel-rivals}. UE5, NetEase NEAC. Never launched or modded.
- Rivals paks are AES-encrypted (community guides use a key extracted from the game). Decision: do NOT decrypt with an extracted key (circumvents a protection measure). Only read what is stored unencrypted. tools/inspect-game.ps1 reports which containers are encrypted and what loose files exist.

## Gotchas
- Card rewards need >= 3 unique cards of each of COMMON/UNCOMMON/RARE per character pool, or reward generation can loop forever.
- BaseMod has no campfire-option API: patch CampfireUI.initializeButtons.

## Blocked on
- rivals-report.txt from the player's PC.
- desktop-1.0.jar to compile against (never committed): the build script on the player's PC will compile there.
