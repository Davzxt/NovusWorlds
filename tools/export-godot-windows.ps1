$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $env:USERPROFILE ".dotnet-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$godot = "C:\Users\Administrator\Downloads\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe"
New-Item -ItemType Directory -Force (Join-Path $root "build\client") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $root "build\studio") | Out-Null
& $godot --headless --path (Join-Path $root "godot-client") --export-release "Windows Desktop"
& $godot --headless --path (Join-Path $root "godot-studio") --export-release "Windows Desktop"
$clientExe = Join-Path $root "build\client\NovusWorldsClient.exe"
$studioExe = Join-Path $root "build\studio\NovusWorldsStudio.exe"
if (!(Test-Path $clientExe) -or !(Test-Path $studioExe)) {
  throw "Godot export failed. Install Godot .NET export templates 4.6.2 first, then run this script again."
}
Write-Host "Exported $clientExe"
Write-Host "Exported $studioExe"
