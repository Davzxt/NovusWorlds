$ErrorActionPreference = "Stop"
$launcher = Join-Path $PSScriptRoot "launcher.js"
$node = "node.exe"
$uri = if ($args.Count -gt 0) { $args[0] } else { "" }
Start-Process -FilePath $node -ArgumentList @("`"$launcher`"", "`"$uri`"") -WindowStyle Hidden -WorkingDirectory $PSScriptRoot
