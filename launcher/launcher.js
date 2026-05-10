const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const arg = process.argv.slice(2).join(' ');
if (!arg) {
  console.log('Usage: node launcher.js "novus://join?ticket=...&gameId=...&baseUrl=..."');
  process.exit(0);
}

const configPath = path.join(__dirname, 'config.json');
const config = fs.existsSync(configPath)
  ? JSON.parse(fs.readFileSync(configPath, 'utf8'))
  : JSON.parse(fs.readFileSync(path.join(__dirname, 'config.example.json'), 'utf8'));

const uri = new URL(arg.replace(/^"|"$/g, ''));
const cacheDir = expandEnv(config.cacheDir || path.join(process.env.LOCALAPPDATA || __dirname, 'NovusWorlds', 'Cache'));
fs.mkdirSync(cacheDir, { recursive: true });

main().catch(err => {
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
  const joinScript = await text(`${baseUrl}/api/legacy/join-script?ticket=${encodeURIComponent(ticket)}`);
  const avatar = await json(`${baseUrl}/api/legacy/avatar?ticket=${encodeURIComponent(ticket)}`);
  const place = await json(`${baseUrl}/api/legacy/place/${encodeURIComponent(gameId)}`);
  const joinPath = write('join-script.lua', joinScript);
  const avatarPath = write('avatar.json', JSON.stringify(avatar, null, 2));
  const placePath = write(`place-${gameId}.json`, JSON.stringify(place, null, 2));
  const placeFile = write(`place-${gameId}.rbxlx`, mapToRbxlx(place.map || {}, place.title || `Place ${gameId}`));
  const avatarScript = write('avatar-appearance.lua', avatarToLua(avatar.avatar || {}));
  const args = applyArgs(config.playerArgs || ['{joinScript}', '{placeFile}'], { joinScript: joinPath, avatarJson: avatarPath, avatarScript, placeJson: placePath, placeFile });
  console.log(`Downloaded join data:\n${joinPath}\n${avatarPath}\n${placePath}\n${placeFile}\n${avatarScript}`);
  launch(config.playerExe, args, 'player');
}

async function openStudio() {
  const ticket = need('ticket');
  const baseUrl = need('baseUrl');
  const project = await json(`${baseUrl}/api/legacy/studio-project?ticket=${encodeURIComponent(ticket)}`);
  const projectPath = write(`studio-project-${project.gameId || 'new'}.json`, JSON.stringify(project, null, 2));
  const placeFile = write(`studio-project-${project.gameId || 'new'}.rbxlx`, mapToRbxlx(project.map || {}, project.title || 'Novo Mundo'));
  const args = applyArgs(config.studioArgs || ['{placeFile}'], { projectJson: projectPath, placeFile });
  console.log(`Downloaded studio project:\n${projectPath}\n${placeFile}`);
  launch(config.studioExe, args, 'studio');
}

function launch(exe, args, label) {
  if (!exe || config.launchMode === 'dry-run') {
    console.log(`[dry-run] Would launch ${label}: ${exe || '(not configured)'} ${args.join(' ')}`);
    return;
  }
  if (!fs.existsSync(exe)) throw new Error(`${label} executable not found: ${exe}`);
  const child = spawn(exe, args, { detached: true, stdio: 'ignore' });
  child.unref();
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

function mapToRbxlx(map, title) {
  const objects = Array.isArray(map.objects) ? map.objects : [];
  const parts = objects.map((part, index) => partToXml(part, index)).join('\n');
  return `<?xml version="1.0" encoding="utf-8"?>
<roblox version="4">
  <External>null</External>
  <External>nil</External>
  <Item class="Workspace" referent="RBX0">
    <Properties>
      <string name="Name">Workspace</string>
    </Properties>
${parts}
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

function avatarToLua(avatar) {
  const colors = avatar.colors || {};
  const items = Array.isArray(avatar.items) ? avatar.items : [];
  const lines = [
    '-- Novus Worlds generated avatar appearance',
    '-- Intended for the launcher/client adapter to run after character spawn.',
    'local Players = game:GetService("Players")',
    'local player = Players.LocalPlayer',
    'local function apply(character)',
    `  local colors = { head = "${colors.head || '#f5cd30'}", torso = "${colors.torso || '#0d69ac'}", arms = "${colors.arms || '#f5cd30'}", legs = "${colors.legs || '#1b2a35'}" }`,
    '  -- Client adapter should translate hex colors to BrickColor/Color3 for R6 body parts.',
  ];
  for (const item of items) {
    lines.push(`  -- ${item.type}: ${lua(item.name)} legacy=${lua(item.legacyType)} texture=${lua(item.textureUrl || '')} model=${lua(item.modelUrl || '')}`);
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

function num(value, fallback) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function xml(value) {
  return String(value ?? '').replace(/[<>&"']/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' }[c]));
}

function lua(value) {
  return String(value ?? '').replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}
