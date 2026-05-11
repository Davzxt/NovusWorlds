$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "NovusFastClientPackager.cs"
$out = Join-Path $PSScriptRoot "NovusFastClientPackager.exe"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe not found. Cannot build fast packager." }

& $csc /nologo /target:winexe /out:$out /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source
if (!(Test-Path $out)) { throw "csc did not create $out" }
Write-Host "Created $out"
