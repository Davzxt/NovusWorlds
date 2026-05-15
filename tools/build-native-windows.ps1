$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Find-VsDevCmd {
  $candidates = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\VsDevCmd.bat",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\VsDevCmd.bat"
  )
  foreach ($candidate in $candidates) {
    if (Test-Path $candidate) { return $candidate }
  }
  return $null
}

$vsdev = Find-VsDevCmd
if (-not $vsdev) {
  throw "MSVC Build Tools not found. Run tools\install-native-toolchain.ps1 first."
}

$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
if (-not $cmake -and (Test-Path "${env:ProgramFiles}\CMake\bin\cmake.exe")) {
  $cmake = "${env:ProgramFiles}\CMake\bin\cmake.exe"
}
if (-not $cmake) {
  throw "CMake not found. Run tools\install-native-toolchain.ps1 first, then restart PowerShell."
}

$script = @"
call "$vsdev" -arch=x64
cd /d "$root"
"$cmake" --preset windows-msvc
"$cmake" --build --preset windows-release
"@

$cmd = Join-Path $env:TEMP "novus-native-build.cmd"
Set-Content -Path $cmd -Value $script -Encoding ASCII
cmd.exe /c $cmd
