import { api, toast } from './main.js';
import { createR6Viewer, applyHats } from './r6-viewer.js';

const state = { colors: { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35' }, hats: [] };
const data = await api('/api/avatar/me').catch(() => null);
if (data) Object.assign(state, data.avatar || {});
state.colors = { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35', ...(state.colors || {}) };
state.hats = (state.hats || []).map(Number);

const inventory = (data?.inventory || []).map((item) => ({
  ...item,
  hat_transform: parseJson(item.hat_transform, {})
}));
const hats = inventory.filter((item) => item.type === 'hat');
const faceItems = inventory.filter((item) => item.type === 'face');
const viewer = createR6Viewer(document.getElementById('avatarPreview'), { avatar: state, spin: false });
const inv = document.getElementById('inventory');
const faces = document.getElementById('faces');

function refreshPreview() {
  syncEquippedItems();
  viewer.avatar.traverse((obj) => {
    if (!obj.isMesh || !obj.material?.color) return;
    const n = obj.name.toLowerCase();
    if (n.includes('head') || n.includes('pyramid')) obj.material.color.set(state.colors.head || '#f5cd30');
    else if (n.includes('arm')) obj.material.color.set(state.colors.arms || '#f5cd30');
    else if (n.includes('leg') || n === 'mesh') obj.material.color.set(state.colors.legs || '#1b2a35');
    else obj.material.color.set(state.colors.torso || '#0d69ac');
  });
  applyHats(viewer.avatar, state);
  avatarSummary.textContent = `Face: ${faceName()} | Chapeus: ${(state.hats || []).length}/3`;
}

const palette = ['#f5cd30','#ffaf00','#d7c59a','#a3a2a5','#ffffff','#111111','#0d69ac','#0055bf','#4b974b','#287f47','#c4281c','#ff0000','#b480ff','#8e44ad','#ff66cc','#f2f3f3'];
document.getElementById('palette').innerHTML = ['head','torso','arms','legs'].map(part => `<h3>${part}</h3><div class="grid">${palette.map(c=>`<button class="swatch" data-part="${part}" data-color="${c}" style="background:${c};height:28px"></button>`).join('')}</div>`).join('');
document.getElementById('palette').onclick = (e) => {
  if (!e.target.dataset.part) return;
  state.colors[e.target.dataset.part] = e.target.dataset.color;
  refreshPreview();
};

function renderHats() {
  inv.innerHTML = hats.map((item) => {
    const equipped = state.hats.includes(item.id);
    return `<div class="card"><b>${escapeHtml(item.name)}</b><p>Chapeu</p><button data-hat="${item.id}">${equipped ? 'Remover' : 'Equipar'}</button></div>`;
  }).join('') || '<p>Nenhum chapeu comprado ainda.</p>';
}

inv.onclick = (e) => {
  const id = Number(e.target.dataset.hat);
  if (!id) return;
  state.hats = state.hats.includes(id)
    ? state.hats.filter((hatId) => hatId !== id)
    : [...state.hats, id].slice(0, 3);
  renderHats();
  refreshPreview();
};

function renderFaces() {
  const builtInFaces = ['Classic Smile', 'Serious Face', 'Chill Face'];
  const builtIn = builtInFaces.map((face) => ({ value: face, name: face, source: 'Face gratis' }));
  const owned = faceItems.map((item) => ({ value: String(item.id), name: item.name, source: 'Face comprada' }));
  faces.innerHTML = [...builtIn, ...owned].map((face) => {
    const using = String(state.face || 'Classic Smile') === String(face.value) || (!state.face && face.value === 'Classic Smile');
    return `<div class="card"><b>${escapeHtml(face.name)}</b><p>${face.source}</p><button data-face="${escapeHtml(face.value)}">${using ? 'Usando' : 'Usar'}</button></div>`;
  }).join('');
}

faces.onclick = (e) => {
  if (!e.target.dataset.face) return;
  state.face = e.target.dataset.face;
  renderFaces();
  refreshPreview();
};

document.querySelectorAll('[data-tab]').forEach((btn) => btn.onclick = () => {
  document.querySelectorAll('[data-tab]').forEach(b => b.classList.toggle('active', b === btn));
  document.querySelectorAll('[data-pane]').forEach(p => p.classList.toggle('hidden', p.dataset.pane !== btn.dataset.tab));
});

document.getElementById('saveAvatar').onclick = async () => {
  syncEquippedItems();
  await api('/api/avatar/save', { method: 'POST', body: JSON.stringify({ avatar: state }) });
  toast('Avatar salvo.');
};

function syncEquippedItems() {
  state.equippedItems = hats.filter((item) => state.hats.includes(item.id));
}

function faceName() {
  const match = faceItems.find((item) => String(item.id) === String(state.face));
  return match?.name || state.face || 'Classic Smile';
}

function parseJson(value, fallback) {
  if (!value || typeof value !== 'string') return value || fallback;
  try { return JSON.parse(value); } catch { return fallback; }
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
}

renderHats();
renderFaces();
refreshPreview();
