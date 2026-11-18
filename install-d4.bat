@echo off
rem D4 - automatic installer. Double-click to run.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1"
echo.
pause
