package main

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Current Civ VI builds log under %LOCALAPPDATA%\Firaxis Games, not Documents/My Games.
func TestLuaLogLocations(t *testing.T) {
	t.Setenv("LOCALAPPDATA", filepath.FromSlash("/la"))
	got := luaLogs(paths{mygames: filepath.FromSlash("/docs/My Games")})
	want := []string{
		filepath.FromSlash("/la/Firaxis Games/Sid Meier's Civilization VI/Logs/Lua.log"),
		filepath.FromSlash("/docs/My Games/Sid Meier's Civilization VI/Logs/Lua.log"),
	}
	if strings.Join(got, "|") != strings.Join(want, "|") {
		t.Fatalf("got %q", got)
	}
}

// Only handoffs written after the launcher started count, and a new log (Civ VI
// restarted) is read from the top.
func TestLogTail(t *testing.T) {
	fixture, err := os.ReadFile("../tests/fixtures/Lua.log")
	if err != nil {
		t.Fatal(err)
	}
	p := filepath.Join(t.TempDir(), "Lua.log")
	os.WriteFile(p, fixture, 0o644) // an old game's handoff
	fi, _ := os.Stat(p)
	tail := &logTail{path: p, offset: fi.Size()}
	if got := tail.poll(); len(got) != 0 {
		t.Fatalf("old handoff picked up: %d", len(got))
	}
	f, _ := os.OpenFile(p, os.O_APPEND|os.O_WRONLY, 0o644)
	f.Write(fixture)
	f.Close()
	if got := tail.poll(); len(got) != 1 || got[0].Player().Name != "Roman Empire" {
		t.Fatalf("appended handoff: %d", len(got))
	}
	os.WriteFile(p, []byte("SA_Gameplay: SA_EVENT|LOADED|standard\n"), 0o644) // Civ VI restarted
	tail.poll()
	f, _ = os.OpenFile(p, os.O_APPEND|os.O_WRONLY, 0o644)
	f.Write(fixture)
	f.Close()
	if got := tail.poll(); len(got) != 1 {
		t.Fatalf("handoff after restart: %d", len(got))
	}
}
