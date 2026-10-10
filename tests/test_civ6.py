#!/usr/bin/env python3
"""Run the generated Civ VI scripts against tests/civ6_mock.lua (Lua 5.1 via lupa).

Scenario A (standard): Japan lands on the Moon first, then the player (Rome).
Scenario B (ruthless): Japan lands first; the player must lose.
Writes tests/fixtures/Lua.log for the launcher tests.
"""
import os, sys
from lupa import lua51

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD = os.path.join(ROOT, "build", "civ6", "StellarAscension")

def run(level, ui_first=False):
    lua = lua51.LuaRuntime(unpack_returned_tuples=True)
    lua.execute(open(os.path.join(ROOT, "tests", "civ6_mock.lua"), encoding="utf-8").read())
    lua.globals().SETUP.SA_SPACE_RACE = level
    order = [("SA_Gameplay", "Scripts/SA_Gameplay.lua"), ("SA_Handoff", "UI/SA_Handoff.lua")]
    if ui_first:
        order.reverse()
    for ctx, rel in order:
        src = open(os.path.join(MOD, rel), encoding="utf-8").read()
        lua.execute(f'CONTEXT = "{ctx}"')
        fn = lua.eval("function(src, name) local f, e = loadstring(src, name); if not f then error(e) end; local env = setmetatable({}, {__index = _G}); setfenv(f, env); f() end")
        fn(src, rel)
    moon = lua.eval("GameInfo.Projects.PROJECT_LAUNCH_MOON_LANDING.Index")
    lua.execute('CONTEXT = "events"')
    fire = lua.globals().Fire
    fire("CityProjectCompleted", 1, 1, moon, -1, 3, 4, False)   # Japan first
    fire("CityProjectCompleted", 2, 1, 0, -1, 3, 4, False)      # Scythia: satellite only, not a trigger
    fire("CityProjectCompleted", 0, 1, moon, -1, 3, 4, False)   # the player
    fire("CityProjectCompleted", 0, 1, moon, -1, 3, 4, False)   # repeat must not dump twice
    g = lua.globals()
    return [g.LOG[i] for i in range(1, len(g.LOG) + 1)], g

def check(cond, msg):
    print(("PASS " if cond else "FAIL ") + msg)
    return cond

def main():
    ok = True
    for ui_first in (False, True):
        log, g = run("standard", ui_first)
        sa = [l.split(": ", 1)[1] for l in log if ": SA|" in l]
        tag = " (UI context loaded first)" if ui_first else ""
        ok &= check(sa.count("SA|BEGIN|1") == 1, "standard: exactly one handoff block" + tag)
        ok &= check("SA|LEVEL|standard" in sa, "standard: level recorded" + tag)
        ok &= check("SA|CIV|1|1|2|Roman Empire|Roman|Trajan|Male" in sa, "player is slot 1, reached space 2nd" + tag)
        ok &= check("SA|CIV|2|0|1|Japanese Empire|Japanese|Hojo Tokimune|Male" in sa, "Japan is a rival that reached space 1st" + tag)
        ok &= check("SA|CIV|3|0|0|Scythian Empire|Scythian|Tomyris|Female" in sa, "Scythia never reached space (satellite is not a trigger)" + tag)
        ok &= check([l for l in sa if l.startswith("SA|CITY|1|")] == ["SA|CITY|1|Rome", "SA|CITY|1|Cumae pipe", "SA|CITY|1|Antium"],
                    "player cities: capital first, then by population, '|' cleaned" + tag)
        ok &= check("SA|CITY|2|Kyōto" in sa, "rival cities carried, non-ASCII kept" + tag)
        ok &= check([l for l in sa if l.startswith("SA|TECH|")] == ["SA|TECH|TECH_ASTROLOGY", "SA|TECH|TECH_ROCKETRY"], "only researched sheet techs" + tag)
        ok &= check("SA|WONDER|BUILDING_PYRAMIDS|Pyramids|+25% Production toward districts." in sa
                    and "SA|WONDER|BUILDING_MACHU_PICCHU|Machu Picchu|" in sa, "wonders incl. one not in the sheet (DEFAULT row)" + tag)
        ok &= check([l for l in sa if l.startswith("SA|NATURAL|")] == ["SA|NATURAL|FEATURE_EVEREST|Mount Everest", "SA|NATURAL|FEATURE_PAITITI|Paititi"],
                    "natural wonders once each, multi-tile deduped" + tag)
        n = len(sa[sa.index("SA|BEGIN|1"):sa.index(next(l for l in sa if l.startswith("SA|END|"))) + 1])
        ok &= check(f"SA|END|{n}" in sa, "END carries the record count" + tag)
        ok &= check(g.WINNER is None, "standard: nobody is made the winner" + tag)
        ok &= check(g.HIDDEN is False and "pick Roman Empire" in g.Controls.Body.text, "Act 1 popup shown" + tag)
        ok &= check(any("Japanese Empire first" in n for n in g.NOTIFICATIONS.values()), "rival-first notification" + tag)
        if not ui_first:
            os.makedirs(os.path.join(ROOT, "tests", "fixtures"), exist_ok=True)
            open(os.path.join(ROOT, "tests", "fixtures", "Lua.log"), "w", encoding="utf-8").write("\n".join(log) + "\n")

    log, g = run("ruthless")
    sa = [l.split(": ", 1)[1] for l in log if ": SA|" in l]
    ok &= check(sa == ["SA|BEGIN|1", "SA|DEFEAT|Japanese Empire", "SA|END|3"], "ruthless: defeat block only, no handoff")
    ok &= check(g.WINNER is not None and g.WINNER[1] == 101 and g.WINNER[2] == 1, "ruthless: Japan's team set as winner (technology victory)")
    ok &= check("Japanese Empire won" in g.Controls.Body.text, "ruthless: defeat popup")
    print("civ6 script tests: " + ("ALL PASS" if ok else "FAILURES"))
    return 0 if ok else 1

if __name__ == "__main__":
    sys.exit(main())
