package main

// The design sheets are embedded into the launcher at build time (build.sh copies
// sheets/*.json to launcher/sheets/). They are the source of truth for every
// Stellaris row the launcher writes.

import (
	"embed"
	"encoding/json"
	"fmt"
	"strings"
)

//go:embed sheets/*.json
var sheetFS embed.FS

type Sheet struct {
	Sheet string            `json:"sheet"`
	Rows  []map[string]any  `json:"rows"`
	Cols  map[string]string `json:"columns"`
}

type Sheets map[string]*Sheet

func LoadSheets() (Sheets, error) {
	out := Sheets{}
	ents, err := sheetFS.ReadDir("sheets")
	if err != nil {
		return nil, err
	}
	for _, e := range ents {
		b, err := sheetFS.ReadFile("sheets/" + e.Name())
		if err != nil {
			return nil, err
		}
		var s Sheet
		if err := json.Unmarshal(b, &s); err != nil {
			return nil, fmt.Errorf("%s: %w", e.Name(), err)
		}
		out[s.Sheet] = &s
	}
	return out, nil
}

func str(v any) string {
	switch t := v.(type) {
	case string:
		return t
	case float64:
		return strings.TrimRight(strings.TrimRight(fmt.Sprintf("%.4f", t), "0"), ".")
	case bool:
		if t {
			return "true"
		}
		return "false"
	}
	return fmt.Sprint(v)
}

// Row finds the row whose col equals key, falling back to the DEFAULT row.
func (s Sheets) Row(sheet, col, key string) map[string]any {
	var def map[string]any
	for _, r := range s[sheet].Rows {
		if str(r[col]) == key {
			return r
		}
		if str(r[col]) == "DEFAULT" {
			def = r
		}
	}
	return def
}

func (s Sheets) Const(key string) string {
	r := s.Row("constants", "key", key)
	if r == nil {
		return ""
	}
	return str(r["value"])
}

func (s Sheets) Modifier(id string) map[string]any { return s.Row("modifiers", "id", id) }
