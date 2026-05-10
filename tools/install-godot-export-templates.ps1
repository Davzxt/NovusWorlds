$ErrorActionPreference = "Stop"
$version = "4.6.2.stable.mono"
$target = Join-Path $env:APPDATA "Godot\export_templates\$version"
$cache = Join-Path $env:TEMP "Godot_v4.6.2-stable_mono_export_templates.tpz"
$url = "https://github.com/godotengine/godot/releases/download/4.6.2-stable/Godot_v4.6.2-stable_mono_export_templates.tpz"
New-Item -ItemType Directory -Force $target | Out-Null
if (!(Test-Path $cache)) {
  Write-Host "Downloading Godot .NET export templates 4.6.2. This file is large."
  Invoke-WebRequest $url -OutFile $cache
}
$extract = Join-Path $env:TEMP "godot-export-templates-4.6.2"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
New-Item -ItemType Directory -Force $extract | Out-Null
$zipCache = Join-Path $env:TEMP "Godot_v4.6.2-stable_mono_export_templates.zip"
Copy-Item $cache $zipCache -Force
Expand-Archive $zipCache -DestinationPath $extract -Force
$templates = Get-ChildItem $extract -Recurse -Directory | Where-Object { $_.Name -eq "templates" } | Select-Object -First 1
if (!$templates) { throw "Could not find templates directory inside $cache" }
Copy-Item (Join-Path $templates.FullName "*") $target -Recurse -Force
Write-Host "Installed Godot export templates to $target"
