$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $env:USERPROFILE ".dotnet9-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$godot = "C:\Users\Administrator\Downloads\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe"
New-Item -ItemType Directory -Force (Join-Path $root "build\server") | Out-Null
& $godot --headless --path (Join-Path $root "godot-server") --export-release "Linux Dedicated Server"
$serverExe = Join-Path $root "build\server\novus-godot-server.x86_64"
if (!(Test-Path $serverExe)) {
  throw "Godot server export failed. Install Godot .NET export templates 4.6.2 first, then run this script again."
}
Write-Host "Exported $serverExe"

