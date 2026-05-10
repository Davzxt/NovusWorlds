$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$outDir = Join-Path $root "public\download"
$outExe = Join-Path $outDir "NovusLauncherSetup.exe"
$source = Join-Path $PSScriptRoot "NovusLauncherSetupStub.cs"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe not found. Cannot build installer." }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& $csc /nologo /target:winexe /out:$outExe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source
if (!(Test-Path $outExe)) { throw "csc did not create $outExe" }
Write-Host "Created $outExe"
