@echo off
setlocal
rem Never start a program from this folder by accident (e.g. a planted python.cmd):
set "NoDefaultCurrentDirectoryInExePath=1"
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" python -m venv .venv
if not exist ".venv\Scripts\python.exe" goto error
".venv\Scripts\python.exe" -c "import yt_dlp, imageio_ffmpeg, yt_dlp_ejs, webview" >nul 2>&1
if errorlevel 1 (
  ".venv\Scripts\python.exe" -m pip install --disable-pip-version-check --prefer-binary -r requirements.txt
  if errorlevel 1 goto error
)
".venv\Scripts\python.exe" app.py
if errorlevel 1 goto error
exit /b
:error
echo Start failed. Python 3.11+ and an internet connection are required.
pause
