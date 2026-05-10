$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $env:USERPROFILE ".dotnet9-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$godot = "C:\Users\Administrator\Downloads\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe"
$project = Join-Path $root "godot-client"
$game = if ($args.Count -ge 1) { $args[0] } else { "1" }
$baseUrl = if ($args.Count -ge 2) { $args[1] } else { "http://localhost:3000" }
& $godot --path $project -- --game $game --base-url $baseUrl --server 127.0.0.1 --port 53640

