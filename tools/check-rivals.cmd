@echo off
rem Double-click: read-only check of how Marvel Rivals stores its files.
rem Saves the report next to this file and opens it in Notepad.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0inspect-game.ps1" -AppId 2767030 > "%~dp0rivals-report.txt" 2>&1
start notepad "%~dp0rivals-report.txt"
