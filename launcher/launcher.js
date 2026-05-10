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
  const avatarAssets = await cacheAvatarAssets(avatar.avatar || {});
  const joinPath = write('join-script.lua', joinScript);
  const avatarPath = write('avatar.json', JSON.stringify(avatar, null, 2));
  const avatarAssetsPath = write('avatar-assets.json', JSON.stringify(avatarAssets, null, 2));
  const placePath = write(`place-${gameId}.json`, JSON.stringify(place, null, 2));
  const placeFile = write(`place-${gameId}.rbxlx`, mapToRbxlx(place.map || {}, place.title || `Place ${gameId}`));
  const avatarScript = write('avatar-appearance.lua', avatarToLua(avatar.avatar || {}, avatarAssets));
  const args = applyArgs(config.playerArgs || ['{joinScript}', '{placeFile}'], { joinScript: joinPath, avatarJson: avatarPath, avatarAssets: avatarAssetsPath, avatarScript, placeJson: placePath, placeFile });
  console.log(`Downloaded join data:\n${joinPath}\n${avatarPath}\n${avatarAssetsPath}\n${placePath}\n${placeFile}\n${avatarScript}`);
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

async function cacheAvatarAssets(avatar) {
  const items = Array.isArray(avatar.items) ? avatar.items : [];
  const assets = [];
  for (const item of items) {
    const cached = { id: item.id, type: item.type, name: item.name, textureUrl: item.textureUrl || '', modelUrl: item.modelUrl || '', texturePath: '', modelPath: '', hatTransform: item.hatTransform || {} };
    if (item.textureUrl?.startsWith('data:')) cached.texturePath = writeDataUrl(item.textureUrl, `item-${item.id}-texture.png`);
    else if (item.textureUrl) cached.texturePath = await downloadAsset(item.textureUrl, `item-${item.id}-texture`);
    if (item.modelUrl && !item.modelUrl.startsWith('data:')) cached.modelPath = await downloadAsset(item.modelUrl, `item-${item.id}-model`);
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

function writeDataUrl(dataUrl, name) {
  const match = String(dataUrl).match(/^data:([^;]+);base64,(.+)$/);
  if (!match) return '';
  const file = path.join(cacheDir, safe(name));
  fs.writeFileSync(file, Buffer.from(match[2], 'base64'));
  return file;
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
    if (item.type === 'hat' && model) {
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
