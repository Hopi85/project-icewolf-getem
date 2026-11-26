@echo off
setlocal EnableExtensions
title Icewolf - Setup Builder
color 0B
cd /d "%~dp0"

echo.
echo   ==============================================
echo        I C E W O L F   -   Setup Builder
echo   ==============================================
echo.
echo   Creates the standalone IcewolfSetup.exe.
echo   Nothing extra to install - only Windows' built-in tools are used.
echo.
echo     1. downloads the newest LibreWolf engine (librewolf.net)
echo     2. verifies its checksum and digital signatures
echo     3. builds the setup and opens the output folder
echo.

if not exist "%~dp0build.ps1" goto :notextracted
if not exist "%~dp0setup\IcewolfSetup.cs" goto :notextracted
echo "%~dp0" | find /I "\AppData\Local\Temp\" >nul && goto :notextracted

echo   ----------------------------------------------
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 goto :failed

color 0A
echo.
echo   ==============================================
echo     SUCCESS - your setup is in the "dist" folder
echo   ==============================================
start "" explorer.exe "%~dp0dist"
goto :end

:failed
color 0C
echo.
echo   ==============================================
echo     Build FAILED - see the red message above.
echo   ==============================================
echo   A full log was saved to:  build-log.txt
echo   Send that file (or a screenshot) to get it fixed quickly.
echo.
echo   Common causes: no internet connection, or an antivirus
echo   blocking PowerShell downloads.
goto :end

:notextracted
color 0C
echo   ERROR: Please EXTRACT the whole zip first (right-click - "Extract All"),
echo   then run Build-Icewolf-Setup.bat from the extracted folder.
goto :end

:end
echo.
pause
endlocal
