@echo off
setlocal
title Novus Worlds Installer

set SCRIPT=%TEMP%\NovusLauncherSetup.ps1
echo Baixando instalador Novus Worlds...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/Davzxt/NovusWorlds/main/launcher/NovusLauncherSetup.ps1' -OutFile '%SCRIPT%'"
if errorlevel 1 (
  echo.
  echo Falha ao baixar o instalador.
  pause
  exit /b 1
)

echo Abrindo instalador...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
if errorlevel 1 (
  echo.
  echo O instalador terminou com erro.
  pause
  exit /b 1
)

echo.
echo Instalacao concluida.
pause
