const fs = require('fs');
const path = require('path');
const { spawn, spawnSync } = require('child_process');

const arg = process.argv.slice(2).join(' ');
const configPath = path.join(__dirname, 'config.json');
const config = fs.existsSync(configPath)
  ? readJson(configPath)
  : readJson(path.join(__dirname, 'config.example.json'));

const cacheDir = expandEnv(config.cacheDir || path.join(process.env.LOCALAPPDATA || __dirname, 'NovusWorlds', 'Cache'));
fs.mkdirSync(cacheDir, { recursive: true });
const logPath = path.join(cacheDir, 'launcher.log');

if (!arg) {
  const message = 'Abra um jogo pelo site Novus Worlds. O Player precisa de um ticket novus:// para entrar em uma partida.';
  log(message);
  console.log(message);
  process.exit(0);
}

const uri = new URL(arg.replace(/^"|"$/g, ''));

main().catch(err => {
  log(err.stack || err.message);
  showError(err.message || String(err));
  console.error(err.stack || err.message);
  process.exit(1);
});

async function main() {
  if (uri.protocol === 'novus:') return joinGame();
  if (uri.protocol === 'novus-studio:') return openStudio();
  throw new Error(`Unsupported protocol: ${uri.protocol}`);
}

async function joinGame() {
  const ticket = need('ticket');
  const baseUrl = need('baseUrl');
  const gameId = need('gameId');
  const serverHost = uri.searchParams.get('server') || config.godotServerHost || '127.0.0.1';
  const serverPort = uri.searchParams.get('port') || config.godotServerPort || config.novetusPort || 53640;
  const joinScript = await text(`${baseUrl}/api/legacy/join-script?ticket=${encodeURIComponent(ticket)}`);
  const avatar = await json(`${baseUrl}/api/legacy/avatar?ticket=${encodeURIComponent(ticket)}`);
  const place = await json(`${baseUrl}/api/legacy/place/${encodeURIComponent(gameId)}`);
  const avatarAssets = await cacheAvatarAssets(avatar.avatar || {});
  const joinPath = write('join-script.lua', joinScript);
  const avatarPath = write('avatar.json', JSON.stringify(avatar, null, 2));
  const avatarAssetsPath = write('avatar-assets.json', JSON.stringify(avatarAssets, null, 2));
  const placePath = write(`place-${gameId}.json`, JSON.stringify(place, null, 2));
  const map = normalizePlayableMap(place.map || {});
  const placeFile = write(`place-${gameId}.rbxl`, mapToRbxlx(map, place.title || `Place ${gameId}`));
  const serverBootstrap = write('server-bootstrap.lua', novetusServerBootstrapScript(map));
  const avatarScript = write('avatar-appearance.lua', avatarToLua(avatar.avatar || {}, avatarAssets));
  const target = resolveExecutable(config.playerExe, 'player');
  const serverTarget = resolveExecutable(config.serverExe || config.playerExe, 'server');
  const values = {
    joinScript: joinPath,
    avatarJson: avatarPath,
    avatarAssets: avatarAssetsPath,
    avatarScript,
    placeJson: placePath,
    placeFile,
    ticket,
    gameId,
    baseUrl,
    serverHost,
    serverPort,
    novetusSoloScript: novetusSoloScript(avatar.avatar || {}, target),
    novetusClientScript: novetusClientScript(avatar.avatar || {}, target),
    novetusServerScript: novetusServerScript(serverTarget, serverBootstrap)
  };
  console.log(`Downloaded join data:\n${joinPath}\n${avatarPath}\n${avatarAssetsPath}\n${placePath}\n${placeFile}\n${avatarScript}`);
  if (isNovetusExe(target.exe) && isNovetusExe(serverTarget.exe) && config.useNovetusLocalServer !== false) {
    closeExistingNovetus();
    launch(serverTarget, applyArgs(chooseTemplate('server', config.serverArgs, serverTarget.exe), values), 'server');
    return setTimeout(() => launch(target, applyArgs(chooseTemplate('player', config.playerArgs, target.exe), values), 'player'), Number(config.clientJoinDelayMs || 4500));
  }
  const args = applyArgs(chooseTemplate('player', config.playerArgs, target.exe), values);
  launch(target, args, 'player');
}

async function openStudio() {
  const ticket = need('ticket');
  const baseUrl = need('baseUrl');
  const project = await json(`${baseUrl}/api/legacy/studio-project?ticket=${encodeURIComponent(ticket)}`);
  const projectPath = write(`studio-project-${project.gameId || 'new'}.json`, JSON.stringify(project, null, 2));
  const placeFile = write(`studio-project-${project.gameId || 'new'}.rbxl`, mapToRbxlx(normalizePlayableMap(project.map || {}), project.title || 'Novo Mundo'));
  const target = resolveExecutable(config.studioExe, 'studio');
  const args = applyArgs(chooseTemplate('studio', config.studioArgs, target.exe), { ticket, baseUrl, projectJson: projectPath, placeFile, novetusStudioScript: novetusStudioScript(target) });
  console.log(`Downloaded studio project:\n${projectPath}\n${placeFile}`);
  launch(target, args, 'studio');
}

function launch(resolved, args, label) {
  if (!resolved.exe || config.launchMode === 'dry-run') {
    log(`[dry-run] Would launch ${label}: ${resolved.exe || '(not configured)'} ${args.join(' ')}`);
    console.log(`[dry-run] Would launch ${label}: ${resolved.exe || '(not configured)'} ${args.join(' ')}`);
    return;
  }
  if (!fs.existsSync(resolved.exe)) throw new Error(`${label} executable not found: ${resolved.exe}`);
  log(`Launching ${label}: ${resolved.exe} ${args.join(' ')}`);
  if (process.platform === 'win32') return launchViaCmd(resolved, args, label);
  const child = spawn(resolved.exe, args, { detached: true, cwd: resolved.cwd, stdio: 'ignore' });
  child.unref();
}

function launchViaCmd(resolved, args, label) {
  const script = path.join(cacheDir, `launch-${label}.cmd`);
  const windowControl = writeWindowControlScript();
  const quotedArgs = args.map(cmdQuote).join(' ');
  const launchLines = label === 'server'
    ? [
        `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Set-Location -LiteralPath ${psQuote(resolved.cwd)}; Start-Process -FilePath ${psQuote(resolved.exe)} -ArgumentList @(${args.map(psQuote).join(',')}) -WindowStyle Hidden"`,
        `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ${cmdQuote(windowControl)} RobloxApp_server hide`
      ]
    : [
        `start "" /max ${cmdQuote(resolved.exe)} ${quotedArgs}`,
        `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ${cmdQuote(windowControl)} RobloxApp_server hide`,
        `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ${cmdQuote(windowControl)} RobloxApp_client foreground`
      ];
  const body = ['@echo off', `cd /d ${cmdQuote(resolved.cwd)}`, ...launchLines, `echo %date% %time% launched ${label} >> ${cmdQuote(logPath)}`].join('\r\n');
  fs.writeFileSync(script, body);
  log(`Wrote ${script}`);
  const child = spawn('cmd.exe', ['/c', script], { detached: true, stdio: 'ignore', windowsHide: true });
  child.unref();
}

function writeWindowControlScript() {
  const file = path.join(cacheDir, 'window-control.ps1');
  const source = `
param([string]$ProcessName, [string]$Mode)
$signature = @"
[DllImport("user32.dll")]
public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")]
public static extern bool SetForegroundWindow(IntPtr hWnd);
"@
Add-Type -MemberDefinition $signature -Name Win32 -Namespace Native -ErrorAction SilentlyContinue
if ($Mode -eq "hide") {
  $iterations = 160
  $sleepMs = 250
} else {
  $iterations = 50
  $sleepMs = 150
}
for ($i = 0; $i -lt $iterations; $i++) {
  Start-Sleep -Milliseconds $sleepMs
  $processes = Get-Process $ProcessName -ErrorAction SilentlyContinue
  foreach ($p in $processes) {
    if ($p.MainWindowHandle -ne 0) {
      if ($Mode -eq "hide") {
        [Native.Win32]::ShowWindowAsync($p.MainWindowHandle, 0) | Out-Null
      } elseif ($Mode -eq "foreground") {
        [Native.Win32]::ShowWindowAsync($p.MainWindowHandle, 3) | Out-Null
        [Native.Win32]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
      }
    }
  }
}
`;
  fs.writeFileSync(file, source);
  return file;
}

function cmdQuote(value) {
  return `"${String(value ?? '').replace(/"/g, '""')}"`;
}

function psQuote(value) {
  return `'${String(value ?? '').replace(/'/g, "''")}'`;
}

function chooseTemplate(kind, template, exe) {
  const current = Array.isArray(template) ? template : ['auto'];
  if (isGodotClientExe(exe) && kind === 'player' && current.includes('auto')) {
    return ['--game', '{gameId}', '--base-url', '{baseUrl}', '--server', '{serverHost}', '--port', '{serverPort}', '--ticket', '{ticket}'];
  }
  if (isGodotStudioExe(exe) && kind === 'studio' && current.includes('auto')) {
    return ['--base-url', '{baseUrl}', '--ticket', '{ticket}', '--project', '{projectJson}'];
  }
  if (isNovetusExe(exe) && (current.includes('auto') || isOldDefaultTemplate(kind, current))) {
    if (kind === 'studio') return ['-script', '{novetusStudioScript}', '{placeFile}'];
    if (kind === 'server') return ['-script', '{novetusServerScript}', '-no3d', '{placeFile}'];
    return ['-script', '{novetusClientScript}'];
  }
  if (current.includes('auto')) return kind === 'studio' ? ['{placeFile}'] : ['{joinScript}', '{placeFile}'];
  return current;
}

function isOldDefaultTemplate(kind, template) {
  const joined = template.join('|');
  if (kind === 'server') return joined === '{placeFile}';
  return kind === 'studio' ? joined === '{placeFile}' : joined === '{joinScript}|{placeFile}';
}

function isNovetusExe(exe) {
  return /RobloxApp_(solo|client|studio|server)\.exe$/i.test(String(exe || ''));
}

function isGodotClientExe(exe) {
  return /NovusWorldsClient\.exe$/i.test(String(exe || ''));
}

function isGodotStudioExe(exe) {
  return /NovusWorldsStudio\.exe$/i.test(String(exe || ''));
}

function novetusSoloScript(avatar, target) {
  const userId = Number(avatar.userId || 1) || 1;
  const username = lua(avatar.username || 'NovusPlayer');
  const colors = avatar.colors || {};
  const scriptPath = lua(novetusScriptPath(target));
  const head = brick(colors.head, 24);
  const torso = brick(colors.torso, 23);
  const arms = brick(colors.arms, 24);
  const legs = brick(colors.legs, 26);
  return `dofile('${scriptPath}'); _G.CSSolo(${userId},'${username}',0,0,0,${head},${torso},${arms},${arms},${legs},${legs},0,0,0,0,0,0,0,true)`;
}

function novetusStudioScript(target) {
  return `dofile('${lua(novetusScriptPath(target))}'); _G.CSStudio(true)`;
}

function novetusServerScript(target, bootstrapFile) {
  return `dofile('${lua(novetusScriptPath(target))}'); dofile('${lua(bootstrapFile)}'); _G.CSServer(${novetusPort()},20,'','','',false,0,true)`;
}

function novetusClientScript(avatar, target) {
  const userId = Number(avatar.userId || 1) || 1;
  const username = lua(avatar.username || 'NovusPlayer');
  const colors = avatar.colors || {};
  const scriptPath = lua(novetusScriptPath(target));
  const head = brick(colors.head, 24);
  const torso = brick(colors.torso, 23);
  const arms = brick(colors.arms, 24);
  const legs = brick(colors.legs, 26);
  return `dofile('${scriptPath}'); _G.CSConnect(${userId},'127.0.0.1',${novetusPort()},'${username}',0,0,0,${head},${torso},${arms},${arms},${legs},${legs},0,0,0,0,0,'NBC',0,'','','','',0,true,''); ${novetusGameLayoutScript()}`;
}

function novetusPort() {
  return Number(config.novetusPort || 53640);
}

function closeExistingNovetus() {
  if (process.platform !== 'win32' || config.closeExistingNovetus === false) return;
  for (const image of ['RobloxApp_client.exe', 'RobloxApp_server.exe', 'RobloxApp_solo.exe']) {
    try { spawnSync('taskkill.exe', ['/IM', image, '/F'], { stdio: 'ignore', windowsHide: true }); } catch {}
  }
}

function novetusGameLayoutScript() {
  return `
pcall(function() game:SetRemoteBuildMode(false) end)
local function removeByName(root, name)
  pcall(function()
    local item = root:FindFirstChild(name, true)
    if item then item:Remove() end
  end)
end
local function forceGameLayout()
  pcall(function()
    local gui = game:GetService('CoreGui'):FindFirstChild('RobloxGui')
    if gui then
      removeByName(gui, 'BuildTools')
      removeByName(gui, 'PropertyTools')
      removeByName(gui, 'CurrentLoadout')
      removeByName(gui, 'Backpack')
    end
  end)
  pcall(function()
    local player = game:GetService('Players').LocalPlayer
    if player and player.Backpack then player.Backpack:ClearAllChildren() end
    if player and player.Character then
      local humanoid = player.Character:FindFirstChild('Humanoid')
      local torso = player.Character:FindFirstChild('Torso')
      if humanoid then workspace.CurrentCamera.CameraSubject = humanoid end
      if torso then workspace.CurrentCamera.CoordinateFrame = CFrame.new(torso.Position + Vector3.new(0, 6, 12), torso.Position) end
    end
  end)
end
delay(0.5, forceGameLayout)
delay(1.5, forceGameLayout)
delay(3.0, forceGameLayout)
`.replace(/\s+/g, ' ').trim();
}

function novetusServerBootstrapScript(map) {
  const playable = normalizePlayableMap(map);
  const objects = playable.objects.map((part, index) => {
    const p = part.position || {};
    const s = part.size || {};
    const rgb = rgbFromHex(part.color || '#cccccc');
    const className = part.type === 'SpawnLocation' ? 'SpawnLocation' : 'Part';
    return [
      `  if not workspace:FindFirstChild("${lua(part.name || `Part${index + 1}`)}") then`,
      `    local part = Instance.new("${className}")`,
      `    part.Name = "${lua(part.name || `Part${index + 1}`)}"`,
      `    part.Size = Vector3.new(${num(s.x, 4)}, ${num(s.y, 1)}, ${num(s.z, 4)})`,
      `    part.CFrame = CFrame.new(${num(p.x, 0)}, ${num(p.y, 0)}, ${num(p.z, 0)})`,
      `    part.Anchored = ${part.anchored === false ? 'false' : 'true'}`,
      `    part.CanCollide = ${part.canCollide === false ? 'false' : 'true'}`,
      `    pcall(function() part.Color = Color3.new(${(rgb.r / 255).toFixed(4)}, ${(rgb.g / 255).toFixed(4)}, ${(rgb.b / 255).toFixed(4)}) end)`,
      '    pcall(function() part.TopSurface = 1; part.BottomSurface = 1 end)',
      '    part.Parent = workspace',
      '  end'
    ].join(' ');
  }).join(' ');
  const spawn = playable.spawnPoints[0] || { x: 0, y: 6, z: 0 };
  return `
_G.CSScript_PostInit = function()
  pcall(function()
${objects}
    if not workspace:FindFirstChild("SpawnLocation") then
      local spawn = Instance.new("SpawnLocation")
      spawn.Name = "SpawnLocation"
      spawn.Size = Vector3.new(6, 1, 6)
      spawn.CFrame = CFrame.new(${num(spawn.x, 0)}, ${num(spawn.y, 3)}, ${num(spawn.z, 0)})
      spawn.Anchored = true
      spawn.CanCollide = true
      spawn.Neutral = true
      spawn.Parent = workspace
    end
  end)
end
`;
}

function novetusScriptPath(target) {
  const local = path.join(target.cwd || '', 'content', 'scripts', 'CSMPFunctions.lua');
  if (fs.existsSync(local)) return local.replace(/\\/g, '/');
  return 'rbxasset://scripts/CSMPFunctions.lua';
}

function resolveExecutable(value, label) {
  const raw = expandEnv(value || '');
  if (!raw) return { exe: '', cwd: __dirname };
  if (fs.existsSync(raw) && fs.statSync(raw).isFile()) return { exe: raw, cwd: path.dirname(raw) };
  if (fs.existsSync(raw) && fs.statSync(raw).isDirectory()) {
    const candidates = label === 'studio'
      ? ['NovusWorldsStudio.exe', 'RobloxApp_studio.exe', 'RobloxStudioBeta.exe', 'Novetus.exe']
      : label === 'server'
        ? ['RobloxApp_server.exe', 'RobloxApp_solo.exe', 'Novetus.exe']
        : ['NovusWorldsClient.exe', 'RobloxApp_client.exe', 'RobloxPlayerBeta.exe', 'RobloxApp_solo.exe', 'Novetus.exe'];
    for (const name of candidates) {
      const candidate = path.join(raw, name);
      if (fs.existsSync(candidate)) return { exe: candidate, cwd: raw };
    }
    const exe = fs.readdirSync(raw).find(file => /\.exe$/i.test(file));
    if (exe) return { exe: path.join(raw, exe), cwd: raw };
  }
  return { exe: raw, cwd: path.dirname(raw) || __dirname };
}

function log(message) {
  const line = `[${new Date().toISOString()}] ${message}\n`;
  try { fs.appendFileSync(logPath, line); } catch {}
}

function showError(message) {
  if (process.platform !== 'win32') return;
  const text = `${message}\n\nLog: ${logPath}`.replace(/'/g, "''");
  const script = `Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('${text}', 'Novus Launcher', 'OK', 'Error')`;
  try { spawn('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], { detached: true, stdio: 'ignore' }).unref(); } catch {}
}

async function text(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${url} returned ${res.status}`);
  return res.text();
}

async function json(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${url} returned ${res.status}`);
  return res.json();
}

function write(name, content) {
  const file = path.join(cacheDir, safe(name));
  fs.writeFileSync(file, content);
  return file;
}

function need(key) {
  const value = uri.searchParams.get(key);
  if (!value) throw new Error(`Missing ${key}`);
  return value;
}

function safe(name) {
  return name.replace(/[^A-Za-z0-9_.-]/g, '_');
}

function expandEnv(value) {
  return String(value).replace(/%([^%]+)%/g, (_, key) => process.env[key] || '');
}

function applyArgs(template, values) {
  return template.map(arg => String(arg).replace(/\{([A-Za-z0-9_]+)\}/g, (_, key) => values[key] || ''));
}

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
}

async function cacheAvatarAssets(avatar) {
  const items = Array.isArray(avatar.items) ? avatar.items : [];
  const assets = [];
  for (const item of items) {
    const cached = { id: item.id, type: item.type, name: item.name, textureUrl: item.textureUrl || '', modelUrl: item.modelUrl || '', texturePath: '', modelPath: '', hatTransform: item.hatTransform || {} };
    if (item.textureUrl?.startsWith('data:')) cached.texturePath = writeDataUrl(item.textureUrl, `item-${item.id}-texture.png`);
    else if (isFetchableAsset(item.textureUrl)) cached.texturePath = await downloadAsset(item.textureUrl, `item-${item.id}-texture`);
    if (isFetchableAsset(item.modelUrl)) cached.modelPath = await downloadAsset(item.modelUrl, `item-${item.id}-model`);
    assets.push(cached);
  }
  return assets;
}

async function downloadAsset(url, prefix) {
  try {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`${url} returned ${res.status}`);
    const parsed = new URL(url);
    const ext = path.extname(parsed.pathname) || '.asset';
    const file = path.join(cacheDir, safe(`${prefix}${ext}`));
    fs.writeFileSync(file, Buffer.from(await res.arrayBuffer()));
    return file;
  } catch (err) {
    console.warn(`Could not cache asset ${url}: ${err.message}`);
    return url;
  }
}

function isFetchableAsset(url) {
  return /^https?:\/\//i.test(String(url || ''));
}

function isLegacyMesh(value) {
  return /\.(mesh|rbxm|rbxmx)$/i.test(String(value || '')) || /^rbxasset:\/\//i.test(String(value || ''));
}

function writeDataUrl(dataUrl, name) {
  const match = String(dataUrl).match(/^data:([^;]+);base64,(.+)$/);
  if (!match) return '';
  const file = path.join(cacheDir, safe(name));
  fs.writeFileSync(file, Buffer.from(match[2], 'base64'));
  return file;
}

function normalizePlayableMap(map) {
  const sourceObjects = Array.isArray(map.objects) ? map.objects : [];
  const objects = sourceObjects.length ? [...sourceObjects] : [{
    type: 'Part',
    name: 'Baseplate',
    position: { x: 0, y: -0.5, z: 0 },
    size: { x: 80, y: 1, z: 80 },
    color: '#6b8e23',
    anchored: true,
    canCollide: true
  }];
  const hasBaseplate = objects.some(part => String(part.name || '').toLowerCase() === 'baseplate');
  if (!hasBaseplate) {
    objects.unshift({
      type: 'Part',
      name: 'Baseplate',
      position: { x: 0, y: -0.5, z: 0 },
      size: { x: 80, y: 1, z: 80 },
      color: '#6b8e23',
      anchored: true,
      canCollide: true
    });
  }
  return {
    ...map,
    objects,
    spawnPoints: Array.isArray(map.spawnPoints) && map.spawnPoints.length ? map.spawnPoints : [{ x: 0, y: 4, z: 0 }]
  };
}

function mapToRbxlx(map, title) {
  const playable = normalizePlayableMap(map);
  const objects = Array.isArray(playable.objects) ? playable.objects : [];
  const parts = objects.map((part, index) => partToXml(part, index)).join('\n');
  const spawn = (Array.isArray(playable.spawnPoints) && playable.spawnPoints[0]) || { x: 0, y: 4, z: 0 };
  const spawnXml = spawnToXml(spawn);
  const lockScript = gameplayLockScript(spawn);
  return `<?xml version="1.0" encoding="utf-8"?>
<roblox xmlns:xmime="http://www.w3.org/2005/05/xmlmime" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.roblox.com/roblox.xsd" version="4">
  <External>null</External>
  <External>nil</External>
  <Item class="Workspace" referent="RBX0">
    <Properties>
      <string name="Name">Workspace</string>
    </Properties>
${parts}
${spawnXml}
${lockScript}
  </Item>
  <Item class="Lighting" referent="RBX_LIGHTING">
    <Properties>
      <string name="Name">Lighting</string>
      <Color3 name="Ambient"><R>0.25</R><G>0.25</G><B>0.25</B></Color3>
    </Properties>
  </Item>
  <Item class="Players" referent="RBX_PLAYERS">
    <Properties><string name="Name">Players</string></Properties>
  </Item>
  <Item class="StarterPack" referent="RBX_STARTERPACK">
    <Properties><string name="Name">StarterPack</string></Properties>
  </Item>
  <Item class="StarterGui" referent="RBX_STARTERGUI">
    <Properties><string name="Name">StarterGui</string></Properties>
  </Item>
  <Item class="SoundService" referent="RBX_SOUND">
    <Properties><string name="Name">SoundService</string></Properties>
  </Item>
  <Meta name="ExplicitAutoJoints">true</Meta>
  <Meta name="PlaceTitle">${xml(title)}</Meta>
</roblox>`;
}

function spawnToXml(spawn) {
  const x = num(spawn.x, 0);
  const y = num(spawn.y, 3);
  const z = num(spawn.z, 0);
  return `    <Item class="SpawnLocation" referent="RBX_SPAWN_0">
      <Properties>
        <string name="Name">SpawnLocation</string>
        <bool name="Anchored">true</bool>
        <bool name="CanCollide">true</bool>
        <bool name="AllowTeamChangeOnTouch">false</bool>
        <bool name="Neutral">true</bool>
        <Vector3 name="size"><X>6</X><Y>1</Y><Z>6</Z></Vector3>
        <CoordinateFrame name="CFrame">
          <X>${x}</X><Y>${y}</Y><Z>${z}</Z>
          <R00>1</R00><R01>0</R01><R02>0</R02>
          <R10>0</R10><R11>1</R11><R12>0</R12>
          <R20>0</R20><R21>0</R21><R22>1</R22>
        </CoordinateFrame>
        <Color3uint8 name="Color3uint8">255</Color3uint8>
        <int name="BrickColor">23</int>
        <int name="TopSurface">3</int>
        <int name="BottomSurface">3</int>
        <float name="Transparency">0.25</float>
      </Properties>
    </Item>`;
}

function gameplayLockScript(spawn) {
  const source = `
local spawn = Vector3.new(${num(spawn.x, 0)}, ${num(spawn.y, 3) + 4}, ${num(spawn.z, 0)})
local function lockPlayer(player)
  pcall(function() player.Backpack:ClearAllChildren() end)
  player.CharacterAdded:connect(function(character)
    wait(0.2)
    local torso = character:FindFirstChild("Torso")
    if torso then torso.CFrame = CFrame.new(spawn) end
  end)
end
game.Players.PlayerAdded:connect(lockPlayer)
for _, player in pairs(game.Players:GetPlayers()) do lockPlayer(player) end
`;
  return `    <Item class="Script" referent="RBX_GAMEPLAY_LOCK">
      <Properties>
        <string name="Name">NovusGameplayLock</string>
        <bool name="Disabled">false</bool>
        <ProtectedString name="Source">${xml(source)}</ProtectedString>
      </Properties>
    </Item>`;
}

function partToXml(part, index) {
  const p = part.position || {};
  const s = part.size || {};
  const color = hexToRgb(part.color || '#cccccc');
  const name = xml(part.name || `Part${index + 1}`);
  const anchored = part.anchored === false ? 'false' : 'true';
  const collide = part.canCollide === false ? 'false' : 'true';
  return `    <Item class="Part" referent="RBX_PART_${index}">
      <Properties>
        <string name="Name">${name}</string>
        <bool name="Anchored">${anchored}</bool>
        <bool name="CanCollide">${collide}</bool>
        <Vector3 name="size"><X>${num(s.x, 4)}</X><Y>${num(s.y, 1)}</Y><Z>${num(s.z, 4)}</Z></Vector3>
        <CoordinateFrame name="CFrame">
          <X>${num(p.x, 0)}</X><Y>${num(p.y, 0)}</Y><Z>${num(p.z, 0)}</Z>
          <R00>1</R00><R01>0</R01><R02>0</R02>
          <R10>0</R10><R11>1</R11><R12>0</R12>
          <R20>0</R20><R21>0</R21><R22>1</R22>
        </CoordinateFrame>
        <Color3uint8 name="Color3uint8">${color}</Color3uint8>
        <int name="TopSurface">1</int>
        <int name="BottomSurface">1</int>
      </Properties>
    </Item>`;
}

function avatarToLua(avatar, assets = []) {
  const colors = avatar.colors || {};
  const items = Array.isArray(avatar.items) ? avatar.items : [];
  const assetById = new Map(assets.map(asset => [String(asset.id), asset]));
  const lines = [
    '-- Novus Worlds generated avatar appearance',
    'local Players = game:GetService("Players")',
    'local player = Players.LocalPlayer',
    '',
    'local function hexColor(hex)',
    '  hex = string.gsub(tostring(hex or "#ffffff"), "#", "")',
    '  local r = tonumber(string.sub(hex, 1, 2), 16) or 255',
    '  local g = tonumber(string.sub(hex, 3, 4), 16) or 255',
    '  local b = tonumber(string.sub(hex, 5, 6), 16) or 255',
    '  return Color3.new(r / 255, g / 255, b / 255)',
    'end',
    '',
    'local function setPartColor(character, name, color)',
    '  local part = character:FindFirstChild(name)',
    '  if part and part:IsA("BasePart") then part.Color = color end',
    'end',
    '',
    'local function clearClass(character, className)',
    '  for _, child in pairs(character:GetChildren()) do',
    '    if child.ClassName == className then child:Remove() end',
    '  end',
    'end',
    '',
    'local function makeContent(id)',
    '  if id == nil or id == "" then return "" end',
    '  if string.find(id, "http://") == 1 or string.find(id, "https://") == 1 or string.find(id, "rbxasset://") == 1 then return id end',
    '  return "file:///" .. string.gsub(id, "\\\\", "/")',
    'end',
    '',
    'local function weldToHead(character, handle, transform)',
    '  local head = character:FindFirstChild("Head")',
    '  if not head or not handle then return end',
    '  handle.Anchored = false',
    '  handle.CanCollide = false',
    '  handle.CFrame = head.CFrame * CFrame.new(transform.px, transform.py, transform.pz) * CFrame.Angles(math.rad(transform.rx), math.rad(transform.ry), math.rad(transform.rz))',
    '  local weld = Instance.new("Weld")',
    '  weld.Name = "NovusHatWeld"',
    '  weld.Part0 = head',
    '  weld.Part1 = handle',
    '  weld.C0 = CFrame.new(transform.px, transform.py, transform.pz) * CFrame.Angles(math.rad(transform.rx), math.rad(transform.ry), math.rad(transform.rz))',
    '  weld.C1 = CFrame.new()',
    '  weld.Parent = handle',
    'end',
    '',
    'local function apply(character)',
    `  local colors = { head = "${lua(colors.head || '#f5cd30')}", torso = "${lua(colors.torso || '#0d69ac')}", arms = "${lua(colors.arms || '#f5cd30')}", legs = "${lua(colors.legs || '#1b2a35')}" }`,
    '  setPartColor(character, "Head", hexColor(colors.head))',
    '  setPartColor(character, "Torso", hexColor(colors.torso))',
    '  setPartColor(character, "Left Arm", hexColor(colors.arms))',
    '  setPartColor(character, "Right Arm", hexColor(colors.arms))',
    '  setPartColor(character, "Left Leg", hexColor(colors.legs))',
    '  setPartColor(character, "Right Leg", hexColor(colors.legs))',
    '  clearClass(character, "Shirt")',
    '  clearClass(character, "Pants")',
  ];
  for (const item of items) {
    const asset = assetById.get(String(item.id)) || {};
    const texture = asset.texturePath || item.textureUrl || '';
    const model = asset.modelPath || item.modelUrl || '';
    if (item.type === 'face' && texture) {
      lines.push(`  do local head = character:FindFirstChild("Head"); if head then local old = head:FindFirstChild("face"); if old then old:Remove() end; local face = Instance.new("Decal"); face.Name = "face"; face.Face = Enum.NormalId.Front; face.Texture = makeContent("${lua(texture)}"); face.Parent = head end end`);
    }
    if (item.type === 'shirt' && texture) {
      lines.push(`  do local shirt = Instance.new("Shirt"); shirt.Name = "NovusShirt"; shirt.ShirtTemplate = makeContent("${lua(texture)}"); shirt.Parent = character end`);
    }
    if (item.type === 'pants' && texture) {
      lines.push(`  do local pants = Instance.new("Pants"); pants.Name = "NovusPants"; pants.PantsTemplate = makeContent("${lua(texture)}"); pants.Parent = character end`);
    }
    if (item.type === 'hat' && model && item.compatible !== false && isLegacyMesh(model)) {
      const t = item.hatTransform || {};
      const p = t.position || {};
      const r = t.rotation || {};
      const s = t.scale || {};
      lines.push('  do');
      lines.push(`    local hat = Instance.new("Hat"); hat.Name = "${lua(item.name || 'NovusHat')}"`);
      lines.push('    local handle = Instance.new("Part"); handle.Name = "Handle"; handle.Size = Vector3.new(2, 1, 2); handle.TopSurface = 0; handle.BottomSurface = 0; handle.Parent = hat');
      lines.push(`    local mesh = Instance.new("SpecialMesh"); mesh.MeshType = Enum.MeshType.FileMesh; mesh.MeshId = makeContent("${lua(model)}"); mesh.TextureId = makeContent("${lua(texture)}"); mesh.Scale = Vector3.new(${num(s.x, 1)}, ${num(s.y, 1)}, ${num(s.z, 1)}); mesh.Parent = handle`);
      lines.push(`    hat.Parent = character; weldToHead(character, handle, { px = ${num(p.x, 0)}, py = ${num(p.y, 1.2)}, pz = ${num(p.z, 0)}, rx = ${num(r.x, 0)}, ry = ${num(r.y, 0)}, rz = ${num(r.z, 0)} })`);
      lines.push('  end');
    }
  }
  lines.push('end');
  lines.push('if player and player.Character then apply(player.Character) end');
  lines.push('if player then player.CharacterAdded:connect(apply) end');
  return lines.join('\n');
}

function hexToRgb(hex) {
  const value = String(hex).replace('#', '');
  const n = Number.parseInt(value.length === 3 ? value.split('').map(c => c + c).join('') : value, 16);
  const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
  return r * 65536 + g * 256 + b;
}

function rgbFromHex(hex) {
  const value = String(hex || '').replace('#', '');
  const n = Number.parseInt(value.length === 3 ? value.split('').map(c => c + c).join('') : value, 16);
  if (!Number.isFinite(n)) return { r: 204, g: 204, b: 204 };
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
}

function num(value, fallback) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function brick(hex, fallback) {
  const value = String(hex || '').toLowerCase();
  const table = {
    '#f5cd30': 24,
    '#0d69ac': 23,
    '#1b2a35': 26,
    '#ffffff': 1,
    '#c4281c': 21,
    '#00cc44': 28,
    '#ffd700': 24
  };
  return table[value] || fallback;
}

function xml(value) {
  return String(value ?? '').replace(/[<>&"']/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' }[c]));
}

function lua(value) {
  return String(value ?? '').replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}
