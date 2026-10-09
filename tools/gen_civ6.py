#!/usr/bin/env python3
"""Generate the Civ VI half of Stellar Ascension from the sheets.

Output: build/civ6/StellarAscension/ (a plain Civ VI mod: no loader needed).
Every techs row becomes a tooltip row + a Lua table entry, every space_race row a
setup dropdown value, and so on. Nothing in the output is edited by hand.
"""
import json, os, glob, shutil, html

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "build", "civ6", "StellarAscension")
SRC = os.path.join(ROOT, "civ6")  # hand-written Lua/XML templates

def sheets():
    return {d["sheet"]: d for d in (json.load(open(p, encoding="utf-8")) for p in glob.glob(os.path.join(ROOT, "sheets", "*.json")))}

def const(s, key):
    return next(r["value"] for r in s["constants"]["rows"] if r["key"] == key)

def x(text):
    return html.escape(str(text), quote=True)

def sql(text):
    return "'" + str(text).replace("'", "''") + "'"

def lua_str(text):
    return '"' + str(text).replace("\\", "\\\\").replace('"', '\\"') + '"'

def write(rel, text):
    p = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    return rel

def text_xml(rows):
    body = "\n".join(f'    <Row Tag="{x(t)}" Language="en_US"><Text>{x(v)}</Text></Row>' for t, v in rows)
    return f'<?xml version="1.0" encoding="utf-8"?>\n<GameData>\n  <LocalizedText>\n{body}\n  </LocalizedText>\n</GameData>\n'

def main():
    s = sheets()
    if os.path.exists(OUT):
        shutil.rmtree(OUT)
    files = []
    mods = {r["id"]: r for r in s["modifiers"]["rows"]}
    levels = s["space_race"]["rows"]

    # --- hook civ_setup_option: Advanced Setup dropdown -------------------------
    cfg = ["-- generated from sheets/space_race.json",
           "INSERT INTO Parameters (ParameterId, Name, Description, Domain, DefaultValue, ConfigurationGroup, ConfigurationId, GroupId, SortIndex)",
           "VALUES ('SA_SpaceRace', 'LOC_SA_SPACE_RACE_NAME', 'LOC_SA_SPACE_RACE_DESC', 'SA_SpaceRaceLevels', 'standard', 'Game', 'SA_SPACE_RACE', 'AdvancedOptions', 2010);"]
    cfg_text = [("LOC_SA_SPACE_RACE_NAME", "Space Race (Stellar Ascension)"),
                ("LOC_SA_SPACE_RACE_DESC", "How hard losing the race to space hits you when your empire continues in Stellaris.")]
    for i, r in enumerate(levels):
        tag = "LOC_SA_LEVEL_" + r["level"].upper()
        cfg.append(f"INSERT INTO DomainValues (Domain, Value, Name, Description, SortIndex) VALUES ('SA_SpaceRaceLevels', {sql(r['level'])}, {sql(tag + '_NAME')}, {sql(tag + '_DESC')}, {10 * (i + 1)});")
        cfg_text += [(tag + "_NAME", r["label"]), (tag + "_DESC", r["description"])]
    files.append(write("Config/SA_Config.sql", "\n".join(cfg) + "\n"))
    files.append(write("Text/SA_ConfigText.xml", text_xml(cfg_text)))

    # --- hook civ_tech_tooltips --------------------------------------------------
    data = ["-- generated from sheets/techs.json"]
    text = []
    for r in s["techs"]["rows"]:
        m = mods[r["modifier"]]
        tag = f"LOC_SA_{r['civ_tech']}_DESCRIPTION"
        data.append(f"UPDATE Technologies SET Description = {sql(tag)} WHERE TechnologyType = {sql(r['civ_tech'])};")
        text.append((tag, f"Space Age legacy: {r['title']} ({m['effect_text']} in Stellaris)."))
    trig = const(s, "trigger_projects").split()
    text += [
        ("LOC_SA_ACT1_TITLE", "Act 1 complete: your civilization reaches space"),
        ("LOC_SA_ACT1_BODY", "Your empire is ready to continue in Stellaris. Save if you like, then exit Civilization VI: Stellaris opens with your civilization, your rivals and your wonders. In Stellaris's empire list, pick {1_Name}."),
        ("LOC_SA_DEFEAT_TITLE", "Lost the race to space"),
        ("LOC_SA_DEFEAT_BODY", "{1_Name} reached space before you. On Ruthless, only the winner of the space race goes on to the stars. Your game is over."),
        ("LOC_SA_RIVAL_SPACE_TITLE", "A rival reached space"),
        ("LOC_SA_RIVAL_SPACE_BODY", "{1_Name} reached space first. Any natural wonders on this map will become their rare star systems in Stellaris."),
        ("LOC_SA_POPUP_OK", "Continue"),
    ]
    files.append(write("Data/SA_Gameplay.sql", "\n".join(data) + "\n"))
    files.append(write("Text/SA_Text.xml", text_xml(text)))

    # --- Lua: data tables generated from sheets, logic from civ6/*.lua templates ---
    triggers = "{ " + ", ".join(f"[{lua_str(t)}] = true" for t in trig) + " }"
    defeat = "{ " + ", ".join(f"[{lua_str(r['level'])}] = true" for r in levels if r["civ_defeat_if_beaten"]) + " }"
    techs = "{ " + ", ".join(lua_str(r["civ_tech"]) for r in s["techs"]["rows"]) + " }"
    gen = {
        "--@TRIGGERS@": f"local SA_TRIGGERS = {triggers}",
        "--@DEFEAT_LEVELS@": f"local SA_DEFEAT_LEVELS = {defeat}",
        "--@TECHS@": f"local SA_TECHS = {techs}",
        "--@MAX_RIVALS@": f"local SA_MAX_RIVALS = {int(const(s, 'max_rivals'))}",
        "--@MAX_CITIES@": f"local SA_MAX_CITIES = {int(const(s, 'max_home_planet_names'))}",
    }
    for rel in ("Scripts/SA_Gameplay.lua", "UI/SA_Handoff.lua", "UI/SA_Handoff.xml"):
        src = open(os.path.join(SRC, rel), encoding="utf-8").read()
        for k, v in gen.items():
            src = src.replace(k, v)
        assert "--@" not in src, f"unfilled template marker in {rel}"
        files.append(write(rel, src))

    # --- modinfo ------------------------------------------------------------------
    flist = "\n".join(f"    <File>{f}</File>" for f in sorted(files))
    modinfo = f'''<?xml version="1.0" encoding="utf-8"?>
<Mod id="{const(s, 'civ_mod_id')}" version="1">
  <Properties>
    <Name>Stellar Ascension</Name>
    <Teaser>Win the race to space, then take your civilization to the stars in Stellaris.</Teaser>
    <Description>Act 1 of the Stellar Ascension mashup. Reaching the Moon Landing ends Act 1; your civilization, rivals, wonders and natural wonders continue in Stellaris. Choose how harsh losing the space race is under Advanced Setup.</Description>
    <Authors>Stellar Ascension</Authors>
    <AffectsSavedGames>1</AffectsSavedGames>
    <CompatibleVersions>1.2,2.0</CompatibleVersions>
  </Properties>
  <FrontEndActions>
    <UpdateDatabase id="SA_Config">
      <File>Config/SA_Config.sql</File>
    </UpdateDatabase>
    <UpdateText id="SA_ConfigText">
      <File>Text/SA_ConfigText.xml</File>
    </UpdateText>
  </FrontEndActions>
  <InGameActions>
    <UpdateDatabase id="SA_Gameplay">
      <File>Data/SA_Gameplay.sql</File>
    </UpdateDatabase>
    <UpdateText id="SA_Text">
      <File>Text/SA_Text.xml</File>
    </UpdateText>
    <AddGameplayScripts id="SA_Scripts">
      <File>Scripts/SA_Gameplay.lua</File>
    </AddGameplayScripts>
    <AddUserInterfaces id="SA_UI">
      <Properties>
        <Context>InGame</Context>
      </Properties>
      <File>UI/SA_Handoff.xml</File>
    </AddUserInterfaces>
    <ImportFiles id="SA_UIFiles">
      <File>UI/SA_Handoff.lua</File>
    </ImportFiles>
  </InGameActions>
  <Files>
{flist}
  </Files>
</Mod>
'''
    write("StellarAscension.modinfo", modinfo)
    print(f"gen_civ6: wrote {len(files) + 1} files to {os.path.relpath(OUT, ROOT)}")

if __name__ == "__main__":
    main()
