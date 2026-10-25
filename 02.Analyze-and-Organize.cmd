@echo off
echo STEP 2 - Analyze pending files and organize resolved tracks
echo This step also applies Ready tracks automatically; no separate apply command is needed.
echo.
call "%~dp0Mp3Organizer.cmd" run --online --workspace "E:\Mp3-Ai-test-workspace" --target "E:\Mp3-Ai-test-organized" --reports "E:\Mp3-Ai-test-reports"
set "organizerExit=%errorlevel%"
echo.
if not "%organizerExit%"=="0" goto failed
echo NEXT: 03.Review.cmd if Needs review is greater than zero. Otherwise you are done, except any reported errors.
goto finish
:failed
echo This step reported an error. Read the message above before continuing. Progress already saved is retained.
:finish
echo Press any key to close this window.
pause >nul
exit /b %organizerExit%