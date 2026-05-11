$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$dotnetRoot = Join-Path $env:USERPROFILE ".dotnet9-x64"
$dotnet = Join-Path $dotnetRoot "dotnet.exe"
if (!(Test-Path $dotnet)) { $dotnet = "dotnet" }

$clientProject = Join-Path $root "godot-client\NovusWorldsClient.csproj"
$clientExe = Join-Path $root "build\client\NovusWorldsClient.exe"
$clientData = Join-Path $root "build\client\data_NovusWorldsClient_windows_x86_64"
$clientBin = Join-Path $root "godot-client\.godot\mono\temp\bin\Debug"
$packageDir = Join-Path $root "build\package-client-fast"
$downloadDir = Join-Path $root "public\download"
$zipPath = Join-Path $downloadDir "NovusWorldsClient-Windows.zip"

if (!(Test-Path $clientExe) -or !(Test-Path $clientData)) {
  throw "Client export base nao encontrado. Rode tools\export-godot-windows.ps1 uma vez antes do fast package."
}

Write-Host "Building client C#..."
& $dotnet build $clientProject

Write-Host "Updating exported client assemblies..."
foreach ($name in @(
  "NovusWorldsClient.dll",
  "NovusWorldsClient.pdb",
  "NovusWorldsClient.deps.json",
  "NovusWorldsClient.runtimeconfig.json"
)) {
  $source = Join-Path $clientBin $name
  if (Test-Path $source) {
    Copy-Item -LiteralPath $source -Destination (Join-Path $clientData $name) -Force
  }
}

Write-Host "Creating site download zip..."
if (Test-Path $packageDir) { Remove-Item -LiteralPath $packageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath $clientExe -Destination $packageDir -Force
Copy-Item -LiteralPath $clientData -Destination $packageDir -Recurse -Force

New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($packageDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$hash = Get-FileHash $zipPath -Algorithm SHA256
Write-Host "Updated $zipPath"
Write-Host "SHA256 $($hash.Hash)"
Write-Host "Note: if you changed scenes/assets/resources embedded in the Godot pack, run the full export instead."
