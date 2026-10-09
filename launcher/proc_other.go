//go:build !windows

package main

import (
	"os/exec"
	"strings"
)

// Non-Windows builds exist for tests only; the games run on Windows.
func running(names []string) bool {
	out, err := exec.Command("ps", "-eo", "comm").Output()
	if err != nil {
		return false
	}
	for _, line := range strings.Split(string(out), "\n") {
		for _, n := range names {
			if strings.EqualFold(strings.TrimSpace(line), n) {
				return true
			}
		}
	}
	return false
}

func detach(cmd *exec.Cmd) {}
