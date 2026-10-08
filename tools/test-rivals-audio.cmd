@echo off
rem Double-click: tries to extract sound from a few Marvel Rivals videos (read-only for the game).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0test-rivals-audio.ps1" > "%~dp0rivals-audio-report.txt" 2>&1
start notepad "%~dp0rivals-audio-report.txt"
start "" "%TEMP%\rivals-audio-test"
