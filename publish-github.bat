@echo off
rem D4 - publish the project to GitHub (repository + site + release). Double-click to run.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1"
echo.
pause
