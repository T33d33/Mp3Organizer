@echo off
dotnet "%~dp0artifacts-progress\Mp3Organizer.dll" %*
exit /b %errorlevel%
