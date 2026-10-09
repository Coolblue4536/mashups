//go:build windows

package main

import (
	"os/exec"
	"strings"
	"syscall"
)

func hide(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
}

// running reports whether any process with one of these image names is alive.
func running(names []string) bool {
	cmd := exec.Command("tasklist", "/FO", "CSV", "/NH")
	hide(cmd)
	out, err := cmd.Output()
	if err != nil {
		return false
	}
	text := strings.ToLower(string(out))
	for _, n := range names {
		if strings.Contains(text, "\""+strings.ToLower(n)+"\"") {
			return true
		}
	}
	return false
}

func detach(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: 0x00000008 | 0x00000200} // DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
}
