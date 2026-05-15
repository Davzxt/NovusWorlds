$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "build\native-msvc\Release\NovusWorldsClient.exe"
if (-not (Test-Path $exe)) { throw "Native client not built. Run npm run native:build first." }
$game = if ($args.Count -gt 0) { $args[0] } else { "1" }
$baseUrl = if ($args.Count -gt 1) { $args[1] } else { "http://localhost:3000" }
& $exe --game $game --base-url $baseUrl
