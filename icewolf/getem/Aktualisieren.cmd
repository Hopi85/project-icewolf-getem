@echo off
setlocal
rem Getem: updates yt-dlp (it has to follow website changes). Started by Shift+click on Getem in
rem Icewolf's menu. Never runs a program from this folder by accident:
set "NoDefaultCurrentDirectoryInExePath=1"
cd /d "%~dp0"
title Getem - updating download components
if not exist ".venv\Scripts\python.exe" (
  echo   Getem is not set up yet - please start Getem once first.
  pause
  exit /b 1
)
echo.
echo   Getem: updating yt-dlp ...
echo.
".venv\Scripts\python.exe" -m pip install --disable-pip-version-check --prefer-binary --upgrade "yt-dlp[default]"
pause
