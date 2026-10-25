@echo off
echo OPTIONAL - Show saved progress
echo This displays saved counts; it does not process music.
echo.
call "%~dp0Mp3Organizer.cmd" status --workspace "E:\Mp3-Ai-test-workspace"
set "organizerExit=%errorlevel%"
echo.
if not "%organizerExit%"=="0" goto failed
echo NEXT: 01.Scan.cmd after adding files; 02.Analyze-and-Organize.cmd for pending analysis; 03.Review.cmd for NeedsReview; 04.Apply-Reviewed.cmd for Ready files.
goto finish
:failed
echo This step reported an error. Read the message above before continuing. Progress already saved is retained.
:finish
echo Press any key to close this window.
pause >nul
exit /b %organizerExit%