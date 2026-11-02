@echo off
echo Repair inherited soundtrack album assignments, one folder at a time.
echo Song edits are preserved. /q stops; rerun this file to resume.
call "%~dp0Mp3Organizer.cmd" repair-albums "E:\Mp3-Ai-test\soundtracki" --workspace "E:\Mp3-Ai-test-workspace"
set "repairExit=%errorlevel%"
echo.
echo After confirming albums, run 04.Apply-Reviewed.cmd to update copies and playlists.
pause
exit /b %repairExit%
