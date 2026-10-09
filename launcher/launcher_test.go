package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

// A stand-in Stellaris install: only the files the launcher reads, written for
// the test in the game's format (not copied from the game).
func fakeStellaris(t *testing.T) (game, docs string) {
	root := t.TempDir()
	game, docs = filepath.Join(root, "Stellaris"), filepath.Join(root, "Documents")
	w := func(rel, s string) {
		p := filepath.Join(game, filepath.FromSlash(rel))
		os.MkdirAll(filepath.Dir(p), 0o755)
		os.WriteFile(p, []byte(s), 0o644)
	}
	w("prescripted_countries/00_test.txt", `# test
lizards = {
	name = "LIZ" species = { class = "REP" portrait = "rep1" }
	ruler = { name = "Liz" gender = male portrait = "rep1" texture = 0 }
}
earthlings = {
	name = "TEST_EMPIRE"
	adjective = "TEST_ADJ"
	spawn_enabled = yes # comment
	ship_prefix = "TST"
	species = { class = "HUM" portrait = "human" name = "Human" plural = "Humans" adjective = "Human" name_list = "HUMAN1" trait = "trait_adaptive" }
	room = "test_room"
	authority = "auth_democratic"
	civics = { "civic_one" "civic_two" }
	origin = "origin_default"
	ethic = "ethic_egalitarian"
	flags = { custom_start_screen }
	planet_name = "TEST_PLANET"
	planet_class = "pc_continental"
	initializer = "sol_system_initializer"
	system_name = "TEST_SYSTEM"
	graphical_culture = "mammalian_01"
	city_graphical_culture = "mammalian_01"
	empire_flag = { icon = { category = "human" file = "flag_human_1.dds" } background = { category = "backgrounds" file = "00_solid.dds" } colors = { "blue" "black" "null" "null" } }
	ruler = { gender = female name = { first_name = "A" second_name = "B" } portrait = "human" texture = 0 hair = 1 clothes = 2 }
}
`)
	w("common/deposit_categories/00_cats.txt", "deposit_cat_blockers = { blocker = yes }\ndeposit_cat_rare = { }\n")
	w("common/ship_sizes/00_starbases.txt", "starbase_outpost = {\n\tmax_speed = 0\n}\n")
	w("flags/colors.txt", "colors = {\n\tred = { flag = hsv { 0 1 1 } }\n\tblue = { flag = hsv { 0.6 1 1 } }\n\tgreen = { flag = hsv { 0.3 1 1 } }\n}\n")
	w("flags/human/flag_human_1.dds", "x")
	w("flags/human/flag_human_2.dds", "x")
	w("gfx/interface/icons/deposits/d_ancient_ruins.dds", "x")
	w("gfx/interface/icons/modifiers/mod_planet_stability_add.dds", "x")
	w("launcher-settings.json", `{"rawVersion":"v4.1.3","version":"Test v4.1.3"}`)
	ud := filepath.Join(docs, "Paradox Interactive", "Stellaris")
	os.MkdirAll(ud, 0o755)
	os.WriteFile(filepath.Join(ud, "dlc_load.json"), []byte(`{"enabled_mods":["mod/ugc_123.mod"],"disabled_dlcs":["dlc_x"]}`), 0o644)
	return
}

func fixtureHandoff(t *testing.T) *Handoff {
	b, err := os.ReadFile("../tests/fixtures/Lua.log")
	if err != nil {
		t.Fatalf("run tests/test_civ6.py first: %v", err)
	}
	h := ParseHandoffLog(string(b))
	if h == nil {
		t.Fatal("no handoff in fixture")
	}
	return h
}

func TestHandoffParse(t *testing.T) {
	h := fixtureHandoff(t)
	if h.Level != "standard" || len(h.Civs) != 3 || h.Player().Name != "Roman Empire" || h.First().Name != "Japanese Empire" {
		t.Fatalf("bad handoff: %+v", h)
	}
	if got := strings.Join(h.Player().Cities, ","); got != "Rome,Cumae pipe,Antium" {
		t.Fatalf("cities %q", got)
	}
	if len(h.Techs) != 2 || len(h.Wonders) != 3 || len(h.Naturals) != 2 {
		t.Fatalf("techs/wonders/naturals: %v %v %v", h.Techs, h.Wonders, h.Naturals)
	}
	// a block cut short (missing lines) is rejected
	if ParseHandoffLog("x: SA|BEGIN|1\nx: SA|CIV|1|1|1|A|A|A\nx: SA|END|9\n") != nil {
		t.Fatal("accepted a truncated block")
	}
	// chunked feeding works across line breaks
	var r HandoffReader
	b, _ := os.ReadFile("../tests/fixtures/Lua.log")
	for i := 0; i < len(b); i += 7 {
		j := i + 7
		if j > len(b) {
			j = len(b)
		}
		r.Feed(string(b[i:j]))
	}
	if len(r.Done) != 1 {
		t.Fatalf("chunked: %d blocks", len(r.Done))
	}
	d := ParseHandoffLog("SA_Handoff: SA|BEGIN|1\nSA_Handoff: SA|DEFEAT|Japanese Empire\nSA_Handoff: SA|END|3\n")
	if d == nil || d.Defeat != "Japanese Empire" {
		t.Fatal("defeat block not read")
	}
}

var structural = map[string]bool{"namespace": true, "country_event": true, "event": true, "id": true, "hide_window": true, "is_triggered_only": true,
	"trigger": true, "immediate": true, "if": true, "limit": true, "NOT": true, "capital_scope": true, "solar_system": true, "modifier": true,
	"days": true, "source": true, "max_jumps": true, "owner": true, "size": true, "events": true,
	"on_game_start_country": true, "on_game_start": true, "on_yearly_pulse_country": true}

func TestGenerateAndInstall(t *testing.T) {
	sh, err := LoadSheets()
	if err != nil {
		t.Fatal(err)
	}
	game, docs := fakeStellaris(t)
	st := &StellarisInstall{Game: game, UserDir: filepath.Join(docs, "Paradox Interactive", "Stellaris")}
	if err := st.Inspect(); err != nil {
		t.Fatal(err)
	}
	if st.tmplFrom != "00_test.txt:earthlings" || st.depCat != "deposit_cat_rare" || !st.hasOutpost || st.version != "v4.1.*" {
		t.Fatalf("inspect: %+v", st)
	}
	h := fixtureHandoff(t)
	m, err := st.Generate(sh, h)
	if err != nil {
		t.Fatal(err)
	}
	pc := m.Files["prescripted_countries/sa_countries.txt"]
	if strings.Count(pc, "spawn_enabled = always") != 2 || strings.Count(pc, "spawn_enabled = no") != 1 {
		t.Fatal("spawn rules wrong:\n" + pc)
	}
	if strings.Contains(pc, "initializer") || strings.Contains(pc, "first_name") {
		t.Fatal("template's Sol initializer or ruler name leaked:\n" + pc)
	}
	for _, want := range []string{"sa_civ_1", "sa_player", "sa_first", "sa_ahead_of_player", `name = "SA_CIV_1_RULER"`, `"flag_human_`} {
		if !strings.Contains(pc, want) {
			t.Fatalf("prescripted missing %q:\n%s", want, pc)
		}
	}
	ev := m.Files["events/sa_events.txt"]
	for _, want := range []string{`set_name = "Rome"`, `set_name = "Kyōto"`, "sa_legacy_astrology", "sa_legacy_rocketry",
		"add_deposit = sa_wonder_pyramids", "add_deposit = sa_wonder_machu_picchu", `set_name = "Mount Everest"`, "sa_rare_2",
		"sa_space_race_leader_fleet", "create_starbase"} {
		if !strings.Contains(ev, want) {
			t.Fatalf("events missing %q:\n%s", want, ev)
		}
	}
	if strings.Contains(ev, "sa_legacy_writing") {
		t.Fatal("granted a tech the player did not research")
	}
	// every key the events use is structural or a documented Stellaris effect/trigger
	kwb, _ := os.ReadFile("stellaris_keywords.txt")
	allowed := map[string]bool{}
	for _, l := range strings.Split(string(kwb), "\n") {
		if l = strings.TrimSpace(l); l != "" && !strings.HasPrefix(l, "#") {
			allowed[l] = true
		}
	}
	for _, src := range []string{ev, m.Files["common/on_actions/sa_on_actions.txt"]} {
		for _, k := range regexp.MustCompile(`([A-Za-z_][A-Za-z0-9_]*)\s*=`).FindAllStringSubmatch(src, -1) {
			if !structural[k[1]] && !allowed[k[1]] {
				t.Errorf("events use %q, which is not in stellaris_keywords.txt", k[1])
			}
		}
	}
	// modifiers in generated files are the sheet's
	sm := m.Files["common/static_modifiers/sa_static_modifiers.txt"]
	if !strings.Contains(sm, "sa_rare_1 = {") || !strings.Contains(sm, "country_minerals_produces_mult = 0.05") {
		t.Fatal("static modifiers:\n" + sm)
	}
	lf := m.Files["localisation/english/sa_handoff_l_english.yml"]
	if !strings.HasPrefix(lf, "\xef\xbb\xbfl_english:\n") || !strings.Contains(lf, `SA_CIV_2_NAME:0 "Japanese Empire"`) || !strings.Contains(lf, `sa_wonder_pyramids:0 "Pyramids"`) {
		t.Fatal("localisation:\n" + lf)
	}

	dir, err := st.Install(sh, m)
	if err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(dir, "descriptor.mod")); err != nil {
		t.Fatal(err)
	}
	outer, _ := os.ReadFile(filepath.Join(st.UserDir, "mod", "stellar_ascension.mod"))
	if !strings.Contains(string(outer), `supported_version="v4.1.*"`) || !strings.Contains(string(outer), "path=\"") {
		t.Fatal("outer descriptor:\n" + string(outer))
	}
	var dl map[string][]string
	b, _ := os.ReadFile(filepath.Join(st.UserDir, "dlc_load.json"))
	json.Unmarshal(b, &dl)
	if strings.Join(dl["enabled_mods"], ",") != "mod/ugc_123.mod,mod/stellar_ascension.mod" || strings.Join(dl["disabled_dlcs"], ",") != "dlc_x" {
		t.Fatalf("dlc_load.json: %s", b)
	}
	// installing twice does not duplicate the entry
	st.Install(sh, m)
	b, _ = os.ReadFile(filepath.Join(st.UserDir, "dlc_load.json"))
	if strings.Count(string(b), "stellar_ascension") != 1 {
		t.Fatalf("duplicated: %s", b)
	}
	for _, s := range m.Summary {
		t.Log(s)
	}
}

func TestPdxRoundTrip(t *testing.T) {
	src := "a = { b = 1 c = \"x y\" d = hsv { 0 1 1 } e >= 3 { anon = 1 } list = { one two } }\n"
	es := ParsePdx(src)
	out := WritePdx(es, 0)
	if err := Balanced(out); err != nil {
		t.Fatal(err)
	}
	again := WritePdx(ParsePdx(out), 0)
	if again != out {
		t.Fatalf("not stable:\n%s\n%s", out, again)
	}
	a := Find(es, "a")
	if Find(a.Block, "d").Value != "hsv" || Find(a.Block, "e").Op != ">=" || len(Find(a.Block, "list").Block) != 2 {
		t.Fatalf("parse: %s", out)
	}
}
