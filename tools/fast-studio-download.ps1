$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dotnetRoot = Join-Path $env:USERPROFILE ".dotnet9-x64"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
if (!(Test-Path $dotnet)) { $dotnet = "dotnet" }

$studioProject = Join-Path $root "godot-studio\NovusWorldsStudio.csproj"
$studioExe = Join-Path $root "build\studio\NovusWorldsStudio.exe"
$studioData = Join-Path $root "build\studio\data_NovusWorldsStudio_windows_x86_64"
$studioBin = Join-Path $root "godot-studio\.godot\mono\temp\bin\Debug"
$packageDir = Join-Path $root "build\package-studio-fast"
$downloadDir = Join-Path $root "public\download"
$zipPath = Join-Path $downloadDir "NovusWorldsStudio-Windows.zip"

if (!(Test-Path $studioExe) -or !(Test-Path $studioData)) {
  throw "Studio export base nao encontrado. Rode tools\export-godot-windows.ps1 uma vez antes do fast package."
}

Write-Host "Building studio C#..."
& $dotnet build $studioProject

Write-Host "Updating exported studio assemblies..."
foreach ($name in @(
  "NovusWorldsStudio.dll",
  "NovusWorldsStudio.pdb"
)) {
  $source = Join-Path $studioBin $name
  if (Test-Path $source) {
    Copy-Item -LiteralPath $source -Destination (Join-Path $studioData $name) -Force
  }
}

Write-Host "Keeping exported deps/runtimeconfig untouched so the self-contained .NET runtime keeps working."

Write-Host "Creating site studio download zip..."
if (Test-Path $packageDir) { Remove-Item -LiteralPath $packageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath $studioExe -Destination $packageDir -Force
Copy-Item -LiteralPath $studioData -Destination $packageDir -Recurse -Force

New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$hash = Get-FileHash $zipPath -Algorithm SHA256
Write-Host "Updated $zipPath"
Write-Host "SHA256 $($hash.Hash)"
