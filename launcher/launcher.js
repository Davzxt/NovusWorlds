const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

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
  const baseUrl = normalizeBaseUrl(need('baseUrl'));
  const gameId = need('gameId');
  const serverHost = uri.searchParams.get('server') || config.godotServerHost || '127.0.0.1';
  const serverPort = uri.searchParams.get('port') || config.godotServerPort || 53640;
  const target = resolveExecutable(config.playerExe, 'player');
  const joinData = await json(`${baseUrl}/api/legacy/tickets/${encodeURIComponent(ticket)}`);
  const joinPath = write(`join-${gameId}.json`, JSON.stringify(joinData, null, 2));
  const args = applyArgs(['--game', '{gameId}', '--base-url', '{baseUrl}', '--server', '{serverHost}', '--port', '{serverPort}', '--ticket', '{ticket}', '--join-json', '{joinJson}'], {
    ticket,
    gameId,
    baseUrl,
    serverHost,
    serverPort,
    joinJson: joinPath
  });
  console.log(`Downloaded Godot join ticket:\n${joinPath}`);
  launch(target, args, 'player');
}

async function openStudio() {
  const ticket = need('ticket');
  const baseUrl = normalizeBaseUrl(need('baseUrl'));
  const project = await json(`${baseUrl}/api/legacy/studio-project?ticket=${encodeURIComponent(ticket)}`);
  const projectPath = write(`studio-project-${project.gameId || 'new'}.json`, JSON.stringify(project, null, 2));
  const target = resolveExecutable(config.studioExe, 'studio');
  const args = applyArgs(['--base-url', '{baseUrl}', '--ticket', '{ticket}', '--project-json', '{projectJson}'], {
    ticket,
    baseUrl,
    projectJson: projectPath
  });
  console.log(`Downloaded studio project:\n${projectPath}`);
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
  const quotedArgs = args.map(cmdQuote).join(' ');
  const body = [
    '@echo off',
    `cd /d ${cmdQuote(resolved.cwd)}`,
    `start "" /max ${cmdQuote(resolved.exe)} ${quotedArgs}`,
    `echo %date% %time% launched ${label} >> ${cmdQuote(logPath)}`
  ].join('\r\n');
  fs.writeFileSync(script, body);
  log(`Wrote ${script}`);
  const child = spawn('cmd.exe', ['/c', script], { detached: true, stdio: 'ignore', windowsHide: true });
  child.unref();
}

function resolveExecutable(value, label) {
  const raw = expandEnv(value || '');
  if (!raw) return { exe: '', cwd: __dirname };
  if (fs.existsSync(raw) && fs.statSync(raw).isFile()) return { exe: raw, cwd: path.dirname(raw) };
  if (fs.existsSync(raw) && fs.statSync(raw).isDirectory()) {
    const candidates = label === 'studio' ? ['NovusWorldsStudio.exe'] : ['NovusWorldsClient.exe'];
    for (const name of candidates) {
      const candidate = path.join(raw, name);
      if (fs.existsSync(candidate)) return { exe: candidate, cwd: raw };
    }
    const exe = fs.readdirSync(raw).find(file => /^NovusWorlds(Client|Studio)\.exe$/i.test(file));
    if (exe) return { exe: path.join(raw, exe), cwd: raw };
  }
  return { exe: raw, cwd: path.dirname(raw) || __dirname };
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

function normalizeBaseUrl(value) {
  const url = String(value || '').trim();
  if (/^http:\/\/[^/]+\.onrender\.com/i.test(url)) return url.replace(/^http:\/\//i, 'https://');
  return url;
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

function cmdQuote(value) {
  return `"${String(value ?? '').replace(/"/g, '""')}"`;
}

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
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
