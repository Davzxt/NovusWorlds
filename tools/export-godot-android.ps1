$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $env:USERPROFILE ".dotnet9-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$godot = "C:\Users\Administrator\Downloads\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe"
New-Item -ItemType Directory -Force (Join-Path $root "build\android") | Out-Null
& $godot --headless --path (Join-Path $root "godot-client") --export-debug "Android"
$apk = Join-Path $root "build\android\NovusWorldsClient.apk"
if (!(Test-Path $apk)) {
  throw "Godot Android export failed. Install Android SDK/JDK and configure Godot Android export settings, then run this script again."
}
Write-Host "Exported $apk"

