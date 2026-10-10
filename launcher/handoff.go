package main

// Reads the handoff block that Civ VI's UI script prints to Lua.log
// (record types: sheets/handoff.json).

import (
	"strconv"
	"strings"
)

type Civ struct {
	Slot       int
	IsPlayer   bool
	SpaceOrder int // 1 = first to space, 0 = never
	Name       string
	Adjective  string
	Leader     string
	Sex        string // "male" or "female" (Civ VI Leaders.Sex); empty in older handoffs
	Cities     []string
}

type Named struct {
	Type, Name, Description string
}

type Handoff struct {
	Level    string
	Civs     []*Civ
	Techs    []string
	Wonders  []Named
	Naturals []Named
	Defeat   string // set when the player lost the space race on Ruthless
}

func (h *Handoff) Player() *Civ {
	for _, c := range h.Civs {
		if c.IsPlayer {
			return c
		}
	}
	return nil
}

func (h *Handoff) First() *Civ {
	for _, c := range h.Civs {
		if c.SpaceOrder == 1 {
			return c
		}
	}
	return nil
}

// HandoffReader is fed Lua.log text in chunks and yields complete blocks.
type HandoffReader struct {
	partial string
	cur     *Handoff
	records int
	Done    []*Handoff
}

func (r *HandoffReader) Feed(chunk string) {
	r.partial += chunk
	for {
		i := strings.IndexByte(r.partial, '\n')
		if i < 0 {
			return
		}
		line := strings.TrimRight(r.partial[:i], "\r")
		r.partial = r.partial[i+1:]
		r.line(line)
	}
}

func (r *HandoffReader) Reset() { r.partial, r.cur, r.records = "", nil, 0 }

func (r *HandoffReader) line(line string) {
	var rec string
	switch {
	case strings.HasPrefix(line, "SA|"):
		rec = line[3:]
	default:
		i := strings.Index(line, ": SA|")
		if i < 0 {
			return
		}
		rec = line[i+5:]
	}
	f := strings.Split(rec, "|")
	at := func(n int) string {
		if n < len(f) {
			return strings.TrimSpace(f[n])
		}
		return ""
	}
	num := func(n int) int { v, _ := strconv.Atoi(at(n)); return v }
	if f[0] == "BEGIN" {
		r.cur, r.records = &Handoff{Level: "standard"}, 1
		return
	}
	if r.cur == nil {
		return
	}
	r.records++
	h := r.cur
	switch f[0] {
	case "LEVEL":
		h.Level = at(1)
	case "CIV":
		h.Civs = append(h.Civs, &Civ{Slot: num(1), IsPlayer: at(2) == "1", SpaceOrder: num(3), Name: at(4), Adjective: at(5), Leader: at(6), Sex: strings.ToLower(at(7))})
	case "CITY":
		for _, c := range h.Civs {
			if c.Slot == num(1) && at(2) != "" {
				c.Cities = append(c.Cities, at(2))
			}
		}
	case "TECH":
		h.Techs = append(h.Techs, at(1))
	case "WONDER":
		h.Wonders = append(h.Wonders, Named{at(1), at(2), at(3)})
	case "NATURAL":
		h.Naturals = append(h.Naturals, Named{at(1), at(2), ""})
	case "DEFEAT":
		h.Defeat = at(1)
	case "END":
		// A block that lost lines (the log was cut mid-write) is dropped.
		if num(1) == r.records && (h.Defeat != "" || h.Player() != nil) {
			r.Done = append(r.Done, h)
		}
		r.cur = nil
	}
}

// ParseHandoffLog returns the last complete block in a whole log.
func ParseHandoffLog(text string) *Handoff {
	var r HandoffReader
	r.Feed(text + "\n")
	if len(r.Done) == 0 {
		return nil
	}
	return r.Done[len(r.Done)-1]
}
