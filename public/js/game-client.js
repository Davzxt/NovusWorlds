import * as THREE from '/vendor/three/build/three.module.js';
import { api, currentUser, esc } from './main.js';
import { createR6Avatar } from './r6-viewer.js';

const id = new URLSearchParams(location.search).get('id') || '1';
const { game } = await api('/api/games/' + id);
const user = await currentUser() || { id: Math.random().toString(36).slice(2), username: 'Guest', avatar_data: {} };
const settings = { mouseSensitivity: 0.004, walkSpeed: 7.5, runSpeed: 12, jumpPower: 11, fov: 70, shiftToRun: true, graphics: 'high' };
const guestKey = getGuestKey();

document.getElementById('gameTitle').textContent = game.title;

const scene = new THREE.Scene();
scene.background = new THREE.Color(game.map_data.skyColor || '#87CEEB');
scene.fog = new THREE.Fog(scene.background, 70, 180);

const camera = new THREE.PerspectiveCamera(settings.fov, innerWidth / innerHeight, 0.1, 500);
const renderer = new THREE.WebGLRenderer({ antialias: settings.graphics !== 'low' });
renderer.setPixelRatio(settings.graphics === 'low' ? 1 : Math.min(devicePixelRatio, 2));
renderer.setSize(innerWidth, innerHeight);
renderer.domElement.className = 'game-canvas';
document.body.appendChild(renderer.domElement);

scene.add(new THREE.HemisphereLight(0xffffff, 0x283344, 1.8));
const sun = new THREE.DirectionalLight(0xffffff, 1.4);
sun.position.set(40, 70, 30);
scene.add(sun);

const spawn = game.map_data.spawnPoints?.[0] || { x: 0, y: 3, z: 0 };
const local = await createCharacter(user.avatar_data, true);
local.position.set(spawn.x, spawn.y, spawn.z);
scene.add(local);

const players = new Map();
const colliders = [];
for (const object of game.map_data.objects || []) addPart(object);

const keys = {};
let yaw = Math.PI;
let pitch = -0.18;
let cameraDistance = 9;
let rightMouse = false;
let velocityY = 0;
let grounded = false;
let lastSentChat = 0;
let touchVector = new THREE.Vector3();
let touchJump = false;
let mobileCameraTouch = null;

addEventListener('keydown', (e) => {
  if (document.activeElement === document.getElementById('chatInput')) return;
  keys[e.key.toLowerCase()] = true;
});
addEventListener('keyup', (e) => keys[e.key.toLowerCase()] = false);
renderer.domElement.addEventListener('contextmenu', (e) => e.preventDefault());
renderer.domElement.addEventListener('mousedown', (e) => { if (e.button === 2) rightMouse = true; });
addEventListener('mouseup', (e) => { if (e.button === 2) rightMouse = false; });
addEventListener('mousemove', (e) => {
  if (!rightMouse) return;
  yaw -= e.movementX * settings.mouseSensitivity;
  pitch = THREE.MathUtils.clamp(pitch - e.movementY * settings.mouseSensitivity, -1.1, 0.45);
});
addEventListener('wheel', (e) => {
  cameraDistance = THREE.MathUtils.clamp(cameraDistance + Math.sign(e.deltaY) * 0.8, 5, 22);
});

const ws = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws/game/${id}`);
ws.onopen = () => ws.send(JSON.stringify({ type: 'join', gameId: id, userId: user.id, guestKey, username: user.username, avatarData: user.avatar_data, position: packVec(local.position) }));
ws.onmessage = (ev) => {
  const data = JSON.parse(ev.data);
  if (data.type === 'world_state') syncPlayers(data.players);
  if (data.type === 'player_leave') {
    players.get(data.playerId)?.group.removeFromParent();
    players.delete(data.playerId);
  }
  if (data.type === 'chat_broadcast') addChat(data);
};

setInterval(() => {
  if (ws.readyState !== 1) return;
  ws.send(JSON.stringify({ type: 'move', position: packVec(local.position), rotation: { x: 0, y: local.rotation.y, z: 0 }, animation: getAnimation() }));
}, 80);

document.getElementById('chatInput').addEventListener('keydown', (e) => {
  if (e.key !== 'Enter') return;
  const text = e.target.value.trim();
  if (!text || Date.now() - lastSentChat < 450) return;
  lastSentChat = Date.now();
  ws.send(JSON.stringify({ type: 'chat', message: text }));
  e.target.value = '';
});
document.getElementById('leaveBtn').onclick = () => location.href = '/games.html';
function addPart(o) {
  const material = materialFor(o);
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(o.size.x, o.size.y, o.size.z), material);
  mesh.position.set(o.position.x, o.position.y, o.position.z);
  mesh.rotation.set(o.rotation.x || 0, o.rotation.y || 0, o.rotation.z || 0);
  mesh.userData = { ...o, half: { x: o.size.x / 2, y: o.size.y / 2, z: o.size.z / 2 } };
  scene.add(mesh);
  if (o.canCollide) colliders.push(mesh);
}

function materialFor(o) {
  const roughness = o.material === 'Metal' ? 0.25 : 0.85;
  const metalness = o.material === 'Metal' ? 0.55 : 0;
  return new THREE.MeshStandardMaterial({ color: o.color, roughness, metalness, transparent: o.transparency > 0, opacity: 1 - (o.transparency || 0) });
}

async function createCharacter(avatarData, isLocal = false) {
  const group = new THREE.Group();
  group.userData.parts = {};
  group.add(await createR6Avatar(avatarData));
  const label = makeNameLabel(isLocal ? user.username : '');
  label.position.y = 4.0;
  group.add(label);
  group.userData.label = label;
  return group;
}

function animateR6(group, name, t) {
  const root = group.children[0]?.children?.find?.(c => c.type === 'Group') || group.children[0];
  const parts = root?.children || [];
  const speed = name === 'run' ? 12 : name === 'walk' ? 8 : name === 'climb' ? 10 : 2;
  const swing = Math.sin(t * speed);
  parts.forEach((m) => {
    const x = m.position?.x || 0, y = m.position?.y || 0;
    m.rotation.x *= 0.75;
    m.rotation.z *= 0.75;
    if (name === 'idle') { if (y > 1.8) m.rotation.y = Math.sin(t * 1.5) * 0.04; return; }
    if (name === 'jump') { if (Math.abs(x) > .55 && y > .8) m.rotation.x = 0.45; if (y < .8) m.rotation.x = -0.25; return; }
    if (name === 'climb') { if (Math.abs(x) > .55 || y < .8) m.rotation.x = -swing * 0.65; return; }
    if (Math.abs(x) > .55 && y > .8) m.rotation.x = (x > 0 ? swing : -swing) * 0.42;
    if (y < .8) m.rotation.x = (x > 0 ? -swing : swing) * 0.36;
  });
}

function moveAndCollide(dt) {
  const touch = getTouchMove();
  const input = touch.lengthSq() ? touch : new THREE.Vector3((keys.a ? 1 : 0) - (keys.d ? 1 : 0), 0, (keys.w ? 1 : 0) - (keys.s ? 1 : 0));
  const isMoving = input.lengthSq() > 0;
  const run = keys.shift && settings.shiftToRun;
  if (isMoving) {
    input.normalize();
    const camForward = new THREE.Vector3(-Math.sin(yaw), 0, -Math.cos(yaw));
    const camRight = new THREE.Vector3(camForward.z, 0, -camForward.x);
    const dir = new THREE.Vector3().addScaledVector(camRight, input.x).addScaledVector(camForward, input.z).normalize();
    tryHorizontalMove(dir.x * (run ? settings.runSpeed : settings.walkSpeed) * dt, 0);
    tryHorizontalMove(0, dir.z * (run ? settings.runSpeed : settings.walkSpeed) * dt);
    local.rotation.y = Math.atan2(dir.x, dir.z) + Math.PI;
  }
  velocityY -= 28 * dt;
  if ((keys[' '] || touchJump) && grounded) {
    velocityY = settings.jumpPower;
    grounded = false;
  }
  local.position.y += velocityY * dt;
  resolveVertical();
  animateR6(local, getAnimation(), performance.now() / 1000);
}

function tryHorizontalMove(dx, dz) {
  local.position.x += dx;
  local.position.z += dz;
  for (const c of colliders) {
    if (intersectsPlayer(c)) {
      local.position.x -= dx;
      local.position.z -= dz;
      return;
    }
  }
}

function resolveVertical() {
  grounded = false;
  for (const c of colliders) {
    const half = c.userData.half;
    const withinX = Math.abs(local.position.x - c.position.x) < half.x + 0.42;
    const withinZ = Math.abs(local.position.z - c.position.z) < half.z + 0.42;
    const top = c.position.y + half.y;
    const feet = local.position.y;
    if (withinX && withinZ && feet <= top + 0.22 && feet >= top - 1.2 && velocityY <= 0) {
      local.position.y = top;
      velocityY = 0;
      grounded = true;
    }
  }
  if (local.position.y < -30) {
    local.position.set(spawn.x, spawn.y, spawn.z);
    velocityY = 0;
  }
}

function intersectsPlayer(c) {
  const h = c.userData.half;
  const px = local.position.x, py = local.position.y + 1.6, pz = local.position.z;
  return Math.abs(px - c.position.x) < h.x + 0.45 && Math.abs(py - c.position.y) < h.y + 1.55 && Math.abs(pz - c.position.z) < h.z + 0.45;
}

function updateCamera() {
  camera.fov = settings.fov;
  camera.updateProjectionMatrix();
  const target = new THREE.Vector3(local.position.x, local.position.y + 2.0, local.position.z);
  const offset = new THREE.Vector3(
    Math.sin(yaw) * Math.cos(pitch) * cameraDistance,
    2.2 + Math.sin(pitch) * cameraDistance,
    Math.cos(yaw) * Math.cos(pitch) * cameraDistance
  );
  camera.position.lerp(target.clone().add(offset), 0.18);
  camera.lookAt(target);
}

function syncPlayers(list) {
  document.getElementById('onlineCount').textContent = list.length;
  for (const p of list) {
    if (String(p.id) === String(user.id)) continue;
    let entry = players.get(p.id);
    if (!entry) {
      const group = new THREE.Group();
      createR6Avatar(p.avatar).then(avatar => group.add(avatar)).catch(err => novusFatal?.(err));
      group.userData.label = makeNameLabel(p.username);
      group.userData.label.position.y = 4;
      group.add(group.userData.label);
      scene.add(group);
      entry = { group, target: new THREE.Vector3() };
      players.set(p.id, entry);
    }
    entry.target.set(p.position.x, p.position.y, p.position.z);
    entry.group.position.lerp(entry.target, 0.35);
    entry.group.rotation.y = p.rotation?.y || 0;
    animateR6(entry.group, p.animation || 'idle', performance.now() / 1000);
  }
}

function addChat(data) {
  const log = document.getElementById('gameChatLog');
  log.insertAdjacentHTML('beforeend', `<div><b>${esc(data.from)}:</b> ${esc(data.message)}</div>`);
  log.scrollTop = log.scrollHeight;
  const target = String(data.playerId) === String(user.id) ? local : players.get(data.playerId)?.group;
  if (target) showBubble(target, data.message);
}

function showBubble(group, text) {
  group.userData.bubble?.removeFromParent();
  const sprite = makeBubble(text);
  sprite.position.y = 4.7;
  group.add(sprite);
  group.userData.bubble = sprite;
  setTimeout(() => { if (group.userData.bubble === sprite) sprite.removeFromParent(); }, 5000);
}

function makeBubble(text) {
  const canvas = document.createElement('canvas');
  canvas.width = 512; canvas.height = 128;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#ffffff'; ctx.strokeStyle = '#111111'; ctx.lineWidth = 6;
  ctx.roundRect(10, 10, 492, 86, 16); ctx.fill(); ctx.stroke();
  ctx.fillStyle = '#111111'; ctx.font = 'bold 32px Verdana';
  ctx.fillText(String(text).slice(0, 42), 28, 62);
  const tex = new THREE.CanvasTexture(canvas);
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true }));
  sprite.scale.set(4.2, 1.05, 1);
  return sprite;
}

function makeNameLabel(text) {
  const canvas = document.createElement('canvas');
  canvas.width = 256; canvas.height = 64;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = 'rgba(0,0,0,.55)'; ctx.fillRect(0, 8, 256, 42);
  ctx.fillStyle = '#ffffff'; ctx.font = 'bold 24px Verdana'; ctx.textAlign = 'center'; ctx.fillText(text, 128, 38);
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(canvas), transparent: true }));
  sprite.scale.set(2.6, 0.65, 1);
  return sprite;
}

function getAnimation() {
  if (isClimbing()) return 'climb';
  if (!grounded) return 'jump';
  if (moving()) return keys.shift && settings.shiftToRun ? 'run' : 'walk';
  return 'idle';
}
function moving() { return keys.w || keys.a || keys.s || keys.d || touchVector.lengthSq() > 0.01; }
function isClimbing() {
  if (!moving() || grounded) return false;
  return colliders.some(c => Math.abs(local.position.x - c.position.x) < c.userData.half.x + .5 && Math.abs(local.position.z - c.position.z) < c.userData.half.z + .5 && c.userData.half.y > 1.4);
}
function packVec(v) { return { x: v.x, y: v.y, z: v.z }; }

let last = performance.now();
function tick(now = performance.now()) {
  requestAnimationFrame(tick);
  const dt = Math.min(0.04, (now - last) / 1000);
  last = now;
  moveAndCollide(dt);
  updateCamera();
  renderer.render(scene, camera);
  document.querySelector('.loading')?.remove();
}
tick();

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

function getGuestKey() {
  let key = localStorage.getItem('novusGuestKey');
  if (!key) {
    key = randomId();
    localStorage.setItem('novusGuestKey', key);
  }
  return key;
}

function randomId() {
  return globalThis.crypto?.randomUUID ? crypto.randomUUID() : `guest_${Date.now()}_${Math.random().toString(36).slice(2)}`;
}

function getTouchMove() { return touchVector.clone(); }
function setupMobileControls() {
  if (!matchMedia('(pointer: coarse), (max-width: 780px)').matches) return;
  document.body.classList.add('mobile-client');
  const pad = document.createElement('div');
  pad.className = 'mobile-pad';
  pad.innerHTML = '<div class="stick"></div>';
  const jump = document.createElement('button');
  jump.className = 'mobile-jump';
  jump.textContent = 'Pular';
  document.querySelector('.hud').append(pad, jump);
  const stick = pad.querySelector('.stick');
  let active = null, origin = null;
  pad.addEventListener('touchstart', (e) => { const t = e.changedTouches[0]; active = t.identifier; origin = { x: t.clientX, y: t.clientY }; }, { passive: true });
  pad.addEventListener('touchmove', (e) => {
    const t = [...e.changedTouches].find((x) => x.identifier === active);
    if (!t || !origin) return;
    const dx = THREE.MathUtils.clamp((t.clientX - origin.x) / 45, -1, 1);
    const dy = THREE.MathUtils.clamp((t.clientY - origin.y) / 45, -1, 1);
    touchVector.set(-dx, 0, -dy);
    stick.style.transform = `translate(${dx * 34}px, ${dy * 34}px)`;
  }, { passive: true });
  pad.addEventListener('touchend', () => { active = null; origin = null; touchVector.set(0,0,0); stick.style.transform = 'translate(0,0)'; }, { passive: true });
  jump.addEventListener('touchstart', () => touchJump = true, { passive: true });
  jump.addEventListener('touchend', () => touchJump = false, { passive: true });
  renderer.domElement.addEventListener('touchstart', (e) => {
    for (const t of e.changedTouches) {
      if (t.clientX > innerWidth * 0.42) mobileCameraTouch = { id: t.identifier, x: t.clientX, y: t.clientY };
    }
  }, { passive: true });
  renderer.domElement.addEventListener('touchmove', (e) => {
    if (!mobileCameraTouch) return;
    const t = [...e.changedTouches].find(x => x.identifier === mobileCameraTouch.id);
    if (!t) return;
    yaw -= (t.clientX - mobileCameraTouch.x) * settings.mouseSensitivity;
    pitch = THREE.MathUtils.clamp(pitch - (t.clientY - mobileCameraTouch.y) * settings.mouseSensitivity, -1.1, 0.45);
    mobileCameraTouch.x = t.clientX;
    mobileCameraTouch.y = t.clientY;
  }, { passive: true });
  renderer.domElement.addEventListener('touchend', (e) => {
    if ([...e.changedTouches].some(x => x.identifier === mobileCameraTouch?.id)) mobileCameraTouch = null;
  }, { passive: true });
}
setupMobileControls();
