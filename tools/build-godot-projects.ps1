$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT = Join-Path $env:USERPROFILE ".dotnet-x64"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$dotnet = Join-Path $env:DOTNET_ROOT "dotnet.exe"
& $dotnet build (Join-Path $root "godot-client\NovusWorldsClient.csproj")
& $dotnet build (Join-Path $root "godot-studio\NovusWorldsStudio.csproj")
& $dotnet build (Join-Path $root "godot-server\NovusWorldsServer.csproj")
