@echo off
echo Resuming saved MP3 processing. Configuration and progress are reused.
call "%~dp0Mp3Organizer.cmd" run --online --workspace "E:\Mp3-Ai-test-workspace" --target "E:\Mp3-Ai-test-organized" --reports "E:\Mp3-Ai-test-reports"
set "organizerExit=%errorlevel%"
if not "%organizerExit%"=="0" echo Processing stopped with an error. Progress is saved; see the message above.
echo Press any key to close this window.
pause >nul
exit /b %organizerExit%
