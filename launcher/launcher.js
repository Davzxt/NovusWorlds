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
  console.log(`Downloaded join data:\n${joinPath}\n${avatarPath}\n${placePath}`);
  launch(config.playerExe, [joinPath], 'player');
}

async function openStudio() {
  const ticket = need('ticket');
  const baseUrl = need('baseUrl');
  const project = await json(`${baseUrl}/api/legacy/studio-project?ticket=${encodeURIComponent(ticket)}`);
  const projectPath = write(`studio-project-${project.gameId || 'new'}.json`, JSON.stringify(project, null, 2));
  console.log(`Downloaded studio project:\n${projectPath}`);
  launch(config.studioExe, [projectPath], 'studio');
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
