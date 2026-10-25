@echo off
echo STEP 4 - Organize accepted tracks and update playlists
echo This uses the normal run pipeline. It also analyzes pending files if any remain; NeedsReview files stay blocked.
echo.
call "%~dp0Mp3Organizer.cmd" run --online --workspace "E:\Mp3-Ai-test-workspace" --target "E:\Mp3-Ai-test-organized" --reports "E:\Mp3-Ai-test-reports"
set "organizerExit=%errorlevel%"
echo.
if not "%organizerExit%"=="0" goto failed
echo NEXT: 05.Status.cmd to inspect progress, or 03.Review.cmd to continue reviewing. After adding files, start at 01.Scan.cmd.
goto finish
:failed
echo This step reported an error. Read the message above before continuing. Progress already saved is retained.
:finish
echo Press any key to close this window.
pause >nul
exit /b %organizerExit%