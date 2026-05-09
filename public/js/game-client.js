import { api, currentUser, esc } from './main.js';
import { animateR6, createR6 } from './babylon-r6.js';

const B = window.BABYLON;
if (!B) throw new Error('Babylon.js nao carregou.');

const gameId = new URLSearchParams(location.search).get('id') || '1';
const { game } = await api('/api/games/' + gameId);
const animationConfig = (await api('/api/animation-presets').catch(() => ({ animations: {} }))).animations || {};
const user = await currentUser() || { id: getGuestKey(), username: 'Guest', avatar_data: {} };
const guestKey = getGuestKey();

document.getElementById('gameTitle').textContent = game.title;

const canvas = document.createElement('canvas');
canvas.className = 'game-canvas';
document.body.appendChild(canvas);

const engine = new B.Engine(canvas, true, { preserveDrawingBuffer: true, stencil: true });
const scene = new B.Scene(engine);
scene.clearColor = B.Color4.FromHexString((game.map_data.skyColor || '#87CEEB') + 'ff');
scene.fogMode = B.Scene.FOGMODE_LINEAR;
scene.fogStart = 85;
scene.fogEnd = 180;
scene.fogColor = B.Color3.FromHexString(game.map_data.skyColor || '#87CEEB');

const camera = new B.FreeCamera('camera', new B.Vector3(0, 7, -12), scene);
camera.minZ = .1;
camera.fov = B.Tools.ToRadians(70);

const hemi = new B.HemisphericLight('ambient2008', new B.Vector3(0, 1, 0), scene);
hemi.intensity = .68;
hemi.groundColor = new B.Color3(.22, .26, .32);
const sun = new B.DirectionalLight('sun', new B.Vector3(-.45, -1, -.35), scene);
sun.position.set(44, 72, 36);
sun.intensity = 1.45;
const shadow = new B.ShadowGenerator(2048, sun);
shadow.usePercentageCloserFiltering = true;
shadow.filteringQuality = B.ShadowGenerator.QUALITY_MEDIUM;

const spawn = game.map_data.spawnPoints?.[0] || { x: 0, y: 3, z: 0 };
const local = await createR6(scene, user.avatar_data, { scale: 1.35 });
local.position.set(spawn.x, spawn.y, spawn.z);
local.getChildMeshes().forEach(m => shadow.addShadowCaster(m));

const colliders = [];
for (const object of game.map_data.objects || []) addPart(object);

const keys = {};
const players = new Map();
let yaw = Math.PI;
let pitch = -0.18;
let distance = 9;
let rightMouse = false;
let velocityY = 0;
let grounded = false;
let lastSentChat = 0;
let touchVector = new B.Vector3();
let touchJump = false;
let mobileCameraTouch = null;

addEventListener('keydown', e => { if (document.activeElement !== document.getElementById('chatInput')) keys[e.key.toLowerCase()] = true; });
addEventListener('keyup', e => keys[e.key.toLowerCase()] = false);
canvas.addEventListener('contextmenu', e => e.preventDefault());
canvas.addEventListener('mousedown', e => { if (e.button === 0 || e.button === 2) rightMouse = true; });
addEventListener('mouseup', () => { rightMouse = false; });
addEventListener('mousemove', e => {
  if (!rightMouse) return;
  yaw -= e.movementX * .004;
  pitch = B.Scalar.Clamp(pitch - e.movementY * .004, -1.1, .45);
});
addEventListener('wheel', e => distance = B.Scalar.Clamp(distance + Math.sign(e.deltaY) * .8, 5, 22));

const ws = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws/game/${gameId}`);
ws.onopen = () => ws.send(JSON.stringify({ type: 'join', gameId, userId: user.id, guestKey, username: user.username, avatarData: user.avatar_data, position: vec(local.position) }));
ws.onmessage = ev => {
  const data = JSON.parse(ev.data);
  if (data.type === 'world_state') syncPlayers(data.players);
  if (data.type === 'player_leave') { players.get(data.playerId)?.root.dispose(false, true); players.delete(data.playerId); }
  if (data.type === 'chat_broadcast') addChat(data);
};
setInterval(() => {
  if (ws.readyState === 1) ws.send(JSON.stringify({ type: 'move', position: vec(local.position), rotation: { y: local.rotation.y }, animation: getAnimation() }));
}, 80);

document.getElementById('chatInput').addEventListener('keydown', e => {
  if (e.key !== 'Enter') return;
  const message = e.target.value.trim();
  if (!message || Date.now() - lastSentChat < 450) return;
  lastSentChat = Date.now();
  ws.send(JSON.stringify({ type: 'chat', message }));
  e.target.value = '';
});
document.getElementById('leaveBtn').onclick = () => location.href = '/games.html';

let last = performance.now();
engine.runRenderLoop(() => {
  const now = performance.now();
  const dt = Math.min(.04, (now - last) / 1000);
  last = now;
  updateLocal(dt, now / 1000);
  updateCamera();
  scene.render();
  document.querySelector('.loading')?.remove();
});
addEventListener('resize', () => engine.resize());
setupMobileControls();

function addPart(o) {
  const mesh = B.MeshBuilder.CreateBox(o.name || 'Part', { width: o.size.x, height: o.size.y, depth: o.size.z }, scene);
  mesh.position.set(o.position.x, o.position.y, o.position.z);
  mesh.rotation.set(o.rotation.x || 0, o.rotation.y || 0, o.rotation.z || 0);
  mesh.material = materialFor(o);
  mesh.receiveShadows = true;
  shadow.addShadowCaster(mesh);
  mesh.metadata = { ...o, half: { x: o.size.x / 2, y: o.size.y / 2, z: o.size.z / 2 } };
  if (o.canCollide) colliders.push(mesh);
}

function materialFor(o) {
  const mat = new B.StandardMaterial((o.name || 'Part') + 'Mat', scene);
  mat.diffuseColor = B.Color3.FromHexString(o.color || '#cccccc');
  mat.specularColor = o.material === 'Metal' ? new B.Color3(.45, .45, .45) : new B.Color3(.08, .08, .08);
  mat.roughness = o.material === 'Metal' ? .25 : .9;
  mat.diffuseTexture = makeTexture(o.material || 'Plastic', o.color || '#cccccc');
  mat.diffuseTexture.uScale = Math.max(1, o.size.x / 4);
  mat.diffuseTexture.vScale = Math.max(1, o.size.z / 4);
  if (o.transparency > 0) mat.alpha = 1 - o.transparency;
  return mat;
}

function makeTexture(material, color) {
  const tex = new B.DynamicTexture('tex' + Math.random(), { width: 128, height: 128 }, scene, false, B.Texture.NEAREST_SAMPLINGMODE);
  const ctx = tex.getContext();
  ctx.fillStyle = color;
  ctx.fillRect(0, 0, 128, 128);
  if (material === 'Grass') {
    ctx.fillStyle = 'rgba(0,70,0,.35)';
    for (let i = 0; i < 70; i++) ctx.fillRect(Math.random() * 128, Math.random() * 128, 2, 6);
  } else if (material === 'Brick') {
    ctx.strokeStyle = 'rgba(90,0,0,.35)';
    for (let y = 0; y < 128; y += 32) {
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(128, y); ctx.stroke();
      for (let x = (y / 32) % 2 ? 0 : 32; x < 128; x += 64) { ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x, y + 32); ctx.stroke(); }
    }
  } else {
    ctx.fillStyle = 'rgba(255,255,255,.24)';
    for (let x = 24; x < 128; x += 40) for (let y = 24; y < 128; y += 40) { ctx.beginPath(); ctx.arc(x, y, 8, 0, Math.PI * 2); ctx.fill(); }
  }
  tex.update();
  tex.wrapU = tex.wrapV = B.Texture.WRAP_ADDRESSMODE;
  return tex;
}

function updateLocal(dt, time) {
  const input = touchVector.lengthSquared() > .01 ? touchVector.clone() : new B.Vector3((keys.d ? 1 : 0) - (keys.a ? 1 : 0), 0, (keys.w ? 1 : 0) - (keys.s ? 1 : 0));
  if (input.lengthSquared() > 0) {
    input.normalize();
    const forward = new B.Vector3(-Math.sin(yaw), 0, -Math.cos(yaw));
    const right = new B.Vector3(forward.z, 0, -forward.x);
    const dir = right.scale(input.x).add(forward.scale(input.z)).normalize();
    tryMove(dir.x * 7.5 * dt, 0);
    tryMove(0, dir.z * 7.5 * dt);
    local.rotation.y = Math.atan2(dir.x, dir.z) + Math.PI;
  }
  velocityY -= 28 * dt;
  if ((keys[' '] || touchJump) && grounded) { velocityY = 11; grounded = false; }
  local.position.y += velocityY * dt;
  resolveVertical();
  animateR6(local, getAnimation(), time, animationConfig);
}

function tryMove(dx, dz) {
  local.position.x += dx;
  local.position.z += dz;
  for (const c of colliders) {
    if (intersects(c)) {
      local.position.x -= dx;
      local.position.z -= dz;
      return;
    }
  }
}

function resolveVertical() {
  grounded = false;
  for (const c of colliders) {
    const h = c.metadata.half;
    const top = c.position.y + h.y;
    if (Math.abs(local.position.x - c.position.x) < h.x + .45 && Math.abs(local.position.z - c.position.z) < h.z + .45 && local.position.y <= top + .22 && local.position.y >= top - 1.2 && velocityY <= 0) {
      local.position.y = top;
      velocityY = 0;
      grounded = true;
    }
  }
  if (local.position.y < -30) { local.position.set(spawn.x, spawn.y, spawn.z); velocityY = 0; }
}

function intersects(c) {
  const h = c.metadata.half;
  return Math.abs(local.position.x - c.position.x) < h.x + .45 && Math.abs(local.position.y + 1.5 - c.position.y) < h.y + 1.45 && Math.abs(local.position.z - c.position.z) < h.z + .45;
}

function updateCamera() {
  const target = local.position.add(new B.Vector3(0, 2, 0));
  const offset = new B.Vector3(Math.sin(yaw) * Math.cos(pitch) * distance, 2.2 + Math.sin(pitch) * distance, Math.cos(yaw) * Math.cos(pitch) * distance);
  camera.position = B.Vector3.Lerp(camera.position, target.add(offset), .18);
  camera.setTarget(target);
}

async function syncPlayers(list) {
  document.getElementById('onlineCount').textContent = list.length;
  for (const p of list) {
    if (String(p.id) === String(user.id)) continue;
    let entry = players.get(p.id);
    if (!entry) {
      const root = await createR6(scene, p.avatar, { scale: 1.35 });
      root.getChildMeshes().forEach(m => shadow.addShadowCaster(m));
      const label = makeLabel(p.username);
      label.parent = root;
      label.position.y = 4;
      entry = { root, target: new B.Vector3() };
      players.set(p.id, entry);
    }
    entry.target.set(p.position.x, p.position.y, p.position.z);
    entry.root.position = B.Vector3.Lerp(entry.root.position, entry.target, .35);
    entry.root.rotation.y = p.rotation?.y || 0;
    animateR6(entry.root, p.animation || 'idle', performance.now() / 1000, animationConfig);
  }
}

function addChat(data) {
  const log = document.getElementById('gameChatLog');
  log.insertAdjacentHTML('beforeend', `<div><b>${esc(data.from)}:</b> ${esc(data.message)}</div>`);
  log.scrollTop = log.scrollHeight;
}

function makeLabel(text) {
  const tex = new B.DynamicTexture('label', { width: 256, height: 64 }, scene);
  tex.drawText(text, null, 40, 'bold 24px Verdana', '#ffffff', 'rgba(0,0,0,.55)');
  const mat = new B.StandardMaterial('labelMat', scene);
  mat.diffuseTexture = tex;
  mat.emissiveColor = B.Color3.White();
  const plane = B.MeshBuilder.CreatePlane('label', { width: 2.6, height: .65 }, scene);
  plane.billboardMode = B.Mesh.BILLBOARDMODE_ALL;
  plane.material = mat;
  return plane;
}

function getAnimation() {
  if (isClimbing()) return 'climb';
  if (!grounded) return velocityY > 0 ? 'jump' : 'fall';
  return moving() ? 'walk' : 'idle';
}
function moving() { return keys.w || keys.a || keys.s || keys.d || touchVector.lengthSquared() > .01; }
function isClimbing() { return moving() && !grounded && colliders.some(c => Math.abs(local.position.x - c.position.x) < c.metadata.half.x + .5 && Math.abs(local.position.z - c.position.z) < c.metadata.half.z + .5 && c.metadata.half.y > 1.4); }
function vec(v) { return { x: v.x, y: v.y, z: v.z }; }
function getGuestKey() { let k = localStorage.getItem('novusGuestKey'); if (!k) { k = crypto.randomUUID?.() || `guest_${Date.now()}`; localStorage.setItem('novusGuestKey', k); } return k; }

function setupMobileControls() {
  if (!matchMedia('(pointer: coarse), (max-width: 780px)').matches) return;
  document.body.classList.add('mobile-client');
  const pad = document.createElement('div'); pad.className = 'mobile-pad'; pad.innerHTML = '<div class="stick"></div>';
  const jump = document.createElement('button'); jump.className = 'mobile-jump'; jump.textContent = 'Pular';
  document.querySelector('.hud').append(pad, jump);
  const stick = pad.querySelector('.stick');
  let active = null, origin = null;
  pad.addEventListener('touchstart', e => { const t = e.changedTouches[0]; active = t.identifier; origin = { x:t.clientX, y:t.clientY }; }, { passive:true });
  pad.addEventListener('touchmove', e => {
    const t = [...e.changedTouches].find(x => x.identifier === active); if (!t || !origin) return;
    const dx = B.Scalar.Clamp((t.clientX - origin.x) / 45, -1, 1), dy = B.Scalar.Clamp((t.clientY - origin.y) / 45, -1, 1);
    touchVector.set(dx, 0, -dy); stick.style.transform = `translate(${dx * 34}px, ${dy * 34}px)`;
  }, { passive:true });
  pad.addEventListener('touchend', () => { active = null; origin = null; touchVector.set(0,0,0); stick.style.transform = 'translate(0,0)'; }, { passive:true });
  jump.addEventListener('touchstart', () => touchJump = true, { passive:true });
  jump.addEventListener('touchend', () => touchJump = false, { passive:true });
  canvas.addEventListener('touchstart', e => { for (const t of e.changedTouches) if (t.clientX > innerWidth * .42) mobileCameraTouch = { id:t.identifier, x:t.clientX, y:t.clientY }; }, { passive:true });
  canvas.addEventListener('touchmove', e => {
    if (!mobileCameraTouch) return; const t = [...e.changedTouches].find(x => x.identifier === mobileCameraTouch.id); if (!t) return;
    yaw -= (t.clientX - mobileCameraTouch.x) * .004; pitch = B.Scalar.Clamp(pitch - (t.clientY - mobileCameraTouch.y) * .004, -1.1, .45); mobileCameraTouch.x = t.clientX; mobileCameraTouch.y = t.clientY;
  }, { passive:true });
  canvas.addEventListener('touchend', e => { if ([...e.changedTouches].some(x => x.identifier === mobileCameraTouch?.id)) mobileCameraTouch = null; }, { passive:true });
}
