$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $env:USERPROFILE ".dotnet9-x64"
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
$download = Join-Path $root "public\download"
New-Item -ItemType Directory -Force $download | Out-Null

function Compress-WithRetry($sourceDir, $destination) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  for ($i = 1; $i -le 5; $i++) {
    try {
      Start-Sleep -Milliseconds (350 * $i)
      $temp = "$destination.tmp"
      if (Test-Path $temp) { Remove-Item -Force $temp }
      [System.IO.Compression.ZipFile]::CreateFromDirectory($sourceDir, $temp, [System.IO.Compression.CompressionLevel]::Optimal, $false)
      Move-Item -Force $temp $destination
      return
    } catch {
      if ($i -eq 5) { throw }
      Write-Warning "Zip retry $i for ${destination}: $($_.Exception.Message)"
    }
  }
}

Compress-WithRetry (Join-Path $root "build\client") (Join-Path $download "NovusWorldsClient-Windows.zip")
Compress-WithRetry (Join-Path $root "build\studio") (Join-Path $download "NovusWorldsStudio-Windows.zip")
Write-Host "Exported $clientExe"
Write-Host "Exported $studioExe"
Write-Host "Updated public\download Godot packages."

