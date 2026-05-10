$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $env:USERPROFILE ".dotnet9-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$godot = "C:\Users\Administrator\Downloads\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe"
$project = Join-Path $root "godot-studio"
$baseUrl = if ($args.Count -ge 1) { $args[0] } else { "http://localhost:3000" }
& $godot --path $project -- --base-url $baseUrl

