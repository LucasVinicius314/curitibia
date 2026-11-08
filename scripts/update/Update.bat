@echo off
:: Double-click to update Curitibia to the latest release. One line, so replacing this file mid-run is safe.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update.ps1" & echo. & pause & exit /b
