package main

// A small reader/writer for Paradox (Clausewitz) script, enough to copy a block
// out of the player's own Stellaris files, change a few keys and write it back.

import (
	"fmt"
	"strings"
	"unicode"
)

// Entry is one `key op value`, `key op { ... }`, `key = word { ... }` (hsv/rgb
// colors) or a bare list item (`word` inside braces, Key only, Op empty).
type Entry struct {
	Key   string
	Op    string
	Value string  // scalar value, or the type word before a typed block
	Block []Entry // nil when scalar
	IsBlk bool
}

func tokenize(src string) []string {
	var toks []string
	i := 0
	for i < len(src) {
		c := src[i]
		switch {
		case c == '#':
			for i < len(src) && src[i] != '\n' {
				i++
			}
		case unicode.IsSpace(rune(c)) || c == 0xEF || c == 0xBB || c == 0xBF:
			i++
		case c == '{' || c == '}':
			toks = append(toks, string(c))
			i++
		case c == '=' || c == '<' || c == '>' || c == '!':
			if i+1 < len(src) && src[i+1] == '=' {
				toks = append(toks, src[i:i+2])
				i += 2
			} else {
				toks = append(toks, string(c))
				i++
			}
		case c == '"':
			j := i + 1
			for j < len(src) && src[j] != '"' {
				if src[j] == '\\' {
					j++
				}
				j++
			}
			if j >= len(src) {
				j = len(src) - 1
			}
			toks = append(toks, src[i:j+1])
			i = j + 1
		default:
			j := i
			for j < len(src) && !unicode.IsSpace(rune(src[j])) && !strings.ContainsRune("{}=<>!#\"", rune(src[j])) {
				j++
			}
			toks = append(toks, src[i:j])
			i = j
		}
	}
	return toks
}

func isOp(t string) bool {
	switch t {
	case "=", "<", ">", "<=", ">=", "!=", "==":
		return true
	}
	return false
}

// ParsePdx parses a whole file. Unbalanced braces are tolerated (the game does too).
func ParsePdx(src string) []Entry {
	toks := tokenize(src)
	pos := 0
	var parse func() []Entry
	parse = func() []Entry {
		var out []Entry
		for pos < len(toks) {
			t := toks[pos]
			if t == "}" {
				pos++
				return out
			}
			if t == "{" { // anonymous block in a list
				pos++
				out = append(out, Entry{Block: parse(), IsBlk: true})
				continue
			}
			pos++
			if pos < len(toks) && isOp(toks[pos]) {
				op := toks[pos]
				pos++
				if pos >= len(toks) {
					out = append(out, Entry{Key: t, Op: op})
					break
				}
				if toks[pos] == "{" {
					pos++
					out = append(out, Entry{Key: t, Op: op, Block: parse(), IsBlk: true})
				} else {
					v := toks[pos]
					pos++
					if pos < len(toks) && toks[pos] == "{" && !strings.HasPrefix(v, "\"") && (v == "hsv" || v == "rgb" || v == "hsv360" || v == "hex") {
						pos++
						out = append(out, Entry{Key: t, Op: op, Value: v, Block: parse(), IsBlk: true})
					} else {
						out = append(out, Entry{Key: t, Op: op, Value: v})
					}
				}
			} else {
				out = append(out, Entry{Key: t})
			}
		}
		return out
	}
	return parse()
}

// Find returns the first entry with key, or nil.
func Find(es []Entry, key string) *Entry {
	for i := range es {
		if es[i].Key == key {
			return &es[i]
		}
	}
	return nil
}

// Set replaces (or appends) a scalar key.
func Set(es []Entry, key, value string) []Entry {
	if e := Find(es, key); e != nil {
		e.Value, e.Block, e.IsBlk, e.Op = value, nil, false, "="
		return es
	}
	return append(es, Entry{Key: key, Op: "=", Value: value})
}

// SetBlock replaces (or appends) a block key.
func SetBlock(es []Entry, key string, block []Entry) []Entry {
	if e := Find(es, key); e != nil {
		e.Value, e.Block, e.IsBlk, e.Op = "", block, true, "="
		return es
	}
	return append(es, Entry{Key: key, Op: "=", Block: block, IsBlk: true})
}

// Remove drops every entry with key.
func Remove(es []Entry, key string) []Entry {
	out := es[:0:0]
	for _, e := range es {
		if e.Key != key {
			out = append(out, e)
		}
	}
	return out
}

// Unquote strips surrounding quotes.
func Unquote(s string) string {
	if len(s) >= 2 && s[0] == '"' && s[len(s)-1] == '"' {
		return s[1 : len(s)-1]
	}
	return s
}

// Quote makes a script string literal. Paradox strings have no escapes we can
// rely on, so double quotes become single quotes.
func Quote(s string) string {
	return "\"" + strings.ReplaceAll(s, "\"", "'") + "\""
}

// WritePdx renders entries with tabs.
func WritePdx(es []Entry, depth int) string {
	var b strings.Builder
	ind := strings.Repeat("\t", depth)
	for _, e := range es {
		b.WriteString(ind)
		switch {
		case e.Key == "" && e.IsBlk:
			b.WriteString("{\n" + WritePdx(e.Block, depth+1) + ind + "}\n")
			continue
		case e.Op == "":
			b.WriteString(e.Key + "\n")
			continue
		}
		b.WriteString(e.Key + " " + e.Op + " ")
		if e.IsBlk {
			if e.Value != "" {
				b.WriteString(e.Value + " ")
			}
			b.WriteString("{\n" + WritePdx(e.Block, depth+1) + ind + "}\n")
		} else {
			b.WriteString(e.Value + "\n")
		}
	}
	return b.String()
}

// Balanced reports whether braces balance, as a cheap syntax check on output.
func Balanced(src string) error {
	depth := 0
	for _, t := range tokenize(src) {
		switch t {
		case "{":
			depth++
		case "}":
			depth--
			if depth < 0 {
				return fmt.Errorf("unexpected }")
			}
		}
	}
	if depth != 0 {
		return fmt.Errorf("%d unclosed {", depth)
	}
	return nil
}
