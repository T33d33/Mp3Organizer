@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-Online.ps1"
if errorlevel 1 echo Setup did not finish. Read the message above; settings already saved are kept.
echo Press any key to close setup and return to your terminal.
pause
