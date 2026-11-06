@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Repair-Folder-Tracks.ps1" "%~1"
set "repairExit=%errorlevel%"
pause
exit /b %repairExit%
