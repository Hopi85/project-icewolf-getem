@echo off
rem Icewolf Getem Edition: installs Getem's components completely first, then starts Getem.
rem While installing, this window keeps ".setup-running" open - so Icewolf knows the
rem installation is really still running (a crashed or closed window leaves no lock behind).
setlocal
rem Never start a program from this folder by accident (e.g. a planted python.cmd):
set "NoDefaultCurrentDirectoryInExePath=1"
cd /d "%~dp0"
title Getem - installing components
call :install 9>".setup-running"
set "RESULT=%ERRORLEVEL%"
del ".setup-running" >nul 2>&1
if not "%RESULT%"=="0" goto error
if /i "%~1"=="--server" (
  start "" ".venv\Scripts\pythonw.exe" icewolf_server.py %2 %3
  exit /b 0
)
".venv\Scripts\python.exe" app.py %*
exit /b

:install
echo.
echo   Getem: installing download components - please wait until this window closes.
echo.
rem An environment made with an older Python (yt-dlp needs 3.11+) is rebuilt
if exist ".venv\Scripts\python.exe" (
  ".venv\Scripts\python.exe" -c "import sys; sys.exit(0 if sys.version_info >= (3, 11) else 1)"
  if errorlevel 1 (
    echo   Getem's Python environment is too old - setting it up again with a newer Python.
    rmdir /s /q ".venv"
  )
)
if exist ".venv\Scripts\python.exe" goto venvready
rem Python: the "py" launcher from python.org first, then "python" from PATH
set "PY="
where py >nul 2>&1 && set "PY=py -3"
if not defined PY where python >nul 2>&1 && set "PY=python"
if not defined PY exit /b 1
%PY% -c "import sys; sys.exit(0 if sys.version_info >= (3, 11) else 1)"
if errorlevel 1 (
  echo   Python 3.11 or newer is required.
  exit /b 1
)
%PY% -m venv .venv
if not exist ".venv\Scripts\python.exe" exit /b 1
:venvready
".venv\Scripts\python.exe" -m pip install --disable-pip-version-check --prefer-binary -r requirements.txt
if errorlevel 1 exit /b 1
".venv\Scripts\python.exe" -c "import yt_dlp, imageio_ffmpeg, curl_cffi, catalog"
if errorlevel 1 exit /b 1
rem the marker holds the requirements it was made for: a new Icewolf version with other
rem package versions makes Icewolf run this installation again
copy /y requirements.txt ".venv\icewolf-ready" >nul
exit /b 0

:error
echo.
echo   Installation failed. Python 3.11+ (python.org) and an internet connection are required.
echo   (When installing Python, tick "Add Python to PATH".)
pause
