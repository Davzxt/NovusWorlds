$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$launcherDir = Join-Path $root "launcher"
$outDir = Join-Path $root "public\download"
$outExe = Join-Path $outDir "NovusLauncherSetup.exe"
$sedPath = Join-Path $env:TEMP "NovusLauncherSetup.sed"

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$bat = Join-Path $launcherDir "NovusLauncherSetup.bat"
$ps1 = Join-Path $launcherDir "NovusLauncherSetup.ps1"
if (!(Test-Path $bat)) { throw "Missing $bat" }
if (!(Test-Path $ps1)) { throw "Missing $ps1" }

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=Novus Launcher Setup finalizado.
TargetName=$outExe
FriendlyName=Novus Worlds Launcher Setup
AppLaunched=cmd /c NovusLauncherSetup.bat
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
FILE0=NovusLauncherSetup.bat
FILE1=NovusLauncherSetup.ps1
[SourceFiles]
SourceFiles0=$launcherDir
[SourceFiles0]
%FILE0%=
%FILE1%=
"@

Set-Content -Path $sedPath -Value $sed -Encoding ASCII
& iexpress.exe /N $sedPath
for ($i = 0; $i -lt 20 -and !(Test-Path $outExe); $i++) {
  Start-Sleep -Milliseconds 250
}
if (!(Test-Path $outExe)) { throw "IExpress did not create $outExe" }
Write-Host "Created $outExe"
