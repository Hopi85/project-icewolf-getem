@echo off
rem Icewolf GetemTab: installs Getem's components completely first, then starts Getem.
rem While installing, this window keeps ".setup-running" open - so Icewolf knows the
rem installation is really still running (a crashed or closed window leaves no lock behind).
cd /d "%~dp0"
title Getem - installing components
call :install 9>".setup-running"
set "RESULT=%ERRORLEVEL%"
del ".setup-running" >nul 2>&1
if not "%RESULT%"=="0" goto error
if /i "%~1"=="--server" (
  start "" ".venv\Scripts\pythonw.exe" icewolf_server.py
  exit /b 0
)
".venv\Scripts\python.exe" app.py %*
exit /b

:install
echo.
echo   Getem: installing download components - please wait until this window closes.
echo.
if not exist ".venv\Scripts\python.exe" python -m venv .venv
if not exist ".venv\Scripts\python.exe" exit /b 1
".venv\Scripts\python.exe" -m pip install --disable-pip-version-check -r requirements.txt
if errorlevel 1 exit /b 1
".venv\Scripts\python.exe" -c "import yt_dlp, imageio_ffmpeg, curl_cffi, catalog"
if errorlevel 1 exit /b 1
echo ok>".venv\icewolf-ready"
exit /b 0

:error
echo.
echo   Installation failed. Python 3.10+ and an internet connection are required.
echo   (Python: python.org - tick "Add Python to PATH" during setup.)
pause
