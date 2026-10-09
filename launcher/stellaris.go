package main

// Builds the Stellaris half of the mashup for one handoff: a mod in
// Documents/Paradox Interactive/Stellaris/mod/stellar_ascension, enabled in
// dlc_load.json. Empires are copied from the player's own Stellaris human
// template, so portraits, rooms, flags and civics are always ones their game has.

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strings"
)

type StellarisInstall struct {
	Game       string // {game:stellaris}
	UserDir    string // Documents/Paradox Interactive/Stellaris
	Warnings   []string
	template   []Entry
	tmplFrom   string
	depCat     string
	depIcon    string
	modIcon    string
	flagCat    string
	flagFiles  []string
	colors     []string
	hasOutpost bool
	version    string
}

func (s *StellarisInstall) warn(f string, a ...any) {
	s.Warnings = append(s.Warnings, fmt.Sprintf(f, a...))
}

func readFiles(dir, pattern string) map[string]string {
	out := map[string]string{}
	paths, _ := filepath.Glob(filepath.Join(dir, pattern))
	sort.Strings(paths)
	for _, p := range paths {
		if b, err := os.ReadFile(p); err == nil {
			out[p] = string(b)
		}
	}
	return out
}

func stems(dir, ext string) []string {
	var out []string
	ents, _ := os.ReadDir(dir)
	for _, e := range ents {
		if !e.IsDir() && strings.EqualFold(filepath.Ext(e.Name()), ext) {
			out = append(out, strings.TrimSuffix(e.Name(), filepath.Ext(e.Name())))
		}
	}
	sort.Strings(out)
	return out
}

func pick(have []string, prefer ...string) string {
	set := map[string]bool{}
	for _, h := range have {
		set[h] = true
	}
	for _, p := range prefer {
		if set[p] {
			return p
		}
	}
	if len(have) > 0 {
		return have[0]
	}
	return ""
}

// Inspect reads what this player's Stellaris has: the human empire template,
// deposit categories, icons, flags, colors and the outpost starbase size.
func (s *StellarisInstall) Inspect() error {
	files := readFiles(filepath.Join(s.Game, "prescripted_countries"), "*.txt")
	if len(files) == 0 {
		return fmt.Errorf("no prescripted_countries in %s: is this the Stellaris folder?", s.Game)
	}
	keys := make([]string, 0, len(files))
	for k := range files {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	for _, p := range keys {
		for _, e := range ParsePdx(files[p]) {
			if !e.IsBlk {
				continue
			}
			sp := Find(e.Block, "species")
			if sp != nil && sp.IsBlk {
				if c := Find(sp.Block, "class"); c != nil && Unquote(c.Value) == "HUM" && Find(e.Block, "ruler") != nil {
					s.template, s.tmplFrom = e.Block, filepath.Base(p)+":"+e.Key
					break
				}
			}
		}
		if s.template != nil {
			break
		}
	}
	if s.template == nil {
		return fmt.Errorf("no human (class HUM) empire found in %s/prescripted_countries", s.Game)
	}

	var cats []string
	for _, src := range readFiles(filepath.Join(s.Game, "common", "deposit_categories"), "*.txt") {
		for _, e := range ParsePdx(src) {
			if e.IsBlk && Find(e.Block, "blocker") == nil {
				cats = append(cats, e.Key)
			}
		}
	}
	sort.Strings(cats)
	s.depCat = pick(cats, "deposit_cat_rare", "deposit_cat_research", "deposit_cat_energy")
	if s.depCat == "" {
		s.warn("no deposit categories found; wonder features may not show")
		s.depCat = "deposit_cat_rare"
	}
	s.depIcon = pick(stems(filepath.Join(s.Game, "gfx", "interface", "icons", "deposits"), ".dds"),
		"d_ancient_ruins", "d_ruins", "d_archaeological_site", "d_ancient_monument")
	s.modIcon = pick(stems(filepath.Join(s.Game, "gfx", "interface", "icons", "modifiers"), ".dds"),
		"mod_country_physics_research_produces_mult", "mod_planet_stability_add")

	if fe := Find(s.template, "empire_flag"); fe != nil && fe.IsBlk {
		if ic := Find(fe.Block, "icon"); ic != nil && ic.IsBlk {
			if c := Find(ic.Block, "category"); c != nil {
				s.flagCat = Unquote(c.Value)
				for _, st := range stems(filepath.Join(s.Game, "flags", s.flagCat), ".dds") {
					s.flagFiles = append(s.flagFiles, st+".dds")
				}
			}
		}
	}
	for _, src := range readFiles(filepath.Join(s.Game, "flags"), "colors.txt") {
		for _, e := range ParsePdx(src) {
			if e.Key == "colors" && e.IsBlk {
				for _, c := range e.Block {
					if c.IsBlk && c.Key != "" {
						s.colors = append(s.colors, c.Key)
					}
				}
			}
		}
	}
	for _, src := range readFiles(filepath.Join(s.Game, "common", "ship_sizes"), "*.txt") {
		if regexp.MustCompile(`(?m)^\s*starbase_outpost\s*=`).MatchString(src) {
			s.hasOutpost = true
		}
	}
	if !s.hasOutpost {
		s.warn("starbase_outpost not found: rare systems will be named but not pre-claimed")
	}
	if b, err := os.ReadFile(filepath.Join(s.Game, "launcher-settings.json")); err == nil {
		var ls struct {
			RawVersion string `json:"rawVersion"`
		}
		if json.Unmarshal(b, &ls) == nil {
			if m := regexp.MustCompile(`v?(\d+)\.(\d+)`).FindStringSubmatch(ls.RawVersion); m != nil {
				s.version = "v" + m[1] + "." + m[2] + ".*"
			}
		}
	}
	if s.version == "" {
		s.version = "v4.*"
	}
	return nil
}

// --- generation -----------------------------------------------------------------

type loc map[string]string

func (l loc) add(k, v string) string { l[k] = v; return k }

func locFile(l loc) string {
	keys := make([]string, 0, len(l))
	for k := range l {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	var b strings.Builder
	b.WriteString("\xef\xbb\xbfl_english:\n")
	for _, k := range keys {
		v := strings.NewReplacer("\"", "'", "\n", " ", "\r", " ").Replace(l[k])
		fmt.Fprintf(&b, " %s:0 \"%s\"\n", k, v)
	}
	return b.String()
}

var nonID = regexp.MustCompile(`[^a-z0-9_]+`)

func ident(s string) string {
	return strings.Trim(nonID.ReplaceAllString(strings.ToLower(s), "_"), "_")
}

// GeneratedMod is every file of the Stellaris mod, by path relative to the mod folder.
type GeneratedMod struct {
	Files   map[string]string
	Summary []string
}

func flagSet(h *Handoff, c *Civ) []string {
	f := []string{"sa_civ", fmt.Sprintf("sa_civ_%d", c.Slot)}
	if c.IsPlayer {
		f = append(f, "sa_player")
	}
	if c.SpaceOrder == 1 {
		f = append(f, "sa_first")
	}
	if p := h.Player(); !c.IsPlayer && p != nil && c.SpaceOrder > 0 && c.SpaceOrder < p.SpaceOrder {
		f = append(f, "sa_ahead_of_player")
	}
	return f
}

func cloneEntries(es []Entry) []Entry {
	out := make([]Entry, len(es))
	for i, e := range es {
		out[i] = e
		if e.Block != nil {
			out[i].Block = cloneEntries(e.Block)
		}
	}
	return out
}

func (s *StellarisInstall) Generate(sh Sheets, h *Handoff) (*GeneratedMod, error) {
	if h.Player() == nil {
		return nil, fmt.Errorf("handoff has no player civilization")
	}
	L := loc{}
	files := map[string]string{}
	var summary []string
	level := sh.Row("space_race", "level", h.Level)
	if level == nil {
		level = sh.Row("space_race", "level", "standard")
	}

	// hook stellaris_empires: one prescripted empire per civ (rows: handoff CIV records)
	var pc strings.Builder
	pc.WriteString("# Stellar Ascension: generated from your Civilization VI game. Template: " + s.tmplFrom + "\n")
	for i, c := range h.Civs {
		b := cloneEntries(s.template)
		for _, k := range []string{"initializer", "default", "playable", "spawn_as_fallen", "secondary_species"} {
			b = Remove(b, k)
		}
		key := fmt.Sprintf("SA_CIV_%d", c.Slot)
		capital := c.Name
		if len(c.Cities) > 0 {
			capital = c.Cities[0]
		}
		b = Set(b, "name", Quote(L.add(key+"_NAME", c.Name)))
		b = Set(b, "adjective", Quote(L.add(key+"_ADJ", c.Adjective)))
		if c.IsPlayer {
			b = Set(b, "spawn_enabled", "no")
		} else {
			b = Set(b, "spawn_enabled", "always")
		}
		b = Set(b, "ignore_portrait_duplication", "yes")
		b = Set(b, "planet_name", Quote(L.add(key+"_PLANET", capital)))
		b = Set(b, "system_name", Quote(L.add(key+"_SYSTEM", capital)))
		if r := Find(b, "ruler"); r != nil && r.IsBlk {
			r.Block = Set(Remove(r.Block, "name"), "name", Quote(L.add(key+"_RULER", c.Leader)))
		}
		var flags []Entry
		for _, f := range flagSet(h, c) {
			flags = append(flags, Entry{Key: f})
		}
		b = SetBlock(b, "flags", flags)
		if fe := Find(b, "empire_flag"); fe != nil && fe.IsBlk {
			if ic := Find(fe.Block, "icon"); ic != nil && ic.IsBlk && len(s.flagFiles) > 0 {
				ic.Block = Set(ic.Block, "file", Quote(s.flagFiles[(i*7)%len(s.flagFiles)]))
			}
			if co := Find(fe.Block, "colors"); co != nil && co.IsBlk && len(s.colors) > 1 && len(co.Block) > 0 {
				co.Block[0] = Entry{Key: Quote(s.colors[(i*5)%len(s.colors)])}
			}
		}
		fmt.Fprintf(&pc, "%s = {\n%s}\n", ident(key), WritePdx(b, 1))
		role := "rival"
		if c.IsPlayer {
			role = "you"
		}
		summary = append(summary, fmt.Sprintf("empire: %s (%s), led by %s, capital %s, reached space #%d", c.Name, role, c.Leader, capital, c.SpaceOrder))
	}
	files["prescripted_countries/sa_countries.txt"] = pc.String()

	// static modifiers: every modifiers row, plus one per natural wonder system
	var sm strings.Builder
	sm.WriteString("# Stellar Ascension: from sheets/modifiers.json\n")
	writeMod := func(id, key, value, name, effect string) {
		fmt.Fprintf(&sm, "%s = {\n", id)
		if s.modIcon != "" {
			fmt.Fprintf(&sm, "\ticon = \"gfx/interface/icons/modifiers/%s.dds\"\n", s.modIcon)
		}
		fmt.Fprintf(&sm, "\t%s = %s\n}\n", key, value)
		L.add(id, name)
		L.add(id+"_desc", effect)
	}
	for _, r := range sh["modifiers"].Rows {
		writeMod(str(r["id"]), str(r["stellaris_modifier"]), str(r["value"]), str(r["name"]), str(r["effect_text"]))
	}

	// hook stellaris_game_start (per country): names, legacy techs, wonders, space race
	var ev strings.Builder
	ev.WriteString("# Stellar Ascension: generated from your Civilization VI game\nnamespace = stellar_ascension\n\n")
	ev.WriteString("country_event = {\n\tid = stellar_ascension.1\n\thide_window = yes\n\tis_triggered_only = yes\n\ttrigger = { has_country_flag = sa_civ }\n\timmediate = {\n")
	maxNames := 8
	fmt.Sscan(sh.Const("max_home_planet_names"), &maxNames)
	for _, c := range h.Civs {
		fmt.Fprintf(&ev, "\t\tif = {\n\t\t\tlimit = { has_country_flag = sa_civ_%d }\n\t\t\tcapital_scope = {\n", c.Slot)
		if len(c.Cities) > 0 {
			fmt.Fprintf(&ev, "\t\t\t\tset_name = %s\n\t\t\t\tset_planet_flag = sa_named\n", Quote(c.Cities[0]))
			fmt.Fprintf(&ev, "\t\t\t\tsolar_system = { set_name = %s }\n", Quote(c.Cities[0]))
		}
		for j, city := range c.Cities {
			if j == 0 || j >= maxNames {
				continue
			}
			fmt.Fprintf(&ev, "\t\t\t\tsolar_system = { random_system_planet = { limit = { NOT = { has_planet_flag = sa_named } NOT = { is_star = yes } } set_name = %s set_planet_flag = sa_named } }\n", Quote(city))
		}
		ev.WriteString("\t\t\t}\n\t\t}\n")
	}
	// legacy techs (rows: techs sheet, filtered by the player's TECH records)
	ev.WriteString("\t\tif = {\n\t\t\tlimit = { has_country_flag = sa_player }\n")
	for _, t := range h.Techs {
		r := sh.Row("techs", "civ_tech", t)
		if r == nil || str(r["civ_tech"]) != t {
			continue
		}
		fmt.Fprintf(&ev, "\t\t\tadd_modifier = { modifier = %s days = -1 }\n", str(r["modifier"]))
		m := sh.Modifier(str(r["modifier"]))
		summary = append(summary, fmt.Sprintf("legacy tech: %s -> %s (%s)", t, str(m["name"]), str(m["effect_text"])))
	}
	// wonders (rows: wonders sheet; DEFAULT for wonders not listed)
	var dep strings.Builder
	dep.WriteString("# Stellar Ascension: your Civilization VI wonders, from sheets/wonders.json\n")
	if len(h.Wonders) > 0 {
		ev.WriteString("\t\t\tcapital_scope = {\n")
		for _, w := range h.Wonders {
			r := sh.Row("wonders", "civ_building", w.Type)
			if r == nil {
				continue
			}
			m := sh.Modifier(str(r["modifier"]))
			id := "sa_wonder_" + ident(strings.TrimPrefix(w.Type, "BUILDING_"))
			fmt.Fprintf(&dep, "%s = {\n\tis_for_colonizable = yes\n\tcategory = %s\n", id, s.depCat)
			if s.depIcon != "" {
				fmt.Fprintf(&dep, "\ticon = %s\n", s.depIcon)
			}
			fmt.Fprintf(&dep, "\tplanet_modifier = {\n\t\t%s = %s\n\t}\n\tpotential = { always = no }\n\tdrop_weight = { weight = 0 }\n}\n", str(m["stellaris_modifier"]), str(m["value"]))
			name := w.Name
			if name == "" {
				name = w.Type
			}
			desc := "Built by your civilization in the age before the stars. " + str(m["effect_text"]) + "."
			if w.Description != "" {
				desc = w.Description + " " + desc
			}
			L.add(id, name)
			L.add(id+"_desc", desc)
			fmt.Fprintf(&ev, "\t\t\t\tadd_deposit = %s\n", id)
			summary = append(summary, fmt.Sprintf("wonder: %s on your homeworld (%s)", name, str(m["effect_text"])))
		}
		ev.WriteString("\t\t\t}\n")
	}
	for _, m := range strings.Fields(str(level["player_first_modifiers"])) {
		if m != "none" {
			fmt.Fprintf(&ev, "\t\t\tif = { limit = { has_country_flag = sa_first } add_modifier = { modifier = %s days = -1 } }\n", m)
		}
	}
	ev.WriteString("\t\t}\n")
	for _, m := range strings.Fields(str(level["rival_lead_modifiers"])) {
		if m != "none" {
			fmt.Fprintf(&ev, "\t\tif = { limit = { has_country_flag = sa_ahead_of_player } add_modifier = { modifier = %s days = -1 } }\n", m)
		}
	}
	ev.WriteString("\t}\n}\n\n")

	// rare systems (rows: natural_wonders sheet; one per NATURAL record)
	maxRare, jumps := 8, 8
	fmt.Sscan(sh.Const("max_rare_systems"), &maxRare)
	fmt.Sscan(sh.Const("rare_system_max_jumps"), &jumps)
	nat := h.Naturals
	if len(nat) > maxRare {
		nat = nat[:maxRare]
	}
	ev.WriteString("event = {\n\tid = stellar_ascension.2\n\thide_window = yes\n\tis_triggered_only = yes\n\timmediate = {\n")
	ev.WriteString("\t\trandom_country = { limit = { has_country_flag = sa_first } save_event_target_as = sa_first capital_scope = { solar_system = { save_event_target_as = sa_first_home } } }\n")
	ev.WriteString("\t\tif = {\n\t\t\tlimit = { exists = event_target:sa_first_home }\n")
	var pulse strings.Builder
	for i, n := range nat {
		r := sh.Row("natural_wonders", "civ_feature", n.Type)
		if r == nil {
			continue
		}
		m := sh.Modifier(str(r["modifier"]))
		id := fmt.Sprintf("sa_rare_%d", i+1)
		writeMod(id, str(m["stellaris_modifier"]), str(m["value"]), n.Name, str(m["effect_text"])+" (rare system "+n.Name+")")
		claim := ""
		if s.hasOutpost {
			claim = " create_starbase = { owner = event_target:sa_first size = starbase_outpost }"
		}
		body := fmt.Sprintf("set_star_flag = sa_rare set_star_flag = %s set_name = %s%s", id, Quote(n.Name), claim)
		fmt.Fprintf(&ev, "\t\t\trandom_system = { limit = { has_owner = no NOT = { has_star_flag = sa_rare } distance = { source = event_target:sa_first_home max_jumps = %d } } %s }\n", jumps, body)
		fmt.Fprintf(&ev, "\t\t\tif = { limit = { NOT = { any_system = { has_star_flag = %s } } } random_system = { limit = { has_owner = no NOT = { has_star_flag = sa_rare } } %s } }\n", id, body)
		fmt.Fprintf(&pulse, "\t\tif = { limit = { any_system_within_border = { has_star_flag = %s } NOT = { has_modifier = %s } } add_modifier = { modifier = %s days = -1 } }\n", id, id, id)
		fmt.Fprintf(&pulse, "\t\tif = { limit = { NOT = { any_system_within_border = { has_star_flag = %s } } has_modifier = %s } remove_modifier = %s }\n", id, id, id)
		owner := "nobody"
		if f := h.First(); f != nil {
			owner = f.Name
		}
		summary = append(summary, fmt.Sprintf("rare system: %s, claimed by %s (%s)", n.Name, owner, str(m["effect_text"])))
	}
	ev.WriteString("\t\t\tevery_country = { limit = { has_country_flag = sa_civ } country_event = { id = stellar_ascension.3 } }\n")
	ev.WriteString("\t\t}\n\t}\n}\n\n")
	// hook stellaris_yearly: the rare system bonus follows whoever owns the system
	ev.WriteString("country_event = {\n\tid = stellar_ascension.3\n\thide_window = yes\n\tis_triggered_only = yes\n\timmediate = {\n")
	if pulse.Len() == 0 {
		ev.WriteString("\t\tset_country_flag = sa_no_rare_systems\n")
	}
	ev.WriteString(pulse.String())
	ev.WriteString("\t}\n}\n")

	files["common/static_modifiers/sa_static_modifiers.txt"] = sm.String()
	files["common/deposits/sa_wonder_deposits.txt"] = dep.String()
	files["events/sa_events.txt"] = ev.String()
	files["common/on_actions/sa_on_actions.txt"] = "# Stellar Ascension\non_game_start_country = {\n\tevents = {\n\t\tstellar_ascension.1\n\t}\n}\non_game_start = {\n\tevents = {\n\t\tstellar_ascension.2\n\t}\n}\non_yearly_pulse_country = {\n\tevents = {\n\t\tstellar_ascension.3\n\t}\n}\n"
	files["localisation/english/sa_handoff_l_english.yml"] = locFile(L)

	for p, src := range files {
		if strings.HasSuffix(p, ".txt") {
			if err := Balanced(src); err != nil {
				return nil, fmt.Errorf("generated %s is malformed: %w", p, err)
			}
		}
	}
	return &GeneratedMod{Files: files, Summary: summary}, nil
}

// Install writes the mod, its two descriptors, and enables it in dlc_load.json.
func (s *StellarisInstall) Install(sh Sheets, m *GeneratedMod) (string, error) {
	folder := sh.Const("stellaris_mod_folder")
	modRoot := filepath.Join(s.UserDir, "mod")
	dir := filepath.Join(modRoot, folder)
	if err := os.RemoveAll(dir); err != nil {
		return "", err
	}
	for rel, src := range m.Files {
		p := filepath.Join(dir, filepath.FromSlash(rel))
		if err := os.MkdirAll(filepath.Dir(p), 0o755); err != nil {
			return "", err
		}
		if err := os.WriteFile(p, []byte(src), 0o644); err != nil {
			return "", err
		}
	}
	desc := fmt.Sprintf("version=\"0.1.0\"\ntags={\n\t\"Gameplay\"\n}\nname=\"Stellar Ascension (your Civilization VI empire)\"\nsupported_version=\"%s\"\n", s.version)
	if err := os.WriteFile(filepath.Join(dir, "descriptor.mod"), []byte(desc), 0o644); err != nil {
		return "", err
	}
	outer := desc + "path=\"" + filepath.ToSlash(dir) + "\"\n"
	if err := os.WriteFile(filepath.Join(modRoot, folder+".mod"), []byte(outer), 0o644); err != nil {
		return "", err
	}

	// dlc_load.json: add our mod, keep everything the player already had.
	dl := filepath.Join(s.UserDir, "dlc_load.json")
	doc := map[string]any{}
	if b, err := os.ReadFile(dl); err == nil {
		if err := json.Unmarshal(b, &doc); err != nil {
			return "", fmt.Errorf("dlc_load.json is not valid JSON, leaving it alone: %w", err)
		}
		_ = os.WriteFile(dl+".stellar_ascension.bak", b, 0o644)
	}
	entry := "mod/" + folder + ".mod"
	var mods []any
	if v, ok := doc["enabled_mods"].([]any); ok {
		mods = v
	}
	found := false
	for _, v := range mods {
		if v == entry {
			found = true
		}
	}
	if !found {
		mods = append(mods, entry)
	}
	doc["enabled_mods"] = mods
	if _, ok := doc["disabled_dlcs"]; !ok {
		doc["disabled_dlcs"] = []any{}
	}
	b, _ := json.Marshal(doc)
	if err := os.WriteFile(dl, b, 0o644); err != nil {
		return "", err
	}
	return dir, nil
}
