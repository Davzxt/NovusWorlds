$ErrorActionPreference = "Stop"
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$siteDownloadBase = "https://novusworlds.onrender.com/download"
$launcherBases = @(
  "$siteDownloadBase/launcher",
  "https://raw.githubusercontent.com/Davzxt/NovusWorlds/main/launcher"
)
$packageBases = @(
  $siteDownloadBase,
  "https://github.com/Davzxt/NovusWorlds/raw/main/public/download"
)
$defaultInstallDir = Join-Path $env:LOCALAPPDATA "NovusWorlds\Launcher"
$defaultCacheDir = Join-Path $env:LOCALAPPDATA "NovusWorlds\Cache"
$defaultClientRoot = Join-Path $env:LOCALAPPDATA "NovusWorlds\Client"
$defaultStudioRoot = Join-Path $env:LOCALAPPDATA "NovusWorlds\Studio"
$desktop = [Environment]::GetFolderPath("Desktop")

function Find-Node {
  $cmd = Get-Command node.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $paths = @("$env:ProgramFiles\nodejs\node.exe", "${env:ProgramFiles(x86)}\nodejs\node.exe")
  foreach ($p in $paths) {
    if ($p -and (Test-Path $p)) { return $p }
  }
  return ""
}

function Pick-Exe($title, $targetBox) {
  $dialog = New-Object System.Windows.Forms.OpenFileDialog
  $dialog.Title = $title
  $dialog.Filter = "Executaveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*"
  $dialog.CheckFileExists = $true
  if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    $targetBox.Text = $dialog.FileName
  }
}

function Pick-ClientFolder($targetBox) {
  $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
  $dialog.Description = "Escolha a pasta onde esta o NovusWorldsClient.exe"
  if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    $targetBox.Text = $dialog.SelectedPath
  }
}

function Pick-Folder($targetBox) {
  $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
  $dialog.Description = "Escolha a pasta de instalacao do Novus Launcher"
  if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    $targetBox.Text = $dialog.SelectedPath
  }
}

function Download-Url($url, $target) {
  New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
  if (Test-Path $target) { Remove-Item -Force $target }
  $headers = @{ "User-Agent" = "NovusWorldsSetup/1.0" }
  try {
    Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing -Headers $headers -TimeoutSec 90
    if ((Test-Path $target) -and ((Get-Item $target).Length -gt 0)) { return $target }
  } catch {
    $firstError = $_.Exception.Message
  }

  $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
  if ($curl) {
    & $curl.Source -L --ssl-no-revoke --fail --silent --show-error --output $target $url
    if ($LASTEXITCODE -eq 0 -and (Test-Path $target) -and ((Get-Item $target).Length -gt 0)) { return $target }
  }
  throw "Falha ao baixar $url. $firstError"
}

function Download-File($name, $installDir) {
  $target = Join-Path $installDir $name
  $errors = @()
  foreach ($base in $launcherBases) {
    try { return Download-Url "$base/$name" $target } catch { $errors += $_.Exception.Message }
  }
  throw "Nao foi possivel baixar $name.`r`n$($errors -join "`r`n")"
}

function Download-Package($name, $target) {
  $errors = @()
  foreach ($base in $packageBases) {
    try { return Download-Url "$base/$name" $target } catch { $errors += $_.Exception.Message }
  }
  throw "Nao foi possivel baixar $name.`r`n$($errors -join "`r`n")"
}

function Expand-Package($zip, $targetDir) {
  if (Test-Path $targetDir) { Remove-Item -Recurse -Force $targetDir }
  New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
  Expand-Archive -Path $zip -DestinationPath $targetDir -Force
}

function Find-ExeInFolder($folder, $name) {
  $hit = Get-ChildItem -Path $folder -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($hit) { return $hit.FullName }
  return ""
}

function Register-Protocol($scheme, $description, $protocolLauncherExe) {
  $base = "HKCU:\Software\Classes\$scheme"
  New-Item -Path $base -Force | Out-Null
  New-ItemProperty -Path $base -Name "(default)" -Value "URL:$description" -Force | Out-Null
  New-ItemProperty -Path $base -Name "URL Protocol" -Value "" -Force | Out-Null
  New-Item -Path "$base\shell\open\command" -Force | Out-Null
  $command = '"' + $protocolLauncherExe + '" "%1"'
  New-ItemProperty -Path "$base\shell\open\command" -Name "(default)" -Value $command -Force | Out-Null
}

function Create-Shortcut($name, $target, $arguments, $workingDir) {
  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut((Join-Path $desktop $name))
  $shortcut.TargetPath = $target
  $shortcut.Arguments = $arguments
  $shortcut.WorkingDirectory = $workingDir
  $shortcut.Save()
}

function Add-Label($form, $text, $x, $y, $w, $h) {
  $label = New-Object System.Windows.Forms.Label
  $label.Text = $text
  $label.Location = New-Object System.Drawing.Point($x, $y)
  $label.Size = New-Object System.Drawing.Size($w, $h)
  $label.ForeColor = [System.Drawing.Color]::FromArgb(30, 45, 70)
  $form.Controls.Add($label)
  return $label
}

function Add-TextBox($form, $text, $x, $y, $w) {
  $box = New-Object System.Windows.Forms.TextBox
  $box.Text = $text
  $box.Location = New-Object System.Drawing.Point($x, $y)
  $box.Size = New-Object System.Drawing.Size($w, 24)
  $form.Controls.Add($box)
  return $box
}

function Add-Button($form, $text, $x, $y, $w, $h) {
  $button = New-Object System.Windows.Forms.Button
  $button.Text = $text
  $button.Location = New-Object System.Drawing.Point($x, $y)
  $button.Size = New-Object System.Drawing.Size($w, $h)
  $button.FlatStyle = "Standard"
  $form.Controls.Add($button)
  return $button
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Novus Worlds Setup Wizard"
$form.Size = New-Object System.Drawing.Size(680, 520)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(235, 245, 255)
$form.Font = New-Object System.Drawing.Font("Verdana", 9)

$stepLabel = Add-Label $form "Etapa 1 de 3" 26 6 180 18
$stepLabel.ForeColor = [System.Drawing.Color]::FromArgb(90, 100, 115)
$title = Add-Label $form "Novus Worlds Setup Wizard" 24 24 620 30
$title.Font = New-Object System.Drawing.Font("Verdana", 16, [System.Drawing.FontStyle]::Bold)
$title.ForeColor = [System.Drawing.Color]::FromArgb(20, 80, 150)
Add-Label $form "Este assistente instala o Client, o Studio, o launcher local, os protocolos novus:// e novus-studio:// e cria atalhos na area de trabalho." 26 60 610 40 | Out-Null

Add-Label $form "Pasta de instalacao" 26 108 200 20 | Out-Null
$installBox = Add-TextBox $form $defaultInstallDir 26 130 500
$installBrowse = Add-Button $form "Escolher" 535 128 105 28

Add-Label $form "NovusWorldsClient.exe" 26 170 260 20 | Out-Null
$defaultClientDir = Join-Path $defaultClientRoot "NovusWorldsClient.exe"
if (!(Test-Path $defaultClientDir)) { $defaultClientDir = "" }
$playerBox = Add-TextBox $form $defaultClientDir 26 192 500
$playerBrowse = Add-Button $form "Procurar" 535 190 105 28

Add-Label $form "NovusWorldsStudio.exe" 26 232 260 20 | Out-Null
$defaultStudioDir = Join-Path $defaultStudioRoot "NovusWorldsStudio.exe"
if (!(Test-Path $defaultStudioDir)) { $defaultStudioDir = "" }
$studioBox = Add-TextBox $form $defaultStudioDir 26 254 500
$studioBrowse = Add-Button $form "Procurar" 535 252 105 28

$install = Add-Button $form "Instalar agora" 26 300 210 32
$close = Add-Button $form "Fechar" 250 300 90 32

$status = New-Object System.Windows.Forms.TextBox
$status.Location = New-Object System.Drawing.Point(26, 350)
$status.Size = New-Object System.Drawing.Size(614, 100)
$status.Multiline = $true
$status.ReadOnly = $true
$status.ScrollBars = "Vertical"
$status.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($status)

function Is-InsideFolder($path, $folder) {
  if (-not $path) { return $true }
  try {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    $fullFolder = [System.IO.Path]::GetFullPath($folder)
    return $fullPath.StartsWith($fullFolder, [System.StringComparison]::OrdinalIgnoreCase)
  } catch {
    return $false
  }
}

function Log($text) {
  $status.AppendText($text + [Environment]::NewLine)
}

$installBrowse.Add_Click({ Pick-Folder $installBox })
$playerBrowse.Add_Click({ Pick-Exe "Escolha NovusWorldsClient.exe" $playerBox })
$studioBrowse.Add_Click({ Pick-Exe "Escolha NovusWorldsStudio.exe" $studioBox })
$close.Add_Click({ $form.Close() })

$form.Add_Shown({
  $answer = [System.Windows.Forms.MessageBox]::Show("Deseja instalar o Novus Worlds Client e Studio agora?", "Novus Worlds Setup", "YesNo", "Question")
  if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
    $form.Close()
    return
  }
  Log "Assistente iniciado. Clique em Instalar agora para baixar e configurar tudo automaticamente."
})

$install.Add_Click({
  try {
    $install.Enabled = $false
    $stepLabel.Text = "Etapa 2 de 3"
    $installDir = $installBox.Text.Trim()
    if (-not $installDir) { throw "Escolha uma pasta de instalacao." }
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    New-Item -ItemType Directory -Force -Path $defaultCacheDir | Out-Null
    Log "Pasta pronta: $installDir"

    $temp = Join-Path $env:TEMP "NovusWorldsInstall"
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    $playerPath = $playerBox.Text.Trim()
    $shouldRefreshClient = (-not $playerPath) -or (Is-InsideFolder $playerPath $defaultClientRoot)
    if ($shouldRefreshClient -or -not (Test-Path $playerPath)) {
      Log "Baixando/atualizando Novus Client Windows..."
      $clientZip = Download-Package "NovusWorldsClient-Windows.zip" (Join-Path $temp "NovusWorldsClient-Windows.zip")
      Expand-Package $clientZip $defaultClientRoot
      $clientExe = Find-ExeInFolder $defaultClientRoot "NovusWorldsClient.exe"
      if (-not $clientExe) { throw "Client baixado, mas NovusWorldsClient.exe nao foi encontrado." }
      $playerBox.Text = $clientExe
      Log "Client instalado: $clientExe"
    }

    $studioPath = $studioBox.Text.Trim()
    $shouldRefreshStudio = (-not $studioPath) -or (Is-InsideFolder $studioPath $defaultStudioRoot)
    if ($shouldRefreshStudio -or -not (Test-Path $studioPath)) {
      Log "Baixando/atualizando Novus Studio Windows..."
      $studioZip = Download-Package "NovusWorldsStudio-Windows.zip" (Join-Path $temp "NovusWorldsStudio-Windows.zip")
      Expand-Package $studioZip $defaultStudioRoot
      $studioExe = Find-ExeInFolder $defaultStudioRoot "NovusWorldsStudio.exe"
      if (-not $studioExe) { throw "Studio baixado, mas NovusWorldsStudio.exe nao foi encontrado." }
      $studioBox.Text = $studioExe
      Log "Studio instalado: $studioExe"
    }

    $launcherPath = Download-File "launcher.js" $installDir
    $protocolLauncherPath = Download-File "launch-hidden.ps1" $installDir
    Download-File "launch-hidden.vbs" $installDir | Out-Null
    Download-File "config.example.json" $installDir | Out-Null
    Download-File "README.md" $installDir | Out-Null
    $protocolLauncherExe = Download-Package "NovusProtocolLauncher.exe" (Join-Path $installDir "NovusProtocolLauncher.exe")
    Log "Launcher baixado."

    $config = [ordered]@{
      playerExe = $playerBox.Text.Trim()
      studioExe = $studioBox.Text.Trim()
      cacheDir = $defaultCacheDir
      launchMode = "live"
      realtimeHost = "127.0.0.1"
      realtimePort = 53640
      playerArgs = @("auto")
      studioArgs = @("auto")
    }
    $configPath = Join-Path $installDir "config.json"
    $config | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
    Log "Config salvo: $configPath"

    $stepLabel.Text = "Etapa 3 de 3"
    Register-Protocol "novus" "Novus Worlds Player" $protocolLauncherExe
    Register-Protocol "novus-studio" "Novus Worlds Studio" $protocolLauncherExe
    Log "Protocolos registrados."

    Create-Shortcut "Novus Worlds Launcher.lnk" $protocolLauncherExe "" $installDir
    Create-Shortcut "Novus Worlds Studio.lnk" $studioBox.Text.Trim() "" (Split-Path $studioBox.Text.Trim())
    Create-Shortcut "Novus Worlds Client.lnk" $playerBox.Text.Trim() "" (Split-Path $playerBox.Text.Trim())
    Log "Atalhos criados na area de trabalho."
    Log "Concluido. Abra um jogo pelo site para iniciar o Player com ticket."
    $stepLabel.Text = "Concluido"
    [System.Windows.Forms.MessageBox]::Show("Novus Launcher instalado com sucesso.", "Novus Worlds", "OK", "Information") | Out-Null
  } catch {
    Log "Erro: $($_.Exception.Message)"
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Novus Worlds", "OK", "Warning") | Out-Null
  } finally {
    $install.Enabled = $true
  }
})

[void]$form.ShowDialog()

