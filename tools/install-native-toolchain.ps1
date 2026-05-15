$ErrorActionPreference = "Stop"

Write-Host "Installing native C++ build dependencies for Novus Worlds."
Write-Host "This uses winget and may open installer prompts."

winget install --id Kitware.CMake -e --accept-source-agreements --accept-package-agreements
winget install --id Microsoft.VisualStudio.2022.BuildTools -e --accept-source-agreements --accept-package-agreements --override "--wait --quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"

Write-Host "Done. Restart PowerShell, then run: npm run native:build"
