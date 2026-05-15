$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "build\native-msvc\Release\NovusWorldsStudio.exe"
if (-not (Test-Path $exe)) { throw "Native studio not built. Run npm run native:build first." }
$baseUrl = if ($args.Count -gt 0) { $args[0] } else { "http://localhost:3000" }
& $exe --base-url $baseUrl
