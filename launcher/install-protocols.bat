@echo off
setlocal
set LAUNCHER_DIR=%~dp0
set NODE=node

reg add HKCU\Software\Classes\novus /ve /d "URL:Novus Worlds Player" /f
reg add HKCU\Software\Classes\novus /v "URL Protocol" /d "" /f
reg add HKCU\Software\Classes\novus\shell\open\command /ve /d "\"%NODE%\" \"%LAUNCHER_DIR%launcher.js\" \"%%1\"" /f

reg add HKCU\Software\Classes\novus-studio /ve /d "URL:Novus Worlds Studio" /f
reg add HKCU\Software\Classes\novus-studio /v "URL Protocol" /d "" /f
reg add HKCU\Software\Classes\novus-studio\shell\open\command /ve /d "\"%NODE%\" \"%LAUNCHER_DIR%launcher.js\" \"%%1\"" /f

echo Protocolos novus:// e novus-studio:// registrados.
echo Copie config.example.json para config.json e configure os caminhos do Player e Studio 2012.
pause
