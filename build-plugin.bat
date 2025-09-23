@echo off
:: build-plugin.bat
:: Wrapper to run PowerShell build script

echo Running PowerShell build script...
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-plugin.ps1"

echo.
pause
