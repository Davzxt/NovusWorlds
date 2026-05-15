$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $scriptDir "config.json"
$fallbackConfig = Join-Path $scriptDir "config.example.json"
$config = if (Test-Path $configPath) {
  Get-Content -Raw $configPath | ConvertFrom-Json
} elseif (Test-Path $fallbackConfig) {
  Get-Content -Raw $fallbackConfig | ConvertFrom-Json
} else {
  [pscustomobject]@{}
}

$cacheDir = if ($config.cacheDir) { [Environment]::ExpandEnvironmentVariables([string]$config.cacheDir) } else { Join-Path $env:LOCALAPPDATA "NovusWorlds\Cache" }
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
$logPath = Join-Path $cacheDir "launcher.log"

function Log($message) {
  $line = "[{0}] {1}" -f (Get-Date).ToString("s"), $message
  Add-Content -Path $logPath -Value $line
}

function Fail($message) {
  Log "ERROR: $message"
  [System.Windows.Forms.MessageBox]::Show("$message`n`nLog: $logPath", "Novus Worlds Launcher", "OK", "Error") | Out-Null
  exit 1
}

function Normalize-BaseUrl($value) {
  $url = [string]$value
  if ($url -match '^http://[^/]+\.onrender\.com') { return $url -replace '^http://', 'https://' }
  return $url
}

function Parse-Query($query) {
  $map = @{}
  $q = ([string]$query).TrimStart("?")
  if (-not $q) { return $map }
  foreach ($pair in $q -split "&") {
    if (-not $pair) { continue }
    $kv = $pair -split "=", 2
    $key = [Uri]::UnescapeDataString($kv[0].Replace("+", " "))
    $value = if ($kv.Count -gt 1) { [Uri]::UnescapeDataString($kv[1].Replace("+", " ")) } else { "" }
    $map[$key] = $value
  }
  return $map
}

function Need($map, $key) {
  if (-not $map.ContainsKey($key) -or -not $map[$key]) { Fail "Ticket invalido: parametro ausente '$key'." }
  return $map[$key]
}

function Resolve-Exe($value, $name) {
  $raw = [Environment]::ExpandEnvironmentVariables([string]$value)
  if (-not $raw) { Fail "$name nao configurado. Rode o instalador novamente." }
  if ((Test-Path $raw) -and -not (Get-Item $raw).PSIsContainer) { return (Resolve-Path $raw).Path }
  if (Test-Path $raw) {
    $hit = Get-ChildItem -Path $raw -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { return $hit.FullName }
  }
  Fail "$name nao encontrado em: $raw"
}

function Get-Json($url) {
  Log "GET $url"
  try {
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 30
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) { Fail "$url retornou HTTP $($response.StatusCode)" }
    return $response.Content | ConvertFrom-Json
  } catch {
    Fail "Falha ao baixar dados do site: $($_.Exception.Message)"
  }
}

function Write-JsonFile($name, $object) {
  $safeName = $name -replace '[^A-Za-z0-9_.-]', '_'
  $path = Join-Path $cacheDir $safeName
  $object | ConvertTo-Json -Depth 80 | Set-Content -Path $path -Encoding UTF8
  return $path
}

function Start-App($exe, $arguments) {
  $cwd = Split-Path -Parent $exe
  Log "Launching $exe $($arguments -join ' ')"
  Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $cwd
}

$rawUri = if ($args.Count -gt 0) { [string]$args[0] } else { "" }
if (-not $rawUri) {
  [System.Windows.Forms.MessageBox]::Show("Abra um jogo pelo site Novus Worlds para iniciar o Client com ticket.", "Novus Worlds", "OK", "Information") | Out-Null
  exit 0
}

try {
  $uri = [Uri]$rawUri
} catch {
  Fail "Protocolo invalido: $rawUri"
}

$query = Parse-Query $uri.Query

if ($uri.Scheme -eq "novus") {
  $ticket = Need $query "ticket"
  $gameId = Need $query "gameId"
  $baseUrl = Normalize-BaseUrl (Need $query "baseUrl")
  $serverHost = if ($query["server"]) { $query["server"] } elseif ($config.realtimeHost) { $config.realtimeHost } else { "127.0.0.1" }
  $serverPort = if ($query["port"]) { $query["port"] } elseif ($config.realtimePort) { $config.realtimePort } else { "53640" }
  $joinData = Get-Json "$baseUrl/api/legacy/tickets/$ticket"
  $joinPath = Write-JsonFile "join-$gameId.json" $joinData
  $playerExe = Resolve-Exe $config.playerExe "NovusWorldsClient.exe"
  Start-App $playerExe @("--game", $gameId, "--base-url", $baseUrl, "--server", $serverHost, "--port", $serverPort, "--ticket", $ticket, "--join-json", $joinPath)
  exit 0
}

if ($uri.Scheme -eq "novus-studio") {
  $ticket = Need $query "ticket"
  $baseUrl = Normalize-BaseUrl (Need $query "baseUrl")
  $project = Get-Json "$baseUrl/api/legacy/studio-project?ticket=$ticket"
  $projectPath = Write-JsonFile ("studio-project-{0}.json" -f ($(if ($project.gameId) { $project.gameId } else { "new" }))) $project
  $studioExe = Resolve-Exe $config.studioExe "NovusWorldsStudio.exe"
  Start-App $studioExe @("--base-url", $baseUrl, "--ticket", $ticket, "--project-json", $projectPath)
  exit 0
}

Fail "Protocolo nao suportado: $($uri.Scheme)"
