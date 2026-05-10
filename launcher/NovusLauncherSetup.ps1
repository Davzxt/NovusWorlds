$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$rawBase = "https://raw.githubusercontent.com/Davzxt/NovusWorlds/main/launcher"
$defaultInstallDir = Join-Path $env:LOCALAPPDATA "NovusWorlds\Launcher"
$defaultCacheDir = Join-Path $env:LOCALAPPDATA "NovusWorlds\Cache"
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
  $dialog.Description = "Escolha a pasta do client Novetus, por exemplo clients\2011M"
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

function Download-File($name, $installDir) {
  $target = Join-Path $installDir $name
  Invoke-WebRequest -Uri "$rawBase/$name" -OutFile $target
  return $target
}

function Register-Protocol($scheme, $description, $nodePath, $launcherPath) {
  $base = "HKCU:\Software\Classes\$scheme"
  New-Item -Path $base -Force | Out-Null
  New-ItemProperty -Path $base -Name "(default)" -Value "URL:$description" -Force | Out-Null
  New-ItemProperty -Path $base -Name "URL Protocol" -Value "" -Force | Out-Null
  New-Item -Path "$base\shell\open\command" -Force | Out-Null
  $hidden = Join-Path (Split-Path $launcherPath) "launch-hidden.ps1"
  $command = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $hidden + '" "%1"'
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
$form.Text = "Novus Worlds Launcher Setup"
$form.Size = New-Object System.Drawing.Size(680, 520)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(235, 245, 255)
$form.Font = New-Object System.Drawing.Font("Verdana", 9)

$title = Add-Label $form "Novus Worlds Launcher" 24 18 620 30
$title.Font = New-Object System.Drawing.Font("Verdana", 16, [System.Drawing.FontStyle]::Bold)
$title.ForeColor = [System.Drawing.Color]::FromArgb(20, 80, 150)
Add-Label $form "Configure a ponte local para abrir jogos e Studio pelo Novetus. O instalador nao inclui executaveis proprietarios." 26 54 610 38 | Out-Null

Add-Label $form "Pasta de instalacao" 26 108 200 20 | Out-Null
$installBox = Add-TextBox $form $defaultInstallDir 26 130 500
$installBrowse = Add-Button $form "Escolher" 535 128 105 28

Add-Label $form "Player/Client do Novetus" 26 170 260 20 | Out-Null
$defaultClientDir = "C:\Users\Administrator\Downloads\novetus-windows\clients\2011M"
if (!(Test-Path $defaultClientDir)) { $defaultClientDir = "" }
$playerBox = Add-TextBox $form $defaultClientDir 26 192 500
$playerBrowse = Add-Button $form "Procurar" 535 190 105 28

Add-Label $form "Studio do Novetus" 26 232 260 20 | Out-Null
$studioBox = Add-TextBox $form $defaultClientDir 26 254 500
$studioBrowse = Add-Button $form "Procurar" 535 252 105 28

$openNovetus = Add-Button $form "Abrir pagina do Novetus" 26 300 190 32
$install = Add-Button $form "Instalar Novus Launcher" 230 300 210 32
$close = Add-Button $form "Fechar" 455 300 90 32

$status = New-Object System.Windows.Forms.TextBox
$status.Location = New-Object System.Drawing.Point(26, 350)
$status.Size = New-Object System.Drawing.Size(614, 100)
$status.Multiline = $true
$status.ReadOnly = $true
$status.ScrollBars = "Vertical"
$status.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($status)

function Log($text) {
  $status.AppendText($text + [Environment]::NewLine)
}

$installBrowse.Add_Click({ Pick-Folder $installBox })
$playerBrowse.Add_Click({ Pick-ClientFolder $playerBox })
$studioBrowse.Add_Click({ Pick-ClientFolder $studioBox })
$openNovetus.Add_Click({ Start-Process "https://github.com/Novetus/Novetus_src" })
$close.Add_Click({ $form.Close() })

$install.Add_Click({
  try {
    $install.Enabled = $false
    $installDir = $installBox.Text.Trim()
    if (-not $installDir) { throw "Escolha uma pasta de instalacao." }
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    New-Item -ItemType Directory -Force -Path $defaultCacheDir | Out-Null
    Log "Pasta pronta: $installDir"

    $nodePath = Find-Node
    if (-not $nodePath) {
      Log "Node.js nao encontrado. Abrindo pagina de download."
      Start-Process "https://nodejs.org/en/download"
      throw "Instale o Node.js LTS e rode o instalador novamente."
    }
    Log "Node encontrado: $nodePath"

    $launcherPath = Download-File "launcher.js" $installDir
    Download-File "launch-hidden.ps1" $installDir | Out-Null
    Download-File "config.example.json" $installDir | Out-Null
    Download-File "README.md" $installDir | Out-Null
    Log "Launcher baixado."

    $config = [ordered]@{
      playerExe = $playerBox.Text.Trim()
      studioExe = $studioBox.Text.Trim()
      cacheDir = $defaultCacheDir
      launchMode = "live"
      playerArgs = @("auto")
      studioArgs = @("auto")
    }
    $configPath = Join-Path $installDir "config.json"
    $config | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
    Log "Config salvo: $configPath"

    Register-Protocol "novus" "Novus Worlds Player" $nodePath $launcherPath
    Register-Protocol "novus-studio" "Novus Worlds Studio" $nodePath $launcherPath
    Log "Protocolos registrados."

    Create-Shortcut "Novus Worlds Player.lnk" $nodePath ('"' + $launcherPath + '"') $installDir
    if ($studioBox.Text.Trim() -and (Test-Path $studioBox.Text.Trim())) {
      Create-Shortcut "Novus Worlds Studio.lnk" $studioBox.Text.Trim() "" (Split-Path $studioBox.Text.Trim())
    } else {
      Create-Shortcut "Novus Worlds Studio.lnk" $nodePath ('"' + $launcherPath + '"') $installDir
    }
    Log "Atalhos criados na area de trabalho."
    Log "Concluido. Abra um jogo pelo site para iniciar o Player com ticket."
    [System.Windows.Forms.MessageBox]::Show("Novus Launcher instalado com sucesso.", "Novus Worlds", "OK", "Information") | Out-Null
  } catch {
    Log "Erro: $($_.Exception.Message)"
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Novus Worlds", "OK", "Warning") | Out-Null
  } finally {
    $install.Enabled = $true
  }
})

[void]$form.ShowDialog()
