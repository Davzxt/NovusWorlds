$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root "build\native-msvc\Release"
$download = Join-Path $root "public\download"
$clientExe = Join-Path $build "NovusWorldsClient.exe"
$studioExe = Join-Path $build "NovusWorldsStudio.exe"

if (-not (Test-Path $clientExe) -or -not (Test-Path $studioExe)) {
  throw "Native executables not found. Run npm run native:build first."
}

New-Item -ItemType Directory -Force -Path $download | Out-Null

$clientDir = Join-Path $env:TEMP "NovusWorldsClientNative"
$studioDir = Join-Path $env:TEMP "NovusWorldsStudioNative"
Remove-Item -Recurse -Force $clientDir,$studioDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $clientDir,$studioDir | Out-Null

Copy-Item $clientExe (Join-Path $clientDir "NovusWorldsClient.exe")
Copy-Item $studioExe (Join-Path $studioDir "NovusWorldsStudio.exe")

$r6Source = Join-Path $root "public\assets\r6"
if (Test-Path $r6Source) {
  New-Item -ItemType Directory -Force -Path (Join-Path $clientDir "assets\r6") | Out-Null
  Copy-Item -Recurse -Force (Join-Path $r6Source "*") (Join-Path $clientDir "assets\r6")
}

Compress-Archive -Path (Join-Path $clientDir "*") -DestinationPath (Join-Path $download "NovusWorldsClient-Windows.zip") -Force
Compress-Archive -Path (Join-Path $studioDir "*") -DestinationPath (Join-Path $download "NovusWorldsStudio-Windows.zip") -Force

Write-Host "Updated native downloads in public\download."
