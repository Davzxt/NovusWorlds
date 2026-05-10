@echo off
setlocal
set "LOCAL_PS1=%~dp0NovusLauncherSetup.ps1"
if exist "%LOCAL_PS1%" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%LOCAL_PS1%"
  exit /b %errorlevel%
)
set "RAW=https://raw.githubusercontent.com/Davzxt/NovusWorlds/main/launcher/NovusLauncherSetup.ps1"
set "TMP=%TEMP%\NovusLauncherSetup.ps1"
echo Baixando instalador do Novus Launcher...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%RAW%' -OutFile '%TMP%'"
if errorlevel 1 (
  echo Nao foi possivel baixar o instalador.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%TMP%"
pause
