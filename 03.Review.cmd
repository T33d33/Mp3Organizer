@echo off
echo STEP 3 - Review unresolved tracks
echo Type /q and press Enter to stop review. Saved decisions are retained; unresolved tracks can wait.
echo.
call "%~dp0Mp3Organizer.cmd" review --workspace "E:\Mp3-Ai-test-workspace"
set "organizerExit=%errorlevel%"
echo.
if not "%organizerExit%"=="0" goto failed
echo NEXT: 04.Apply-Reviewed.cmd to organize the accepted tracks.
goto finish
:failed
echo This step reported an error. Read the message above before continuing. Progress already saved is retained.
:finish
echo Press any key to close this window.
pause >nul
exit /b %organizerExit%