@echo off
echo STEP 1 - Scan for new and changed music
echo Existing unchanged files keep their progress.
echo.
call "%~dp0Mp3Organizer.cmd" scan "E:\Mp3-Ai-test" --workspace "E:\Mp3-Ai-test-workspace"
set "organizerExit=%errorlevel%"
echo.
if not "%organizerExit%"=="0" goto failed
echo NEXT: 02.Analyze-and-Organize.cmd
goto finish
:failed
echo This step reported an error. Read the message above before continuing. Progress already saved is retained.
:finish
echo Press any key to close this window.
pause >nul
exit /b %organizerExit%