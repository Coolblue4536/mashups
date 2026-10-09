// Stellar Ascension launcher. Melty starts it on Play with both games' folders:
//
//	StellarAscension.exe --civ6 {game} --stellaris {game:stellaris} --mygames {my-games} --documents {documents}
//
// It starts Civilization VI (Act 1), watches Lua.log for the handoff block the
// Civ VI mod prints when you reach space, builds the Stellaris mod from it, and
// starts Stellaris (Act 2) once Civ VI is closed.
package main

import (
	"flag"
	"fmt"
	"io"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

var civExes = []string{
	"Base/Binaries/Win64Steam/CivilizationVI.exe",
	"Base/Binaries/Win64Steam/CivilizationVI_DX12.exe",
	"Base/Binaries/Win64EOS/CivilizationVI.exe",
	"Base/Binaries/Win64EOS/CivilizationVI_DX12.exe",
}
var civProcs = []string{"CivilizationVI.exe", "CivilizationVI_DX12.exe"}

func firstExisting(root string, rels []string) string {
	for _, r := range rels {
		p := filepath.Join(root, filepath.FromSlash(r))
		if _, err := os.Stat(p); err == nil {
			return p
		}
	}
	return ""
}

func start(exe string) error {
	cmd := exec.Command(exe)
	cmd.Dir = filepath.Dir(exe)
	detach(cmd)
	return cmd.Start()
}

type paths struct{ civ6, stellaris, mygames, documents string }

func stellarisInstall(p paths) *StellarisInstall {
	return &StellarisInstall{Game: p.stellaris, UserDir: filepath.Join(p.documents, "Paradox Interactive", "Stellaris")}
}

// buildAct2 turns a handoff into the installed Stellaris mod.
func buildAct2(p paths, sh Sheets, h *Handoff) error {
	st := stellarisInstall(p)
	if err := st.Inspect(); err != nil {
		return err
	}
	m, err := st.Generate(sh, h)
	if err != nil {
		return err
	}
	dir, err := st.Install(sh, m)
	if err != nil {
		return err
	}
	log.Printf("Stellaris mod written to %s", dir)
	for _, w := range st.Warnings {
		log.Printf("warning: %s", w)
	}
	for _, s := range m.Summary {
		log.Printf("  %s", s)
	}
	return nil
}

func main() {
	var p paths
	var fromLog string
	var noLaunch bool
	flag.StringVar(&p.civ6, "civ6", "", "Civilization VI install folder")
	flag.StringVar(&p.stellaris, "stellaris", "", "Stellaris install folder")
	flag.StringVar(&p.mygames, "mygames", "", "Documents/My Games folder")
	flag.StringVar(&p.documents, "documents", "", "Documents folder")
	flag.StringVar(&fromLog, "from-log", "", "build Act 2 from an existing Lua.log and exit (no game launch)")
	flag.BoolVar(&noLaunch, "no-launch", false, "with --from-log: do not start Stellaris")
	flag.Parse()

	exe, _ := os.Executable()
	if f, err := os.OpenFile(filepath.Join(filepath.Dir(exe), "launcher.log"), os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o644); err == nil {
		log.SetOutput(io.MultiWriter(os.Stderr, f))
		defer f.Close()
	}
	log.Printf("Stellar Ascension launcher: civ6=%q stellaris=%q mygames=%q documents=%q", p.civ6, p.stellaris, p.mygames, p.documents)

	sh, err := LoadSheets()
	if err != nil {
		log.Fatalf("sheets: %v", err)
	}

	if fromLog != "" {
		b, err := os.ReadFile(fromLog)
		if err != nil {
			log.Fatal(err)
		}
		h := ParseHandoffLog(string(b))
		if h == nil {
			log.Fatal("no complete Stellar Ascension handoff in that log")
		}
		if h.Defeat != "" {
			log.Fatalf("that game ended in defeat (%s reached space first on Ruthless); no Act 2", h.Defeat)
		}
		if err := buildAct2(p, sh, h); err != nil {
			log.Fatal(err)
		}
		if !noLaunch {
			startStellaris(p)
		}
		return
	}

	if p.civ6 == "" || p.mygames == "" || p.documents == "" || p.stellaris == "" {
		log.Fatal("missing --civ6, --stellaris, --mygames or --documents (Melty passes these on Play)")
	}
	luaLog := filepath.Join(p.mygames, "Sid Meier's Civilization VI", "Logs", "Lua.log")
	var offset int64
	if fi, err := os.Stat(luaLog); err == nil {
		offset = fi.Size() // only handoffs written from now on count
	}

	civ := firstExisting(p.civ6, civExes)
	if civ == "" {
		log.Fatalf("Civilization VI's executable was not found under %s", p.civ6)
	}
	if !running(civProcs) {
		if err := start(civ); err != nil {
			log.Fatalf("starting Civilization VI: %v", err)
		}
	}
	log.Printf("Act 1: Civilization VI started (%s). Watching %s", civ, luaLog)

	var reader HandoffReader
	var handoff *Handoff
	seen, lastSeen := false, time.Now()
	for {
		time.Sleep(2 * time.Second)
		if fi, err := os.Stat(luaLog); err == nil {
			if fi.Size() < offset { // Civ VI rewrote the log
				offset = 0
				reader.Reset()
			}
			if fi.Size() > offset {
				if f, err := os.Open(luaLog); err == nil {
					f.Seek(offset, io.SeekStart)
					b, _ := io.ReadAll(f)
					f.Close()
					offset += int64(len(b))
					reader.Feed(string(b))
				}
			}
		}
		for _, h := range reader.Done {
			if h.Defeat != "" {
				log.Printf("You lost the space race to %s (Ruthless). No Act 2 this time.", h.Defeat)
				handoff = nil
			} else if handoff == nil {
				handoff = h
				log.Printf("Act 1 complete: %s reached space. Building Act 2.", h.Player().Name)
				if err := buildAct2(p, sh, h); err != nil {
					log.Printf("could not build the Stellaris mod: %v", err)
					handoff = nil
				}
			}
		}
		reader.Done = nil

		if running(civProcs) {
			seen, lastSeen = true, time.Now()
			continue
		}
		// Civ VI may restart itself through its store; wait a little before deciding it closed.
		wait := 15 * time.Second
		if !seen {
			wait = 90 * time.Second
		}
		if time.Since(lastSeen) > wait {
			break
		}
	}
	if handoff == nil {
		log.Printf("Civilization VI closed before reaching space. Your Act 1 save continues next time you press Play.")
		return
	}
	startStellaris(p)
}

func startStellaris(p paths) {
	exe := firstExisting(p.stellaris, []string{"stellaris.exe", "Stellaris.exe"})
	if exe == "" {
		log.Fatalf("stellaris.exe not found under %s", p.stellaris)
	}
	if err := start(exe); err != nil {
		log.Fatalf("starting Stellaris: %v", err)
	}
	log.Printf("Act 2: Stellaris started. Pick your empire (%s) in the empire list.", strings.TrimSpace(fmt.Sprint("Stellar Ascension")))
}
